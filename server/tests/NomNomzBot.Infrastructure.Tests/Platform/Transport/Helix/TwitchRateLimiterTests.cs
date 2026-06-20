// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Infrastructure.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

/// <summary>
/// Proves the in-process limiter actually <em>throttles</em> from observed headers — not merely parses them.
/// A bucket whose observed <c>Remaining</c> hit 0 must block <c>AcquireAsync</c> until the observed reset
/// instant elapses on the faked clock, then grant the permit.
/// </summary>
public class TwitchRateLimiterTests
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AcquireAsync_WhenBudgetRemaining_ReturnsLeaseImmediately()
    {
        FakeTimeProvider clock = new(Origin);
        TwitchRateLimiter limiter = new(clock);
        limiter.Observe("bucket", limit: 800, remaining: 50, resetsAt: Origin.AddSeconds(60));

        // A bucket with budget grants without advancing the clock.
        Task<ITwitchRateLease> acquire = limiter.AcquireAsync(
            "bucket",
            TwitchCallPriority.UserInteractive
        );

        (await acquire.WaitAsync(TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_WhenExhausted_BlocksUntilObservedResetElapses()
    {
        FakeTimeProvider clock = new(Origin);
        TwitchRateLimiter limiter = new(clock);

        // Observed headers say the bucket is spent and resets 30s out.
        DateTimeOffset reset = Origin.AddSeconds(30);
        limiter.Observe("bucket", limit: 800, remaining: 0, resetsAt: reset);

        Task<ITwitchRateLease> acquire = limiter.AcquireAsync(
            "bucket",
            TwitchCallPriority.UserInteractive
        );

        // Before the reset the permit is genuinely withheld — the task is still pending.
        clock.Advance(TimeSpan.FromSeconds(29));
        await Task.Yield();
        acquire.IsCompleted.Should().BeFalse();

        // Crossing the reset instant releases the permit.
        clock.Advance(TimeSpan.FromSeconds(1));
        ITwitchRateLease lease = await acquire.WaitAsync(TimeSpan.FromSeconds(5));
        lease.Should().NotBeNull();
    }

    [Fact]
    public async Task Observe_HardLimited_BlocksUntilResetEvenWithStaleRemaining()
    {
        FakeTimeProvider clock = new(Origin);
        TwitchRateLimiter limiter = new(clock);

        // A real 429 hard-blocks the bucket until reset, regardless of any positive Remaining echoed back.
        DateTimeOffset reset = Origin.AddSeconds(10);
        limiter.Observe("bucket", limit: 800, remaining: 5, resetsAt: reset, wasHardLimited: true);

        Task<ITwitchRateLease> acquire = limiter.AcquireAsync(
            "bucket",
            TwitchCallPriority.Background
        );

        clock.Advance(TimeSpan.FromSeconds(9));
        await Task.Yield();
        acquire.IsCompleted.Should().BeFalse();

        clock.Advance(TimeSpan.FromSeconds(1));
        (await acquire.WaitAsync(TimeSpan.FromSeconds(5))).Should().NotBeNull();
    }

    [Fact]
    public async Task AcquireAsync_DistinctBuckets_AreIndependent()
    {
        FakeTimeProvider clock = new(Origin);
        TwitchRateLimiter limiter = new(clock);

        // One bucket exhausted, a different bucket untouched — the second must not be throttled.
        limiter.Observe("spent", limit: 800, remaining: 0, resetsAt: Origin.AddSeconds(60));

        ITwitchRateLease lease = await limiter
            .AcquireAsync("fresh", TwitchCallPriority.Background)
            .WaitAsync(TimeSpan.FromSeconds(5));

        lease.Should().NotBeNull();
    }
}
