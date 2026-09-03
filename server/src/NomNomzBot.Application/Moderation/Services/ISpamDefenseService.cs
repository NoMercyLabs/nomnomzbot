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
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>One message to evaluate, in the terms the engine needs rather than the wire's.</summary>
/// <param name="BroadcasterId">Tenant.</param>
/// <param name="Provider">Platform the message arrived on.</param>
/// <param name="MessageId">Platform message id, so a verdict can be acted on or reversed later.</param>
/// <param name="PlatformUserId">Platform-native sender id.</param>
/// <param name="DisplayName">Sender's display name at the time.</param>
/// <param name="Message">Raw text as sent.</param>
/// <param name="IsBroadcaster">From the message's own badges.</param>
/// <param name="IsModerator">From the message's own badges.</param>
/// <param name="IsVip">From the message's own badges.</param>
/// <param name="IsSubscriber">From the message's own badges.</param>
public sealed record SpamEvaluationRequest(
    Guid BroadcasterId,
    string Provider,
    string MessageId,
    string PlatformUserId,
    string DisplayName,
    string Message,
    bool IsBroadcaster,
    bool IsModerator,
    bool IsVip,
    bool IsSubscriber
);

/// <summary>A verdict, with everything a moderator needs to understand it (SD7).</summary>
/// <param name="Decision">What happens, what would have happened, and why.</param>
/// <param name="Confidence">How sure the content layer was.</param>
/// <param name="Tier">The trust tier the sender held at evaluation time.</param>
/// <param name="Signals">Which content signals fired.</param>
/// <param name="DetectionId">The recorded row, when one was written.</param>
public sealed record SpamEvaluationResult(
    SpamDecision Decision,
    SpamConfidence Confidence,
    SpamTrustTier Tier,
    IReadOnlyList<ContentSignal> Signals,
    Guid? DetectionId
);

/// <summary>
/// The spam-defence stack as one callable seam (spam-defense.md §L0–§L5).
///
/// <para>Everything the engine decides goes through here, so there is exactly one place where a
/// message becomes a verdict — and exactly one place to look when asking why somebody was actioned.
/// The layers themselves are pure functions in the Domain; this is what gives them a channel's
/// settings, the sender's history, and somewhere to write the record.</para>
/// </summary>
public interface ISpamDefenseService
{
    /// <summary>This channel's settings, or the shipped defaults when it has never been configured.</summary>
    Task<SpamDefenseSettings> GetSettingsAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Save edited settings after server-side range validation.</summary>
    Task<Result<SpamDefenseSettings>> UpdateSettingsAsync(
        Guid broadcasterId,
        SpamDefenseSettings settings,
        CancellationToken ct = default
    );

    /// <summary>
    /// Evaluate one message and record the verdict. In dry run — the shipped default — this records
    /// and returns what WOULD have happened, and does nothing else.
    /// </summary>
    Task<SpamEvaluationResult?> EvaluateAsync(
        SpamEvaluationRequest request,
        CancellationToken ct = default
    );
}
