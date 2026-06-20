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
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Translates <c>channel.raid</c> (keyed on <c>to_broadcaster_user_id</c> — a raid arriving at this channel)
/// into <see cref="RaidEvent"/>. Payload fields: <c>from_broadcaster_user_id</c>,
/// <c>from_broadcaster_user_name</c>, <c>from_broadcaster_user_login</c>, <c>viewers</c>.
/// </summary>
public sealed class ChannelRaidTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.raid";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        RaidEvent raided = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            FromUserId = payload.GetRequiredString("from_broadcaster_user_id"),
            FromDisplayName = payload.GetRequiredString("from_broadcaster_user_name"),
            FromLogin = payload.GetRequiredString("from_broadcaster_user_login"),
            ViewerCount = payload.GetInt("viewers"),
        };

        return PublishAsync(raided, ct);
    }
}

/// <summary>
/// Translates <c>channel.update</c> into <see cref="ChannelUpdatedEvent"/>. Payload fields:
/// <c>broadcaster_user_name</c>, <c>title</c>, <c>category_name</c> (the new game/category display name).
/// </summary>
public sealed class ChannelUpdateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.update";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ChannelUpdatedEvent updated = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            BroadcasterDisplayName = payload.GetRequiredString("broadcaster_user_name"),
            NewTitle = payload.GetRequiredString("title"),
            NewGameName = payload.GetRequiredString("category_name"),
        };

        return PublishAsync(updated, ct);
    }
}

/// <summary>
/// Translates <c>stream.online</c> into <see cref="ChannelOnlineEvent"/>. Payload fields:
/// <c>broadcaster_user_name</c>, <c>started_at</c>. The online notification carries no title/category, so
/// <see cref="ChannelOnlineEvent.StreamTitle"/> and <see cref="ChannelOnlineEvent.GameName"/> degrade to
/// empty — current stream metadata is hydrated separately via Helix / <c>channel.update</c>.
/// </summary>
public sealed class StreamOnlineTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "stream.online";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ChannelOnlineEvent online = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            BroadcasterDisplayName = payload.GetRequiredString("broadcaster_user_name"),
            StreamTitle = payload.GetRequiredString("title"),
            GameName = payload.GetRequiredString("category_name"),
            StartedAt = payload.GetDateTimeOffset("started_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(online, ct);
    }
}

/// <summary>
/// Translates <c>stream.offline</c> into <see cref="ChannelOfflineEvent"/>. Payload fields:
/// <c>broadcaster_user_name</c>. The offline notification carries no duration, so
/// <see cref="ChannelOfflineEvent.StreamDuration"/> degrades to <see cref="TimeSpan.Zero"/> — elapsed uptime
/// is computed downstream from the recorded online timestamp.
/// </summary>
public sealed class StreamOfflineTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "stream.offline";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ChannelOfflineEvent offline = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            BroadcasterDisplayName = payload.GetRequiredString("broadcaster_user_name"),
            StreamDuration = TimeSpan.Zero,
        };

        return PublishAsync(offline, ct);
    }
}
