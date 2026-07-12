// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Domain.Tts.Events;

// DomainEventBase is a class, so these are sealed CLASSES. BroadcasterId (the tenant) is inherited.

/// <summary>
/// A TTS utterance was synthesized and dispatched to the overlay to play (tts.md §2/§3.4). Carries the spoken
/// text + the resolved voice/provider + character count for the activity feed and the usage ledger's consumers.
/// </summary>
public sealed class TtsUtteranceDispatchedEvent : DomainEventBase
{
    public required string Text { get; init; }
    public required string VoiceId { get; init; }
    public required string Provider { get; init; }
    public required int CharacterCount { get; init; }
    public required int DurationMs { get; init; }

    /// <summary>The requesting viewer's raw platform id (empty for a system/timer-triggered utterance).</summary>
    public required string RequestedByTwitchUserId { get; init; }
}

/// <summary>
/// A TTS request was refused before any audio played (tts.md §3.4) — the channel has TTS disabled, the text
/// exceeded the character cap, or it was empty. Truthful: nothing was synthesized or charged.
/// </summary>
public sealed class TtsUtteranceRejectedEvent : DomainEventBase
{
    /// <summary><c>disabled</c> | <c>too_long</c> | <c>empty</c> | <c>empty_after_censor</c> | <c>no_voice</c> | <c>synthesis_failed</c>.</summary>
    public required string Reason { get; init; }

    public required string RequestedByTwitchUserId { get; init; }
}

/// <summary>
/// A TTS utterance was held for moderator approval instead of being spoken immediately (tts.md P.1a) — the channel
/// runs with <c>ModApprovalRequired</c>. Carries the queue entry id and the raw text for the activity feed / a live
/// moderator-queue push.
/// </summary>
public sealed class TtsUtteranceQueuedEvent : DomainEventBase
{
    public required Guid QueueEntryId { get; init; }
    public required string OriginalText { get; init; }
    public required bool WasCensored { get; init; }
    public required string RequestedByTwitchUserId { get; init; }
}

/// <summary>
/// A moderator approved or rejected a queued TTS utterance (tts.md P.1a). On approval the utterance is then
/// synthesized and dispatched (a separate <see cref="TtsUtteranceDispatchedEvent"/> fires); on rejection nothing
/// is spoken.
/// </summary>
public sealed class TtsUtteranceReviewedEvent : DomainEventBase
{
    public required Guid QueueEntryId { get; init; }
    public required Guid ReviewedByUserId { get; init; }

    /// <summary><c>approved</c> | <c>rejected</c>.</summary>
    public required string Decision { get; init; }
}
