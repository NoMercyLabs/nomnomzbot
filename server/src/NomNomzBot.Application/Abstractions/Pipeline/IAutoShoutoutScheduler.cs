// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Abstractions.Pipeline;

/// <summary>
/// Gives a known NomNomzBot streamer an automatic shoutout after their first chat of a stream, with the old
/// bot's delays and cooldowns. Only a channel that turned <c>AutoShoutoutEnabled</c> on takes part. The
/// pending list lives in memory for one stream session.
/// </summary>
public interface IAutoShoutoutScheduler
{
    /// <summary>
    /// Records the chatter as pending when they are an enabled streamer, the channel has the setting on, and
    /// they are neither the broadcaster nor the bot. A chatter waits only once per stream.
    /// </summary>
    Task TryEnqueueAsync(
        Guid broadcasterId,
        string twitchUserId,
        string displayName,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Runs every pending shoutout that passed all of its gates and drops the ones inside the per-user cooldown.
    /// </summary>
    Task ProcessDueAsync(CancellationToken cancellationToken);
}
