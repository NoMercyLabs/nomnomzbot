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
    Guid? StreamId
);

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
