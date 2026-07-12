// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Events;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The TTS utterance orchestrator (tts.md §3.4), self-host dispatch leg. Gates a request on the channel's
/// enabled flag + character cap, applies the opt-out light profanity censor (§3.5), resolves the voice (per-viewer
/// → channel default → first available), synthesizes the audio, stores it through the shared sound-clip store,
/// pushes it to the overlay via the same audio bus the walk-in sounds use, and appends a truthful usage-ledger row —
/// unless the channel requires moderator approval, in which case the utterance is held in the approval queue (P.1a)
/// for a mod to approve (synthesize + play) or reject. A rejected request synthesizes nothing and charges nothing.
/// BYOK and the client-edge mode are follow-on slices.
/// </summary>
public sealed class TtsDispatchService : ITtsDispatchService
{
    private const int DefaultVolume = 100;
    private const int QueueTtlMinutes = 10;

    private readonly ITtsService _tts;
    private readonly ITtsConfigService _config;
    private readonly ITtsProfanityCensor _censor;
    private readonly ISoundClipStore _audioStore;
    private readonly ISoundClipOverlayNotifier _overlay;
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly ILogger<TtsDispatchService> _logger;

    public TtsDispatchService(
        ITtsService tts,
        ITtsConfigService config,
        ITtsProfanityCensor censor,
        ISoundClipStore audioStore,
        ISoundClipOverlayNotifier overlay,
        IApplicationDbContext db,
        IEventBus eventBus,
        ILogger<TtsDispatchService> logger
    )
    {
        _tts = tts;
        _config = config;
        _censor = censor;
        _audioStore = audioStore;
        _overlay = overlay;
        _db = db;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<Result<TtsDispatchOutcome>> RequestSpeakAsync(
        TtsSpeakRequest request,
        CancellationToken ct = default
    )
    {
        Result<TtsConfigDto> configResult = await _config.GetConfigAsync(
            request.BroadcasterId.ToString(),
            ct
        );
        if (configResult.IsFailure)
            return Result.Failure<TtsDispatchOutcome>(
                configResult.ErrorMessage!,
                configResult.ErrorCode!
            );
        TtsConfigDto config = configResult.Value;

        if (!config.IsEnabled)
            return await RejectRequestAsync(
                request,
                "disabled",
                "TTS is disabled for this channel.",
                "FEATURE_DISABLED",
                ct
            );

        string text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return await RejectRequestAsync(
                request,
                "empty",
                "Nothing to say.",
                "VALIDATION_FAILED",
                ct
            );

        int cap = config.MaxLength > 0 ? config.MaxLength : 500;
        if (text.Length > cap)
            return await RejectRequestAsync(
                request,
                "too_long",
                $"That message is too long to read out ({text.Length}/{cap} characters).",
                "VALIDATION_FAILED",
                ct
            );

        // Opt-out light swear filter (§3.5): mask mild profanity before it is ever synthesized. If nothing survives
        // (e.g. the whole message was filtered away), reject rather than speak silence.
        string spokenText = text;
        bool wasCensored = false;
        if (config.ProfanityCensorEnabled)
        {
            TtsCensorResult censored = _censor.Censor(text);
            spokenText = censored.Text;
            wasCensored = censored.WasCensored;
            if (string.IsNullOrWhiteSpace(spokenText))
                return await RejectRequestAsync(
                    request,
                    "empty_after_censor",
                    "Nothing left to say after filtering.",
                    "VALIDATION_FAILED",
                    ct
                );
        }

        string? voiceId = await ResolveVoiceAsync(request, config, ct);
        if (string.IsNullOrWhiteSpace(voiceId))
            return await RejectRequestAsync(
                request,
                "no_voice",
                "No TTS voice is available.",
                "VALIDATION_FAILED",
                ct
            );

        // Cautious-streamer gate (P.1a): hold the utterance for a moderator instead of speaking it now.
        if (config.ModApprovalRequired)
            return await EnqueueForApprovalAsync(
                request,
                text,
                spokenText,
                wasCensored,
                voiceId,
                ct
            );

        return await SynthesizeStorePlayAsync(
            request.BroadcasterId,
            spokenText,
            voiceId,
            request.RequestedByTwitchUserId,
            ct
        );
    }

    public async Task<Result> ApproveAsync(
        Guid broadcasterId,
        Guid queueEntryId,
        Guid reviewedByUserId,
        CancellationToken ct = default
    )
    {
        TtsApprovalQueueEntry? entry = await _db.TtsApprovalQueueEntries.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcasterId && e.Id == queueEntryId && e.Status == "pending",
            ct
        );
        if (entry is null)
            return Result.Failure(
                $"No pending TTS request '{queueEntryId}' was found.",
                "NOT_FOUND"
            );

        // Speak the censored text the moderator reviewed. A synthesis failure leaves the entry pending for retry.
        string spokenText = entry.CensoredText ?? entry.OriginalText;
        Result<TtsDispatchOutcome> played = await SynthesizeStorePlayAsync(
            broadcasterId,
            spokenText,
            entry.VoiceId,
            entry.RequestedByTwitchUserId,
            ct
        );
        if (played.IsFailure)
            return played;

        entry.Status = "approved";
        entry.ReviewedByUserId = reviewedByUserId;
        entry.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new TtsUtteranceReviewedEvent
            {
                BroadcasterId = broadcasterId,
                QueueEntryId = entry.Id,
                ReviewedByUserId = reviewedByUserId,
                Decision = "approved",
            },
            ct
        );
        return Result.Success();
    }

    public async Task<Result> RejectAsync(
        Guid broadcasterId,
        Guid queueEntryId,
        Guid reviewedByUserId,
        CancellationToken ct = default
    )
    {
        TtsApprovalQueueEntry? entry = await _db.TtsApprovalQueueEntries.FirstOrDefaultAsync(
            e => e.BroadcasterId == broadcasterId && e.Id == queueEntryId && e.Status == "pending",
            ct
        );
        if (entry is null)
            return Result.Failure(
                $"No pending TTS request '{queueEntryId}' was found.",
                "NOT_FOUND"
            );

        entry.Status = "rejected";
        entry.ReviewedByUserId = reviewedByUserId;
        entry.ReviewedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new TtsUtteranceReviewedEvent
            {
                BroadcasterId = broadcasterId,
                QueueEntryId = entry.Id,
                ReviewedByUserId = reviewedByUserId,
                Decision = "rejected",
            },
            ct
        );
        return Result.Success();
    }

    public async Task<Result<PagedList<TtsQueueEntryDto>>> GetPendingQueueAsync(
        Guid broadcasterId,
        int page,
        int pageSize,
        CancellationToken ct = default
    )
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 25 : pageSize;

        IQueryable<TtsApprovalQueueEntry> query = _db
            .TtsApprovalQueueEntries.Where(e =>
                e.BroadcasterId == broadcasterId && e.Status == "pending"
            )
            .OrderByDescending(e => e.CreatedAt);

        int total = await query.CountAsync(ct);
        List<TtsQueueEntryDto> items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new TtsQueueEntryDto(
                e.Id,
                e.RequestedByTwitchUserId,
                e.RequestedByDisplayName,
                e.OriginalText,
                e.CensoredText,
                e.VoiceId,
                e.WasCensored,
                e.Status,
                e.CreatedAt,
                e.ExpiresAt,
                e.SourceMessageId
            ))
            .ToListAsync(ct);

        return Result.Success(new PagedList<TtsQueueEntryDto>(items, page, pageSize, total));
    }

    /// <summary>Holds a passing utterance in the approval queue (P.1a) and emits <c>TtsUtteranceQueuedEvent</c>.</summary>
    private async Task<Result<TtsDispatchOutcome>> EnqueueForApprovalAsync(
        TtsSpeakRequest request,
        string originalText,
        string spokenText,
        bool wasCensored,
        string voiceId,
        CancellationToken ct
    )
    {
        string provider = await ResolveProviderAsync(voiceId, ct);
        TtsApprovalQueueEntry entry = new()
        {
            Id = Guid.CreateVersion7(),
            BroadcasterId = request.BroadcasterId,
            RequestedByUserId = request.RequestedByUserId,
            RequestedByTwitchUserId = request.RequestedByTwitchUserId,
            RequestedByDisplayName = request.RequestedByDisplayName,
            OriginalText = originalText,
            CensoredText = wasCensored ? spokenText : null,
            VoiceId = voiceId,
            Provider = provider,
            Status = "pending",
            WasCensored = wasCensored,
            SourceMessageId = request.SourceMessageId,
            StreamId = request.StreamId,
            ExpiresAt = DateTime.UtcNow.AddMinutes(QueueTtlMinutes),
        };
        _db.TtsApprovalQueueEntries.Add(entry);
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new TtsUtteranceQueuedEvent
            {
                BroadcasterId = request.BroadcasterId,
                QueueEntryId = entry.Id,
                OriginalText = originalText,
                WasCensored = wasCensored,
                RequestedByTwitchUserId = request.RequestedByTwitchUserId,
            },
            ct
        );

        return Result.Success(
            new TtsDispatchOutcome(
                TtsDispatchDisposition.Queued,
                voiceId,
                provider,
                spokenText.Length,
                0,
                null
            )
        );
    }

    /// <summary>Synthesizes → stores → plays on the overlay → ledgers → emits dispatched. Shared by direct dispatch and approval.</summary>
    private async Task<Result<TtsDispatchOutcome>> SynthesizeStorePlayAsync(
        Guid broadcasterId,
        string text,
        string voiceId,
        string requestedByTwitchUserId,
        CancellationToken ct
    )
    {
        TtsResult synth;
        try
        {
            synth = await _tts.SynthesizeAsync(text, voiceId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TTS synthesis failed for channel {Channel}.", broadcasterId);
            return await PublishRejectAsync(
                broadcasterId,
                requestedByTwitchUserId,
                "synthesis_failed",
                "The TTS provider could not synthesize this utterance.",
                "SERVICE_UNAVAILABLE",
                ct
            );
        }

        if (synth.AudioData.Length == 0)
            return await PublishRejectAsync(
                broadcasterId,
                requestedByTwitchUserId,
                "synthesis_failed",
                "The TTS provider returned no audio.",
                "SERVICE_UNAVAILABLE",
                ct
            );

        string fileName = $"tts-{Guid.CreateVersion7():n}.mp3";
        using MemoryStream audio = new(synth.AudioData);
        Result<string> stored = await _audioStore.PutAsync(
            broadcasterId,
            fileName,
            audio,
            "audio/mpeg",
            ct
        );
        if (stored.IsFailure)
            return Result.Failure<TtsDispatchOutcome>(stored.ErrorMessage!, stored.ErrorCode!);

        Result<string> url = await _audioStore.GetPlaybackUrlAsync(stored.Value, ct);
        if (url.IsFailure)
            return Result.Failure<TtsDispatchOutcome>(url.ErrorMessage!, url.ErrorCode!);

        await _overlay.PlaySoundAsync(
            broadcasterId,
            new SoundPlaybackDto(Guid.Empty, url.Value, DefaultVolume, synth.DurationMs),
            ct
        );

        _db.TtsUsageRecords.Add(
            new TtsUsageRecord
            {
                BroadcasterId = broadcasterId,
                UserId = requestedByTwitchUserId,
                CharacterCount = text.Length,
                Provider = synth.Provider,
                VoiceId = synth.VoiceId,
            }
        );
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new TtsUtteranceDispatchedEvent
            {
                BroadcasterId = broadcasterId,
                Text = text,
                VoiceId = synth.VoiceId,
                Provider = synth.Provider,
                CharacterCount = text.Length,
                DurationMs = synth.DurationMs,
                RequestedByTwitchUserId = requestedByTwitchUserId,
            },
            ct
        );

        return Result.Success(
            new TtsDispatchOutcome(
                TtsDispatchDisposition.Dispatched,
                synth.VoiceId,
                synth.Provider,
                text.Length,
                synth.DurationMs,
                url.Value
            )
        );
    }

    /// <summary>Best-effort provider for a voice from the catalogue (informational for the queue entry).</summary>
    private async Task<string> ResolveProviderAsync(string voiceId, CancellationToken ct)
    {
        string? provider = await _db
            .TtsVoices.Where(v => v.Id == voiceId)
            .Select(v => v.Provider)
            .FirstOrDefaultAsync(ct);
        return provider ?? string.Empty;
    }

    /// <summary>Per-viewer voice → explicit override → channel default → first available.</summary>
    private async Task<string?> ResolveVoiceAsync(
        TtsSpeakRequest request,
        TtsConfigDto config,
        CancellationToken ct
    )
    {
        if (!string.IsNullOrWhiteSpace(request.VoiceIdOverride))
            return request.VoiceIdOverride;

        if (!string.IsNullOrWhiteSpace(request.RequestedByTwitchUserId))
        {
            string? userVoice = await _db
                .UserTtsVoices.Where(v =>
                    v.BroadcasterId == request.BroadcasterId
                    && v.UserId == request.RequestedByTwitchUserId
                )
                .Select(v => v.VoiceId)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(userVoice))
                return userVoice;
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultVoiceId))
            return config.DefaultVoiceId;

        IReadOnlyList<TtsVoiceInfo> voices = await _tts.GetAvailableVoicesAsync(ct);
        return voices.Count > 0 ? voices[0].Id : null;
    }

    private Task<Result<TtsDispatchOutcome>> RejectRequestAsync(
        TtsSpeakRequest request,
        string reason,
        string message,
        string errorCode,
        CancellationToken ct
    ) =>
        PublishRejectAsync(
            request.BroadcasterId,
            request.RequestedByTwitchUserId,
            reason,
            message,
            errorCode,
            ct
        );

    private async Task<Result<TtsDispatchOutcome>> PublishRejectAsync(
        Guid broadcasterId,
        string requestedByTwitchUserId,
        string reason,
        string message,
        string errorCode,
        CancellationToken ct
    )
    {
        await _eventBus.PublishAsync(
            new TtsUtteranceRejectedEvent
            {
                BroadcasterId = broadcasterId,
                Reason = reason,
                RequestedByTwitchUserId = requestedByTwitchUserId,
            },
            ct
        );
        return Result.Failure<TtsDispatchOutcome>(message, errorCode);
    }
}
