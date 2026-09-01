// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Api.Hubs.Clients;

public interface IOBSRelayClient
{
    /// <summary>Server → the LEADER bridge: execute one command against local OBS and ack it by id.</summary>
    Task ExecuteObsRequest(Guid commandId, string payloadJson);

    /// <summary>
    /// Server → the bridge on connect: the channel's OBS-WebSocket password (null when passwordless) and LOCAL
    /// port, so the bridge can open and authenticate its OBS-WS handshake against the right endpoint. Modern OBS
    /// enables auth with a generated password by default, so without the password the bridge connects to the
    /// relay ("connected") yet every command fails the OBS Identify ("not reachable"); without the real port a
    /// streamer who moved OBS-WS off its 4455 default can never be reached at all — the bridge always runs
    /// inside OBS on the same machine, so only the port (never the host) can differ. Delivered over the
    /// authenticated relay (wss in prod) — never in the page URL.
    /// </summary>
    Task SetObsCredentials(string? obsPassword, int obsPort);
}
