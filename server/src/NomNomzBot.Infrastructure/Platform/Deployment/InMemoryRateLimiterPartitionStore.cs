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
using NomNomzBot.Application.Common.Interfaces;

namespace NomNomzBot.Infrastructure.Platform.Deployment;

/// <summary>
/// The lite rate-limiter counter store (platform-conventions §3.7): a per-instance fixed-window counter. Correct
/// for the single-process self-host topology — there is no cluster to share counts across. Reads the clock through
/// the injected <see cref="TimeProvider"/> so windows are deterministically testable.
/// </summary>
public sealed class InMemoryRateLimiterPartitionStore : IRateLimiterPartitionStore
{
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, Window> _windows = new();

    public InMemoryRateLimiterPartitionStore(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task<RateLimitLease> AcquireAsync(
        string partitionKey,
        int permitLimit,
        TimeSpan window,
        CancellationToken cancellationToken = default
    )
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();

        Window current = _windows.AddOrUpdate(
            partitionKey,
            _ => new(now + window, 1),
            (_, existing) => existing.ResetAt <= now ? new(now + window, 1) : existing.Increment()
        );

        bool acquired = current.Count <= permitLimit;
        int remaining = Math.Max(0, permitLimit - current.Count);
        TimeSpan retryAfter = acquired ? TimeSpan.Zero : current.ResetAt - now;

        return Task.FromResult(new RateLimitLease(acquired, remaining, retryAfter));
    }

    private readonly record struct Window(DateTimeOffset ResetAt, int Count)
    {
        public Window Increment() => this with { Count = Count + 1 };
    }
}
