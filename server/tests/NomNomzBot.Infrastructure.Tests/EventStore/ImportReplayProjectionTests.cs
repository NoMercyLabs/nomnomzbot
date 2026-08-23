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
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.EventStore;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// Proves "replay" (what Stoney asked for: hitting replay actually re-runs a migrated channel's history through
/// the real handler path). Driven end-to-end via the SAME <see cref="ProjectionRunner"/>/<see cref="EventJournalService"/>
/// stack production uses (event-store.md §3.3) — this isn't a mock of the plumbing, it's the plumbing. Proves:
/// an imported event republishes as the SAME concrete domain event (identity preserved — see the "distinctions
/// that can silently collapse" note below), a live ("domain"-sourced) event is never re-fired, an unrecognized
/// or malformed imported row is skipped without failing the whole catch-up run, and running Replay twice in a
/// row is a safe no-op (the checkpoint already caught up) — the concrete guard against "make sure I cannot
/// sneak in events from someone else": a SECOND tenant's own import never appears in the FIRST tenant's replay.
/// </summary>
public sealed class ImportReplayProjectionTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private static EventJournalService NewJournal(EventStoreTestDbContext db) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            Clock,
            new PassthroughEventPayloadProtector(),
            Substitute.For<ICurrentUserService>()
        );

    private static ProjectionRunner NewRunner(
        EventStoreTestDbContext db,
        EventJournalService journal,
        ImportReplayProjection projection
    ) => new([projection], journal, new EventUpcasterRegistry([]), db, Clock);

    private static AppendEventRequest ImportedFollow(Guid tenant, string userId)
    {
        Guid eventId = Guid.NewGuid();
        return new(
            EventId: eventId,
            BroadcasterId: tenant,
            EventType: nameof(FollowEvent),
            EventVersion: 1,
            Source: "import",
            PayloadJson: JsonPayload(eventId, tenant, userId),
            MetadataJson: "{}",
            OccurredAt: new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc)
        );
    }

    private static string JsonPayload(Guid eventId, Guid tenant, string userId) =>
        Newtonsoft.Json.JsonConvert.SerializeObject(
            new FollowEvent
            {
                EventId = eventId,
                BroadcasterId = tenant,
                OccurredAt = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
                UserId = userId,
                UserDisplayName = "Viewer " + userId,
                UserLogin = "viewer" + userId,
                FollowedAt = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );

    [Fact]
    public async Task RunOnce_ImportedEvent_RepublishesTheSameConcreteEventThroughTheBus()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        AppendEventRequest request = ImportedFollow(tenant, "viewer-42");
        await journal.AppendAsync(request);

        RecordingEventBus bus = new();
        ImportReplayProjection projection = new(
            bus,
            new(),
            NullLogger<ImportReplayProjection>.Instance
        );
        ProjectionRunner runner = NewRunner(db, journal, projection);

        Result<long> applied = await runner.RunOnceAsync(projection.Name, tenant);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(1);
        bus.Published.Should().ContainSingle();
        FollowEvent published = bus.Published[0].Should().BeOfType<FollowEvent>().Subject;
        // Distinctions that must NOT collapse: the republished event carries the SAME EventId/tenant as the
        // journal row, not a fresh identity — a count check alone would miss a swapped/duplicated id.
        published.EventId.Should().Be(request.EventId);
        published.BroadcasterId.Should().Be(tenant);
        published.UserId.Should().Be("viewer-42");
    }

    [Fact]
    public async Task RunOnce_LiveSourcedEvent_IsNeverRepublished()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(ImportedFollow(tenant, "viewer-1") with { Source = "domain" });

        RecordingEventBus bus = new();
        ImportReplayProjection projection = new(
            bus,
            new(),
            NullLogger<ImportReplayProjection>.Instance
        );
        ProjectionRunner runner = NewRunner(db, journal, projection);

        Result<long> applied = await runner.RunOnceAsync(projection.Name, tenant);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(1); // counted as applied so the checkpoint advances past it
        bus.Published.Should().BeEmpty(); // but never republished — it already ran live
    }

    [Fact]
    public async Task RunOnce_UnknownEventType_SkipsWithoutFailingTheRun()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(
            ImportedFollow(tenant, "viewer-1") with
            {
                EventId = Guid.NewGuid(),
                EventType = "SomeEventTypeThatWasRemovedOrNeverExisted",
            }
        );
        await journal.AppendAsync(ImportedFollow(tenant, "viewer-2"));

        RecordingEventBus bus = new();
        ImportReplayProjection projection = new(
            bus,
            new(),
            NullLogger<ImportReplayProjection>.Instance
        );
        ProjectionRunner runner = NewRunner(db, journal, projection);

        Result<long> applied = await runner.RunOnceAsync(projection.Name, tenant);

        applied.IsSuccess.Should().BeTrue(applied.ErrorMessage);
        applied.Value.Should().Be(2);
        bus.Published.Should().ContainSingle(); // only the recognizable one republished
        bus.Published[0].Should().BeOfType<FollowEvent>();
    }

    [Fact]
    public async Task RunOnce_CalledTwiceWithNothingNewImported_IsASafeNoOpTheSecondTime()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(ImportedFollow(tenant, "viewer-1"));

        RecordingEventBus bus = new();
        ImportReplayProjection projection = new(
            bus,
            new(),
            NullLogger<ImportReplayProjection>.Instance
        );
        ProjectionRunner runner = NewRunner(db, journal, projection);

        await runner.RunOnceAsync(projection.Name, tenant);
        bus.Published.Should().ContainSingle();

        Result<long> second = await runner.RunOnceAsync(projection.Name, tenant);

        second.IsSuccess.Should().BeTrue(second.ErrorMessage);
        second.Value.Should().Be(0); // checkpoint already at head — nothing new to apply
        bus.Published.Should().ContainSingle(); // still exactly one — never re-fired
    }

    [Fact]
    public async Task RunOnce_ScopedToOneTenant_NeverSeesAnotherTenantsImportedEvents()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);
        await journal.AppendAsync(ImportedFollow(tenantA, "a-viewer"));
        await journal.AppendAsync(ImportedFollow(tenantB, "b-viewer"));

        RecordingEventBus bus = new();
        ImportReplayProjection projection = new(
            bus,
            new(),
            NullLogger<ImportReplayProjection>.Instance
        );
        ProjectionRunner runner = NewRunner(db, journal, projection);

        await runner.RunOnceAsync(projection.Name, tenantA);

        bus.Published.Should().ContainSingle();
        FollowEvent published = bus.Published[0].Should().BeOfType<FollowEvent>().Subject;
        published.BroadcasterId.Should().Be(tenantA);
        published.UserId.Should().Be("a-viewer"); // never tenant B's imported follow
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<IDomainEvent> Published { get; } = [];

        public Task PublishAsync<TEvent>(
            TEvent @event,
            CancellationToken cancellationToken = default
        )
            where TEvent : class, IDomainEvent
        {
            Published.Add(@event);
            return Task.CompletedTask;
        }

        public void PublishFireAndForget<TEvent>(TEvent @event)
            where TEvent : class, IDomainEvent => Published.Add(@event);
    }
}
