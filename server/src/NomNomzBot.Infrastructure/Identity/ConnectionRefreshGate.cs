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

namespace NomNomzBot.Infrastructure.Identity;

/// <summary>
/// A per-key async mutex used to stop concurrent token refreshes for the SAME integration connection from
/// stampeding the provider (S036): two simultaneous callers racing a refresh can both post the same refresh
/// token, and providers that rotate/invalidate the previous refresh token on use (Kick's OAuth 2.1 rotation,
/// Twitch/Google's revoke-on-reuse behavior) let the loser destroy the winner's freshly-issued token and log
/// the connection out. Every refresh path acquires the gate keyed by <c>"{provider}:{connectionId}"</c> (or
/// the provider's own stable connection identity) before touching the provider's token endpoint, so only one
/// caller per CONNECTION ever reaches the HTTP call; a different connection refreshing concurrently uses a
/// different key and is never blocked by this one.
///
/// <para>
/// <b>In-process only.</b> The gate is a single <see cref="ConcurrentDictionary{TKey,TValue}"/> of
/// <see cref="SemaphoreSlim"/> held in this process's memory — it serializes refreshes across every request
/// thread on ONE instance. Self-host runs a single process, so this closes the race completely there. A
/// multi-replica SaaS deployment has one such gate PER replica: two different replicas can still refresh the
/// same connection at the same moment, because neither replica's in-memory dictionary knows about the
/// other's in-flight refresh. Closing that case requires a distributed lock (e.g. a Redis `SET NX PX` lease)
/// keyed the same way — out of scope here; this gate is the correct fix for self-host and reduces (but does
/// not eliminate) the SaaS window.
/// </para>
/// </summary>
public interface IConnectionRefreshGate
{
    /// <summary>
    /// Waits for exclusive access to <paramref name="key"/> and returns a token that releases it on
    /// <see cref="IDisposable.Dispose"/>. Always <c>await using</c> the result.
    /// </summary>
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class ConnectionRefreshGate : IConnectionRefreshGate
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();

    public async Task<IDisposable> AcquireAsync(
        string key,
        CancellationToken cancellationToken = default
    )
    {
        SemaphoreSlim semaphore = _semaphores.GetOrAdd(key, static _ => new(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                semaphore.Release();
        }
    }
}
