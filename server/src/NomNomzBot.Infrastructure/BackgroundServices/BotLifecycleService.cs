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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.BackgroundServices;

/// <summary>
/// Manages the bot's per-channel EventSub presence. On startup it subscribes to the per-channel topics for
/// every enabled, onboarded channel; chat is read via EventSub (<c>channel.chat.message</c>) and sent via the
/// Helix chat provider, so there is no IRC join — "active in a channel" is "subscribed to its topics".
/// Channels enabled/disabled at runtime are reconciled on a 5-minute poll: a newly-active channel is subscribed,
/// a no-longer-active channel has its subscriptions torn down.
/// </summary>
public sealed class BotLifecycleService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BotLifecycleService> _logger;

    // Channel state tracked locally to detect joins/leaves (keyed by the tenant channel Guid)
    private readonly HashSet<Guid> _joinedChannels = [];
    private readonly Lock _channelLock = new();

    // EventSub event types to subscribe to per channel
    private static readonly string[] ChannelEventTypes =
    [
        "channel.follow",
        "channel.subscribe",
        "channel.subscription.gift",
        "channel.cheer",
        "channel.raid",
        "channel.ban",
        "channel.channel_points_custom_reward_redemption.add",
        "channel.chat.message",
        "stream.online",
        "stream.offline",
    ];

    public BotLifecycleService(
        IServiceProvider serviceProvider,
        ILogger<BotLifecycleService> logger
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BotLifecycleService starting.");

        // Initial join on startup
        await SyncChannelsAsync(stoppingToken);

        // Periodic sync every 5 minutes to detect dynamic channel changes
        using PeriodicTimer timer = new(TimeSpan.FromMinutes(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await SyncChannelsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "BotLifecycleService: Error syncing channels");
            }
        }
    }

    private async Task SyncChannelsAsync(CancellationToken ct)
    {
        using IServiceScope scope = _serviceProvider.CreateScope();
        IApplicationDbContext db =
            scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        ITwitchEventSubService eventSub =
            scope.ServiceProvider.GetRequiredService<ITwitchEventSubService>();

        // Get all currently enabled, onboarded channels.
        var activeChannels = await db
            .Channels.Where(c => c.Enabled && c.IsOnboarded)
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        HashSet<Guid> activeIds = activeChannels.Select(c => c.Id).ToHashSet();

        HashSet<Guid> toSubscribe;
        HashSet<Guid> toUnsubscribe;

        lock (_channelLock)
        {
            toSubscribe = activeIds.Except(_joinedChannels).ToHashSet();
            toUnsubscribe = _joinedChannels.Except(activeIds).ToHashSet();
        }

        // Subscribe newly-active channels to their EventSub topics (chat is read via channel.chat.message).
        foreach (var channel in activeChannels.Where(c => toSubscribe.Contains(c.Id)))
        {
            try
            {
                // Declaratively reconcile this channel's EventSub subscription set to the desired topics.
                await eventSub.EnsureSubscribedAsync(channel.Id, ChannelEventTypes, ct);

                lock (_channelLock)
                    _joinedChannels.Add(channel.Id);
                _logger.LogInformation(
                    "BotLifecycleService: Subscribed channel #{ChannelName} ({Id})",
                    channel.Name,
                    channel.Id
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "BotLifecycleService: Failed to subscribe channel #{ChannelName}",
                    channel.Name
                );
            }
        }

        // Tear down subscriptions for channels that are no longer active.
        foreach (Guid channelId in toUnsubscribe)
        {
            try
            {
                await eventSub.UnsubscribeAllAsync(channelId, ct);

                lock (_channelLock)
                    _joinedChannels.Remove(channelId);
                _logger.LogInformation(
                    "BotLifecycleService: Unsubscribed channel {ChannelId}",
                    channelId
                );
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "BotLifecycleService: Failed to unsubscribe channel {ChannelId}",
                    channelId
                );
            }
        }
    }
}
