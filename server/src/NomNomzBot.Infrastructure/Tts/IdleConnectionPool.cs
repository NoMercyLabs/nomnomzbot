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

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Keeps connections open between uses, so the next use skips the handshake. A connection that is given back
/// waits here; the most recently used one goes out first. One that idled past <c>maxIdle</c>, or that the
/// remote side closed, is disposed instead of reused. More than <c>capacity</c> idle connections are never kept.
/// </summary>
internal sealed class IdleConnectionPool<T>(
    TimeProvider time,
    int capacity,
    TimeSpan maxIdle,
    Func<T, bool> isOpen
)
    where T : class, IDisposable
{
    private readonly object _gate = new();
    private readonly List<(T Connection, DateTimeOffset IdleSince)> _idle = [];

    /// <summary>Takes the freshest idle connection that is still open. False when there is none.</summary>
    public bool TryRent(out T? connection)
    {
        List<T> discarded = [];
        connection = null;
        lock (_gate)
        {
            DateTimeOffset now = time.GetUtcNow();
            foreach ((T Connection, DateTimeOffset IdleSince) entry in _idle.ToList())
            {
                if (now - entry.IdleSince > maxIdle || !isOpen(entry.Connection))
                {
                    _idle.Remove(entry);
                    discarded.Add(entry.Connection);
                }
            }

            if (_idle.Count > 0)
            {
                connection = _idle[^1].Connection;
                _idle.RemoveAt(_idle.Count - 1);
            }
        }

        discarded.ForEach(c => c.Dispose());
        return connection is not null;
    }

    /// <summary>Gives a connection back for reuse. A closed one, or one over the limit, is disposed.</summary>
    public void Return(T connection)
    {
        bool keep = false;
        lock (_gate)
        {
            if (isOpen(connection) && _idle.Count < capacity)
            {
                _idle.Add((connection, time.GetUtcNow()));
                keep = true;
            }
        }

        if (!keep)
            connection.Dispose();
    }

    public void Clear()
    {
        List<T> all;
        lock (_gate)
        {
            all = _idle.ConvertAll(entry => entry.Connection);
            _idle.Clear();
        }

        all.ForEach(c => c.Dispose());
    }
}
