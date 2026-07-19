// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs.Dtos;

namespace NomNomzBot.Api.Hubs.Clients;

public interface IOBSRelayClient
{
    Task OBSCommand(OBSCommandDto command);

    /// <summary>Server → the LEADER bridge: execute one command against local OBS and ack it by id.</summary>
    Task ExecuteObsRequest(Guid commandId, string payloadJson);

    /// <summary>
    /// Server → the bridge on connect: the channel's OBS-WebSocket password (null when passwordless), so the
    /// bridge can authenticate its LOCAL OBS-WS handshake. Modern OBS enables auth with a generated password by
    /// default, so without this the bridge connects to the relay ("connected") yet every command fails the OBS
    /// Identify ("not reachable"). Delivered over the authenticated relay (wss in prod) — never in the page URL.
    /// </summary>
    Task SetObsCredentials(string? obsPassword);
}
