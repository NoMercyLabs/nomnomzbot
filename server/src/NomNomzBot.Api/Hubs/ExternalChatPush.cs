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
using NomNomzBot.Application.Chat.Dtos;
using NomNomzBot.Application.Chat.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>Sends an off-bot chat event to the one dashboard connection that reads the channel.</summary>
public sealed class ExternalChatPush : IExternalChatPush
{
    private readonly IHubContext<DashboardHub, IDashboardClient> _hub;

    public ExternalChatPush(IHubContext<DashboardHub, IDashboardClient> hub) => _hub = hub;

    public Task PushAsync(
        string connectionId,
        ExternalChatEvent chatEvent,
        CancellationToken ct = default
    ) => _hub.Clients.Client(connectionId).ExternalChatEvent(chatEvent);
}
