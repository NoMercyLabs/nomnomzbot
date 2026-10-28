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

using Microsoft.AspNetCore.SignalR;
using NomNomzBot.Api.Hubs.Clients;
using NomNomzBot.Domain.Identity.Events;
using NomNomzBot.Domain.Platform.Interfaces;

namespace NomNomzBot.Api.Hubs.Broadcasters;

/// <summary>
/// The shared platform bot connecting or disconnecting reaches every open operator platform bot page live,
/// so the page reloads its state. <c>BroadcasterId == Guid.Empty</c> is the shared-bot sentinel; a channel's
/// own bot is not the platform bot and pushes nothing here.
/// </summary>
public sealed class AdminPlatformBotBroadcastHandler(IHubContext<AdminHub, IAdminClient> hub)
    : IEventHandler<BotAccountAuthorizedEvent>,
        IEventHandler<BotAccountDisconnectedEvent>
{
    public Task HandleAsync(BotAccountAuthorizedEvent @event, CancellationToken ct = default) =>
        @event.BroadcasterId == Guid.Empty
            ? hub.Clients.All.ReceivePlatformBotChanged(
                new { Connected = true, BotUsername = (string?)@event.BotUsername }
            )
            : Task.CompletedTask;

    public Task HandleAsync(BotAccountDisconnectedEvent @event, CancellationToken ct = default) =>
        @event.BroadcasterId == Guid.Empty
            ? hub.Clients.All.ReceivePlatformBotChanged(
                new { Connected = false, BotUsername = (string?)null }
            )
            : Task.CompletedTask;
}
