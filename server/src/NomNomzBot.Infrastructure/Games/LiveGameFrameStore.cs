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
