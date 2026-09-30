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
using FluentAssertions;
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
/// The song-request restore refills THIS process's in-memory queue, so every instance runs it — a
/// blue/green overlap included. It once sat behind the cluster lease, and the colour that lost the startup
/// race served an empty queue. The one deployment-wide effect, the "stale queue discarded" notice, must
/// still reach the streamer once, not once per colour.
/// </summary>
public sealed class SongRequestQueueRestoreHostedServiceTests
{
    private static readonly string FreshChannel = Guid.Parse("0192a000-0000-7000-8000-0000000f3001")
        .ToString();

    private static readonly Guid StaleChannel = Guid.Parse("0192a000-0000-7000-8000-0000000f3002");

    private static (
        SongRequestQueueRestoreHostedService Service,
        ISongRequestQueueStore Store,
        RecordingEventBus Bus
    ) BuildInstance(SongRequestQueuePersistenceTestDbContext fixture, IRunOnceGuard guard)
    {
        AppDbContext scopedDb = fixture.OpenNewScope();
        ServiceCollection services = new();
        services.AddSingleton<ISongRequestQueuePersistence>(
            new SongRequestQueuePersistence(scopedDb)
        );
        SongRequestQueueStore store = new();
        services.AddSingleton<ISongRequestQueueStore>(store);
        RecordingEventBus bus = new();
        services.AddSingleton<IEventBus>(bus);
        services.AddSingleton(guard);
        ServiceProvider provider = services.BuildServiceProvider();

        SongRequestQueueRestoreHostedService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SongRequestQueueRestoreHostedService>.Instance
        );
        return (service, store, bus);
    }

    [Fact]
    public async Task Both_colours_restore_their_own_queue_and_the_stale_notice_is_sent_once()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        FairQueue<SongRequestEntry> seedQueue = new();
        seedQueue.Enqueue(
            "viewer1",
            new("track-1", "Track One", "Artist", null, 200000, "viewer1", 0, null, "")
        );
        await new SongRequestQueuePersistence(fixture.Db).SyncAsync(
            FreshChannel,
            seedQueue.GetSnapshot(),
            CancellationToken.None
        );
        fixture.Db.SongRequestQueueItems.Add(
            new()
            {
                BroadcasterId = StaleChannel.ToString(),
                Sequence = 0,
                OwnerKey = "viewer2",
                TrackUri = "stale-track",
                TrackName = "Stale",
                Artist = "Old",
                DurationMs = 1000,
                CreatedAt = DateTime.UtcNow - TimeSpan.FromDays(3),
            }
        );
        await fixture.Db.SaveChangesAsync();

        // One lease store for both colours, with the old restore lease name held — the exact moment the
        // leased version left the second colour's queue empty.
        ConcurrentDictionary<string, byte> sharedLeaseStore = new();
        await using IAsyncDisposable? otherColourBooting = await new SharedFakeRunOnceGuard(
            sharedLeaseStore
        ).TryAcquireAsync("song-request-queue-restore", TimeSpan.FromMinutes(5));

        (
            SongRequestQueueRestoreHostedService blue,
            ISongRequestQueueStore blueStore,
            RecordingEventBus blueBus
        ) = BuildInstance(fixture, new SharedFakeRunOnceGuard(sharedLeaseStore));
        (
            SongRequestQueueRestoreHostedService green,
            ISongRequestQueueStore greenStore,
            RecordingEventBus greenBus
        ) = BuildInstance(fixture, new SharedFakeRunOnceGuard(sharedLeaseStore));

        await blue.StartAsync(CancellationToken.None);
        await green.StartAsync(CancellationToken.None);

        foreach (ISongRequestQueueStore store in new[] { blueStore, greenStore })
            store
                .GetOrCreate(FreshChannel)
                .GetSnapshot()
                .Should()
                .ContainSingle()
                .Which.Item.TrackUri.Should()
                .Be("track-1");

        List<SongRequestQueueRestoreDiscardedEvent> notices =
        [
            .. blueBus.Published.OfType<SongRequestQueueRestoreDiscardedEvent>(),
            .. greenBus.Published.OfType<SongRequestQueueRestoreDiscardedEvent>(),
        ];
        notices.Should().ContainSingle().Which.BroadcasterId.Should().Be(StaleChannel);
    }
}
