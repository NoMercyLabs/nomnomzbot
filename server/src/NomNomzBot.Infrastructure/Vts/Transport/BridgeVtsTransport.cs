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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Vts.Services;
using NomNomzBot.Infrastructure.Obs.Bridge;

namespace NomNomzBot.Infrastructure.Vts.Transport;

/// <summary>
/// The SaaS/remote VTS transport (vtube-studio.md D1): ONE browser-source relay carries both OBS and
/// VTS — commands ride the SAME leader election, pusher, and command book as the OBS bridge, with a
/// <c>vts_request</c> payload kind the bridge executes against local <c>ws://localhost:8001</c>.
/// No leader online → <c>VTS_BRIDGE_OFFLINE</c> (graceful, never silent).
/// </summary>
public sealed class BridgeVtsTransport : IVtsTransport
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly ObsBridgeRoundTrip _roundTrip;

    public BridgeVtsTransport(ObsBridgeRoundTrip roundTrip)
    {
        _roundTrip = roundTrip;
    }

    public async Task<Result<string>> RequestAsync(
        Guid broadcasterId,
        string requestType,
        string? dataJson,
        CancellationToken ct = default
    )
    {
        Guid commandId = Guid.CreateVersion7();
        string payload = JsonSerializer.Serialize(
            new
            {
                kind = "vts_request",
                requestType,
                data = dataJson ?? "{}",
            },
            WireJson
        );

        ObsBridgeRoundTripOutcome outcome = await _roundTrip.SendAsync(
            broadcasterId,
            commandId,
            payload,
            ct
        );
        return outcome.State switch
        {
            ObsBridgeRoundTripState.Acked when outcome.Ack!.Ok => Result.Success(
                outcome.Ack.DataJson ?? "{}"
            ),
            ObsBridgeRoundTripState.Acked => Result.Failure<string>(
                outcome.Ack!.Error ?? "VTS rejected the request.",
                "VTS_ERROR"
            ),
            ObsBridgeRoundTripState.Offline => Result.Failure<string>(
                "No bridge is connected for this channel.",
                "VTS_BRIDGE_OFFLINE"
            ),
            ObsBridgeRoundTripState.Timeout => Result.Failure<string>(
                "The bridge did not answer within the timeout.",
                "VTS_TIMEOUT"
            ),
            _ => Result.Failure<string>(
                "The bridge disconnected mid-command.",
                "VTS_BRIDGE_OFFLINE"
            ),
        };
    }
}
