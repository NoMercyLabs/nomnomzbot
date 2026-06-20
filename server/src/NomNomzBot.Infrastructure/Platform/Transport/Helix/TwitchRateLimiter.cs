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
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Platform.Transport.Helix;

/// <summary>
/// In-process adaptive Helix rate limiter (twitch-helix.md §3.5, §7) for the self-host profile — a single
/// node owns the whole Helix quota, so the buckets live in this process. One bucket per token identity;
/// the bucket's remaining/reset are driven entirely by the observed <c>Ratelimit-*</c> headers that
/// <see cref="Observe"/> feeds back after every response. When a bucket is exhausted (remaining ≤ 0) or a
/// real 429 hard-blocks it, <see cref="AcquireAsync"/> waits until the observed reset instant before
/// granting a permit — proactive throttling, not just header parsing. User-interactive callers take a
/// per-bucket priority gate ahead of background polls so polling never starves a user action.
///
/// The SaaS multi-node variant (a distributed bucket shared across nodes) is a separate adapter bound by
/// deployment profile; it depends on the cross-node <c>IRateLimiter</c> from scaling-qos §4 and lands with
/// that subsystem.
/// </summary>
public sealed class TwitchRateLimiter(TimeProvider timeProvider) : ITwitchRateLimiter
{
    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);

    public async Task<ITwitchRateLease> AcquireAsync(
        string tokenBucketKey,
        TwitchCallPriority priority,
        CancellationToken ct = default
    )
    {
        Bucket bucket = _buckets.GetOrAdd(tokenBucketKey, _ => new Bucket());
        await bucket.AcquireAsync(priority, timeProvider, ct);
        return new Lease(bucket);
    }

    public void Observe(
        string tokenBucketKey,
        int? limit,
        int? remaining,
        DateTimeOffset? resetsAt,
        bool wasHardLimited = false
    )
    {
        Bucket bucket = _buckets.GetOrAdd(tokenBucketKey, _ => new Bucket());
        bucket.Observe(limit, remaining, resetsAt, wasHardLimited);
    }

    /// <summary>Per-token bucket: remaining/reset come from observed headers; a hard 429 blocks until reset.</summary>
    private sealed class Bucket
    {
        private readonly object _gate = new();

        // A small two-band semaphore so user-interactive callers enter the wait loop before background polls.
        private readonly SemaphoreSlim _priorityGate = new(1, 1);

        private int? _remaining;
        private DateTimeOffset? _resetsAt;
        private bool _hardBlocked;

        public async Task AcquireAsync(
            TwitchCallPriority priority,
            TimeProvider timeProvider,
            CancellationToken ct
        )
        {
            // Background polls yield the gate so a queued user-interactive call is served first.
            if (priority == TwitchCallPriority.Background)
            {
                await _priorityGate.WaitAsync(ct);
                try
                {
                    await WaitForBudgetAsync(timeProvider, ct);
                }
                finally
                {
                    _priorityGate.Release();
                }
            }
            else
            {
                await WaitForBudgetAsync(timeProvider, ct);
            }
        }

        private async Task WaitForBudgetAsync(TimeProvider timeProvider, CancellationToken ct)
        {
            while (true)
            {
                TimeSpan wait;
                lock (_gate)
                {
                    DateTimeOffset now = timeProvider.GetUtcNow();

                    // A reset instant in the past clears any prior exhaustion/hard-block.
                    if (_resetsAt is { } reset && reset <= now)
                    {
                        _hardBlocked = false;
                        _remaining = null;
                        _resetsAt = null;
                    }

                    bool exhausted = _hardBlocked || _remaining is <= 0;
                    if (!exhausted)
                    {
                        // Optimistically spend one unit; the next Observe corrects from real headers.
                        if (_remaining is > 0)
                            _remaining--;
                        return;
                    }

                    wait = _resetsAt is { } until ? until - now : TimeSpan.FromSeconds(1);
                    if (wait <= TimeSpan.Zero)
                        wait = TimeSpan.FromMilliseconds(50);
                }

                await Task.Delay(wait, timeProvider, ct);
            }
        }

        public void Observe(
            int? limit,
            int? remaining,
            DateTimeOffset? resetsAt,
            bool wasHardLimited
        )
        {
            lock (_gate)
            {
                if (remaining.HasValue)
                    _remaining = remaining;
                if (resetsAt.HasValue)
                    _resetsAt = resetsAt;
                if (wasHardLimited)
                {
                    _hardBlocked = true;
                    _remaining = 0;
                }
            }
        }
    }

    private sealed class Lease(Bucket bucket) : ITwitchRateLease
    {
        // The bucket is header-driven, not permit-counted, so disposal has no counter to release;
        // the lease exists so callers bracket the request and the contract can grow a real release later.
        public ValueTask DisposeAsync()
        {
            _ = bucket;
            return ValueTask.CompletedTask;
        }
    }
}
