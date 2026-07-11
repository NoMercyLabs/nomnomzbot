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
/// pushes it to the overlay via the same audio bus the walk-in sounds use, and appends a truthful usage-ledger row.
/// A rejected request synthesizes nothing and charges nothing. The approval queue / BYOK / client-edge mode are
/// follow-on slices.
/// </summary>
public sealed class TtsDispatchService : ITtsDispatchService
{
    private const int DefaultVolume = 100;

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
            return await RejectAsync(
                request,
                "disabled",
                "TTS is disabled for this channel.",
                "FEATURE_DISABLED",
                ct
            );

        string text = request.Text?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return await RejectAsync(request, "empty", "Nothing to say.", "VALIDATION_FAILED", ct);

        int cap = config.MaxLength > 0 ? config.MaxLength : 500;
        if (text.Length > cap)
            return await RejectAsync(
                request,
                "too_long",
                $"That message is too long to read out ({text.Length}/{cap} characters).",
                "VALIDATION_FAILED",
                ct
            );

        // Opt-out light swear filter (§3.5): mask mild profanity before it is ever synthesized. If nothing survives
        // (e.g. the whole message was filtered away), reject rather than speak silence.
        if (config.ProfanityCensorEnabled)
        {
            TtsCensorResult censored = _censor.Censor(text);
            text = censored.Text;
            if (string.IsNullOrWhiteSpace(text))
                return await RejectAsync(
                    request,
                    "empty_after_censor",
                    "Nothing left to say after filtering.",
                    "VALIDATION_FAILED",
                    ct
                );
        }

        string? voiceId = await ResolveVoiceAsync(request, config, ct);
        if (string.IsNullOrWhiteSpace(voiceId))
            return await RejectAsync(
                request,
                "no_voice",
                "No TTS voice is available.",
                "VALIDATION_FAILED",
                ct
            );

        TtsResult synth;
        try
        {
            synth = await _tts.SynthesizeAsync(text, voiceId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "TTS synthesis failed for channel {Channel}.",
                request.BroadcasterId
            );
            return await RejectAsync(
                request,
                "synthesis_failed",
                "The TTS provider could not synthesize this utterance.",
                "SERVICE_UNAVAILABLE",
                ct
            );
        }

        if (synth.AudioData.Length == 0)
            return await RejectAsync(
                request,
                "synthesis_failed",
                "The TTS provider returned no audio.",
                "SERVICE_UNAVAILABLE",
                ct
            );

        string fileName = $"tts-{Guid.CreateVersion7():n}.mp3";
        using MemoryStream audio = new(synth.AudioData);
        Result<string> stored = await _audioStore.PutAsync(
            request.BroadcasterId,
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
            request.BroadcasterId,
            new SoundPlaybackDto(Guid.Empty, url.Value, DefaultVolume, synth.DurationMs),
            ct
        );

        _db.TtsUsageRecords.Add(
            new TtsUsageRecord
            {
                BroadcasterId = request.BroadcasterId,
                UserId = request.RequestedByTwitchUserId,
                CharacterCount = text.Length,
                Provider = synth.Provider,
                VoiceId = synth.VoiceId,
            }
        );
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishAsync(
            new TtsUtteranceDispatchedEvent
            {
                BroadcasterId = request.BroadcasterId,
                Text = text,
                VoiceId = synth.VoiceId,
                Provider = synth.Provider,
                CharacterCount = text.Length,
                DurationMs = synth.DurationMs,
                RequestedByTwitchUserId = request.RequestedByTwitchUserId,
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

    private async Task<Result<TtsDispatchOutcome>> RejectAsync(
        TtsSpeakRequest request,
        string reason,
        string message,
        string errorCode,
        CancellationToken ct
    )
    {
        await _eventBus.PublishAsync(
            new TtsUtteranceRejectedEvent
            {
                BroadcasterId = request.BroadcasterId,
                Reason = reason,
                RequestedByTwitchUserId = request.RequestedByTwitchUserId,
            },
            ct
        );
        return Result.Failure<TtsDispatchOutcome>(message, errorCode);
    }
}
