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
using NomNomzBot.Infrastructure.Chat;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>
/// Proves S010's send pacing: a per-queue-key token bucket paces bursts to the configured capacity
/// instead of firing every send at once, nothing is ever dropped (every accepted call eventually
/// resolves to the platform's real outcome), and concurrent identical sends coalesce into one actual
/// platform call.
/// </summary>
public sealed class TokenBucketChatSendQueueTests
{
    [Fact]
    public async Task A_hundred_simultaneous_sends_are_paced_and_none_are_dropped()
    {
        // Small, fast bucket so the test proves pacing without a real-time wait: 5 tokens refilling
        // fully every 40ms.
        TokenBucketChatSendQueue queue = new(
            capacity: 5,
            refillPeriod: TimeSpan.FromMilliseconds(40)
        );
        ConcurrentBag<DateTime> sendTimestamps = [];
        int sentCount = 0;

        IEnumerable<Task<bool>> tasks = Enumerable
            .Range(0, 100)
            .Select(i =>
                queue.EnqueueAsync(
                    "channel-1:twitch",
                    $"unique-{i}", // distinct coalesce keys — no coalescing in this test
                    ct =>
                    {
                        sendTimestamps.Add(DateTime.UtcNow);
                        Interlocked.Increment(ref sentCount);
                        return Task.FromResult(true);
                    }
                )
            );

        bool[] results = await Task.WhenAll(tasks);

        Assert.Equal(100, sentCount);
        Assert.All(results, Assert.True); // none dropped — every call resolved to the real outcome
        Assert.Equal(100, sendTimestamps.Count);

        // Pacing proof: the first burst (bucket starts full) sends immediately; the rest are spread
        // across multiple refill windows rather than all landing in the same instant.
        DateTime[] ordered = sendTimestamps.OrderBy(t => t).ToArray();
        TimeSpan span = ordered[^1] - ordered[0];
        Assert.True(
            span > TimeSpan.FromMilliseconds(100),
            $"expected sends to be spread over multiple refill windows, span was {span.TotalMilliseconds}ms"
        );
    }

    [Fact]
    public async Task A_send_that_the_platform_rejects_reports_false_not_a_swallowed_success()
    {
        TokenBucketChatSendQueue queue = new(
            capacity: 10,
            refillPeriod: TimeSpan.FromMilliseconds(10)
        );

        bool result = await queue.EnqueueAsync(
            "channel-1:twitch",
            "rejected-line",
            _ => Task.FromResult(false)
        );

        Assert.False(result);
    }

    [Fact]
    public async Task Concurrent_identical_sends_coalesce_into_one_actual_platform_call()
    {
        TokenBucketChatSendQueue queue = new(
            capacity: 50,
            refillPeriod: TimeSpan.FromMilliseconds(10)
        );
        int actualSendCount = 0;
        using SemaphoreSlim releaseGate = new(0, int.MaxValue);
        using SemaphoreSlim enteredGate = new(0, int.MaxValue);

        async Task<bool> Send(CancellationToken ct)
        {
            Interlocked.Increment(ref actualSendCount);
            enteredGate.Release();
            await releaseGate.WaitAsync(ct); // hold the first sender in-flight so joiners must coalesce
            return true;
        }

        Task<bool> first = queue.EnqueueAsync("channel-1:twitch", "burst-line", Send);
        await enteredGate.WaitAsync(); // wait until the first call is actually in flight

        Task<bool>[] joiners = Enumerable
            .Range(0, 9)
            .Select(_ => queue.EnqueueAsync("channel-1:twitch", "burst-line", Send))
            .ToArray();

        releaseGate.Release();
        bool firstResult = await first;
        bool[] joinerResults = await Task.WhenAll(joiners);

        Assert.Equal(1, actualSendCount); // only the first call actually reached the "platform"
        Assert.True(firstResult);
        Assert.All(joinerResults, Assert.True); // every joiner still gets the honest, real outcome
    }
}
