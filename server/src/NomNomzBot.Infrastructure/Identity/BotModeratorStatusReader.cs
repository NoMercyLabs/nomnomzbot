// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Reads <c>Channel.BotIsModerator</c> for the bot that currently speaks in the channel. A status recorded
/// for a replaced bot is stale and reads as not observed.
/// </summary>
public sealed class BotModeratorStatusReader(
    IApplicationDbContext db,
    IChannelTwitchBotResolver bots
) : IBotModeratorStatusReader
{
    public async Task<BotModeratorReading> ReadAsync(
        Guid channelId,
        CancellationToken cancellationToken = default
    )
    {
        StatusRow? status = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => new StatusRow(
                c.BotIsModerator,
                c.BotModeratorStatusBotUserId,
                c.BotModeratorStatusChangedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);

        ChannelTwitchBot? bot = await bots.ResolveAsync(channelId, cancellationToken);
        if (bot is null)
            return new(BotModeratorStanding.NoDedicatedBot, null, null);

        if (
            status?.IsModerator is null
            || status.ChangedAt is null
            || !string.Equals(bot.TwitchUserId, status.BotUserId, StringComparison.Ordinal)
        )
            return new(BotModeratorStanding.NotObserved, bot, null);

        return new(
            status.IsModerator.Value
                ? BotModeratorStanding.Moderator
                : BotModeratorStanding.NotModerator,
            bot,
            status.ChangedAt
        );
    }

    private sealed record StatusRow(bool? IsModerator, string? BotUserId, DateTime? ChangedAt);
}
