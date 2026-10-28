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

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace NomNomzBot.Infrastructure.Games;

/// <summary>One overlay frame exactly as the engine pushed it: the <c>game.&lt;phase&gt;</c> event name and payload.</summary>
public sealed record LiveGameFrame(string EventType, object? Payload, DateTimeOffset PushedAt);

/// <summary>
/// What a reloaded overlay needs for one session: the frame that opened it (<see cref="Open"/>) and the newest frame
/// since (<see cref="Latest"/>, null until a second frame is pushed). <see cref="Terminal"/> is set once the newest
/// frame is the settled or cancelled one.
/// </summary>
public sealed record LiveGameFrameSnapshot(
    Guid SessionId,
    string GameKey,
    LiveGameFrame Open,
    LiveGameFrame? Latest,
    bool Terminal
);

/// <summary>
/// The singleton memory of the last frames the engine pushed, one snapshot per channel (a channel runs at most one
/// session at a time). A snapshot is replaced when the next session opens, so a reload right after the results can
/// still show the winner. In memory only: after a server restart there is nothing to seed, and the seed stays empty.
/// </summary>
public sealed class LiveGameFrameStore
{
    private readonly ConcurrentDictionary<Guid, LiveGameFrameSnapshot> _byChannel = new();

    public void Record(
        Guid broadcasterId,
        Guid sessionId,
        string gameKey,
        LiveGameFrame frame,
        bool terminal
    ) =>
        _byChannel.AddOrUpdate(
            broadcasterId,
            _ => new(sessionId, gameKey, frame, null, terminal),
            (_, current) =>
                current.SessionId == sessionId
                    ? current with
                    {
                        Latest = frame,
                        Terminal = terminal,
                    }
                    : new(sessionId, gameKey, frame, null, terminal)
        );

    public bool TryGet(
        Guid broadcasterId,
        [NotNullWhen(true)] out LiveGameFrameSnapshot? snapshot
    ) => _byChannel.TryGetValue(broadcasterId, out snapshot);
}
