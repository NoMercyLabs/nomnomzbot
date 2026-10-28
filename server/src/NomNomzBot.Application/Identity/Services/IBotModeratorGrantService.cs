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

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Gives the configured shared platform bot the moderator role again on a channel where it lost it. Granting is
/// the broadcaster's decision, so only the one bot identity pinned in configuration is ever granted, with the
/// broadcaster's own token, and never more often than the per-channel retry window allows.
/// </summary>
public interface IBotModeratorGrantService
{
    /// <summary>
    /// Tries to grant <paramref name="bot"/> moderator in the channel. The value is true when Twitch accepted the
    /// grant; false when the bot is not the configured shared bot, the retry window is still closed, or Twitch
    /// refused. A missing <c>channel:manage:moderators</c> scope is recorded as a scope gap, never as a sign-out.
    /// </summary>
    Task<Result<bool>> TryGrantAsync(
        Guid broadcasterId,
        ChannelTwitchBot bot,
        CancellationToken cancellationToken = default
    );
}
