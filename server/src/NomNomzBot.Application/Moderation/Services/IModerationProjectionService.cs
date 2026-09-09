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

namespace NomNomzBot.Application.Moderation.Services;

/// <summary>
/// The J.4/J.5 moderation projections (moderation.md §3.8) — called by the moderation EVENT HANDLERS,
/// never by controllers. <c>ApplyActionAsync</c> is the incremental path (history rollup + heat accrual +
/// trust recompute in one pass); <c>RebuildAsync</c> is the admin/maintenance full rebuild from the
/// recorded actions. A subject with no local <c>User</c> row is skipped — the projection keys on the
/// resolved user, and every chatter gets a row through normal chat activity.
/// </summary>
public interface IModerationProjectionService
{
    /// <summary>
    /// Projects one moderation action: bumps the J.4 rollup for <paramref name="actionType"/>
    /// (<c>ban</c> | <c>unban</c> | <c>timeout</c> | <c>warn</c> | <c>delete_message</c> |
    /// <c>automod_denied</c> | <c>filter_hit</c> | <c>report_validated</c>), accrues its heat delta
    /// (ban +40, timeout +15, automod/filter +5, validated report +10; half-life 24 h), recomputes the
    /// J.5 trust score, fires <c>UserHeatThresholdCrossedEvent</c> on an UPWARD crossing of the channel's
    /// configured threshold, and appends one row to the queryable <c>ModerationHistoryEntry</c> log (owner
    /// punch list 2026-09-08 §3) — this is the single choke point every ban/timeout/unban/warn already
    /// funnels through regardless of source (bot-issued, Twitch-side, Kick, legacy import), so it is also
    /// the single place the per-action history log is written from.
    /// </summary>
    Task<Result> ApplyActionAsync(
        Guid broadcasterId,
        string subjectTwitchUserId,
        string actionType,
        DateTime occurredAtUtc,
        string? moderatorTwitchUserId = null,
        string? moderatorDisplayName = null,
        string? reason = null,
        int? durationSeconds = null,
        CancellationToken ct = default
    );

    /// <summary>Recomputes the J.5 trust score for one subject from the current J.4 rollup (heat untouched).</summary>
    Task<Result> RecomputeTrustAsync(
        Guid broadcasterId,
        Guid subjectUserId,
        CancellationToken ct = default
    );

    /// <summary>
    /// Full rebuild of both projections for a channel from the recorded <c>moderation_action</c> rows.
    /// Heat is transient (decayed) state and resets to zero — the rollup and trust are the durable truth.
    /// </summary>
    Task<Result> RebuildAsync(Guid broadcasterId, CancellationToken ct = default);
}
