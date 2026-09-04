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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S001b — a bot restart must not silently drop every viewer's pending song request. These tests prove
/// the durability contract end to end against a real SQLite database (self-host-lite's actual runtime,
/// not an InMemory-provider stand-in): a queue synced to disk survives a simulated process restart
/// (a brand-new <see cref="AppDbContext"/>/<see cref="SongRequestQueueStore"/> pair, exactly what a real
/// restart produces), tenant isolation holds across that restore, a mid-write "hard kill" (no commit)
/// recovers to the last COMMITTED state rather than a half-written one, and a stale queue is discarded —
/// with the channel told — rather than resurrected.
/// </summary>
public sealed class SongRequestQueuePersistenceTests
{
    private static readonly string ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000f2001")
        .ToString();
    private static readonly string ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000f2002")
        .ToString();

    [Fact]
    public async Task Queue_survives_a_simulated_restart_with_exact_order_and_requesters()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        // "Before restart": a live store gets three requests from two different requesters, synced
        // through the real persistence implementation exactly the way MusicService does after every
        // mutation.
        SongRequestQueueStore liveStore = new();
        SongRequestQueuePersistence livePersistence = new(fixture.Db);
        FairQueue<SongRequestEntry> live = liveStore.GetOrCreate(ChannelA);

        Enqueue(live, "viewer1", "track-1");
        await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);
        Enqueue(live, "viewer2", "track-2");
        await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);
        Enqueue(live, "viewer1", "track-3");
        await livePersistence.SyncAsync(ChannelA, live.GetSnapshot(), CancellationToken.None);

        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> beforeRestart =
            live.GetSnapshot();

        // "After restart": brand-new store (the singleton is gone — process just came back up) and a
        // fresh scoped AppDbContext over the same underlying database, exactly the shape a real restart
        // produces.
        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);
        SongRequestQueueStore restoredStore = new();

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );
        result.DiscardedStaleBroadcasterIds.Should().BeEmpty();
        RestoredSongRequestQueue restoredChannel = result.Channels.Should().ContainSingle().Subject;
        restoredStore.Restore(restoredChannel.BroadcasterId, restoredChannel.OrderedEntries);

        FairQueue<SongRequestEntry> restored = restoredStore.GetOrCreate(ChannelA);
        IReadOnlyList<(SongRequestEntry Item, int Rank, string OwnerKey)> afterRestart =
            restored.GetSnapshot();

        // Same entries, same order, same requesters, same fair-queue rank — not merely "three items".
        afterRestart.Should().HaveCount(3);
        afterRestart
            .Select(e => (e.Item.TrackUri, e.OwnerKey, e.Rank))
            .Should()
            .BeEquivalentTo(
                beforeRestart.Select(e => (e.Item.TrackUri, e.OwnerKey, e.Rank)),
                o => o.WithStrictOrdering()
            );
    }

    [Fact]
    public async Task Restoring_one_channel_never_touches_a_second_channels_queue()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        SongRequestQueuePersistence persistence = new(fixture.Db);

        FairQueue<SongRequestEntry> queueA = new();
        Enqueue(queueA, "viewerA", "track-a1");
        await persistence.SyncAsync(ChannelA, queueA.GetSnapshot(), CancellationToken.None);

        FairQueue<SongRequestEntry> queueB = new();
        Enqueue(queueB, "viewerB", "track-b1");
        Enqueue(queueB, "viewerB", "track-b2");
        await persistence.SyncAsync(ChannelB, queueB.GetSnapshot(), CancellationToken.None);

        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);
        SongRequestQueueStore restoredStore = new();

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );
        foreach (RestoredSongRequestQueue channel in result.Channels)
            restoredStore.Restore(channel.BroadcasterId, channel.OrderedEntries);

        restoredStore
            .GetOrCreate(ChannelA)
            .GetSnapshot()
            .Should()
            .ContainSingle()
            .Which.Item.TrackUri.Should()
            .Be("track-a1");
        restoredStore.GetOrCreate(ChannelB).GetSnapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task A_hard_kill_before_commit_recovers_the_last_committed_state_not_a_half_write()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        SongRequestQueuePersistence persistence = new(fixture.Db);

        // The one committed write before the "crash".
        FairQueue<SongRequestEntry> queue = new();
        Enqueue(queue, "viewer1", "track-1");
        await persistence.SyncAsync(ChannelA, queue.GetSnapshot(), CancellationToken.None);

        // Simulate a hard kill mid-write: open the transaction SyncAsync would use, delete the old rows
        // and insert the new ones, but never call CommitAsync — exactly what happens when the process is
        // killed between the delete and the commit. Disposing an uncommitted transaction rolls it back,
        // the same guarantee SQLite/Postgres give on an actual process kill (an in-flight, unfsynced
        // transaction is never left half-applied).
        await using (
            Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction crashedTx =
                await fixture.Db.Database.BeginTransactionAsync()
        )
        {
            await fixture
                .Db.SongRequestQueueItems.Where(r => r.BroadcasterId == ChannelA)
                .ExecuteDeleteAsync();
            fixture.Db.SongRequestQueueItems.Add(
                new()
                {
                    BroadcasterId = ChannelA,
                    Sequence = 0,
                    OwnerKey = "viewer2",
                    TrackUri = "track-2-never-committed",
                    TrackName = "Never Committed",
                    Artist = "Nobody",
                    DurationMs = 1000,
                    CreatedAt = DateTime.UtcNow,
                }
            );
            await fixture.Db.SaveChangesAsync();
            // No CommitAsync — the "kill" happens here.
        }

        using AppDbContext restartedDb = fixture.OpenNewScope();
        SongRequestQueuePersistence restoredPersistence = new(restartedDb);

        SongRequestQueueRestoreResult result = await restoredPersistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );

        RestoredSongRequestQueue channel = result.Channels.Should().ContainSingle().Subject;
        // The last COMMITTED write (track-1) survives; the never-committed write is gone entirely —
        // never a half state mixing both.
        channel
            .OrderedEntries.Should()
            .ContainSingle()
            .Which.Entry.TrackUri.Should()
            .Be("track-1");
    }

    [Fact]
    public async Task A_queue_untouched_since_before_the_freshness_window_is_discarded_not_restored()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        fixture.Db.SongRequestQueueItems.Add(
            new()
            {
                BroadcasterId = ChannelA,
                Sequence = 0,
                OwnerKey = "viewer1",
                TrackUri = "stale-track",
                TrackName = "Stale",
                Artist = "Old",
                DurationMs = 1000,
                // Three days old — a full stream length past the freshness window.
                CreatedAt = DateTime.UtcNow - TimeSpan.FromDays(3),
            }
        );
        await fixture.Db.SaveChangesAsync();

        SongRequestQueuePersistence persistence = new(fixture.Db);
        SongRequestQueueRestoreResult result = await persistence.LoadForRestoreAsync(
            SongRequestQueueRestoreHostedService.FreshnessWindow,
            CancellationToken.None
        );

        result.Channels.Should().BeEmpty();
        result.DiscardedStaleBroadcasterIds.Should().ContainSingle().Which.Should().Be(ChannelA);

        // The stale rows are purged, not merely skipped — a later restore attempt (e.g. after another
        // restart) must not keep re-discovering the same dead rows.
        (await fixture.Db.SongRequestQueueItems.CountAsync())
            .Should()
            .Be(0);
    }

    [Fact]
    public async Task A_discarded_stale_channel_is_told_via_a_published_event()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        fixture.Db.SongRequestQueueItems.Add(
            new()
            {
                BroadcasterId = Guid.Parse("0192a000-0000-7000-8000-0000000f2003").ToString(),
                Sequence = 0,
                OwnerKey = "viewer1",
                TrackUri = "stale-track",
                TrackName = "Stale",
                Artist = "Old",
                DurationMs = 1000,
                CreatedAt = DateTime.UtcNow - TimeSpan.FromDays(3),
            }
        );
        await fixture.Db.SaveChangesAsync();

        ServiceCollection services = new();
        services.AddSingleton<ISongRequestQueuePersistence>(
            new SongRequestQueuePersistence(fixture.Db)
        );
        services.AddSingleton<ISongRequestQueueStore, SongRequestQueueStore>();
        RecordingEventBus bus = new();
        services.AddSingleton<IEventBus>(bus);
        services.AddSingleton<IRunOnceGuard>(new SharedFakeRunOnceGuard());
        using ServiceProvider provider = services.BuildServiceProvider();

        SongRequestQueueRestoreHostedService sut = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SongRequestQueueRestoreHostedService>.Instance
        );

        await sut.StartAsync(CancellationToken.None);

        bus.Published.OfType<SongRequestQueueRestoreDiscardedEvent>()
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("discarded");
    }

    private static void Enqueue(
        FairQueue<SongRequestEntry> queue,
        string ownerKey,
        string trackUri
    ) =>
        queue.Enqueue(
            ownerKey,
            new(trackUri, $"Track {trackUri}", "Artist", null, 200000, ownerKey, 0, null, "")
        );
}
