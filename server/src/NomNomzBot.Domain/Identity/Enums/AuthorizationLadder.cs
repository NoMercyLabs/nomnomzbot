// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Identity.Enums;

/// <summary>
/// The canonical mapping from each plane's role/standing enum onto the single numeric ladder the gates
/// compare (roles-permissions §0 — "the numeric <c>LevelValue</c> is the only thing compared"). The two
/// planes deliberately align on their shared rungs (a <c>Moderator</c> is <c>10</c> in every plane), so an
/// effective level can be taken as <c>MAX</c> across planes on one axis. Mappings are explicit, not enum
/// ordinals, and fail closed (an unrecognised value resolves to <c>0</c> / <c>Everyone</c>).
/// </summary>
public static class AuthorizationLadder
{
    /// <summary>The unified-ladder value of a <see cref="PermissionLevel"/> rung (0/2/4/6/10/20/30/40).</summary>
    public static int ToLevelValue(this PermissionLevel level) =>
        level switch
        {
            PermissionLevel.Everyone => 0,
            PermissionLevel.Subscriber => 2,
            PermissionLevel.Vip => 4,
            PermissionLevel.Artist => 6,
            PermissionLevel.Moderator => 10,
            PermissionLevel.LeadModerator => 20,
            PermissionLevel.Editor => 30,
            PermissionLevel.Broadcaster => 40,
            _ => 0,
        };

    /// <summary>The unified-ladder value of a Plane-B <see cref="ManagementRole"/> (10/20/30/40).</summary>
    public static int ToLevel(this ManagementRole role) =>
        role switch
        {
            ManagementRole.Moderator => 10,
            ManagementRole.LeadModerator => 20,
            ManagementRole.Editor => 30,
            ManagementRole.Broadcaster => 40,
            _ => 0,
        };

    /// <summary>The unified-ladder value of a Plane-A <see cref="CommunityStanding"/> (0/2/4/6/10).</summary>
    public static int ToLevel(this CommunityStanding standing) =>
        standing switch
        {
            CommunityStanding.Everyone => 0,
            CommunityStanding.Subscriber => 2,
            CommunityStanding.Vip => 4,
            CommunityStanding.Artist => 6,
            CommunityStanding.Moderator => 10,
            _ => 0,
        };

    /// <summary>
    /// The highest <see cref="PermissionLevel"/> rung AT OR BELOW a unified-ladder value — the inverse of
    /// <see cref="ToLevelValue"/> for resolved effective levels (which are always exact rung values, but an
    /// off-rung input still maps fail-closed to the rung it actually clears, never a higher one).
    /// </summary>
    public static PermissionLevel FromLevelValue(int levelValue) =>
        levelValue switch
        {
            >= 40 => PermissionLevel.Broadcaster,
            >= 30 => PermissionLevel.Editor,
            >= 20 => PermissionLevel.LeadModerator,
            >= 10 => PermissionLevel.Moderator,
            >= 6 => PermissionLevel.Artist,
            >= 4 => PermissionLevel.Vip,
            >= 2 => PermissionLevel.Subscriber,
            _ => PermissionLevel.Everyone,
        };
}
