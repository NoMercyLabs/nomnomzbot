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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Identity.Entities;

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// Records whether the channel's dedicated bot is a Twitch moderator, from the two truthful signals Twitch gives:
/// a moderator role change naming the bot, and a Helix Get Moderators read. The status is stored on the channel
/// with the bot it was observed for, and stamped only when its value changes, so the inbox item it drives keeps
/// one identity per occurrence.
/// </summary>
public sealed class BotModeratorStatusService(
    IApplicationDbContext db,
    IChannelTwitchBotResolver bots,
    ITwitchModeratorsApi moderators,
    IActionRequiredChangeNotifier inbox,
    TimeProvider clock
) : IBotModeratorStatusService
{
    public async Task<Result> ApplyRoleChangeAsync(
        Guid broadcasterId,
        string twitchUserId,
        bool isModerator,
        CancellationToken cancellationToken = default
    )
    {
        ChannelTwitchBot? bot = await bots.ResolveAsync(broadcasterId, cancellationToken);
        if (bot is null || !string.Equals(bot.TwitchUserId, twitchUserId, StringComparison.Ordinal))
            return Result.Success();

        return await RecordAsync(broadcasterId, bot.TwitchUserId, isModerator, cancellationToken);
    }

    public async Task<Result> ReconcileAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Channel? channel = await db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterId,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId.ToString());

        ChannelTwitchBot? bot = await bots.ResolveAsync(broadcasterId, cancellationToken);
        if (bot is null || channel.TwitchChannelId is null)
            return await RecordAsync(channel, null, null, cancellationToken);

        // The streamer's own account acting as the bot is the broadcaster: it outranks a moderator.
        if (string.Equals(bot.TwitchUserId, channel.TwitchChannelId, StringComparison.Ordinal))
            return await RecordAsync(channel, bot.TwitchUserId, true, cancellationToken);

        Result<TwitchPage<TwitchModerator>> read = await moderators.GetModeratorsByUserIdAsync(
            broadcasterId,
            [bot.TwitchUserId],
            cancellationToken
        );
        if (read.IsFailure)
            return read;

        bool isModerator = read.Value.Items.Any(m =>
            string.Equals(m.UserId, bot.TwitchUserId, StringComparison.Ordinal)
        );
        return await RecordAsync(channel, bot.TwitchUserId, isModerator, cancellationToken);
    }

    private async Task<Result> RecordAsync(
        Guid broadcasterId,
        string botUserId,
        bool isModerator,
        CancellationToken cancellationToken
    )
    {
        Channel? channel = await db.Channels.FirstOrDefaultAsync(
            c => c.Id == broadcasterId,
            cancellationToken
        );
        if (channel is null)
            return Errors.ChannelNotFound(broadcasterId.ToString());

        return await RecordAsync(channel, botUserId, isModerator, cancellationToken);
    }

    private async Task<Result> RecordAsync(
        Channel channel,
        string? botUserId,
        bool? isModerator,
        CancellationToken cancellationToken
    )
    {
        if (
            channel.BotIsModerator == isModerator
            && string.Equals(
                channel.BotModeratorStatusBotUserId,
                botUserId,
                StringComparison.Ordinal
            )
        )
            return Result.Success();

        channel.BotIsModerator = isModerator;
        channel.BotModeratorStatusBotUserId = botUserId;
        channel.BotModeratorStatusChangedAt = isModerator is null
            ? null
            : clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(cancellationToken);

        inbox.NotifyChanged(channel.Id);
        return Result.Success();
    }
}
