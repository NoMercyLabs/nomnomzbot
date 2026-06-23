// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Commands.Enums;

/// <summary>
/// The role a chat command requires, on the chat-visible rungs of the unified ladder. <c>SuperMod</c> is a Lead
/// Moderator (Twitch's <c>lead_moderator</c> badge). Editor is intentionally absent — it is a management/dashboard role
/// resolved from the Helix editors list, not a chat badge, so chat commands cannot gate on it.
/// </summary>
public enum CommandPermission
{
    Everyone,
    Subscriber,
    Vip,
    Moderator,
    SuperMod,
    Broadcaster,
}
