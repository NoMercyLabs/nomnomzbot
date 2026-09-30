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

namespace NomNomzBot.Application.Identity.Services;

/// <summary>
/// Keeps <c>Channel.BotIsModerator</c> true to Twitch: whether the channel's dedicated bot holds the moderator
/// role. The action-required inbox reads it to warn the streamer when the bot is not a moderator. A change of
/// value pushes a live inbox refresh; re-observing the same value writes nothing.
/// </summary>
public interface IBotModeratorStatusService
{
    /// <summary>
    /// Applies a Twitch moderator role change (<c>channel.moderator.add</c> / <c>.remove</c>). Only a change
    /// naming the channel's own bot updates the status; any other user is ignored.
    /// </summary>
    Task<Result> ApplyRoleChangeAsync(
        Guid broadcasterId,
        string twitchUserId,
        bool isModerator,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Asks Twitch (Helix Get Moderators) whether the bot is a moderator and records the answer, so a missed
    /// EventSub event heals. A failed read is inconclusive: it returns the failure and leaves the status as is.
    /// </summary>
    Task<Result> ReconcileAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
}
