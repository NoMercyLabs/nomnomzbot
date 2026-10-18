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
