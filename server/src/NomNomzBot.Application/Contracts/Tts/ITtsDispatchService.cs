// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Contracts.Tts;

/// <summary>
/// The TTS utterance orchestrator (tts.md §3.4): gate → censor → queue-or-speak → dispatch → ledger. On the
/// self-host leg a passing request is synthesized and pushed to the overlay to play and a usage-ledger row is
/// appended — unless the channel runs with <c>ModApprovalRequired</c>, in which case the (already-censored)
/// utterance is held in the approval queue for a moderator to approve or reject. BYOK and the client-edge mode
/// land in follow-on slices.
/// </summary>
public interface ITtsDispatchService
{
    /// <summary>
    /// Full utterance flow for one TTS request (self-host leg). Loads the channel's TTS config; a disabled channel,
    /// an over-cap or empty text, or a channel with no resolvable voice is rejected (a <c>TtsUtteranceRejectedEvent</c>
    /// fires and the result is a failure — nothing synthesized or charged). The opt-out profanity censor masks the
    /// text; if the channel requires moderator approval the utterance is queued
    /// (<see cref="TtsDispatchDisposition.Queued"/>, <c>TtsUtteranceQueuedEvent</c>), otherwise the voice is resolved
    /// (per-viewer → channel default → first available), the audio synthesized, stored, pushed to the overlay to
    /// play, a <c>TtsUsageRecord</c> appended, <c>TtsUtteranceDispatchedEvent</c> emitted, and
    /// <see cref="TtsDispatchDisposition.Dispatched"/> returned.
    /// </summary>
    Task<Result<TtsDispatchOutcome>> RequestSpeakAsync(
        TtsSpeakRequest request,
        CancellationToken ct = default
    );

    /// <summary>
    /// The voice <see cref="RequestSpeakAsync"/> would speak with, synthesizing nothing: the override when it names a
    /// catalogue voice, else (for a <see cref="TtsSpeaker.Viewer"/> line) the viewer's assigned voice, the channel default, then the first available voice. Null
    /// when a live request would be refused for its voice (unknown override, or no voice at all). A script test run
    /// uses it so the preview returns what a live run returns.
    /// </summary>
    Task<string?> ResolveVoiceAsync(
        Guid broadcasterId,
        string requestedByTwitchUserId,
        string? voiceIdOverride,
        TtsSpeaker speaker = TtsSpeaker.Bot,
        CancellationToken ct = default
    );

    /// <summary>
    /// A moderator approves a pending queue entry: synthesizes the (censored) text, plays it on the overlay, appends
    /// the usage-ledger row, marks the entry <c>approved</c>, and emits <c>TtsUtteranceReviewedEvent</c> (approved) +
    /// <c>TtsUtteranceDispatchedEvent</c>. <c>NOT_FOUND</c> when there is no pending entry with that id; a synthesis
    /// failure leaves the entry pending (the moderator can retry).
    /// </summary>
    Task<Result> ApproveAsync(
        Guid broadcasterId,
        Guid queueEntryId,
        Guid reviewedByUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// A moderator rejects a pending queue entry: marks it <c>rejected</c> and emits <c>TtsUtteranceReviewedEvent</c>
    /// (rejected). Nothing is synthesized or played. <c>NOT_FOUND</c> when there is no pending entry with that id.
    /// </summary>
    Task<Result> RejectAsync(
        Guid broadcasterId,
        Guid queueEntryId,
        Guid reviewedByUserId,
        CancellationToken ct = default
    );

    /// <summary>Lists the channel's pending approval-queue entries for the moderator UI, newest-first, paged.</summary>
    Task<Result<PagedList<TtsQueueEntryDto>>> GetPendingQueueAsync(
        Guid broadcasterId,
        int page,
        int pageSize,
        CancellationToken ct = default
    );
}

/// <summary>A pending TTS utterance awaiting moderator review (tts.md P.1a), shaped for the moderator queue UI.</summary>
public sealed record TtsQueueEntryDto(
    Guid Id,
    string RequestedByTwitchUserId,
    string? RequestedByDisplayName,
    string OriginalText,
    string? CensoredText,
    string VoiceId,
    bool WasCensored,
    string Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    string? SourceMessageId
);

/// <summary>One TTS utterance request (tts.md §3.4). The caller resolves the requester + community standing.</summary>
/// <param name="ChannelEventId">
/// The activity-feed row id (a <c>DomainEventBase.EventId</c>) of the PAID channel event whose pipeline
/// action chain triggered this utterance (e.g. a reward redemption) — carried through to
/// <c>TtsUtteranceDispatchedEvent</c> so the Replay capture can correlate the <c>tts_speak</c> push back to
/// that event. <c>null</c> for a standalone chat-triggered utterance (a free <c>!tts</c> command never logs
/// a ChannelEvent, so there is genuinely nothing to correlate).
/// </param>
/// <param name="RatePercent">
/// Per-utterance SSML speaking-rate override (e.g. <c>-25</c> for 25% slower), matching SSML's
/// <c>rate='+N%'</c> convention. <c>null</c> means the provider's default rate — today's unchanged behavior.
/// Never persisted against the channel's TTS config; a one-off flourish for a single call (e.g. a script's
/// "evil wizard" voice), clamped server-side by the provider before reaching SSML.
/// </param>
/// <param name="PitchPercent">
/// Per-utterance SSML pitch override (e.g. <c>-20</c> for a lower pitch), matching SSML's
/// <c>pitch='+N%'</c> convention. <c>null</c> means the provider's default pitch. See <see cref="RatePercent"/>.
/// </param>
/// <param name="AssignVoiceIfMissing">
/// When the speaking viewer has no saved voice, pick a random English catalogue voice and save it as theirs,
/// like the old bot. <c>false</c> keeps the channel default voice and saves nothing; used for the
/// broadcaster's own lines.
/// </param>
/// <param name="Speaker">
/// WHOSE words these are, and so whose voice reads them. <see cref="TtsSpeaker.Bot"/> (the default) is any text the
/// bot wrote itself (a command reply, an announcement, an event response): it speaks in the channel's voice and
/// never touches the requester's saved voice. <see cref="TtsSpeaker.Viewer"/> is the requester's own words (a TTS
/// reward, their chat line): it speaks in the requester's saved voice. The requester id stays on the request for
/// attribution, limits and history either way. An explicit <see cref="VoiceIdOverride"/> wins over both.
/// </param>
/// <param name="Segments">
/// An ordered list of parts that play as ONE audio clip, each with its own voice, prosody and trailing silence
/// (e.g. a bot intro in one voice, a pause, then the viewer's words in theirs). When set it replaces
/// <see cref="Text"/>/<see cref="VoiceIdOverride"/>/<see cref="RatePercent"/>/<see cref="PitchPercent"/>; a part
/// naming no voice speaks in <see cref="VoiceIdOverride"/> or the usual resolved voice. The length cap, the
/// censor and the voice check run on every part. <c>null</c> or empty keeps the single-text behavior.
/// </param>
/// <param name="SpokenNames">
/// Names the caller knows sit in the text (a shoutout target, an event actor, the redeeming viewer, a quote author).
/// Each is cleaned for the voice after the channel's pronunciation override ("xX_D4rk_Xx" is read "Dark"); the
/// shown chat text is never changed. Every <c>@mention</c> in the text is cleaned too, with no list needed.
/// </param>
public sealed record TtsSpeakRequest(
    Guid BroadcasterId,
    Guid RequestedByUserId,
    string RequestedByTwitchUserId,
    string RequestedByDisplayName,
    string Text,
    string? VoiceIdOverride,
    int BitsAmount,
    string CommunityStanding,
    string? SourceMessageId,
    Guid? StreamId,
    string? ChannelEventId = null,
    double? RatePercent = null,
    double? PitchPercent = null,
    bool AssignVoiceIfMissing = true,
    IReadOnlyList<TtsSpeakSegment>? Segments = null,
    TtsSpeaker Speaker = TtsSpeaker.Bot,
    IReadOnlyList<string>? SpokenNames = null
);

/// <summary>One part of a segmented <see cref="TtsSpeakRequest"/> (see <see cref="TtsSpeakRequest.Segments"/>).</summary>
/// <param name="Text">The words to speak; checked against the same cap and censor as a single text.</param>
/// <param name="VoiceId">The voice for this part; <c>null</c> uses the request's own voice.</param>
/// <param name="RatePercent">Optional speaking-rate override (percent, clamped by the provider).</param>
/// <param name="PitchPercent">Optional pitch override (percent, clamped by the provider).</param>
/// <param name="BreakAfterMs">Silence after this part in milliseconds; <c>0</c> none. Ignored after the last part.</param>
public sealed record TtsSpeakSegment(
    string Text,
    string? VoiceId = null,
    double? RatePercent = null,
    double? PitchPercent = null,
    int BreakAfterMs = 0
);

/// <summary>Whose words a TTS line carries; picks the voice (see <see cref="TtsSpeakRequest.Speaker"/>).</summary>
public enum TtsSpeaker
{
    /// <summary>Text the bot wrote: the channel's voice.</summary>
    Bot,

    /// <summary>The requester's own words: the requester's saved voice.</summary>
    Viewer,
}

public enum TtsDispatchDisposition
{
    Dispatched,
    Queued,
}

/// <summary>The result of a dispatch: what happened, plus the play URL when it was dispatched.</summary>
public sealed record TtsDispatchOutcome(
    TtsDispatchDisposition Disposition,
    string VoiceId,
    string Provider,
    int CharacterCount,
    int DurationMs,
    string? PlaybackUrl
);
