// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Billing;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Domain.Tts.Events;
using NomNomzBot.Domain.Tts.Interfaces;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// The TTS utterance orchestrator (tts.md §3.4). Gates a request on the channel's enabled flag + character cap,
/// applies the opt-out light profanity censor (§3.5), resolves the voice (per-viewer → channel default → first
/// available), then synthesizes the audio server-side on every plane (<c>self_host</c>/<c>byok</c>; a stored
/// retired <c>client_edge</c> mode is treated as <c>self_host</c>), stores it through the shared sound-clip store, and
/// push it to the overlay's audio bus. Every plane appends a truthful usage-ledger row — unless the channel requires
/// moderator approval, in which case the utterance is held in the approval queue (P.1a) for a mod to approve or
/// reject. A rejected request synthesizes nothing and charges nothing.
/// </summary>
public sealed class TtsDispatchService : ITtsDispatchService
{
    private const int QueueTtlMinutes = 10;

    /// <summary>The most parts one segmented request may carry — a guard against a runaway script or flow.</summary>
    private const int MaxSegments = 20;

    private readonly ITtsService _tts;
    private readonly IByokTtsProviderFactory _byokProviders;
    private readonly ITtsConfigService _config;
    private readonly ITtsLexiconService _lexicon;
    private readonly ISpokenNameFormatter _spokenNames;
    private readonly ITtsProfanityCensor _censor;
    private readonly ISoundClipStore _audioStore;
    private readonly IApplicationDbContext _db;
    private readonly IEventBus _eventBus;
    private readonly IBillingTierService _tiers;
    private readonly IUsageMeteringService _usage;
    private readonly ITtsChannelSerializer _serializer;
    private readonly ITtsSceneReservation _sceneReservation;
    private readonly TimeProvider _clock;
    private readonly Random _random;
    private readonly ILogger<TtsDispatchService> _logger;
    private readonly SemaphoreSlim _preparationGate = new(1, 1);

    public TtsDispatchService(
        ITtsService tts,
        IByokTtsProviderFactory byokProviders,
        ITtsConfigService config,
        ITtsLexiconService lexicon,
        ISpokenNameFormatter spokenNames,
        ITtsProfanityCensor censor,
        ISoundClipStore audioStore,
        IApplicationDbContext db,
        IEventBus eventBus,
        IBillingTierService tiers,
        IUsageMeteringService usage,
        ITtsChannelSerializer serializer,
        ITtsSceneReservation sceneReservation,
        TimeProvider clock,
        ILogger<TtsDispatchService> logger,
        Random? random = null
    )
    {
        _tts = tts;
        _byokProviders = byokProviders;
        _config = config;
        _lexicon = lexicon;
        _spokenNames = spokenNames;
        _censor = censor;
        _audioStore = audioStore;
        _db = db;
        _eventBus = eventBus;
        _tiers = tiers;
        _usage = usage;
        _serializer = serializer;
        _sceneReservation = sceneReservation;
        _clock = clock;
        _logger = logger;
        _random = random ?? Random.Shared;
    }

    public Task<Result<TtsDispatchOutcome>> RequestSpeakAsync(
        TtsSpeakRequest request,
        CancellationToken ct = default
    ) => RequestSpeakCoreAsync(request, null, ct);

    public async Task<Result<TtsDispatchOutcome>> RequestPreparedSpeakAsync(
        TtsSpeakRequest request,
        TtsPreparedClip? prepared,
        CancellationToken ct = default
    )
    {
        await _preparationGate.WaitAsync(ct);
        try
        {
            return await RequestSpeakCoreAsync(request, prepared, ct);
        }
        finally
        {
            _preparationGate.Release();
        }
    }

    public async Task<TtsPreparation> PrepareSpeakAsync(
        TtsSpeakRequest request,
        CancellationToken ct = default
    )
    {
        request = WithEmotesSpoken(request);
        string text;
        string voiceId;
        IReadOnlyList<TtsSegment>? segments = null;
        TtsConfigDto config;
        ITtsProvider? byokProvider = null;

        // Planning touches scoped configuration, voice and lexicon stores. Finish that work before
        // another preparation uses the same DbContext; provider synthesis runs outside this gate.
        await _preparationGate.WaitAsync(ct);
        try
        {
            Result<TtsConfigDto> configResult = await _config.GetConfigAsync(
                request.BroadcasterId,
                ct
            );
            if (configResult.IsFailure)
                return TtsPreparation.Skipped;
            config = configResult.Value;
            if (config.Mode == "byok")
            {
                Result<ITtsProvider> provider = await _byokProviders.CreateForChannelAsync(
                    request.BroadcasterId,
                    config.DefaultProvider,
                    ct
                );
                if (provider.IsFailure)
                    return TtsPreparation.Skipped;
                byokProvider = provider.Value;
            }
            int cap = await ResolveCapAsync(request.BroadcasterId, config, ct);
            IReadOnlyList<string>? names = KnownNames(request);
            if (request.Segments is { Count: > 0 } parts)
            {
                if (parts.Count > MaxSegments)
                    return TtsPreparation.Skipped;
                List<TtsSegment> preparedSegments = [];
                foreach (TtsSpeakSegment part in parts)
                {
                    string partText = part.Text.Trim();
                    if (partText.Length == 0)
                        continue;
                    if (request.Speaker == TtsSpeaker.Bot || part.Speaker is not null)
                        partText = CutToCap(partText, cap);
                    if (partText.Length > cap)
                        return TtsPreparation.Skipped;
                    if (config.ProfanityCensorEnabled)
                        partText = _censor.Censor(partText).Text;
                    if (string.IsNullOrWhiteSpace(partText))
                        continue;
                    string? named = string.IsNullOrWhiteSpace(part.VoiceId)
                        ? request.VoiceIdOverride
                        : part.VoiceId;
                    string? partVoice = string.IsNullOrWhiteSpace(named)
                        ? await ResolveVoiceAsync(
                            request.BroadcasterId,
                            part.SpeakerTwitchUserId ?? request.RequestedByTwitchUserId,
                            null,
                            config,
                            false,
                            part.Speaker ?? request.Speaker,
                            ct
                        )
                        : named;
                    if (string.IsNullOrWhiteSpace(partVoice))
                        return TtsPreparation.Skipped;
                    partText = _spokenNames.ApplyToText(
                        await _lexicon.ApplyAsync(request.BroadcasterId, partText, ct),
                        names
                    );
                    preparedSegments.Add(
                        new(
                            partText,
                            partVoice,
                            part.RatePercent,
                            part.PitchPercent,
                            Math.Max(0, part.BreakAfterMs)
                        )
                    );
                }
                if (preparedSegments.Count == 0)
                    return TtsPreparation.Skipped;
                segments = preparedSegments;
                text = string.Join(' ', preparedSegments.Select(s => s.Text));
                voiceId = preparedSegments[0].VoiceId;
            }
            else
            {
                text = request.Text.Trim();
                if (request.Speaker == TtsSpeaker.Bot)
                    text = CutToCap(text, cap);
                if (text.Length == 0 || text.Length > cap)
                    return TtsPreparation.Skipped;
                if (config.ProfanityCensorEnabled)
                    text = _censor.Censor(text).Text;
                if (string.IsNullOrWhiteSpace(text))
                    return TtsPreparation.Skipped;
                string? resolvedVoice = await ResolveVoiceAsync(
                    request.BroadcasterId,
                    request.RequestedByTwitchUserId,
                    request.VoiceIdOverride,
                    config,
                    false,
                    request.Speaker,
                    ct
                );
                if (string.IsNullOrWhiteSpace(resolvedVoice))
                    return TtsPreparation.Skipped;
                voiceId = resolvedVoice;
                text = _spokenNames.ApplyToText(
                    await _lexicon.ApplyAsync(request.BroadcasterId, text, ct),
                    names
                );
            }
        }
        finally
        {
            _preparationGate.Release();
        }

        return new(
            SynthesizePreparedAsync(request, config, text, voiceId, segments, byokProvider, ct)
        );
    }

    private async Task<Result<TtsPreparedClip?>> SynthesizePreparedAsync(
        TtsSpeakRequest request,
        TtsConfigDto config,
        string text,
        string voiceId,
        IReadOnlyList<TtsSegment>? segments,
        ITtsProvider? byokProvider,
        CancellationToken ct
    )
    {
        long started = _clock.GetTimestamp();
        Result<TtsResult> audio = await SynthesizeAudioAsync(
            request.BroadcasterId,
            config,
            text,
            voiceId,
            request.RatePercent,
            request.PitchPercent,
            segments,
            ct,
            byokProvider
        );
        return audio.IsFailure
            ? Result.Failure<TtsPreparedClip?>(audio.ErrorMessage!, audio.ErrorCode!)
            : Result.Success<TtsPreparedClip?>(
                new(
                    request.BroadcasterId,
                    text,
                    voiceId,
                    config.Mode,
                    config.DefaultProvider,
                    request.RatePercent,
                    request.PitchPercent,
                    segments,
                    audio.Value,
                    ElapsedMs(started)
                )
            );
    }

    private async Task<Result<TtsDispatchOutcome>> RequestSpeakCoreAsync(
        TtsSpeakRequest request,
        TtsPreparedClip? prepared,
        CancellationToken ct
    )
    {
        request = WithEmotesSpoken(request);
        Result<TtsConfigDto> configResult = await _config.GetConfigAsync(request.BroadcasterId, ct);
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

        // Bits gate (P.1 MinBitsToTts): when set, only messages carrying at least that many bits are read.
        // A request paid elsewhere (a channel point redemption) skips this and the role floor below.
        if (
            !request.AlreadyPaid
            && config.MinBitsToTts is { } minBits
            && request.BitsAmount < minBits
        )
            return await RejectRequestAsync(
                request,
                "bits_gate",
                $"TTS on this channel needs at least {minBits} bits.",
                "VALIDATION_FAILED",
                ct
            );

        // Community-standing floor (MinPermission). It was stored, editable in the dashboard and read by
        // NOTHING — a channel set to "moderators only" was read out for every viewer, which is the opposite
        // of what the setting says. An unknown standing is treated as the lowest, never waved through.
        if (
            !request.AlreadyPaid
            && !MeetsStandingFloor(request.CommunityStanding, config.MinPermission)
        )
            return await RejectRequestAsync(
                request,
                "standing_gate",
                "TTS on this channel is limited to a higher role.",
                "FORBIDDEN",
                ct
            );

        if (await IsOnCooldownAsync(request, config.CooldownSeconds, ct))
            return await RejectRequestAsync(
                request,
                "cooldown",
                $"Wait {config.CooldownSeconds} seconds between TTS messages.",
                "RATE_LIMITED",
                ct
            );

        if (request.Segments is { Count: > 0 })
            return await RequestSegmentsAsync(request, config, ct, prepared);

        string text = request.Text.Trim();
        if (text.Length == 0)
            return await RejectRequestAsync(
                request,
                "empty",
                "Nothing to say.",
                "VALIDATION_FAILED",
                ct
            );

        int cap = await ResolveCapAsync(request.BroadcasterId, config, ct);
        if (request.Speaker == TtsSpeaker.Bot)
            text = CutToCap(text, cap);
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

        string? voiceId = await ResolveVoiceAsync(
            request.BroadcasterId,
            request.RequestedByTwitchUserId,
            request.VoiceIdOverride,
            config,
            request.AssignVoiceIfMissing,
            request.Speaker,
            ct
        );
        if (string.IsNullOrWhiteSpace(voiceId))
            return await RejectRequestAsync(
                request,
                "no_voice",
                "No TTS voice is available.",
                "VALIDATION_FAILED",
                ct
            );

        // A caller-supplied override (e.g. a pipeline step's templated `voice` field) is honored ONLY when it
        // names a real catalogue voice — matched case-insensitively, same as VoiceExistsAsync/SearchVoicesAsync
        // (tts.md §6.2). An unknown id fails honestly instead of silently falling back to a different voice and
        // reporting it as if the requested one spoke.
        if (
            !string.IsNullOrWhiteSpace(request.VoiceIdOverride)
            && !await VoiceExistsAsync(voiceId, ct)
        )
            return await RejectRequestAsync(
                request,
                "unknown_voice",
                $"TTS voice '{voiceId}' does not exist.",
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

        // A scripted sequence holding the channel's scene reservation (see ITtsSceneReservation) gets its
        // remaining lines played back to back; anything else waits for it to clear before even entering the
        // ordering gate below, so it can never insert its clip ahead of or between that sequence's own lines.
        await _sceneReservation.WaitForClearAsync(request.BroadcasterId, request.SceneToken, ct);

        // Serialized per channel so this utterance synthesizes/pushes to the overlay strictly in the
        // order it was requested — never in whatever order concurrent providers' network calls happen
        // to resolve (see ITtsChannelSerializer).
        await using IAsyncDisposable gate = await _serializer.AcquireAsync(
            request.BroadcasterId,
            ct
        );
        return await DispatchAsync(
            request.BroadcasterId,
            config,
            spokenText,
            voiceId,
            request.RequestedByTwitchUserId,
            wasCensored,
            wasModApproved: null,
            request.StreamId,
            request.ChannelEventId,
            request.RatePercent,
            request.PitchPercent,
            segments: null,
            KnownNames(request),
            ct,
            prepared
        );
    }

    /// <summary>
    /// The effective character cap: the STRICTER of the streamer's own setting and the plan's
    /// tts_max_characters tier limit (monetization-billing §3.3; -1 / self-host = no tier clamp).
    /// </summary>
    private Task<int> ResolveCapAsync(
        Guid broadcasterId,
        TtsConfigDto config,
        CancellationToken ct
    ) => TtsCharacterCap.ResolveAsync(broadcasterId, config, _tiers, ct);

    /// <summary>
    /// Cuts a bot-written line to the cap at the last word break, so a command reply that repeats a viewer's short
    /// argument is read out shortened instead of refused. A viewer's own words still get refused when too long.
    /// </summary>
    private static string CutToCap(string text, int cap)
    {
        if (text.Length <= cap)
            return text;

        string cut = text[..cap];
        int lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > 0 ? cut[..lastSpace] : cut).TrimEnd();
    }

    /// <summary>
    /// The segmented path of <see cref="RequestSpeakAsync"/>: every part passes the same cap, censor and voice
    /// checks as a single text, and one failing part rejects the whole request (never a half-spoken line). A part
    /// that is empty (before or after the censor) is dropped; if none is left the request is rejected. The
    /// approval queue and the client-edge widget hold ONE text and ONE voice, so on those two paths the parts
    /// play as their joined text in the first part's voice.
    /// </summary>
    private async Task<Result<TtsDispatchOutcome>> RequestSegmentsAsync(
        TtsSpeakRequest request,
        TtsConfigDto config,
        CancellationToken ct,
        TtsPreparedClip? prepared
    )
    {
        IReadOnlyList<TtsSpeakSegment> parts = request.Segments!;
        if (parts.Count > MaxSegments)
            return await RejectRequestAsync(
                request,
                "too_many_segments",
                $"A TTS request can carry at most {MaxSegments} segments.",
                "VALIDATION_FAILED",
                ct
            );

        int cap = await ResolveCapAsync(request.BroadcasterId, config, ct);
        List<TtsSegment> segments = [];
        List<string> originals = [];
        bool wasCensored = false;
        // The voice of a part that names none, kept per speaker so a line of bot and viewer parts resolves each once.
        Dictionary<(TtsSpeaker, string), string?> resolvedVoices = [];
        foreach (TtsSpeakSegment part in parts)
        {
            string text = part.Text.Trim();
            if (text.Length == 0)
                continue;
            if (request.Speaker == TtsSpeaker.Bot || part.Speaker is not null)
                text = CutToCap(text, cap);
            if (text.Length > cap)
                return await RejectRequestAsync(
                    request,
                    "too_long",
                    $"That message is too long to read out ({text.Length}/{cap} characters).",
                    "VALIDATION_FAILED",
                    ct
                );

            string spoken = text;
            if (config.ProfanityCensorEnabled)
            {
                TtsCensorResult censored = _censor.Censor(text);
                spoken = censored.Text;
                wasCensored |= censored.WasCensored;
                if (string.IsNullOrWhiteSpace(spoken))
                    continue;
            }

            string? named = string.IsNullOrWhiteSpace(part.VoiceId)
                ? request.VoiceIdOverride
                : part.VoiceId;
            string? voiceId = string.IsNullOrWhiteSpace(named) ? null : named;
            if (voiceId is null)
            {
                TtsSpeaker speaker = part.Speaker ?? request.Speaker;
                string owner = string.IsNullOrWhiteSpace(part.SpeakerTwitchUserId)
                    ? request.RequestedByTwitchUserId
                    : part.SpeakerTwitchUserId;
                if (!resolvedVoices.TryGetValue((speaker, owner), out voiceId))
                    resolvedVoices[(speaker, owner)] = voiceId = await ResolveVoiceAsync(
                        request.BroadcasterId,
                        owner,
                        null,
                        config,
                        request.AssignVoiceIfMissing,
                        speaker,
                        ct
                    );
            }
            if (string.IsNullOrWhiteSpace(voiceId))
                return await RejectRequestAsync(
                    request,
                    "no_voice",
                    "No TTS voice is available.",
                    "VALIDATION_FAILED",
                    ct
                );
            if (!string.IsNullOrWhiteSpace(named) && !await VoiceExistsAsync(voiceId, ct))
                return await RejectRequestAsync(
                    request,
                    "unknown_voice",
                    $"TTS voice '{voiceId}' does not exist.",
                    "VALIDATION_FAILED",
                    ct
                );

            originals.Add(text);
            segments.Add(
                new(
                    spoken,
                    voiceId,
                    part.RatePercent,
                    part.PitchPercent,
                    Math.Max(0, part.BreakAfterMs)
                )
            );
        }

        if (segments.Count == 0)
            return await RejectRequestAsync(
                request,
                "empty",
                "Nothing to say.",
                "VALIDATION_FAILED",
                ct
            );

        string spokenText = string.Join(' ', segments.Select(s => s.Text));
        if (config.ModApprovalRequired)
            return await EnqueueForApprovalAsync(
                request,
                string.Join(' ', originals),
                spokenText,
                wasCensored,
                segments[0].VoiceId,
                ct
            );

        await _sceneReservation.WaitForClearAsync(request.BroadcasterId, request.SceneToken, ct);

        await using IAsyncDisposable gate = await _serializer.AcquireAsync(
            request.BroadcasterId,
            ct
        );
        return await DispatchAsync(
            request.BroadcasterId,
            config,
            spokenText,
            segments[0].VoiceId,
            request.RequestedByTwitchUserId,
            wasCensored,
            wasModApproved: null,
            request.StreamId,
            request.ChannelEventId,
            ratePercent: null,
            pitchPercent: null,
            segments,
            KnownNames(request),
            ct,
            prepared
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
        Result<TtsConfigDto> configResult = await _config.GetConfigAsync(broadcasterId, ct);
        if (configResult.IsFailure)
            return Result.Failure(configResult.ErrorMessage!, configResult.ErrorCode!);

        string spokenText = entry.CensoredText ?? entry.OriginalText;
        await _sceneReservation.WaitForClearAsync(broadcasterId, callerToken: null, ct);
        await using IAsyncDisposable gate = await _serializer.AcquireAsync(broadcasterId, ct);
        // Approval-queue entries have no ChannelEventId of their own — a ModApprovalRequired channel only
        // ever queues chat-triggered utterances (a reward-pipeline play_tts speaks directly), so there is
        // nothing to correlate here. Likewise, the queue entry carries no rate/pitch override — those are a
        // per-call script flourish, never persisted onto the queued entry — so an approved utterance always
        // speaks at the provider's default prosody.
        Result<TtsDispatchOutcome> played = await DispatchAsync(
            broadcasterId,
            configResult.Value,
            spokenText,
            entry.VoiceId,
            entry.RequestedByTwitchUserId,
            entry.WasCensored,
            wasModApproved: true,
            entry.StreamId,
            channelEventId: null,
            ratePercent: null,
            pitchPercent: null,
            segments: null,
            entry.SpokenNames,
            ct
        );
        if (played.IsFailure)
            return played;

        entry.Status = "approved";
        entry.ReviewedByUserId = reviewedByUserId;
        entry.ReviewedAt = _clock.GetUtcNow().UtcDateTime;
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
        entry.ReviewedAt = _clock.GetUtcNow().UtcDateTime;
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

    /// <summary>The text (and every segment) with the emotes the caller named said as their names, not their prefixes.</summary>
    private TtsSpeakRequest WithEmotesSpoken(TtsSpeakRequest request)
    {
        if (request.Emotes is not { Codes.Count: > 0 } emotes)
            return request;

        string Spoken(string text) =>
            TtsEmoteSpokenNames.Rewrite(text, emotes.Codes, emotes.OwnerLogin, _spokenNames.Format);

        return request with
        {
            Text = Spoken(request.Text),
            Segments = request
                .Segments?.Select(segment => segment with { Text = Spoken(segment.Text) })
                .ToList(),
        };
    }

    /// <summary>Includes the requester even when a producer supplies no separate name list.</summary>
    private static List<string> KnownNames(TtsSpeakRequest request)
    {
        List<string> names = request.SpokenNames?.ToList() ?? [];
        if (!string.IsNullOrWhiteSpace(request.RequestedByDisplayName))
            names.Add(request.RequestedByDisplayName);
        return names;
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
            SpokenNames = KnownNames(request),
            ExpiresAt = _clock.GetUtcNow().UtcDateTime.AddMinutes(QueueTtlMinutes),
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

    private async Task<Result<TtsResult>> SynthesizeAudioAsync(
        Guid broadcasterId,
        TtsConfigDto config,
        string text,
        string voiceId,
        double? ratePercent,
        double? pitchPercent,
        IReadOnlyList<TtsSegment>? segments,
        CancellationToken ct,
        ITtsProvider? preparedProvider = null
    )
    {
        try
        {
            if (config.Mode == "byok")
            {
                ITtsProvider provider;
                if (preparedProvider is not null)
                    provider = preparedProvider;
                else
                {
                    Result<ITtsProvider> created = await _byokProviders.CreateForChannelAsync(
                        broadcasterId,
                        config.DefaultProvider,
                        ct
                    );
                    if (created.IsFailure)
                        return Result.Failure<TtsResult>(
                            $"The channel's {config.DefaultProvider} key is not usable: {created.ErrorMessage}",
                            "BYOK_UNAVAILABLE"
                        );
                    provider = created.Value;
                }
                TtsSynthesisResult result = segments is null
                    ? await provider.SynthesizeAsync(text, voiceId, ratePercent, pitchPercent, ct)
                    : await provider.SynthesizeSegmentsAsync(segments, ct);
                return Result.Success(
                    new TtsResult(
                        result.AudioData,
                        result.DurationMs,
                        result.VoiceId,
                        result.Provider
                    )
                );
            }

            TtsResult audio = segments is null
                ? await _tts.SynthesizeAsync(text, voiceId, ratePercent, pitchPercent, ct)
                : await _tts.SynthesizeSegmentsAsync(segments, ct);
            return Result.Success(audio);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TTS synthesis failed for channel {Channel}.", broadcasterId);
            return Result.Failure<TtsResult>(
                "The TTS provider could not synthesize this utterance.",
                "SERVICE_UNAVAILABLE"
            );
        }
    }

    /// <summary>
    /// Routes a passing utterance to server-side synthesis: the audio always comes from the server, never the browser.
    /// Shared by direct dispatch and post-approval so both planes obey the channel's <c>Mode</c>.
    /// Right before either leg, the channel's pronunciation lexicon rewrites the utterance (usernames and
    /// message content alike) — so what is synthesized/pushed is always the lexicon-applied text. The spoken-name
    /// cleaning runs right after it, so a name the override already rewrote is never touched twice.
    /// </summary>
    private async Task<Result<TtsDispatchOutcome>> DispatchAsync(
        Guid broadcasterId,
        TtsConfigDto config,
        string text,
        string voiceId,
        string requestedByTwitchUserId,
        bool wasCensored,
        bool? wasModApproved,
        Guid? streamId,
        string? channelEventId,
        double? ratePercent,
        double? pitchPercent,
        IReadOnlyList<TtsSegment>? segments,
        IReadOnlyList<string>? spokenNames,
        CancellationToken ct,
        TtsPreparedClip? prepared = null
    )
    {
        if (segments is not null)
        {
            List<TtsSegment> lexed = [];
            foreach (TtsSegment segment in segments)
                lexed.Add(
                    segment with
                    {
                        Text = _spokenNames.ApplyToText(
                            await _lexicon.ApplyAsync(broadcasterId, segment.Text, ct),
                            spokenNames
                        ),
                    }
                );
            segments = lexed;
            text = string.Join(' ', lexed.Select(s => s.Text));
        }
        else
        {
            text = _spokenNames.ApplyToText(
                await _lexicon.ApplyAsync(broadcasterId, text, ct),
                spokenNames
            );
        }

        return await (
            SynthesizeStorePlayAsync(
                broadcasterId,
                config,
                text,
                voiceId,
                requestedByTwitchUserId,
                wasCensored,
                wasModApproved,
                streamId,
                channelEventId,
                ratePercent,
                pitchPercent,
                segments,
                ct,
                prepared
            )
        );
    }

    /// <summary>Synthesizes → stores → plays on the overlay → ledgers → emits dispatched. Shared by direct dispatch and approval.</summary>
    private async Task<Result<TtsDispatchOutcome>> SynthesizeStorePlayAsync(
        Guid broadcasterId,
        TtsConfigDto config,
        string text,
        string voiceId,
        string requestedByTwitchUserId,
        bool wasCensored,
        bool? wasModApproved,
        Guid? streamId,
        string? channelEventId,
        double? ratePercent,
        double? pitchPercent,
        IReadOnlyList<TtsSegment>? segments,
        CancellationToken ct,
        TtsPreparedClip? prepared
    )
    {
        // The plan's monthly total counts the shared voices only; a byok channel pays its own provider.
        bool metered = config.Mode != "byok";
        if (metered && !await TtsMonthlyAllowance.FitsAsync(_usage, broadcasterId, text.Length, ct))
            return await PublishRejectAsync(
                broadcasterId,
                requestedByTwitchUserId,
                "monthly_limit",
                "This channel has used its TTS characters for this month.",
                "QUOTA_EXCEEDED",
                ct
            );

        TtsResult synth;
        long synthesisStartedAt = _clock.GetTimestamp();
        bool matchesPreparation =
            prepared is not null
            && prepared.BroadcasterId == broadcasterId
            && prepared.Text == text
            && prepared.VoiceId == voiceId
            && prepared.Mode == config.Mode
            && prepared.DefaultProvider == config.DefaultProvider
            && prepared.RatePercent == ratePercent
            && prepared.PitchPercent == pitchPercent
            && (
                (segments is null && prepared.Segments is null)
                || (
                    segments is not null
                    && prepared.Segments is not null
                    && segments.SequenceEqual(prepared.Segments)
                )
            );
        if (matchesPreparation)
        {
            synth = prepared!.Audio;
        }
        else
        {
            Result<TtsResult> synthesized = await SynthesizeAudioAsync(
                broadcasterId,
                config,
                text,
                voiceId,
                ratePercent,
                pitchPercent,
                segments,
                ct
            );
            if (synthesized.IsSuccess)
                synth = synthesized.Value;
            else
            {
                if (synthesized.ErrorCode != "BYOK_UNAVAILABLE")
                    await RecordFailedClipAsync(
                        NewLedgerRow(
                            broadcasterId,
                            requestedByTwitchUserId,
                            config.DefaultProvider,
                            voiceId,
                            text,
                            wasCensored,
                            wasModApproved,
                            streamId,
                            synthesisMs: ElapsedMs(synthesisStartedAt),
                            failed: true
                        )
                    );
                return await PublishRejectAsync(
                    broadcasterId,
                    requestedByTwitchUserId,
                    synthesized.ErrorCode == "BYOK_UNAVAILABLE"
                        ? "byok_unavailable"
                        : "synthesis_failed",
                    synthesized.ErrorMessage
                        ?? "The TTS provider could not synthesize this utterance.",
                    "SERVICE_UNAVAILABLE",
                    ct
                );
            }
        }

        int synthesisMs = matchesPreparation
            ? prepared!.SynthesisMs
            : ElapsedMs(synthesisStartedAt);
        if (synth.AudioData.Length == 0)
        {
            await RecordFailedClipAsync(
                NewLedgerRow(
                    broadcasterId,
                    requestedByTwitchUserId,
                    synth.Provider,
                    synth.VoiceId,
                    text,
                    wasCensored,
                    wasModApproved,
                    streamId,
                    synthesisMs,
                    failed: true
                )
            );
            return await PublishRejectAsync(
                broadcasterId,
                requestedByTwitchUserId,
                "synthesis_failed",
                "The TTS provider returned no audio.",
                "SERVICE_UNAVAILABLE",
                ct
            );
        }

        string fileName = $"tts-{Guid.CreateVersion7():n}.mp3";
        using (MemoryStream audio = new(synth.AudioData))
        {
            // Stored for the usage/audit trail (moderation review of what was said) — playback itself does NOT
            // depend on this or on any server-hosted URL (see below), so a store failure here never blocks speech.
            await _audioStore.PutAsync(broadcasterId, fileName, audio, "audio/mpeg", ct);
        }

        // Reverted to the inline data: URI (2026-08-27) after the URL-based delivery 404'd live: the storage
        // key is "{broadcasterId:N}/{fileName}", GetPlaybackUrlAsync Uri.EscapeDataString's it whole (encoding
        // the '/' as %2F), and ASP.NET Core's routing deliberately never decodes %2F back into a path segment
        // for the `stream/{*storageKey}` catch-all — the server was asked to open a file literally named with
        // %2F in it and 404'd every time. That is a real, fixable bug in the URL path, but the data: URI has
        // no such failure mode (no host/scheme/route resolution at all, and CSP already allows it) — it is
        // also how the legacy bot delivered speech (NoMercyBot.TTSService audioBase64). Whether a data: URI
        // can itself be corrupted/truncated in transit was a theory, never confirmed with a captured payload;
        // do not re-decide this again without hard evidence either way.
        string dataUri = $"data:audio/mpeg;base64,{Convert.ToBase64String(synth.AudioData)}";

        TtsUsageRecord usage = NewLedgerRow(
            broadcasterId,
            requestedByTwitchUserId,
            synth.Provider,
            synth.VoiceId,
            text,
            wasCensored,
            wasModApproved,
            streamId,
            synthesisMs,
            failed: false
        );
        _db.TtsUsageRecords.Add(usage);
        await _db.SaveChangesAsync(ct);
        if (metered)
            await TtsMonthlyAllowance.RecordAsync(_usage, broadcasterId, text.Length, ct);

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
                DispatchMode = config.Mode == "byok" ? "byok" : "self_host",
                ContentHash = null,
                AudioUrl = dataUri,
                ChannelEventId = channelEventId,
                UsageRecordId = usage.Id,
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
                dataUri
            )
        );
    }

    // Case-insensitive catalogue lookup — mirrors TtsConfigService.VoiceExistsAsync (tts.md §6.2): "en-US-AriaNeural"
    // and "en-us-arianeural" are the same voice. Used to honestly reject a caller-supplied VoiceIdOverride that
    // doesn't name a real voice, instead of silently dispatching under a voice id nobody actually spoke as.
    private async Task<bool> VoiceExistsAsync(string voiceId, CancellationToken ct)
    {
        string idLower = voiceId.ToLower();
        if (await _db.TtsVoices.AnyAsync(v => v.Id.ToLower() == idLower, ct))
            return true;
        if (await _db.TtsVoices.AnyAsync(ct))
            return false; // catalogue present but no match
        IReadOnlyList<TtsVoiceInfo> providerVoices = await _tts.GetAvailableVoicesAsync(ct);
        return providerVoices.Any(v =>
            string.Equals(v.Id, voiceId, StringComparison.OrdinalIgnoreCase)
        );
    }

    /// <summary>Best-effort provider for a voice from the catalogue (informational for the queue entry).</summary>
    private async Task<string> ResolveProviderAsync(string voiceId, CancellationToken ct)
    {
        string idLower = voiceId.ToLower();
        string? provider = await _db
            .TtsVoices.Where(v => v.Id.ToLower() == idLower)
            .Select(v => v.Provider)
            .FirstOrDefaultAsync(ct);
        return provider ?? string.Empty;
    }

    private int ElapsedMs(long startedAt) =>
        (int)Math.Min(_clock.GetElapsedTime(startedAt).TotalMilliseconds, int.MaxValue);

    private TtsUsageRecord NewLedgerRow(
        Guid broadcasterId,
        string requestedByTwitchUserId,
        string provider,
        string voiceId,
        string text,
        bool wasCensored,
        bool? wasModApproved,
        Guid? streamId,
        int synthesisMs,
        bool failed
    ) =>
        new()
        {
            BroadcasterId = broadcasterId,
            UserId = requestedByTwitchUserId,
            CharacterCount = text.Length,
            Provider = provider,
            VoiceId = voiceId,
            Text = text,
            WasCensored = wasCensored,
            WasModApproved = wasModApproved,
            StreamId = streamId,
            SynthesisMs = synthesisMs,
            Failed = failed,
            OccurredAt = _clock.GetUtcNow().UtcDateTime,
        };

    /// <summary>
    /// Ledgers a clip that never played, with the time spent until it failed, so the admin diagnostics can show
    /// slow and failing providers. It is saved with <see cref="CancellationToken.None"/>: a timeout cancels the
    /// request token, and that is exactly the failure this row has to survive.
    /// </summary>
    private async Task RecordFailedClipAsync(TtsUsageRecord failedClip)
    {
        _db.TtsUsageRecords.Add(failedClip);
        await _db.SaveChangesAsync(CancellationToken.None);
    }

    /// <summary>
    /// True when a viewer's own TTS request lands inside the channel's cooldown after that viewer's last spoken
    /// message. The ledger (<see cref="TtsUsageRecord"/>) is the clock, so the window survives a restart and needs
    /// no extra state. Bot speech, a paid request (<see cref="TtsSpeakRequest.AlreadyPaid"/>), an unknown
    /// requester and a cooldown of 0 are never throttled.
    /// </summary>
    private async Task<bool> IsOnCooldownAsync(
        TtsSpeakRequest request,
        int cooldownSeconds,
        CancellationToken ct
    )
    {
        if (
            request.Speaker != TtsSpeaker.Viewer
            || request.AlreadyPaid
            || cooldownSeconds <= 0
            || string.IsNullOrWhiteSpace(request.RequestedByTwitchUserId)
        )
            return false;

        DateTime windowStart = _clock.GetUtcNow().UtcDateTime.AddSeconds(-cooldownSeconds);
        return await _db.TtsUsageRecords.AnyAsync(
            u =>
                u.BroadcasterId == request.BroadcasterId
                && u.UserId == request.RequestedByTwitchUserId
                && !u.Failed
                && u.OccurredAt > windowStart,
            ct
        );
    }

    /// <summary>Per-viewer voice → explicit override → channel default → first available.</summary>
    /// <summary>
    /// Ranks a caller's chat standing against the channel's <c>MinPermission</c> floor. Both vocabularies are
    /// mapped onto one ladder: the floor speaks in groups (<c>subscribers</c>, <c>moderators</c>) while the
    /// caller arrives as a single <see cref="ChatRole"/> token (<c>subscriber</c>, <c>lead_moderator</c>, …),
    /// and every token above the floor clears it.
    /// </summary>
    private static bool MeetsStandingFloor(string? standing, string? floor) =>
        StandingRank(standing) >= FloorRank(floor);

    private static int StandingRank(string? standing) =>
        (standing ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "broadcaster" => 5,
            "editor" or "lead_moderator" or "moderator" => 4,
            "artist" or "vip" => 3,
            "subscriber" => 2,
            _ => 1,
        };

    private static int FloorRank(string? floor) =>
        (floor ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "broadcaster" => 5,
            "moderators" or "moderator" => 4,
            "vip" => 3,
            "subscribers" or "subscriber" => 2,
            _ => 1,
        };

    public async Task<string?> ResolveVoiceAsync(
        Guid broadcasterId,
        string requestedByTwitchUserId,
        string? voiceIdOverride,
        TtsSpeaker speaker = TtsSpeaker.Bot,
        CancellationToken ct = default
    )
    {
        Result<TtsConfigDto> configResult = await _config.GetConfigAsync(broadcasterId, ct);
        if (configResult.IsFailure)
            return null;

        string? voiceId = await ResolveVoiceAsync(
            broadcasterId,
            requestedByTwitchUserId,
            voiceIdOverride,
            configResult.Value,
            assignVoiceIfMissing: false,
            speaker,
            ct
        );
        if (string.IsNullOrWhiteSpace(voiceId))
            return null;
        if (!string.IsNullOrWhiteSpace(voiceIdOverride) && !await VoiceExistsAsync(voiceId, ct))
            return null;
        return voiceId;
    }

    private async Task<string?> ResolveVoiceAsync(
        Guid broadcasterId,
        string requestedByTwitchUserId,
        string? voiceIdOverride,
        TtsConfigDto config,
        bool assignVoiceIfMissing,
        TtsSpeaker speaker,
        CancellationToken ct
    )
    {
        if (!string.IsNullOrWhiteSpace(voiceIdOverride))
            return voiceIdOverride;

        if (speaker == TtsSpeaker.Viewer && !string.IsNullOrWhiteSpace(requestedByTwitchUserId))
        {
            string? userVoice = await _db
                .UserTtsVoices.Where(v =>
                    v.BroadcasterId == broadcasterId && v.UserId == requestedByTwitchUserId
                )
                .Select(v => v.VoiceId)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(userVoice))
                return userVoice;

            if (assignVoiceIfMissing)
            {
                string? assigned = await AssignFirstVoiceAsync(
                    broadcasterId,
                    requestedByTwitchUserId,
                    config.DefaultProvider,
                    ct
                );
                if (!string.IsNullOrWhiteSpace(assigned))
                    return assigned;
            }
        }

        if (!string.IsNullOrWhiteSpace(config.DefaultVoiceId))
            return config.DefaultVoiceId;

        IReadOnlyList<TtsVoiceInfo> voices = await _tts.GetAvailableVoicesAsync(ct);
        return voices.Count > 0 ? voices[0].Id : null;
    }

    /// <summary>
    /// A viewer with no saved voice gets a random English or multilingual voice of the channel's provider,
    /// preferring one that fits their pronouns (<see cref="TtsFirstVoicePicker"/>), and it is saved as theirs
    /// so every later line sounds the same (the old bot did this). Null when the catalogue holds no eligible
    /// voice for the provider: the channel default speaks and nothing is saved. When two lines race, the
    /// unique (channel, viewer) index lets one row win and both lines use the winner.
    /// </summary>
    private async Task<string?> AssignFirstVoiceAsync(
        Guid broadcasterId,
        string requestedByTwitchUserId,
        string provider,
        CancellationToken ct
    )
    {
        string providerLower = provider.ToLower();
        List<TtsVoice> catalogue = await _db
            .TtsVoices.Where(v => v.Provider.ToLower() == providerLower)
            .ToListAsync(ct);
        User? viewer = await _db
            .Users.Include(u => u.Pronoun)
            .Include(u => u.AltPronoun)
            .FirstOrDefaultAsync(u => u.TwitchUserId == requestedByTwitchUserId, ct);

        string? chosen = TtsFirstVoicePicker.Pick(
            catalogue,
            viewer?.Pronoun,
            viewer?.AltPronoun,
            _random
        );
        if (chosen is null)
            return null;

        UserTtsVoice row = new()
        {
            BroadcasterId = broadcasterId,
            UserId = requestedByTwitchUserId,
            VoiceId = chosen,
        };
        _db.UserTtsVoices.Add(row);
        try
        {
            await _db.SaveChangesAsync(ct);
            return chosen;
        }
        catch (DbUpdateException)
        {
            _db.UserTtsVoices.Remove(row);
            return await _db
                .UserTtsVoices.Where(v =>
                    v.BroadcasterId == broadcasterId && v.UserId == requestedByTwitchUserId
                )
                .Select(v => v.VoiceId)
                .FirstOrDefaultAsync(ct);
        }
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
