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
/// Translates <c>channel.shoutout.create</c> into <see cref="ShoutoutSentEvent"/> — an outgoing shoutout this
/// channel created for another broadcaster. Maps the <c>to_broadcaster_user_*</c> target onto the shared sent-event
/// (which models the recipient identity).
/// </summary>
public sealed class ChannelShoutoutCreateTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.shoutout.create";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ShoutoutSentEvent sent = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            ToUserId = payload.GetRequiredString("to_broadcaster_user_id"),
            ToDisplayName = payload.GetRequiredString("to_broadcaster_user_name"),
        };

        return PublishAsync(sent, ct);
    }
}

/// <summary>
/// Translates <c>channel.shoutout.receive</c> into <see cref="ShoutoutReceivedEvent"/> — another broadcaster
/// shouted this channel out. Maps the <c>from_broadcaster_user_*</c> source identity and the exposed
/// <c>viewer_count</c>.
/// </summary>
public sealed class ChannelShoutoutReceiveTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.shoutout.receive";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        ShoutoutReceivedEvent received = new()
        {
            BroadcasterId = notification.BroadcasterId,
            Timestamp = Clock.GetUtcNow(),
            FromBroadcasterId = payload.GetRequiredString("from_broadcaster_user_id"),
            FromBroadcasterDisplayName = payload.GetRequiredString("from_broadcaster_user_name"),
            FromBroadcasterLogin = payload.GetRequiredString("from_broadcaster_user_login"),
            ViewerCount = payload.GetInt("viewer_count"),
        };

        return PublishAsync(received, ct);
    }
}
