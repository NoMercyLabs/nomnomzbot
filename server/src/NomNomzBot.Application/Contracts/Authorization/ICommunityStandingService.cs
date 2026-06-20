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
}
