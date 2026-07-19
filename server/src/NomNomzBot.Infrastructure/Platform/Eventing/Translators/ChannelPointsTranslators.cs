// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Rewards.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Translates <c>channel.channel_points_custom_reward_redemption.add</c> into <see cref="RewardRedeemedEvent"/>.
/// Payload fields: <c>id</c> (the redemption id), <c>user_id</c>/<c>user_name</c>, <c>user_input</c>, and the
/// nested <c>reward</c> object (<c>id</c>, <c>title</c>, <c>cost</c>). An empty <c>user_input</c> degrades to
/// <c>null</c> so the event carries no spurious empty string.
/// </summary>
public sealed class ChannelPointsRewardRedemptionAddTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType =>
        "channel.channel_points_custom_reward_redemption.add";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? reward = payload.GetObject("reward");
        string userInput = payload.GetRequiredString("user_input");
        RewardRedeemedEvent redeemed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            RedemptionId = payload.GetRequiredString("id"),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserInput = userInput.Length == 0 ? null : userInput,
            RewardId = reward?.GetRequiredString("id") ?? string.Empty,
            RewardTitle = reward?.GetRequiredString("title") ?? string.Empty,
            Cost = reward?.GetInt("cost") ?? 0,
        };

        return PublishAsync(redeemed, ct);
    }
}

/// <summary>
/// Translates <c>channel.channel_points_custom_reward_redemption.update</c> into
/// <see cref="RewardRedemptionUpdatedEvent"/>. Same payload as the add, but <c>status</c> has transitioned to
/// <c>fulfilled</c> or <c>canceled</c> — the event surfaces that transition with the reward title and the
/// viewer it applied to.
/// </summary>
public sealed class ChannelPointsRewardRedemptionUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType =>
        "channel.channel_points_custom_reward_redemption.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? reward = payload.GetObject("reward");
        RewardRedemptionUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            RedemptionId = payload.GetRequiredString("id"),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            Status = payload.GetRequiredString("status"),
            RewardId = reward?.GetRequiredString("id") ?? string.Empty,
            RewardTitle = reward?.GetRequiredString("title") ?? string.Empty,
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>
/// Translates <c>channel.channel_points_custom_reward.add</c> into <see cref="RewardCreatedEvent"/>. The reward
/// fields sit at the top level of the event (not under a nested object): <c>id</c>, <c>title</c>, <c>cost</c>,
/// <c>is_enabled</c>, <c>is_paused</c>.
/// </summary>
public sealed class ChannelPointsRewardAddTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.channel_points_custom_reward.add";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        RewardCreatedEvent created = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            TwitchRewardId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Cost = payload.GetInt("cost"),
            IsEnabled = payload.GetBool("is_enabled"),
            IsPaused = payload.GetBool("is_paused"),
        };

        return PublishAsync(created, ct);
    }
}

/// <summary>
/// Translates <c>channel.channel_points_custom_reward.update</c> into <see cref="RewardUpdatedEvent"/>. Same
/// top-level reward shape as the add: <c>id</c>, <c>title</c>, <c>cost</c>, <c>is_enabled</c>, <c>is_paused</c>.
/// </summary>
public sealed class ChannelPointsRewardUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.channel_points_custom_reward.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        RewardUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            TwitchRewardId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
            Cost = payload.GetInt("cost"),
            IsEnabled = payload.GetBool("is_enabled"),
            IsPaused = payload.GetBool("is_paused"),
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>
/// Translates <c>channel.channel_points_custom_reward.remove</c> into <see cref="RewardRemovedEvent"/>. The
/// removed reward's <c>id</c> and <c>title</c> sit at the top level of the event.
/// </summary>
public sealed class ChannelPointsRewardRemoveTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.channel_points_custom_reward.remove";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        RewardRemovedEvent removed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            TwitchRewardId = payload.GetRequiredString("id"),
            Title = payload.GetRequiredString("title"),
        };

        return PublishAsync(removed, ct);
    }
}

/// <summary>
/// Translates <c>channel.channel_points_automatic_reward_redemption.add</c> (version 2) into
/// <see cref="AutomaticRewardRedeemedEvent"/>. Payload fields: <c>id</c>, <c>user_id</c>/<c>user_login</c>/
/// <c>user_name</c>, the nested <c>reward</c> (<c>type</c>, <c>channel_points</c>, nullable <c>emote.id</c>),
/// and the nested <c>message.text</c>. Both <c>emote</c> and <c>message</c> are situationally null, so each
/// degrades to <c>null</c> rather than throwing.
/// </summary>
public sealed class ChannelPointsAutomaticRewardRedemptionAddTranslator(
    IEventBus bus,
    TimeProvider clock
) : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType =>
        "channel.channel_points_automatic_reward_redemption.add";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? reward = payload.GetObject("reward");
        JsonElement? emote = reward?.GetObject("emote");
        JsonElement? message = payload.GetObject("message");
        AutomaticRewardRedeemedEvent redeemed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            RedemptionId = payload.GetRequiredString("id"),
            UserId = payload.GetRequiredString("user_id"),
            UserLogin = payload.GetRequiredString("user_login"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            RewardType = reward?.GetRequiredString("type") ?? string.Empty,
            Cost = reward?.GetInt("channel_points") ?? 0,
            UnlockedEmoteId = emote?.GetString("id"),
            Message = message?.GetString("text"),
        };

        return PublishAsync(redeemed, ct);
    }
}

/// <summary>
/// Translates <c>channel.custom_power_up_redemption.add</c> into <see cref="CustomPowerUpRedeemedEvent"/>.
/// Payload fields: <c>id</c>, <c>user_id</c>/<c>user_login</c>/<c>user_name</c>, <c>user_input</c>,
/// <c>status</c>, and the nested <c>custom_power_up</c> object (<c>id</c>, <c>title</c>, <c>bits</c>). A custom
/// Power-up is Bits-priced, so the cost comes off <c>custom_power_up.bits</c>, not a channel-point field.
/// </summary>
public sealed class ChannelCustomPowerUpRedemptionAddTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.custom_power_up_redemption.add";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? powerUp = payload.GetObject("custom_power_up");
        string userInput = payload.GetRequiredString("user_input");
        CustomPowerUpRedeemedEvent redeemed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            RedemptionId = payload.GetRequiredString("id"),
            UserId = payload.GetRequiredString("user_id"),
            UserLogin = payload.GetRequiredString("user_login"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            Status = payload.GetRequiredString("status"),
            UserInput = userInput.Length == 0 ? null : userInput,
            PowerUpId = powerUp?.GetRequiredString("id") ?? string.Empty,
            PowerUpTitle = powerUp?.GetRequiredString("title") ?? string.Empty,
            Bits = powerUp?.GetInt("bits") ?? 0,
        };

        return PublishAsync(redeemed, ct);
    }
}
