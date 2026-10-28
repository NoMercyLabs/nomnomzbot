// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
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
