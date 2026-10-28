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
            _sessions.TryRemove(new(broadcasterId, current));
    }
}
