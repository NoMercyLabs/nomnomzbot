// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Application.Contracts.Authorization;

/// <summary>
/// The management membership a user's OWN login would hold on a channel but an act-as session must not write.
/// A user's own channel list grants the Moderator role on every onboarded channel they moderate on Twitch (a
/// persisted <c>TwitchBadge</c> row). While an operator acts as that user nothing may be persisted, so the row
/// is computed per request instead — the act-as answer then equals the user's own login without a write.
/// </summary>
public interface IActAsMembershipOverlay
{
    /// <summary>
    /// The role the user's own login would have been granted on <paramref name="broadcasterId"/>, or null when
    /// the current request is not acting as <paramref name="userId"/> or that login would grant nothing. Callers
    /// ask only when the user holds no membership row there.
    /// </summary>
    Task<ManagementRole?> ResolveAsync(
        Guid userId,
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    );
}
