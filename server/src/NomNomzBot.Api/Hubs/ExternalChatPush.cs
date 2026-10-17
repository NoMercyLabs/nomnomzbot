// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
