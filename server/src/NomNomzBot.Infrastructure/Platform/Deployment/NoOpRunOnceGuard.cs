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
using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// The SQLite (self_host_lite) run-once guard ONLY: a second process against one SQLite file is not a supported
/// topology, so there is no CLUSTER to coordinate against (platform-conventions §3.8) and a lease always succeeds
/// against another PROCESS. Postgres-backed profiles (self_host_full AND saas) — which DO support two overlapping
/// instances against one database (zero-downtime deploys) — are registered onto <see cref="PostgresRunOnceGuard"/>
/// instead; this type is never selected there. It still enforces genuine mutual exclusion WITHIN this one process,
/// though: two async call sites in the same process can race for the same named resource (e.g. the projection
/// driver's periodic tick and an operator's manual replay/rebuild hitting the same projection+channel — S004g). A
/// named, non-reentrant in-process lock — held from acquire to lease dispose — covers that race without needing a
/// database round-trip; <c>ttl</c> is unused because release is always explicit (the lease is
/// disposed, never abandoned, on every code path that acquires it).
/// </summary>
public sealed class NoOpRunOnceGuard : IRunOnceGuard
{
    private static readonly ConcurrentDictionary<string, byte> Held = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceName,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult<IAsyncDisposable?>(
            Held.TryAdd(resourceName, 0) ? new InProcessLease(resourceName) : null
        );

    private sealed class InProcessLease : IAsyncDisposable
    {
        private readonly string _resourceName;

        public InProcessLease(string resourceName) => _resourceName = resourceName;

        public ValueTask DisposeAsync()
        {
            Held.TryRemove(_resourceName, out _);
            return ValueTask.CompletedTask;
        }
    }
}
