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
using NomNomzBot.Infrastructure.Platform.Persistence;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S-SR-INFLIGHT-DURABLE — the in-memory in-flight marker
/// (<c>SongRequestQueueStore.GetInFlight</c>) used to have no durable counterpart, so a process restart
/// forgot which request had already been handed to the provider. On the next tick
/// <c>SongRequestQueueReconciler</c> saw an empty in-flight slot and handed the SAME head entry to the
/// provider a second time — the track already playing got queued again, repeated on every crash-restart
/// (11 times, live, on 2026-08-25). These tests prove the fix end to end against a real SQLite database
/// (mirroring <see cref="SongRequestQueuePersistenceTests"/>'s "simulated restart" shape): the in-flight
/// entry survives a restart without being re-handed, and is still correctly cleared once playback moves
/// on so the queue does not strand forever with a phantom in-flight entry.
/// </summary>
public sealed class SongRequestQueueInFlightDurabilityTests
{
    private static readonly string ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000f3001")
        .ToString();

    [Fact]
    public async Task The_in_flight_request_is_not_re_handed_over_after_a_simulated_restart()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        // Before restart: two requests queued, the head one already handed to the provider — exactly
        // what MusicService.HandOverNextAsync does: SetInFlight, then a write-through sync that now
        // carries the in-flight entry too.
        SongRequestQueueStore liveStore = new();
        SongRequestQueuePersistence livePersistence = new(fixture.Db);
        FairQueue<SongRequestEntry> live = liveStore.GetOrCreate(ChannelA);

        SongRequestEntry track1 = Entry("track-1", "viewer1");
        SongRequestEntry track2 = Entry("track-2", "viewer2");
        live.Enqueue("viewer1", track1);
        live.Enqueue("viewer2", track2);
        await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);

        SongRequestEntry? handedOver = live.Peek();
        handedOver.Should().NotBeNull();
        liveStore.SetInFlight(ChannelA, handedOver);
        await livePersistence.SyncAsync(
            ChannelA,
            live.GetSnapshot(),
            CancellationToken.None,
            handedOver
        );

        // "After restart": a brand-new store and a fresh scoped AppDbContext over the same underlying
        // database — the singleton in-memory store is gone, exactly the shape a real restart produces.
        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);
        SongRequestQueueStore restoredStore = new();

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );
        RestoredSongRequestQueue restoredChannel = result.Channels.Should().ContainSingle().Subject;
        restoredChannel.InFlightIndex.Should().Be(0);

        restoredStore.Restore(
            restoredChannel.BroadcasterId,
            restoredChannel.OrderedEntries,
            restoredChannel.InFlightIndex
        );

        // The restored in-flight entry is the SAME logical request (track-1) — not lost, not replaced by
        // the wrong entry.
        SongRequestEntry? restoredInFlight = restoredStore.GetInFlight(ChannelA);
        restoredInFlight.Should().NotBeNull();
        restoredInFlight!.TrackUri.Should().Be("track-1");

        // It is also the SAME object reference as the entry that sits at the head of the restored
        // queue — the invariant SongRequestQueueReconciler's cleanup relies on
        // (`ReferenceEquals(e.Item, inFlight)`), otherwise the very next playback tick after a restart
        // would immediately look like the in-flight entry had "disappeared" and clear it — putting the
        // bot right back to re-handing the head entry over.
        FairQueue<SongRequestEntry> restoredQueue = restoredStore.GetOrCreate(ChannelA);
        ReferenceEquals(restoredQueue.Peek(), restoredInFlight).Should().BeTrue();

        // The behavioural proof: a hand-over attempt after restart must be a no-op — GetInFlight is
        // still non-null, which is exactly the guard MusicService.HandOverNextAsync checks before ever
        // touching the provider again. Simulating that guard directly proves track-1 will not be pushed
        // to the provider a second time.
        restoredStore
            .GetInFlight(ChannelA)
            .Should()
            .NotBeNull(
                "a non-null in-flight marker after restart must block a second hand-over of the same track"
            );

        // The rest of the queue is untouched and still resumes at track-2 once track-1 completes.
        restoredQueue
            .GetSnapshot()
            .Select(e => e.Item.TrackUri)
            .Should()
            .ContainInOrder("track-1", "track-2");
    }

    [Fact]
    public async Task The_in_flight_marker_clears_on_completion_and_the_queue_resumes_at_the_next_entry()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        SongRequestQueueStore store = new();
        SongRequestQueuePersistence persistence = new(fixture.Db);
        FairQueue<SongRequestEntry> queue = store.GetOrCreate(ChannelA);

        SongRequestEntry track1 = Entry("track-1", "viewer1");
        SongRequestEntry track2 = Entry("track-2", "viewer2");
        queue.Enqueue("viewer1", track1);
        queue.Enqueue("viewer2", track2);

        store.SetInFlight(ChannelA, track1);
        await persistence.SyncAsync(ChannelA, queue.GetSnapshot(), CancellationToken.None, track1);

        // track-1 finishes playing: the reconciler drops it from the queue and clears the in-flight
        // marker (SongRequestQueueReconciler.HandleAsync), then hands the next one over.
        queue.RemoveThrough(e => e.TrackUri == "track-1");
        store.SetInFlight(ChannelA, null);
        SongRequestEntry? next = queue.Peek();
        next.Should().NotBeNull();
        store.SetInFlight(ChannelA, next);
        await persistence.SyncAsync(ChannelA, queue.GetSnapshot(), CancellationToken.None, next);

        // Restart again: the durable state must show ONLY track-2, in-flight, never a stranded/ghost
        // reference to the finished track-1 — proving the marker was actually cleared, not merely
        // overwritten in memory while a stale row lingered on disk.
        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);
        SongRequestQueueStore restoredStore = new();

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );
        RestoredSongRequestQueue restoredChannel = result.Channels.Should().ContainSingle().Subject;
        restoredStore.Restore(
            restoredChannel.BroadcasterId,
            restoredChannel.OrderedEntries,
            restoredChannel.InFlightIndex
        );

        FairQueue<SongRequestEntry> restoredQueue = restoredStore.GetOrCreate(ChannelA);
        restoredQueue
            .GetSnapshot()
            .Should()
            .ContainSingle()
            .Which.Item.TrackUri.Should()
            .Be("track-2");

        SongRequestEntry? restoredInFlight = restoredStore.GetInFlight(ChannelA);
        restoredInFlight.Should().NotBeNull();
        restoredInFlight!.TrackUri.Should().Be("track-2");
    }

    private static SongRequestEntry Entry(string trackUri, string requestedBy) =>
        new(trackUri, $"Track {trackUri}", "Artist", null, 200000, requestedBy);
}
