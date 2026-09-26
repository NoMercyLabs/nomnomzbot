// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// The pure per-channel "effective required level" policy for a gateable action (roles-permissions §3.3):
/// <c>clamp(override ?? platformDefault ?? shippedDefault, floor, Broadcaster)</c>. Shared by
/// <see cref="RoleResolver"/>, <see cref="ActionAuthorizationService"/> and the admin default editor so the
/// formula lives in exactly one place. No I/O.
/// </summary>
internal static class ActionLevelPolicy
{
    /// <summary>Broadcaster (40) is the ceiling every override clamps to — no channel override can exceed it.</summary>
    private const int BroadcasterLevel = 40;

    /// <summary>
    /// The level a channel WITHOUT its own override follows: the platform admin's default when one is set,
    /// otherwise the shipped default — clamped to the floor so a later seed that raises the floor still wins.
    /// </summary>
    public static int DefaultLevel(ActionDefinition action) =>
        Math.Clamp(
            action.PlatformDefaultLevel ?? action.DefaultLevel,
            action.FloorLevel,
            BroadcasterLevel
        );

    /// <summary>
    /// The level a caller must reach on this channel to clear <paramref name="action"/>, given the channel's
    /// override for it (or <c>null</c> when unset): the desired level (<paramref name="overrideLevel"/> ?? the
    /// action's <see cref="DefaultLevel"/>), clamped to <c>[FloorLevel, Broadcaster]</c>.
    /// </summary>
    public static int EffectiveRequiredLevel(ActionDefinition action, int? overrideLevel) =>
        Math.Clamp(overrideLevel ?? DefaultLevel(action), action.FloorLevel, BroadcasterLevel);
}
