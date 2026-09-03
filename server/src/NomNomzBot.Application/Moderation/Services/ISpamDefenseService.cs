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
using NomNomzBot.Application.Moderation.Dtos;
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
/// <param name="Skeleton">
/// The normalized form the engine matched on. Carried out so correlation can group by it without
/// normalizing the same message a second time — and, more importantly, without the risk of the two
/// normalizations drifting and cohorts forming on a different string than detections matched.
/// </param>
/// <param name="Settings">The settings this verdict was reached under, so later layers agree with it.</param>
public sealed record SpamEvaluationResult(
    SpamDecision Decision,
    SpamConfidence Confidence,
    SpamTrustTier Tier,
    IReadOnlyList<ContentSignal> Signals,
    Guid? DetectionId,
    string Skeleton,
    SpamDefenseSettings Settings
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
    /// The full configuration surface: current values, the metadata to render an editor for them, and
    /// the guarantees that have no switch.
    /// </summary>
    Task<SpamDefensePolicyDto> GetPolicyAsync(Guid broadcasterId, CancellationToken ct = default);

    /// <summary>Recent verdicts, newest first — the review queue and the dry-run report.</summary>
    Task<IReadOnlyList<SpamDetectionDto>> GetDetectionsAsync(
        Guid broadcasterId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default
    );

    /// <summary>
    /// Mark a verdict wrong. This is the correction path, and the number that matters when judging
    /// whether the weights are set right — a channel with a rising overturn rate is one whose settings
    /// need loosening, and the operator should be able to see that.
    /// </summary>
    Task<Result> OverturnDetectionAsync(
        Guid broadcasterId,
        Guid detectionId,
        CancellationToken ct = default
    );

    /// <summary>Correlated cohorts, newest first — the Campaigns surface.</summary>
    Task<IReadOnlyList<SpamCampaignDto>> GetCampaignsAsync(
        Guid broadcasterId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default
    );

    /// <summary>Follow-bot blocks, newest first, each carrying its own evidence.</summary>
    Task<IReadOnlyList<FollowBotBlockDto>> GetFollowBotBlocksAsync(
        Guid broadcasterId,
        int page = 1,
        int pageSize = 25,
        CancellationToken ct = default
    );

    /// <summary>
    /// Restore an entire spike batch. Bulk by design: if a viral moment was misread the operator
    /// should not have to undo five hundred blocks one at a time.
    /// </summary>
    Task<Result<int>> RestoreFollowBotBatchAsync(
        Guid broadcasterId,
        Guid batchId,
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
