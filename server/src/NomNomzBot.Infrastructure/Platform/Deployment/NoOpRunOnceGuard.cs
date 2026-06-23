// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// The self-host (lite/full) run-once guard: a single process owns everything, so the lease is always granted
/// (platform-conventions §3.8). Migrate / seed / singleton workers run unconditionally — there is no cluster to
/// coordinate against.
/// </summary>
public sealed class NoOpRunOnceGuard : IRunOnceGuard
{
    private static readonly IAsyncDisposable GrantedLease = new NoOpLease();

    public Task<IAsyncDisposable?> TryAcquireAsync(
        string resourceName,
        TimeSpan ttl,
        CancellationToken cancellationToken = default
    ) => Task.FromResult<IAsyncDisposable?>(GrantedLease);

    private sealed class NoOpLease : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
