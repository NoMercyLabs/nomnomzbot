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
