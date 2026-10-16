// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Identity.Dtos;

/// <summary>
/// The role a person signs in as. Each role asks the platform for only the scopes that role uses in the bot.
/// </summary>
public enum TwitchLoginRole
{
    /// <summary>Proves identity only; reads the person's own data.</summary>
    Viewer,

    /// <summary>Moderates channels from the dashboard on the person's own token.</summary>
    Moderator,

    /// <summary>Owns a channel and runs the bot in it.</summary>
    Broadcaster,
}
