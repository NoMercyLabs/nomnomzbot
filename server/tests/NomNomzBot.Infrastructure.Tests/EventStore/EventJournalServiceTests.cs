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
/// Behavior tests for the append-only journal + per-tenant sequence allocator. Each proves a consequence of an
/// action — the row that lands, the position assigned, the ordering preserved, the per-tenant isolation — not
/// merely that a call returned non-null.
/// </summary>
public sealed class EventJournalServiceTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static AppendEventRequest Request(
        Guid? broadcasterId,
        string eventType = "test.event",
        string payload = "{\"v\":1}",
        Guid? eventId = null
    ) =>
        new(
            EventId: eventId ?? Guid.NewGuid(),
            BroadcasterId: broadcasterId,
            EventType: eventType,
            EventVersion: 1,
            Source: "domain",
            PayloadJson: payload,
            MetadataJson: "{}",
            OccurredAt: new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc)
        );

    private static EventJournalService NewJournal(EventStoreTestDbContext db)
    {
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new EventJournalService(db, allocator, uow, Clock);
    }

    [Fact]
    public async Task AppendThenRead_PreservesOrderAndAssignsMonotonicPositions()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using (EventStoreTestDbContext db = database.NewContext())
        {
            EventJournalService journal = NewJournal(db);
            foreach (string payload in new[] { "{\"n\":1}", "{\"n\":2}", "{\"n\":3}" })
            {
                Result<EventRecord> result = await journal.AppendAsync(
                    Request(tenant, payload: payload)
                );
                result.IsSuccess.Should().BeTrue(result.ErrorMessage);
            }
        }

        await using (EventStoreTestDbContext db = database.NewContext())
        {
            EventJournalService journal = NewJournal(db);
            Result<IReadOnlyList<EventRecord>> read = await journal.ReadStreamAsync(tenant, 0, 100);

            read.IsSuccess.Should().BeTrue();
            read.Value.Select(e => e.StreamPosition)
                .Should()
                .Equal(new[] { 1L, 2L, 3L }, "positions are per-tenant monotonic starting at 1");
            read.Value.Select(e => e.PayloadJson)
                .Should()
                .Equal(
                    new[] { "{\"n\":1}", "{\"n\":2}", "{\"n\":3}" },
                    "read-back is in append order"
                );
            read.Value.Select(e => e.Id)
                .Should()
                .BeInAscendingOrder("the bigint id is the global append order");
        }
    }

    [Fact]
    public async Task Append_RecordsTheStoredRowShape()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        Guid correlation = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        AppendEventRequest request = Request(
            tenant,
            "economy.balance.credited",
            eventId: eventId
        ) with
        {
            CorrelationId = correlation,
            ActorExternalUserId = "12345",
            ActorProvider = "twitch",
        };
        Result<EventRecord> appended = await journal.AppendAsync(request);

        appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        EventRecord record = appended.Value;
        record.EventId.Should().Be(eventId);
        record.BroadcasterId.Should().Be(tenant);
        record.StreamPosition.Should().Be(1);
        record.EventType.Should().Be("economy.balance.credited");
        record.EventVersion.Should().Be(1);
        record.Source.Should().Be("domain");
        record.CorrelationId.Should().Be(correlation);
        record.ActorExternalUserId.Should().Be("12345");
        record.ActorProvider.Should().Be("twitch");
        record.PayloadIsEncrypted.Should().BeFalse();
        record.RecordedAt.Should().Be(Clock.GetUtcNow().UtcDateTime);
        record.OccurredAt.Should().Be(new DateTime(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Append_DuplicateEventId_IsIdempotentNoOp()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        Result<EventRecord> first = await journal.AppendAsync(Request(tenant, eventId: eventId));
        Result<EventRecord> second = await journal.AppendAsync(Request(tenant, eventId: eventId));

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        second
            .Value.StreamPosition.Should()
            .Be(first.Value.StreamPosition, "a duplicate consumes no new position");
        second.Value.Id.Should().Be(first.Value.Id, "the existing row is returned");

        Result<long> head = await journal.GetHeadPositionAsync(tenant);
        head.Value.Should().Be(1, "the second append did not advance the head");
    }

    [Fact]
    public async Task PerTenant_Sequences_AreIsolated_NoCrossBleed()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        // Interleave appends across two tenants; each tenant must get its OWN 1,2,3 stream.
        Result<EventRecord> a1 = await journal.AppendAsync(Request(tenantA));
        Result<EventRecord> b1 = await journal.AppendAsync(Request(tenantB));
        Result<EventRecord> a2 = await journal.AppendAsync(Request(tenantA));
        Result<EventRecord> b2 = await journal.AppendAsync(Request(tenantB));
        Result<EventRecord> a3 = await journal.AppendAsync(Request(tenantA));

        a1.Value.StreamPosition.Should().Be(1);
        a2.Value.StreamPosition.Should().Be(2);
        a3.Value.StreamPosition.Should().Be(3);
        b1.Value.StreamPosition.Should().Be(1, "tenant B's stream is independent of tenant A's");
        b2.Value.StreamPosition.Should().Be(2);

        Result<IReadOnlyList<EventRecord>> streamA = await journal.ReadStreamAsync(tenantA, 0, 100);
        Result<IReadOnlyList<EventRecord>> streamB = await journal.ReadStreamAsync(tenantB, 0, 100);

        streamA.Value.Should().HaveCount(3);
        streamB.Value.Should().HaveCount(2);
        streamA
            .Value.Should()
            .OnlyContain(e => e.BroadcasterId == tenantA, "no row from tenant B bleeds into A");
        streamB
            .Value.Should()
            .OnlyContain(e => e.BroadcasterId == tenantB, "no row from tenant A bleeds into B");
        streamA.Value.Select(e => e.StreamPosition).Should().Equal(1L, 2L, 3L);
        streamB.Value.Select(e => e.StreamPosition).Should().Equal(1L, 2L);
    }

    [Fact]
    public async Task AppendBatch_AssignsContiguousPositions_AllOrNothing()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        IReadOnlyList<AppendEventRequest> batch =
        [
            Request(tenant, payload: "{\"n\":1}"),
            Request(tenant, payload: "{\"n\":2}"),
            Request(tenant, payload: "{\"n\":3}"),
        ];

        Result<IReadOnlyList<EventRecord>> result = await journal.AppendBatchAsync(batch);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Select(e => e.StreamPosition).Should().Equal(1L, 2L, 3L);

        Result<long> head = await journal.GetHeadPositionAsync(tenant);
        head.Value.Should().Be(3);
    }

    [Fact]
    public async Task AppendBatch_MultiTenant_AllocatesPerTenantContiguousBlocks()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenantA = Guid.NewGuid();
        Guid tenantB = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        // Two tenants interleaved in ONE batch: each must be numbered from 1 off its OWN block, with no bleed.
        IReadOnlyList<AppendEventRequest> batch =
        [
            Request(tenantA, payload: "{\"a\":1}"),
            Request(tenantB, payload: "{\"b\":1}"),
            Request(tenantA, payload: "{\"a\":2}"),
            Request(tenantB, payload: "{\"b\":2}"),
            Request(tenantA, payload: "{\"a\":3}"),
        ];

        Result<IReadOnlyList<EventRecord>> result = await journal.AppendBatchAsync(batch);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        // Results stay in REQUEST order; positions are per-tenant, monotonic by arrival.
        result.Value.Select(e => e.StreamPosition).Should().Equal(1L, 1L, 2L, 2L, 3L);
        result
            .Value.Select(e => e.BroadcasterId)
            .Should()
            .Equal(new Guid?[] { tenantA, tenantB, tenantA, tenantB, tenantA });

        Result<IReadOnlyList<EventRecord>> streamA = await journal.ReadStreamAsync(tenantA, 0, 100);
        Result<IReadOnlyList<EventRecord>> streamB = await journal.ReadStreamAsync(tenantB, 0, 100);
        streamA
            .Value.Select(e => e.PayloadJson)
            .Should()
            .Equal("{\"a\":1}", "{\"a\":2}", "{\"a\":3}");
        streamA.Value.Select(e => e.StreamPosition).Should().Equal(1L, 2L, 3L);
        streamB.Value.Select(e => e.StreamPosition).Should().Equal(1L, 2L);
        streamA.Value.Should().OnlyContain(e => e.BroadcasterId == tenantA);
        streamB.Value.Should().OnlyContain(e => e.BroadcasterId == tenantB);
    }

    [Fact]
    public async Task AppendBatch_DedupesAgainstStoredAndWithinBatch_NoDoubleAppend()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        Guid alreadyStored = Guid.NewGuid();
        Guid repeatedInBatch = Guid.NewGuid();
        Guid fresh = Guid.NewGuid();

        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        // Seed one event (position 1) so the batch must dedupe against the DB, not just within itself.
        Result<EventRecord> seed = await journal.AppendAsync(
            Request(tenant, payload: "{\"seed\":1}", eventId: alreadyStored)
        );
        seed.IsSuccess.Should().BeTrue(seed.ErrorMessage);

        IReadOnlyList<AppendEventRequest> batch =
        [
            Request(tenant, payload: "{\"x\":\"stored-dup\"}", eventId: alreadyStored),
            Request(tenant, payload: "{\"x\":\"a\"}", eventId: repeatedInBatch),
            Request(tenant, payload: "{\"x\":\"a-again\"}", eventId: repeatedInBatch),
            Request(tenant, payload: "{\"x\":\"b\"}", eventId: fresh),
        ];

        Result<IReadOnlyList<EventRecord>> result = await journal.AppendBatchAsync(batch);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Should().HaveCount(4, "every request gets a result row, in order");

        // The DB duplicate returns the ALREADY-STORED record (its original position + payload), no re-append.
        result.Value[0].EventId.Should().Be(alreadyStored);
        result.Value[0].StreamPosition.Should().Be(1);
        result.Value[0].PayloadJson.Should().Be("{\"seed\":1}");

        // The in-batch duplicate's two slots resolve to the SAME newly-appended row (one position consumed).
        result.Value[1].EventId.Should().Be(repeatedInBatch);
        result.Value[2].EventId.Should().Be(repeatedInBatch);
        result.Value[1].StreamPosition.Should().Be(2);
        result.Value[2].StreamPosition.Should().Be(2);
        result.Value[1].Id.Should().Be(result.Value[2].Id);
        result.Value[2].PayloadJson.Should().Be("{\"x\":\"a\"}", "the first occurrence's row wins");

        // The fresh event takes the next position; only two NEW events landed after the seed.
        result.Value[3].EventId.Should().Be(fresh);
        result.Value[3].StreamPosition.Should().Be(3);
        (await journal.GetHeadPositionAsync(tenant)).Value.Should().Be(3);
    }

    [Fact]
    public async Task GetByEventId_Missing_FailsNotFound()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db);

        Result<EventRecord> result = await journal.GetByEventIdAsync(Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NOT_FOUND");
    }
}
