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
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

public class FairQueueTests
{
    // ─── Basic enqueue / dequeue ─────────────────────────────────────────────

    [Fact]
    public void Dequeue_SingleItem_ReturnsItem()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "track-a");

        q.Dequeue().Should().Be("track-a");
        q.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Dequeue_Empty_ReturnsDefault()
    {
        FairQueue<string> q = new();
        q.Dequeue().Should().BeNull();
    }

    [Fact]
    public void Count_ReflectsQueuedItems()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "a");
        q.Enqueue("alice", "b");
        q.Enqueue("bob", "c");

        q.Count.Should().Be(3);
    }

    // ─── Fairness algorithm ──────────────────────────────────────────────────

    [Fact]
    public void Enqueue_TwoOwners_InterleavesBeforeRepeat()
    {
        FairQueue<string> q = new();
        // alice adds 2, bob adds 1
        q.Enqueue("alice", "a1");
        q.Enqueue("alice", "a2");
        q.Enqueue("bob", "b1");

        // Expected order: a1, b1, a2  (rank 1 items first, then rank 2)
        q.Dequeue().Should().Be("a1");
        q.Dequeue().Should().Be("b1");
        q.Dequeue().Should().Be("a2");
    }

    [Fact]
    public void Enqueue_ThreeOwners_RoundRobinOrdering()
    {
        FairQueue<string> q = new();
        // Each user enqueues 2 songs
        q.Enqueue("alice", "a1");
        q.Enqueue("alice", "a2");
        q.Enqueue("bob", "b1");
        q.Enqueue("bob", "b2");
        q.Enqueue("carol", "c1");
        q.Enqueue("carol", "c2");

        // Round 1: all rank-1 items (a1, b1, c1) come before round 2 (a2, b2, c2)
        List<string?> dequeued = [.. Enumerable.Range(0, 6).Select(_ => q.Dequeue())];

        dequeued.Should().BeEquivalentTo(["a1", "a2", "b1", "b2", "c1", "c2"]);

        // The first three should be the round-1 items, in insertion order
        dequeued[..3]
            .Should()
            .BeEquivalentTo(
                ["a1", "b1", "c1"],
                "rank-1 items from all owners come before rank-2 items"
            );
        dequeued[3..]
            .Should()
            .BeEquivalentTo(["a2", "b2", "c2"], "rank-2 items follow after all rank-1 items");
    }

    [Fact]
    public void Enqueue_SingleOwner_FifoOrder()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "1");
        q.Enqueue("alice", "2");
        q.Enqueue("alice", "3");

        q.Dequeue().Should().Be("1");
        q.Dequeue().Should().Be("2");
        q.Dequeue().Should().Be("3");
    }

    // ─── Dequeue recalculation ───────────────────────────────────────────────

    [Fact]
    public void Dequeue_AfterDequeuingFirstItem_RanksRecalculated()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "a1");
        q.Enqueue("alice", "a2");
        q.Enqueue("alice", "a3");

        q.Dequeue(); // removes a1, a2 becomes rank 1, a3 becomes rank 2

        // Now add a new item — should go after a2 (same rank 1 → end of rank-1 group)
        q.Enqueue("alice", "a4");

        // remaining: a2 (rank 1), a3 (rank 2) → a4 was added as rank 2 (alice already has a3)
        // so order: a2, a3, a4
        q.Dequeue().Should().Be("a2");
    }

    // ─── Peek ────────────────────────────────────────────────────────────────

    [Fact]
    public void Peek_DoesNotRemoveItem()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "a1");

        q.Peek().Should().Be("a1");
        q.Count.Should().Be(1);
    }

    // ─── Clear ───────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_EmptiesQueue()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "a1");
        q.Enqueue("bob", "b1");

        q.Clear();

        q.IsEmpty.Should().BeTrue();
        q.Count.Should().Be(0);
    }

    // ─── RemoveByOwner ───────────────────────────────────────────────────────

    [Fact]
    public void RemoveByOwner_RemovesAllItemsForOwner()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "a1");
        q.Enqueue("alice", "a2");
        q.Enqueue("bob", "b1");

        q.RemoveByOwner("alice").Should().Be(2);

        q.Count.Should().Be(1);
        q.Dequeue().Should().Be("b1");
    }

    [Fact]
    public void RemoveByOwner_OwnerNotPresent_ReturnsZero()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "a1");

        q.RemoveByOwner("nobody").Should().Be(0);
        q.Count.Should().Be(1);
    }

    // ─── GetSnapshot ─────────────────────────────────────────────────────────

    [Fact]
    public void GetSnapshot_ReturnsCurrentState()
    {
        FairQueue<string> q = new();
        q.Enqueue("alice", "a1");
        q.Enqueue("bob", "b1");

        IReadOnlyList<(string Item, int Rank, string OwnerKey)> snapshot = q.GetSnapshot();

        snapshot.Should().HaveCount(2);
        snapshot.Should().Contain(s => s.Item == "a1" && s.OwnerKey == "alice");
        snapshot.Should().Contain(s => s.Item == "b1" && s.OwnerKey == "bob");
    }

    // ─── Thread safety ───────────────────────────────────────────────────────

    [Fact]
    public async Task Enqueue_ConcurrentAccess_DoesNotCorruptState()
    {
        FairQueue<int> q = new();
        const int iterations = 100;

        IEnumerable<Task> tasks = Enumerable
            .Range(0, 10)
            .Select(ownerId =>
                Task.Run(() =>
                {
                    for (int i = 0; i < iterations; i++)
                        q.Enqueue($"owner{ownerId}", ownerId * iterations + i);
                })
            );

        await Task.WhenAll(tasks);

        q.Count.Should().Be(10 * iterations);
    }

    /// <summary>
    /// The 2026-08-25 duplicate-queue incident, as a test. Two API instances were briefly live, so the
    /// SAME song request was processed twice at once; the old code checked "is it already queued?" and
    /// then enqueued in a separate step, so both passes cleared the check and both inserted. One channel
    /// reached 2,644 rows for five distinct tracks and the dashboard showed the same songs repeatedly.
    /// Deciding and inserting under one lock must make exactly ONE of N concurrent attempts win.
    /// </summary>
    [Fact]
    public void TryEnqueueUnique_under_concurrency_admits_exactly_one_copy()
    {
        FairQueue<string> queue = new();
        const string track = "spotify:track:duplicate";
        int admitted = 0;

        Parallel.For(
            0,
            64,
            _ =>
            {
                if (queue.TryEnqueueUnique("viewer", track, queued => queued == track))
                    Interlocked.Increment(ref admitted);
            }
        );

        admitted.Should().Be(1, "only the first of 64 concurrent attempts may be admitted");
        queue
            .GetSnapshot()
            .Should()
            .HaveCount(1, "the queue must hold exactly one copy of the track");
    }

    [Fact]
    public void TryEnqueueUnique_admits_a_genuinely_different_track()
    {
        FairQueue<string> queue = new();

        queue.TryEnqueueUnique("viewer", "track:a", q => q == "track:a").Should().BeTrue();
        queue.TryEnqueueUnique("viewer", "track:b", q => q == "track:b").Should().BeTrue();

        queue.GetSnapshot().Should().HaveCount(2, "deduping must not block distinct tracks");
    }
}
