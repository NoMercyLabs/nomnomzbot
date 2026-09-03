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
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Stream.Events;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Translates <c>channel.raid</c> — BOTH directions, because both arrive under the same topic name.
///
/// <para>We hold two subscriptions: one keyed on <c>to_broadcaster_user_id</c> (a raid arriving here) and
/// one on <c>from_broadcaster_user_id</c> (a raid this channel sent, reported once it has EXECUTED and the
/// viewers have moved). The payload names both sides either way, so direction is decided by which of them
/// is us — the transport has already resolved that from the subscription's own condition.</para>
///
/// <para>The outgoing half is the fix for a raid pipeline ending the broadcast at the START of the
/// countdown: previously the only outgoing signal was <c>channel.moderate</c>'s <c>raid</c> action, which
/// fires on initiation.</para>
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
        string fromUserId = payload.GetRequiredString("from_broadcaster_user_id");

        // We are the raider → this is our outgoing raid, and it has already happened.
        if (
            !string.IsNullOrEmpty(notification.TwitchBroadcasterUserId)
            && fromUserId == notification.TwitchBroadcasterUserId
        )
        {
            OutgoingRaidEvent outgoing = new()
            {
                BroadcasterId = notification.BroadcasterId,
                OccurredAt = Clock.GetUtcNow(),
                ToUserId = payload.GetRequiredString("to_broadcaster_user_id"),
                ToDisplayName = payload.GetRequiredString("to_broadcaster_user_name"),
                ToLogin = payload.GetRequiredString("to_broadcaster_user_login"),
                ViewerCount = payload.GetInt("viewers"),
            };
            return PublishAsync(outgoing, ct);
        }

        RaidEvent raided = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            FromUserId = fromUserId,
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
            OccurredAt = Clock.GetUtcNow(),
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
            Provider = AuthEnums.Platform.Twitch,
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
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
            Provider = AuthEnums.Platform.Twitch,
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            BroadcasterDisplayName = payload.GetRequiredString("broadcaster_user_name"),
            StreamDuration = TimeSpan.Zero,
        };

        return PublishAsync(offline, ct);
    }
}
