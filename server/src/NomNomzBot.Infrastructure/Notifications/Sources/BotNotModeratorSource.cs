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
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Application.Notifications.Services;

namespace NomNomzBot.Infrastructure.Notifications.Sources;

/// <summary>
/// The channel's dedicated bot is not a Twitch moderator there, as last observed from Twitch
/// (<c>Channel.BotIsModerator</c>, kept true by <see cref="IBotModeratorStatusService"/>). Reported only for the
/// bot that currently speaks in the channel: a status observed for a replaced bot is stale and says nothing. The
/// key embeds when the status last changed, so a bot that is de-modded again after a dismissal surfaces again.
/// </summary>
public sealed class BotNotModeratorSource(IApplicationDbContext db, IChannelTwitchBotResolver bots)
    : IActionRequiredSource
{
    private const string KeyPrefix = "bot-not-moderator:";

    public IReadOnlyCollection<string> KeyPrefixes { get; } = [KeyPrefix];

    // The status service pushes the inbox change itself whenever the status changes value.
    public IReadOnlyCollection<string> InvalidatingEventTypes { get; } = [];

    public async Task<Result<List<ActionRequiredItemDto>>> GetItemsAsync(
        Guid channelId,
        IReadOnlySet<string> dismissedKeys,
        CancellationToken cancellationToken = default
    )
    {
        StatusRow? status = await db
            .Channels.AsNoTracking()
            .Where(c => c.Id == channelId && c.BotIsModerator == false)
            .Select(c => new StatusRow(
                c.BotModeratorStatusBotUserId,
                c.BotModeratorStatusChangedAt
            ))
            .FirstOrDefaultAsync(cancellationToken);
        if (status?.BotUserId is null || status.ChangedAt is null)
            return Result.Success<List<ActionRequiredItemDto>>([]);

        ChannelTwitchBot? bot = await bots.ResolveAsync(channelId, cancellationToken);
        if (
            bot is null
            || !string.Equals(bot.TwitchUserId, status.BotUserId, StringComparison.Ordinal)
        )
            return Result.Success<List<ActionRequiredItemDto>>([]);

        string key = $"{KeyPrefix}{channelId}:{status.ChangedAt.Value.Ticks}";
        if (dismissedKeys.Contains(key))
            return Result.Success<List<ActionRequiredItemDto>>([]);

        ActionRequiredItemDto item = new(
            Id: key,
            Kind: "bot_not_moderator",
            Severity: "warning",
            TitleKey: "attention_bot_not_moderator_title",
            MessageKey: "attention_bot_not_moderator_message",
            Parameters: new() { ["botName"] = bot.Username },
            DetectedAt: status.ChangedAt.Value,
            DeepLinkRoute: "moderation",
            SourceUserId: bot.TwitchUserId,
            SourceUserName: bot.Username,
            Count: 1,
            QueueItemIds: []
        );
        return Result.Success<List<ActionRequiredItemDto>>([item]);
    }

    public Task<Result<List<string>>> ResolveDismissalKeysAsync(
        Guid channelId,
        string itemId,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Result.Success<List<string>>([itemId]));

    private sealed record StatusRow(string? BotUserId, DateTime? ChangedAt);
}
