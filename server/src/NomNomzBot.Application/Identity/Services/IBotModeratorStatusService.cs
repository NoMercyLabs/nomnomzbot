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
