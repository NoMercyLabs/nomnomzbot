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
/// Translates <c>channel.subscribe</c> into <see cref="NewSubscriptionEvent"/>. Payload fields:
/// <c>user_id</c>, <c>user_name</c>, <c>tier</c> (<c>is_gift</c> is carried by the payload but the
/// new-subscription event does not surface it — gifts fan out through <c>channel.subscription.gift</c>).
/// </summary>
public sealed class ChannelSubscribeTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.subscribe";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        NewSubscriptionEvent subscribed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            Tier = payload.GetRequiredString("tier"),
        };

        return PublishAsync(subscribed, ct);
    }
}

/// <summary>
/// Translates <c>channel.subscription.message</c> into <see cref="ResubscriptionEvent"/>. Payload fields:
/// <c>user_id</c>, <c>user_name</c>, <c>tier</c>, <c>cumulative_months</c>, <c>streak_months</c> and the
/// nested <c>message.text</c> (the public resub message — null when the resubscriber sent none).
/// </summary>
public sealed class ChannelSubscriptionMessageTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.subscription.message";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        JsonElement? message = payload.GetObject("message");
        ResubscriptionEvent resubscribed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            Tier = payload.GetRequiredString("tier"),
            CumulativeMonths = payload.GetInt("cumulative_months"),
            StreakMonths = payload.GetInt("streak_months"),
            Message = message?.GetString("text"),
        };

        return PublishAsync(resubscribed, ct);
    }
}

/// <summary>
/// Translates <c>channel.subscription.gift</c> into <see cref="GiftSubscriptionEvent"/>. Payload fields:
/// <c>user_id</c>/<c>user_name</c> (the gifter — empty when <c>is_anonymous</c>), <c>tier</c>,
/// <c>total</c> (this drop's count), <c>is_anonymous</c>. Twitch does not enumerate the recipients on this
/// event, so <see cref="GiftSubscriptionEvent.Recipients"/> is empty — recipients arrive as their own
/// <c>channel.subscribe</c> (<c>is_gift = true</c>) notifications.
/// </summary>
public sealed class ChannelSubscriptionGiftTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.subscription.gift";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        GiftSubscriptionEvent gifted = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            GifterUserId = payload.GetRequiredString("user_id"),
            GifterDisplayName = payload.GetRequiredString("user_name"),
            Tier = payload.GetRequiredString("tier"),
            GiftCount = payload.GetInt("total"),
            IsAnonymous = payload.GetBool("is_anonymous"),
            Recipients = [],
        };

        return PublishAsync(gifted, ct);
    }
}

/// <summary>
/// Translates <c>channel.subscription.end</c> into <see cref="SubscriptionEndedEvent"/>. Payload fields:
/// <c>user_id</c>, <c>user_login</c>, <c>user_name</c>, <c>tier</c>, <c>is_gift</c>.
/// </summary>
public sealed class ChannelSubscriptionEndTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.subscription.end";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        SubscriptionEndedEvent ended = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            Tier = payload.GetRequiredString("tier"),
            IsGift = payload.GetBool("is_gift"),
        };

        return PublishAsync(ended, ct);
    }
}

/// <summary>
/// Translates <c>channel.cheer</c> into <see cref="CheerEvent"/>. Payload fields: <c>user_id</c>,
/// <c>user_name</c> (both absent on an anonymous cheer → empty strings), <c>bits</c>, <c>message</c>,
/// <c>is_anonymous</c>.
/// </summary>
public sealed class ChannelCheerTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.cheer";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        CheerEvent cheered = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            Bits = payload.GetInt("bits"),
            Message = payload.GetRequiredString("message"),
            IsAnonymous = payload.GetBool("is_anonymous"),
        };

        return PublishAsync(cheered, ct);
    }
}
