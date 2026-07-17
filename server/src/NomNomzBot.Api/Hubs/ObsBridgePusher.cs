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
