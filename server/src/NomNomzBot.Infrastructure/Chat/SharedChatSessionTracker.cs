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

namespace NomNomzBot.Infrastructure.Chat;

/// <summary>Process-wide (singleton) <see cref="ISharedChatSessionTracker"/> over a concurrent map.</summary>
public sealed class SharedChatSessionTracker : ISharedChatSessionTracker
{
    private readonly ConcurrentDictionary<Guid, SharedChatSessionInfo> _sessions = new();

    public SharedChatSessionInfo? GetActiveSession(Guid broadcasterId) =>
        _sessions.TryGetValue(broadcasterId, out SharedChatSessionInfo? session) ? session : null;

    public IReadOnlyList<Guid> GetChannelsInSession(string sessionId) =>
        [.. _sessions.Where(kv => kv.Value.SessionId == sessionId).Select(kv => kv.Key)];

    public void SetSession(Guid broadcasterId, SharedChatSessionInfo session) =>
        _sessions[broadcasterId] = session;

    public void ClearSession(Guid broadcasterId, string sessionId)
    {
        // Only clear the session the END event names — a stale end must not wipe a newer session.
        if (
            _sessions.TryGetValue(broadcasterId, out SharedChatSessionInfo? current)
            && current.SessionId == sessionId
        )
            _sessions.TryRemove(
                new KeyValuePair<Guid, SharedChatSessionInfo>(broadcasterId, current)
            );
    }
}
