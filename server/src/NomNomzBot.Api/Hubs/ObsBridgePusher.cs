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
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// Host-side bridge pusher: the transport (Infrastructure) asks for a push, this hands it to the
/// leader's SignalR connection (cross-node delivery rides the backplane).
/// </summary>
public sealed class ObsBridgePusher : IObsBridgePusher
{
    private readonly IHubContext<OBSRelayHub, IOBSRelayClient> _hub;

    public ObsBridgePusher(IHubContext<OBSRelayHub, IOBSRelayClient> hub)
    {
        _hub = hub;
    }

    public Task PushExecuteAsync(
        string connectionId,
        Guid commandId,
        string payloadJson,
        CancellationToken ct = default
    ) => _hub.Clients.Client(connectionId).ExecuteObsRequest(commandId, payloadJson);
}
