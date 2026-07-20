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
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// Plane-A community-standing writes (roles-permissions §3.5) — a viewer's standing (Subscriber / VIP / Artist)
/// sourced from chat tags or an EventSub badge. Hot-path friendly: a single upsert per observed message.
/// </summary>
public interface ICommunityStandingService
{
    /// <summary>
    /// Upserts a viewer's community standing (recomputes <c>LevelValue</c>, stamps <c>LastSeenAt</c>). Emits
    /// <c>CommunityStandingChangedEvent</c> only when the standing actually changes.
    /// </summary>
    Task<Result> UpsertStandingAsync(
        Guid broadcasterId,
        Guid userId,
        CommunityStanding standing,
        StandingSource source,
        string? subTier,
        CancellationToken cancellationToken = default
    );

    /// <summary>The viewer's current standing (<c>Everyone</c> when none is recorded).</summary>
    Task<Result<CommunityStanding>> GetStandingAsync(
        Guid broadcasterId,
        Guid userId,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Reconciles a channel's Plane-A standings against a freshly-read Twitch subscriber + VIP
    /// <paramref name="snapshot"/> (roles-permissions §3.5): raises every reported sub/VIP to their Twitch standing
    /// and, on a FULLY authoritative read (both signals complete), downgrades a Helix-seeded sub/VIP who no longer
    /// appears. Prune-safe — a partial/failed read only raises, never lowers — and it only manages Helix-seeded
    /// Subscriber/Vip rows, so a manually-set Artist or a Moderator standing is never clobbered. The sibling of
    /// <c>IMembershipService.SyncManagementFromTwitchAsync</c> for Plane A.
    /// </summary>
    Task<Result> ReconcileTwitchStandingsAsync(
        Guid broadcasterId,
        CommunityStandingSnapshot snapshot,
        CancellationToken cancellationToken = default
    );
}
