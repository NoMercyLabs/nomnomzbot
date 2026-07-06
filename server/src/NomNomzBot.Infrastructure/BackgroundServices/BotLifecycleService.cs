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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Entities;

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

    // EventSub event types to subscribe to per channel. Internal (not private) so
    // BotLifecycleServiceTopicsTests can assert the desired-subscribe set directly — the InternalsVisibleTo
    // to NomNomzBot.Infrastructure.Tests is already wired for exactly this kind of transport-seam assertion.
    internal static readonly string[] ChannelEventTypes =
    [
        "channel.follow",
        "channel.subscribe",
        "channel.subscription.message",
        "channel.subscription.gift",
        "channel.cheer",
        "channel.raid",
        "channel.ban",
        "channel.channel_points_custom_reward_redemption.add",
        "channel.poll.begin",
        "channel.poll.end",
        "channel.prediction.begin",
        "channel.prediction.lock",
        "channel.prediction.end",
        "channel.hype_train.begin",
        "channel.hype_train.end",
        // Charity/Goals ingest (ROADMAP "Small decided items" — ingest-only, no manage endpoints exist
        // because Twitch offers none): scope-gated by channel:read:charity / channel:read:goals (AuthService
        // RequiredScopes) exactly like the channel:read:hype_train-gated hype-train topics above — a missing
        // scope 403s per-topic in TwitchEventSubHostedService.SubscribeAsync (logged + TwitchHelixReauthRequiredEvent)
        // without blocking any other topic's subscribe.
        "channel.charity_campaign.donate",
        "channel.charity_campaign.start",
        "channel.charity_campaign.progress",
        "channel.charity_campaign.stop",
        "channel.goal.begin",
        "channel.goal.progress",
        "channel.goal.end",
        "channel.chat.message",
        "stream.online",
        "stream.offline",
        // The remaining translator-backed surface (twitch-eventsub.md): every subscription type that already
        // has a live IEventSubEventTranslator but was never asked of Twitch. Each is scope-gated exactly like
        // the blocks above — a missing scope 403s that one topic (logged + TwitchHelixReauthRequiredEvent)
        // without blocking any other topic's subscribe.

        // Stream metadata.
        "channel.update",
        // Chat moderation surface (condition carries the bot's user_id — EventSubConditionBuilder.ChatReadEvents).
        "channel.chat.notification",
        "channel.chat.message_delete",
        "channel.chat.clear",
        "channel.chat.clear_user_messages",
        "channel.chat_settings.update",
        "channel.chat.user_message_hold",
        "channel.chat.user_message_update",
        // Progress ticks for the three engagement-tracker topics already subscribed by begin/end.
        "channel.hype_train.progress",
        "channel.poll.progress",
        "channel.prediction.progress",
        // Channel points: redemption lifecycle + reward CRUD + the automatic/custom-power-up redemption paths.
        "channel.channel_points_custom_reward_redemption.update",
        "channel.channel_points_custom_reward.add",
        "channel.channel_points_custom_reward.update",
        "channel.channel_points_custom_reward.remove",
        "channel.channel_points_automatic_reward_redemption.add",
        "channel.custom_power_up_redemption.add",
        // Moderation: unbans, unban requests, moderator/VIP roster changes, and the unified moderate action feed.
        "channel.moderate",
        "channel.unban",
        "channel.unban_request.create",
        "channel.unban_request.resolve",
        "channel.moderator.add",
        "channel.moderator.remove",
        "channel.vip.add",
        "channel.vip.remove",
        // Warnings, suspicious users (ban evasion / low-trust), and Shield Mode.
        "channel.warning.acknowledge",
        "channel.warning.send",
        "channel.suspicious_user.message",
        "channel.suspicious_user.update",
        "channel.shield_mode.begin",
        "channel.shield_mode.end",
        // Shoutouts (sent and received).
        "channel.shoutout.create",
        "channel.shoutout.receive",
        // Ad breaks, the unified Bits event, and the two user-plane (not channel-plane) topics.
        "channel.ad_break.begin",
        "channel.bits.use",
        "user.update",
        "user.whisper.message",
        // Shared Chat session lifecycle.
        "channel.shared_chat.begin",
        "channel.shared_chat.update",
        "channel.shared_chat.end",
        // Subscription lifecycle's missing leg (new/resub/gift were already covered above).
        "channel.subscription.end",
        // AutoMod.
        "automod.message.hold",
        "automod.message.update",
        "automod.settings.update",
        "automod.terms.update",
        // Guest Star (beta — still live per Twitch's EventSub subscription-types reference, not deprecated;
        // scope-gated by channel:read:guest_star / moderator:read:guest_star, same per-topic 403 degradation).
        "channel.guest_star_session.begin",
        "channel.guest_star_session.end",
        "channel.guest_star_guest.update",
        "channel.guest_star_settings.update",
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
        ITwitchStreamsApi streams = scope.ServiceProvider.GetRequiredService<ITwitchStreamsApi>();

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

        // Reconcile EventSub subscriptions for EVERY active channel each tick (chat is read via
        // channel.chat.message). EnsureSubscribedAsync is idempotent — for an already-joined channel it adopts
        // the live subs and only re-creates ones not yet `enabled`, so a channel stranded by a reconnect
        // self-heals within one tick without waiting for the next reconnect. Join-bookkeeping (mark joined +
        // bootstrap live status) still runs only for newly-active channels.
        foreach (var channel in activeChannels)
        {
            try
            {
                // Declaratively reconcile this channel's EventSub subscription set to the desired topics.
                await eventSub.EnsureSubscribedAsync(channel.Id, ChannelEventTypes, ct);

                if (!toSubscribe.Contains(channel.Id))
                    continue;

                lock (_channelLock)
                    _joinedChannels.Add(channel.Id);
                _logger.LogInformation(
                    "BotLifecycleService: Subscribed channel #{ChannelName} ({Id})",
                    channel.Name,
                    channel.Id
                );

                // Bootstrap live status from Helix — stream.online won't fire for a stream that is
                // already live when we first subscribe, so we must poll once to set the initial state.
                await BootstrapLiveStatusAsync(db, streams, channel.Id, ct);
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

    // Poll Helix once to get the channel's current stream state when we first subscribe. EventSub only
    // delivers stream.online for transitions — not for streams that are already live when we subscribe —
    // so the initial IsLive / title / game would otherwise stay stale until the streamer goes offline
    // and back online.
    private async Task BootstrapLiveStatusAsync(
        IApplicationDbContext db,
        ITwitchStreamsApi streams,
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        try
        {
            Channel? channel = await db.Channels.FindAsync([broadcasterId], ct);
            if (channel is null)
                return;

            Result<TwitchStream> result = await streams.GetStreamAsync(broadcasterId, ct);

            bool wasLive = channel.IsLive;
            channel.IsLive = result.IsSuccess;
            if (result.IsSuccess)
            {
                if (!string.IsNullOrEmpty(result.Value.Title))
                    channel.Title = result.Value.Title;
                if (!string.IsNullOrEmpty(result.Value.GameName))
                    channel.GameName = result.Value.GameName;
            }

            if (channel.IsLive != wasLive || result.IsSuccess)
                await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "BotLifecycleService: Bootstrapped live status for channel {BroadcasterId}: IsLive={IsLive}",
                broadcasterId,
                channel.IsLive
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "BotLifecycleService: Could not bootstrap live status for channel {BroadcasterId}",
                broadcasterId
            );
        }
    }
}
