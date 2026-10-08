// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Application.Identity.Services;

/// <summary>Where the channel's speaking bot stands as a Twitch moderator, as last stored.</summary>
public enum BotModeratorStanding
{
    /// <summary>No dedicated bot speaks here: the streamer's own account does, and it needs no moderator role.</summary>
    NoDedicatedBot,

    /// <summary>A dedicated bot speaks here and no status was recorded for it (never observed, or recorded for a replaced bot).</summary>
    NotObserved,

    /// <summary>The bot that speaks here holds the moderator role.</summary>
    Moderator,

    /// <summary>The bot that speaks here does not hold the moderator role.</summary>
    NotModerator,
}

/// <summary>The stored moderator status of the bot that speaks in a channel.</summary>
/// <param name="Standing">Where the bot stands.</param>
/// <param name="Bot">The bot that speaks here, or null when none does.</param>
/// <param name="ChangedAt">When the status last changed value; null unless the status is observed.</param>
public sealed record BotModeratorReading(
    BotModeratorStanding Standing,
    ChannelTwitchBot? Bot,
    DateTime? ChangedAt
);

/// <summary>
/// The one read of the status <see cref="IBotModeratorStatusService"/> keeps on the channel row. The inbox item
/// and the protection status both read it here, so they cannot disagree about whether the bot is a moderator.
/// </summary>
public interface IBotModeratorStatusReader
{
    Task<BotModeratorReading> ReadAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    );
}
