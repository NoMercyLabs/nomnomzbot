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
