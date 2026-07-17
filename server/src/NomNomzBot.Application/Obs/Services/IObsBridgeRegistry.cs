// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Application.Obs.Services;

/// <summary>One connected bridge's live status for a channel (obs-control.md §3.3).</summary>
public sealed record ObsBridgeStatusDto(int InstanceCount, bool HasLeader, DateTime? LeaderSince);

/// <summary>
/// Single-executor election for the browser-source bridges (obs-control.md §3.3/D2): a channel may
/// run several OBS instances with the bridge installed, but exactly ONE — the longest-lived
/// connection — executes commands. State lives in the cache keyed by channel, so every node sees the
/// same leader.
/// </summary>
public interface IObsBridgeRegistry
{
    Task RegisterAsync(
        Guid broadcasterId,
        string connectionId,
        DateTime connectedAt,
        CancellationToken ct = default
    );

    Task UnregisterAsync(Guid broadcasterId, string connectionId, CancellationToken ct = default);

    /// <summary>The leader's connection id, or null when no bridge is online.</summary>
    Task<string?> GetLeaderAsync(Guid broadcasterId, CancellationToken ct = default);

    Task<ObsBridgeStatusDto> GetStatusAsync(Guid broadcasterId, CancellationToken ct = default);
}

/// <summary>
/// Pushes an execute-command to a specific bridge connection. Implemented in the host over the
/// SignalR hub (the transport lives below the hub layer and cannot reference it directly).
/// </summary>
public interface IObsBridgePusher
{
    Task PushExecuteAsync(
        string connectionId,
        Guid commandId,
        string payloadJson,
        CancellationToken ct = default
    );
}
