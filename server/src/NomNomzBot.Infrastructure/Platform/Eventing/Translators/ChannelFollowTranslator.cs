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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Eventing.Translators;

/// <summary>
/// Translates <c>channel.follow</c> (v2) into <see cref="FollowEvent"/>. Payload fields: <c>user_id</c>,
/// <c>user_login</c>, <c>user_name</c>, <c>followed_at</c> (the broadcaster fields are already resolved to the
/// tenant by the dispatcher). The canonical translator template: read the typed fields off the raw payload,
/// build the domain event with the resolved tenant + injected clock, publish.
/// </summary>
public sealed class ChannelFollowTranslator(IEventBus bus, TimeProvider clock)
    : EventSubEventTranslator(bus, clock)
{
    public override string SubscriptionType => "channel.follow";

    public override Task TranslateAsync(
        EventSubNotification notification,
        CancellationToken ct = default
    )
    {
        JsonElement payload = notification.Event;
        FollowEvent followed = new()
        {
            BroadcasterId = notification.BroadcasterId,
            OccurredAt = Clock.GetUtcNow(),
            UserId = payload.GetRequiredString("user_id"),
            UserDisplayName = payload.GetRequiredString("user_name"),
            UserLogin = payload.GetRequiredString("user_login"),
            FollowedAt = payload.GetDateTimeOffset("followed_at") ?? Clock.GetUtcNow(),
        };

        return PublishAsync(followed, ct);
    }
}
