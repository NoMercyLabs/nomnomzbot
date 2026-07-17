// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Obs.Services;
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
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    private readonly IObsBridgeRegistry _registry;
    private readonly IObsBridgePusher _pusher;
    private readonly ObsBridgeCommandBook _commands;
    private readonly TimeProvider _clock;

    public BridgeVtsTransport(
        IObsBridgeRegistry registry,
        IObsBridgePusher pusher,
        ObsBridgeCommandBook commands,
        TimeProvider clock
    )
    {
        _registry = registry;
        _pusher = pusher;
        _commands = commands;
        _clock = clock;
    }

    public async Task<Result<string>> RequestAsync(
        Guid broadcasterId,
        string requestType,
        string? dataJson,
        CancellationToken ct = default
    )
    {
        string? leader = await _registry.GetLeaderAsync(broadcasterId, ct);
        if (leader is null)
            return Result.Failure<string>(
                "No bridge is connected for this channel.",
                "VTS_BRIDGE_OFFLINE"
            );

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

        Task<ObsBridgeAck> ack = _commands.BeginAsync(commandId);
        try
        {
            await _pusher.PushExecuteAsync(leader, commandId, payload, ct);
            ObsBridgeAck answered = await ack.WaitAsync(CommandTimeout, _clock, ct);
            return answered.Ok
                ? Result.Success(answered.DataJson ?? "{}")
                : Result.Failure<string>(
                    answered.Error ?? "VTS rejected the request.",
                    "VTS_ERROR"
                );
        }
        catch (TimeoutException)
        {
            _commands.Abandon(commandId);
            return Result.Failure<string>(
                "The bridge did not answer within the timeout.",
                "VTS_TIMEOUT"
            );
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<string>(
                "The bridge disconnected mid-command.",
                "VTS_BRIDGE_OFFLINE"
            );
        }
    }
}
