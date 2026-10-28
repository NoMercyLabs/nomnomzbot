// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Infrastructure.EventStore;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.EventStore;

/// <summary>
/// The journal page's read filters and its live signal: a query narrows by source, and a committed append tells
/// the dashboard once per channel that the journal grew. A refused or empty append tells nobody.
/// </summary>
public sealed class EventJournalReadAndNotifyTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private static AppendEventRequest Request(Guid? tenant, string source, Guid? eventId = null) =>
        new(
            EventId: eventId ?? Guid.NewGuid(),
            BroadcasterId: tenant,
            EventType: "test.event",
            EventVersion: 1,
            Source: source,
            PayloadJson: "{}",
            MetadataJson: "{}",
            OccurredAt: new(2026, 6, 20, 11, 0, 0, DateTimeKind.Utc)
        );

    private static EventJournalService NewJournal(
        EventStoreTestDbContext db,
        IEventJournalChangeNotifier notifier
    ) =>
        new(
            db,
            new TenantSequenceAllocator(db),
            new EventStoreTestUnitOfWork(db),
            Clock,
            new PassthroughEventPayloadProtector(),
            Substitute.For<ICurrentUserService>(),
            notifier
        );

    [Fact]
    public async Task Query_with_a_source_returns_only_rows_of_that_source_and_that_channel()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db, Substitute.For<IEventJournalChangeNotifier>());
        await journal.AppendBatchAsync([
            Request(tenant, "eventsub"),
            Request(tenant, "domain"),
            Request(tenant, "eventsub"),
            Request(other, "eventsub"),
        ]);

        Result<PagedList<EventRecord>> page = await journal.QueryAsync(
            new(tenant, null, null, null, null, 1, 25, Source: "eventsub")
        );

        page.IsSuccess.Should().BeTrue(page.ErrorMessage);
        page.Value.TotalCount.Should().Be(2);
        page.Value.Items.Should()
            .OnlyContain(r => r.Source == "eventsub" && r.BroadcasterId == tenant);
    }

    [Fact]
    public async Task A_committed_append_notifies_its_channel_once()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        IEventJournalChangeNotifier notifier = Substitute.For<IEventJournalChangeNotifier>();
        await using EventStoreTestDbContext db = database.NewContext();

        Result<EventRecord> appended = await NewJournal(db, notifier)
            .AppendAsync(Request(tenant, "domain"));

        appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        notifier.Received(1).NotifyChanged(tenant);
        notifier.ReceivedCalls().Should().ContainSingle();
    }

    [Fact]
    public async Task A_batch_across_two_channels_notifies_each_channel_once()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        IEventJournalChangeNotifier notifier = Substitute.For<IEventJournalChangeNotifier>();
        await using EventStoreTestDbContext db = database.NewContext();

        Result<IReadOnlyList<EventRecord>> appended = await NewJournal(db, notifier)
            .AppendBatchAsync([
                Request(first, "import"),
                Request(first, "import"),
                Request(second, "import"),
            ]);

        appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        notifier.Received(1).NotifyChanged(first);
        notifier.Received(1).NotifyChanged(second);
        notifier.ReceivedCalls().Should().HaveCount(2);
    }

    [Fact]
    public async Task A_duplicate_batch_adds_no_rows_and_notifies_nobody()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        Guid eventId = Guid.NewGuid();
        IEventJournalChangeNotifier notifier = Substitute.For<IEventJournalChangeNotifier>();
        await using EventStoreTestDbContext db = database.NewContext();
        EventJournalService journal = NewJournal(db, notifier);
        await journal.AppendBatchAsync([Request(tenant, "import", eventId)]);
        notifier.ClearReceivedCalls();

        await journal.AppendBatchAsync([Request(tenant, "import", eventId)]);

        notifier.ReceivedCalls().Should().BeEmpty("a duplicate adds no row, so nothing grew");
    }

    [Fact]
    public async Task A_platform_wide_event_with_no_channel_notifies_nobody()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        IEventJournalChangeNotifier notifier = Substitute.For<IEventJournalChangeNotifier>();
        await using EventStoreTestDbContext db = database.NewContext();

        Result<EventRecord> appended = await NewJournal(db, notifier)
            .AppendAsync(Request(null, "domain"));

        appended.IsSuccess.Should().BeTrue(appended.ErrorMessage);
        notifier.ReceivedCalls().Should().BeEmpty();
    }
}
