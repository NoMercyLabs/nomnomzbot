// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Behavior tests for the generic EventSub notification dispatcher over the REAL append-only journal (SQLite,
/// with its actual unique constraints) and a capturing bus. Each test proves a consequence of dispatching a
/// notification — the journal row that lands (tenant, type, raw payload), the position it consumes, the
/// idempotent collapse of a redelivery, and the journaled event published — not merely that a call returned.
/// </summary>
public sealed class NotificationDispatcherTests
{
    private static readonly FakeTimeProvider Clock = new(
        new DateTimeOffset(2026, 6, 20, 12, 0, 0, TimeSpan.Zero)
    );

    private static EventJournalService NewJournal(EventStoreTestDbContext db)
    {
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new EventJournalService(db, allocator, uow, Clock);
    }

    private static EventSubNotification Notification(
        Guid tenant,
        string messageId,
        string type = "channel.follow",
        string payload = """{"user_id":"42","user_name":"alice"}"""
    )
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new EventSubNotification
        {
            MessageId = messageId,
            MessageTimestamp = new DateTimeOffset(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
            SubscriptionType = type,
            SubscriptionVersion = "2",
            BroadcasterId = tenant,
            TwitchBroadcasterUserId = "twitch-123",
            Event = doc.RootElement.Clone(),
        };
    }

    [Fact]
    public async Task Dispatch_JournalsRawPayload_WithTenantAndTypeAndPosition_ThenPublishes()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();

        await using EventStoreTestDbContext db = database.NewContext();
        NotificationDispatcher dispatcher = new(
            NewJournal(db),
            bus,
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        Result<NotificationDispatchResult> result = await dispatcher.DispatchAsync(
            Notification(tenant, "msg-1")
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.WasDuplicate.Should().BeFalse();
        result.Value.StreamPosition.Should().Be(1, "the first event takes the tenant's position 1");

        // The journal holds exactly one row, typed by subscription_type, scoped to the tenant, raw payload intact.
        Result<EventRecord> stored = await NewJournal(db).GetByEventIdAsync(result.Value.EventId);
        stored.IsSuccess.Should().BeTrue();
        EventRecord record = stored.Value;
        record.BroadcasterId.Should().Be(tenant);
        record.EventType.Should().Be("channel.follow");
        record.EventVersion.Should().Be(2);
        record.Source.Should().Be("eventsub");
        record.ActorTwitchUserId.Should().Be("twitch-123");
        JsonDocument
            .Parse(record.PayloadJson)
            .RootElement.GetProperty("user_name")
            .GetString()
            .Should()
            .Be("alice", "the raw event payload is persisted verbatim");

        // Exactly one journaled-event was published, carrying the same journal id + position, not a duplicate.
        bus.EventsOf<EventSubNotificationJournaledEvent>()
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    BroadcasterId = tenant,
                    JournalEventId = result.Value.EventId,
                    StreamPosition = 1L,
                    EventType = "channel.follow",
                    WasDuplicate = false,
                }
            );
    }

    [Fact]
    public async Task Dispatch_SameMessageIdTwice_IsDeduped_OneJournalRow_NoSecondPosition()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();

        await using EventStoreTestDbContext db = database.NewContext();
        NotificationDispatcher dispatcher = new(
            NewJournal(db),
            bus,
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        Result<NotificationDispatchResult> first = await dispatcher.DispatchAsync(
            Notification(tenant, "redelivered-msg")
        );
        Result<NotificationDispatchResult> second = await dispatcher.DispatchAsync(
            Notification(tenant, "redelivered-msg")
        );

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        // Same message-id ⇒ same derived EventId ⇒ the journal collapses the redelivery to the existing row.
        second
            .Value.EventId.Should()
            .Be(first.Value.EventId, "the message-id deterministically derives the EventId");
        second
            .Value.WasDuplicate.Should()
            .BeTrue("the second delivery is recognised as a duplicate");
        second
            .Value.StreamPosition.Should()
            .Be(first.Value.StreamPosition, "a duplicate consumes no new position");

        // The journal advanced its head exactly once — there is no second row.
        Result<long> head = await NewJournal(db).GetHeadPositionAsync(tenant);
        head.Value.Should().Be(1, "the duplicate did not append a second journal entry");

        // The duplicate is still announced (WasDuplicate=true) so consumers can observe the redelivery.
        bus.EventsOf<EventSubNotificationJournaledEvent>().Should().HaveCount(2);
        bus.EventsOf<EventSubNotificationJournaledEvent>()
            .Select(e => e.WasDuplicate)
            .Should()
            .Equal(false, true);
    }

    [Fact]
    public async Task Dispatch_TwoDistinctMessages_SameTenant_GetMonotonicPositions()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();

        await using EventStoreTestDbContext db = database.NewContext();
        NotificationDispatcher dispatcher = new(
            NewJournal(db),
            bus,
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        Result<NotificationDispatchResult> a = await dispatcher.DispatchAsync(
            Notification(tenant, "msg-a", type: "channel.subscribe")
        );
        Result<NotificationDispatchResult> b = await dispatcher.DispatchAsync(
            Notification(tenant, "msg-b", type: "channel.cheer")
        );

        a.Value.StreamPosition.Should().Be(1);
        b.Value.StreamPosition.Should().Be(2, "distinct messages advance the tenant stream");
        a.Value.EventId.Should().NotBe(b.Value.EventId);
    }
}
