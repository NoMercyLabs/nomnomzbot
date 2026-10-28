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

using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Infrastructure.Moderation.EventHandlers;

/// <summary>
/// Turns the Twitch unban-request signals into the dashboard's <c>unban-requests</c> config push, so an open
/// moderation page refetches the appeal list when a viewer files one or another moderator resolves one on Twitch.
/// Appeals are not stored locally, so the push is the only way the page learns of a change.
/// </summary>
public sealed class UnbanRequestCreatedConfigPushHandler(IEventBus bus)
    : IEventHandler<UnbanRequestCreatedEvent>
{
    public Task HandleAsync(UnbanRequestCreatedEvent @event, CancellationToken cancellationToken) =>
        UnbanRequestConfigPush.PublishAsync(
            bus,
            @event.BroadcasterId,
            @event.RequestId,
            "created",
            cancellationToken
        );
}

/// <summary>The resolve leg of the <c>unban-requests</c> config push (approved, denied or canceled on Twitch).</summary>
public sealed class UnbanRequestResolvedConfigPushHandler(IEventBus bus)
    : IEventHandler<UnbanRequestResolvedEvent>
{
    public Task HandleAsync(
        UnbanRequestResolvedEvent @event,
        CancellationToken cancellationToken
    ) =>
        UnbanRequestConfigPush.PublishAsync(
            bus,
            @event.BroadcasterId,
            @event.RequestId,
            "updated",
            cancellationToken
        );
}

internal static class UnbanRequestConfigPush
{
    public static Task PublishAsync(
        IEventBus bus,
        Guid broadcasterId,
        string requestId,
        string action,
        CancellationToken cancellationToken
    ) =>
        bus.PublishAsync(
            new ChannelConfigChangedEvent
            {
                BroadcasterId = broadcasterId,
                Domain = "unban-requests",
                EntityId = requestId,
                Action = action,
            },
            cancellationToken
        );
}
