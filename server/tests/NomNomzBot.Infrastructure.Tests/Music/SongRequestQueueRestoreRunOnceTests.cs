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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves the multi-instance fix for the song-request queue restore pass: two API instances starting
/// against one database (a zero-downtime deploy overlap) must not both replay the persisted queue — a
/// second pass would double every restored channel's in-memory queue and double the discarded-stale-
/// queue notification. Gated by <see cref="IRunOnceGuard"/>; a non-holder must be a clean no-op.
/// </summary>
public sealed class SongRequestQueueRestoreRunOnceTests
{
    private static readonly string ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f3001")
        .ToString();

    private static (
        SongRequestQueueRestoreHostedService Service,
        ISongRequestQueueStore Store
    ) BuildInstance(SongRequestQueuePersistenceTestDbContext fixture, IRunOnceGuard guard)
    {
        AppDbContext scopedDb = fixture.OpenNewScope();
        ServiceCollection services = new();
        services.AddSingleton<ISongRequestQueuePersistence>(
            new SongRequestQueuePersistence(scopedDb)
        );
        SongRequestQueueStore store = new();
        services.AddSingleton<ISongRequestQueueStore>(store);
        services.AddSingleton<IEventBus>(new RecordingEventBus());
        services.AddSingleton(guard);
        ServiceProvider provider = services.BuildServiceProvider();

        SongRequestQueueRestoreHostedService service = new(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<SongRequestQueueRestoreHostedService>.Instance
        );
        return (service, store);
    }

    [Fact]
    public async Task An_instance_that_loses_the_startup_race_leaves_its_store_empty_while_the_winner_restores()
    {
        using SongRequestQueuePersistenceTestDbContext fixture =
            SongRequestQueuePersistenceTestDbContext.Create();

        SongRequestQueuePersistence seedPersistence = new(fixture.Db);
        FairQueue<SongRequestEntry> seedQueue = new();
        seedQueue.Enqueue(
            "viewer1",
            new("track-1", "Track One", "Artist", null, 200000, "viewer1")
        );
        await seedPersistence.SyncAsync(ChannelId, seedQueue.GetSnapshot(), CancellationToken.None);

        System.Collections.Concurrent.ConcurrentDictionary<string, byte> sharedLeaseStore = new();

        // Instance A is already restoring at startup — its lease sits on the shared store before
        // instance B's StartAsync ever runs.
        IAsyncDisposable? preHeldLease = await new SharedFakeRunOnceGuard(
            sharedLeaseStore
        ).TryAcquireAsync(
            SongRequestQueueRestoreHostedService.LeaseResourceName,
            TimeSpan.FromMinutes(5),
            CancellationToken.None
        );
        preHeldLease.Should().NotBeNull();

        (SongRequestQueueRestoreHostedService serviceB, ISongRequestQueueStore storeB) =
            BuildInstance(fixture, new SharedFakeRunOnceGuard(sharedLeaseStore));

        await serviceB.StartAsync(CancellationToken.None);

        // Instance B lost the race: a clean no-op — its own in-memory store never got the restore pass.
        storeB.GetOrCreate(ChannelId).GetSnapshot().Should().BeEmpty();

        await preHeldLease.DisposeAsync();

        (SongRequestQueueRestoreHostedService serviceA, ISongRequestQueueStore storeA) =
            BuildInstance(fixture, new SharedFakeRunOnceGuard(sharedLeaseStore));

        await serviceA.StartAsync(CancellationToken.None);

        // Instance A won the (now-free) lease: exactly one restore pass took effect, and it landed here.
        storeA
            .GetOrCreate(ChannelId)
            .GetSnapshot()
            .Should()
            .ContainSingle()
            .Which.Item.TrackUri.Should()
            .Be("track-1");
    }
}
