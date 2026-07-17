// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using NomNomzBot.Application.DTOs.Economy;
using NomNomzBot.Application.Games;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>
/// The in-memory half of one active round: the game, its manifest-derived timing, the participants and
/// their stakes, and the state bag the engine snapshots to <c>GameSession.StateJson</c> on every transition.
/// All mutation happens under <see cref="Gate"/> — chat input, ticks, and cancel serialize per session.
/// </summary>
public sealed class LiveGameSessionRuntime
{
    public required Guid SessionId { get; init; }
    public required Guid BroadcasterId { get; init; }
    public required ILiveGame Game { get; init; }
    public required Guid GameConfigId { get; init; }
    public required GameConfigView Config { get; init; }
    public required DateTime JoinClosesAt { get; init; }
    public Guid? OverlayWidgetId { get; init; }

    public SemaphoreSlim Gate { get; } = new(1, 1);
    public List<LiveGameParticipant> Participants { get; } = [];
    public Dictionary<Guid, LiveGameStakeResult> Stakes { get; } = [];
    public Dictionary<string, object?> Data { get; } = [];
    public LiveGamePhase Phase { get; set; } = LiveGamePhase.Lobby;
    public DateTime? NextTickAt { get; set; }

    /// <summary>Set once the session settles or cancels — late chat/ticks bounce off instead of reviving it.</summary>
    public bool Terminal { get; set; }
}

/// <summary>
/// The singleton holder of every active round on this node, keyed by channel (at most one non-terminal
/// session per channel — D7). The chat listener's hot path is a lock-free lookup here, so chat stays cheap
/// while no game runs.
/// </summary>
public sealed class LiveGameSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, LiveGameSessionRuntime> _active = new();

    public bool TryGet(
        Guid broadcasterId,
        [NotNullWhen(true)] out LiveGameSessionRuntime? runtime
    ) => _active.TryGetValue(broadcasterId, out runtime);

    public bool TryRegister(LiveGameSessionRuntime runtime) =>
        _active.TryAdd(runtime.BroadcasterId, runtime);

    public void Remove(Guid broadcasterId, Guid sessionId)
    {
        if (
            _active.TryGetValue(broadcasterId, out LiveGameSessionRuntime? current)
            && current.SessionId == sessionId
        )
            _active.TryRemove(broadcasterId, out _);
    }

    public IReadOnlyList<LiveGameSessionRuntime> Snapshot() => [.. _active.Values];
}
