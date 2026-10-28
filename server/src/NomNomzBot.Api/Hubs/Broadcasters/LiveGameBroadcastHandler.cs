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

using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Economy.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// A chat game that opens, settles or is canceled reaches the channel's open dashboards live: the Games page
/// reloads the running game, the catalog and the finished games. Each lifecycle event is forwarded as a
/// <c>games</c> config change, so the page needs no bespoke hub event.
/// </summary>
public sealed class LiveGameBroadcastHandler(IDashboardNotifier notifier)
    : IEventHandler<LiveGameStartedEvent>,
        IEventHandler<LiveGameResolvedEvent>,
        IEventHandler<LiveGameCancelledEvent>
{
    private const string Domain = "games";

    public Task HandleAsync(LiveGameStartedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, @event.SessionId, "created", ct);

    public Task HandleAsync(LiveGameResolvedEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, @event.SessionId, "updated", ct);

    public Task HandleAsync(LiveGameCancelledEvent @event, CancellationToken ct = default) =>
        PushAsync(@event.BroadcasterId, @event.SessionId, "updated", ct);

    private Task PushAsync(Guid broadcasterId, Guid sessionId, string action, CancellationToken ct)
    {
        if (broadcasterId == Guid.Empty)
            return Task.CompletedTask;

        string channel = broadcasterId.ToString();

        return notifier.SendConfigChangedAsync(
            channel,
            new ConfigChangedDto(channel, Domain, sessionId.ToString(), action),
            ct
        );
    }
}
