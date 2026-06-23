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
/// The single unified authorization ladder spanning the community plane (A) and the channel-management
/// plane (B) (roles-permissions §0). The numeric <c>LevelValue</c> (via <see cref="AuthorizationLadder.ToLevelValue"/>)
/// is the only thing the per-action gate compares — the ordinal of this enum is not. Order, low→high:
/// <c>Everyone &lt; Subscriber &lt; Vip &lt; Artist &lt; Moderator &lt; LeadModerator &lt; Editor &lt; Broadcaster</c>.
/// </summary>
public enum PermissionLevel
{
    Everyone,
    Subscriber,
    Vip,
    Artist,
    Moderator,
    LeadModerator,
    Editor,
    Broadcaster,
}
