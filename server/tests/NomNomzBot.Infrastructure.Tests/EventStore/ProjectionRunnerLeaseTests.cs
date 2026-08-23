// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Threading;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// S004g — a manual replay/rebuild must not race the projection driver's own tick for the SAME projection+scope
/// (event-store.md §3.3). These tests exercise the actual contention through <see cref="ProjectionRunner"/> and
/// the real self-host <see cref="NoOpRunOnceGuard"/> (a static, process-wide named lock), not a mocked lease:
/// a first caller is held mid-flight by a gated projection while a second caller races it for the same key.
/// </summary>
public sealed class ProjectionRunnerLeaseTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 8, 23, 12, 0, 0, TimeSpan.Zero));

    private static EventJournalService NewJournal(EventStoreTestDbContext db) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            Clock,
            new PassthroughEventPayloadProtector(),
            Substitute.For<ICurrentUserService>()
        );

    [Fact]
    public async Task Rebuild_RefusedForSameBroadcasterAndProjection_WhileFirstRebuildIsInFlight()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        // A fresh, uniquely-named guard resource per test run (the guard's lock table is process-static) —
        // achieved by giving the two competing projections in each test their own distinct projection Name.
        GatedProjection gated = new("s004g.rebuild-refused");
        NoOpRunOnceGuard guard = new();
        ProjectionRunner runner = new(
            [gated],
            journal,
            new EventUpcasterRegistry([]),
            db,
            Clock,
            guard
        );

        // Start the first rebuild; it blocks inside ResetAsync until released, holding the lease the whole time.
        Task<Result<long>> firstRebuild = runner.RebuildAsync(gated.Name, tenant);
        await gated.EnteredReset.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // A second rebuild for the EXACT SAME key (same projection name + same broadcaster) must be refused
        // immediately with an honest, typed "already running" result — never a silent no-op and never a hang.
        Result<long> secondRebuild = await runner
            .RebuildAsync(gated.Name, tenant)
            .WaitAsync(TimeSpan.FromSeconds(5));

        secondRebuild
            .IsFailure.Should()
            .BeTrue("the driver/manual rebuild race must be refused, not silent");
        secondRebuild.ErrorCode.Should().Be("PROJECTION_RUN_IN_PROGRESS");

        // Release the first rebuild and confirm it still completes successfully once it has the lease to itself.
        gated.Release();
        Result<long> firstResult = await firstRebuild.WaitAsync(TimeSpan.FromSeconds(5));
        firstResult.IsSuccess.Should().BeTrue(firstResult.ErrorMessage);
    }

    [Fact]
    public async Task Rebuild_ForDifferentBroadcaster_ProceedsUnaffected_WhileAnotherIsInFlight()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        // Two independent DbContexts (and runners) over the same underlying database, mirroring the two real
        // call sites — the driver's own scoped context and the controller's request-scoped context — that must
        // NOT share a single DbContext instance across concurrent operations.
        NoOpRunOnceGuard guard = new();
        GatedProjection gated = new("s004g.different-tenant");

        await using EventStoreTestDbContext dbA = database.NewContext();
        ProjectionRunner runnerA = new(
            [gated],
            NewJournal(dbA),
            new EventUpcasterRegistry([]),
            dbA,
            Clock,
            guard
        );

        await using EventStoreTestDbContext dbB = database.NewContext();
        ProjectionRunner runnerB = new(
            [gated],
            NewJournal(dbB),
            new EventUpcasterRegistry([]),
            dbB,
            Clock,
            guard
        );

        // Hold the lease for tenant A.
        Task<Result<long>> forTenantA = runnerA.RebuildAsync(gated.Name, tenantA);
        await gated.EnteredReset.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Tenant B's rebuild of the SAME projection is a different key (projection+broadcaster) and must proceed
        // — release its own gate immediately so it doesn't deadlock behind tenant A's still-held one.
        gated.Release();
        Result<long> forTenantB = await runnerB
            .RebuildAsync(gated.Name, tenantB)
            .WaitAsync(TimeSpan.FromSeconds(5));

        forTenantB
            .IsSuccess.Should()
            .BeTrue(
                "a different broadcaster+projection key is unaffected by tenant A's held lease"
            );

        Result<long> tenantAResult = await forTenantA.WaitAsync(TimeSpan.FromSeconds(5));
        tenantAResult.IsSuccess.Should().BeTrue(tenantAResult.ErrorMessage);
    }

    [Fact]
    public async Task NoOpRunOnceGuard_NeverAdmitsTwoConcurrentHoldersOfTheSameKey()
    {
        // Proves the self-host in-process lock itself: fire two acquires for the SAME resource concurrently and
        // assert the OBSERVED execution count under the lease never exceeds one at a time — not merely that one
        // TryAcquireAsync call returned null, but that the guarded work bodies never overlapped in time.
        NoOpRunOnceGuard guard = new();
        const string resource = "s004g.guard-mutual-exclusion";

        int concurrentHolders = 0;
        int maxObservedConcurrency = 0;
        int successfulAcquisitions = 0;
        object gate = new();

        async Task<bool> TryHoldBrieflyAsync()
        {
            IAsyncDisposable? lease = await guard.TryAcquireAsync(
                resource,
                TimeSpan.FromMinutes(1)
            );
            if (lease is null)
                return false;

            await using (lease)
            {
                lock (gate)
                {
                    concurrentHolders++;
                    maxObservedConcurrency = Math.Max(maxObservedConcurrency, concurrentHolders);
                }

                await Task.Delay(50);

                lock (gate)
                {
                    concurrentHolders--;
                }
            }

            Interlocked.Increment(ref successfulAcquisitions);
            return true;
        }

        Task<bool>[] attempts = Enumerable.Range(0, 8).Select(_ => TryHoldBrieflyAsync()).ToArray();
        bool[] results = await Task.WhenAll(attempts);

        maxObservedConcurrency
            .Should()
            .Be(
                1,
                "the in-process lock must serialize every holder of the same key, one at a time"
            );
        results
            .Count(r => r)
            .Should()
            .Be(
                1,
                "exactly one of the racing acquisitions wins the non-reentrant lock; the rest are refused"
            );
        successfulAcquisitions.Should().Be(1);
    }

    /// <summary>
    /// A projection double whose <see cref="ResetAsync"/> signals <see cref="EnteredReset"/> the moment it starts,
    /// then blocks until the test calls <see cref="Release"/> — so a test can deterministically observe "rebuild A
    /// is currently in flight" before racing rebuild B against it, instead of guessing with a sleep.
    /// </summary>
    private sealed class GatedProjection(string name) : IProjection
    {
        public TaskCompletionSource EnteredReset { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource _released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public string Name { get; } = name;
        public bool IsGlobal => false;
        public IReadOnlySet<string> SubscribedEventTypes { get; } = new HashSet<string>();

        public void Release() => _released.TrySetResult();

        public Task<Result> ApplyAsync(
            EventRecord @event,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success());

        public async Task<Result> ResetAsync(
            Guid? broadcasterId,
            CancellationToken cancellationToken = default
        )
        {
            EnteredReset.TrySetResult();
            await _released.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            return Result.Success();
        }
    }
}
