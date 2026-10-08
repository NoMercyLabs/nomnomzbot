// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Bridge;

/// <summary>How one pushed command ended: an ack from the bridge, or one of the three ways it did not.</summary>
public enum ObsBridgeRoundTripState
{
    Acked,
    Offline,
    Timeout,
    Disconnected,
}

public sealed record ObsBridgeRoundTripOutcome(ObsBridgeRoundTripState State, ObsBridgeAck? Ack);

/// <summary>
/// One command to the channel's LEADER bridge, awaited to its ack (obs-control.md §3.2/D2). Shared by the
/// OBS and VTS bridge transports (one relay carries both — vtube-studio.md D1). A leader that does not
/// answer within the timeout is a dead socket (a bridge that reconnected keeps its old entry until SignalR
/// notices, up to ~45 s): it is evicted from the election and the command is pushed once more to the next
/// leader, so a live bridge standing by never watches a command die on a stale one.
/// </summary>
public sealed class ObsBridgeRoundTrip
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(15);

    private readonly IObsBridgeRegistry _registry;
    private readonly IObsBridgePusher _pusher;
    private readonly ObsBridgeCommandBook _commands;
    private readonly TimeProvider _clock;
    private readonly ILogger<ObsBridgeRoundTrip> _logger;

    public ObsBridgeRoundTrip(
        IObsBridgeRegistry registry,
        IObsBridgePusher pusher,
        ObsBridgeCommandBook commands,
        TimeProvider clock,
        ILogger<ObsBridgeRoundTrip> logger
    )
    {
        _registry = registry;
        _pusher = pusher;
        _commands = commands;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ObsBridgeRoundTripOutcome> SendAsync(
        Guid broadcasterId,
        Guid commandId,
        string payloadJson,
        CancellationToken ct = default
    )
    {
        string? leader = await _registry.GetLeaderAsync(broadcasterId, ct);
        if (leader is null)
            return new(ObsBridgeRoundTripState.Offline, null);

        ObsBridgeRoundTripOutcome first = await PushAndAwaitAsync(
            broadcasterId,
            leader,
            commandId,
            payloadJson,
            ct
        );
        if (first.State != ObsBridgeRoundTripState.Timeout)
            return first;

        await _registry.UnregisterAsync(broadcasterId, leader, ct);
        string? next = await _registry.GetLeaderAsync(broadcasterId, ct);
        _logger.LogWarning(
            "OBS bridge leader {Leader} for {Channel} did not ack command {Command} in {Timeout}s; evicted it, next leader: {Next}.",
            leader,
            broadcasterId,
            commandId,
            CommandTimeout.TotalSeconds,
            next ?? "none"
        );
        if (next is null)
            return first;
        return await PushAndAwaitAsync(broadcasterId, next, commandId, payloadJson, ct);
    }

    private async Task<ObsBridgeRoundTripOutcome> PushAndAwaitAsync(
        Guid broadcasterId,
        string connectionId,
        Guid commandId,
        string payloadJson,
        CancellationToken ct
    )
    {
        Task<ObsBridgeAck> ack = _commands.BeginAsync(broadcasterId, commandId);
        try
        {
            await _pusher.PushExecuteAsync(connectionId, commandId, payloadJson, ct);
            ObsBridgeAck answered = await ack.WaitAsync(CommandTimeout, _clock, ct);
            return new(ObsBridgeRoundTripState.Acked, answered);
        }
        catch (TimeoutException)
        {
            _commands.Abandon(commandId);
            return new(ObsBridgeRoundTripState.Timeout, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new(ObsBridgeRoundTripState.Disconnected, null);
        }
    }
}
