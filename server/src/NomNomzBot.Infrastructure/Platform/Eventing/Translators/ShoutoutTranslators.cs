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
            OccurredAt = Clock.GetUtcNow(),
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
            OccurredAt = Clock.GetUtcNow(),
            FromBroadcasterId = payload.GetRequiredString("from_broadcaster_user_id"),
            FromBroadcasterDisplayName = payload.GetRequiredString("from_broadcaster_user_name"),
            FromBroadcasterLogin = payload.GetRequiredString("from_broadcaster_user_login"),
            ViewerCount = payload.GetInt("viewer_count"),
        };

        return PublishAsync(received, ct);
    }
}
