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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.EventStore;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Behavior tests for the projection runner: that an incremental fold and a reset→replay rebuild produce the
/// SAME read-model state purely from the journal, that a stale event shape is upcast before the projection sees
/// it, and that the checkpoint advances/lags as expected. These assert the folded consequence, not call counts.
/// </summary>
public sealed class ProjectionRunnerTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventJournalService NewJournal(EventStoreTestDbContext db) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            Clock,
            new PassthroughEventPayloadProtector()
        );

    private static AppendEventRequest CounterEvent(
        Guid tenant,
        long amount,
        int version,
        string field
    ) =>
        new(
            EventId: Guid.NewGuid(),
            BroadcasterId: tenant,
            EventType: "counter.incremented",
            EventVersion: version,
            Source: "domain",
            PayloadJson: $"{{\"key\":\"hits\",\"{field}\":{amount}}}",
            MetadataJson: "{}",
            OccurredAt: new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc)
        );

    [Fact]
    public async Task RunOnce_FoldsSubscribedEvents_AndAdvancesCheckpoint()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(CounterEvent(tenant, 5, version: 2, field: "amount"));
        await journal.AppendAsync(CounterEvent(tenant, 7, version: 2, field: "amount"));

        CounterProjection projection = new();
        ProjectionRunner runner = new(
            [projection],
            journal,
            new EventUpcasterRegistry([]),
            db,
            Clock
        );

        Result<long> applied = await runner.RunOnceAsync(CounterProjection.ProjectionName, tenant);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(2);
        projection.State.Should().ContainKey("hits").WhoseValue.Should().Be(12);

        Result<ProjectionCheckpointDto> checkpoint = await runner.GetCheckpointAsync(
            CounterProjection.ProjectionName,
            tenant
        );
        checkpoint.Value.LastPosition.Should().Be(2);
        checkpoint.Value.HeadPosition.Should().Be(2);
        checkpoint.Value.Lag.Should().Be(0);
        checkpoint.Value.Status.Should().Be("running");
    }

    [Fact]
    public async Task Rebuild_ResetThenReplay_ReconstructsIdenticalStateFromJournalAlone()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(CounterEvent(tenant, 5, version: 2, field: "amount"));
        await journal.AppendAsync(CounterEvent(tenant, 7, version: 2, field: "amount"));
        await journal.AppendAsync(CounterEvent(tenant, 3, version: 2, field: "amount"));

        CounterProjection projection = new();
        ProjectionRunner runner = new(
            [projection],
            journal,
            new EventUpcasterRegistry([]),
            db,
            Clock
        );

        // Incremental fold to the head.
        await runner.RunOnceAsync(CounterProjection.ProjectionName, tenant);
        long incrementalState = projection.State["hits"];
        incrementalState.Should().Be(15);

        // Now mutate the in-memory model to a wrong value, then rebuild: reset wipes it, replay re-derives it
        // purely from the journal. The rebuilt state must equal the incremental fold.
        projection.State["hits"] = 9999;

        Result<long> rebuilt = await runner.RebuildAsync(CounterProjection.ProjectionName, tenant);

        rebuilt.IsSuccess.Should().BeTrue(rebuilt.ErrorMessage);
        rebuilt.Value.Should().Be(3, "all three journal rows are replayed from position 0");
        projection
            .State["hits"]
            .Should()
            .Be(
                incrementalState,
                "reset→replay reconstructs the same state the incremental fold produced"
            );

        Result<ProjectionCheckpointDto> checkpoint = await runner.GetCheckpointAsync(
            CounterProjection.ProjectionName,
            tenant
        );
        checkpoint.Value.LastPosition.Should().Be(3, "the rebuilt checkpoint is back at the head");
        checkpoint.Value.Lag.Should().Be(0);
    }

    [Fact]
    public async Task Replay_UpcastsStaleEventShape_BeforeApply()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        // A v1 row stores the increment under "value"; the v1->v2 upcaster renames it to "amount". The
        // projection only understands "amount", so without upcasting it would read 0 and the total would be wrong.
        await journal.AppendAsync(CounterEvent(tenant, 4, version: 1, field: "value"));
        await journal.AppendAsync(CounterEvent(tenant, 6, version: 2, field: "amount"));

        CounterProjection projection = new();
        EventUpcasterRegistry registry = new([new CounterV1ToV2Upcaster()]);
        ProjectionRunner runner = new([projection], journal, registry, db, Clock);

        Result<long> applied = await runner.RunOnceAsync(CounterProjection.ProjectionName, tenant);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        projection
            .State["hits"]
            .Should()
            .Be(
                10,
                "the v1 row was upcast (value->amount=4) and summed with the v2 row (amount=6)"
            );
    }

    [Fact]
    public async Task PauseThenResume_TogglesCheckpointStatus()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(CounterEvent(tenant, 1, version: 2, field: "amount"));

        CounterProjection projection = new();
        ProjectionRunner runner = new(
            [projection],
            journal,
            new EventUpcasterRegistry([]),
            db,
            Clock
        );
        await runner.RunOnceAsync(CounterProjection.ProjectionName, tenant);

        await runner.PauseAsync(CounterProjection.ProjectionName, tenant);
        (await runner.GetCheckpointAsync(CounterProjection.ProjectionName, tenant))
            .Value.Status.Should()
            .Be("paused");

        await runner.ResumeAsync(CounterProjection.ProjectionName, tenant);
        (await runner.GetCheckpointAsync(CounterProjection.ProjectionName, tenant))
            .Value.Status.Should()
            .Be("running");
    }
}
