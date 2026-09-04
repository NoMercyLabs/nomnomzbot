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
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.DTOs.Twitch.EventSub;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.Twitch.Events;
using NomNomzBot.Infrastructure.EventStore;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Platform.Eventing.Translators;
using NomNomzBot.Infrastructure.Tests.EventStore;
using NomNomzBot.Infrastructure.Tests.Platform.Transport.Helix;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Platform.Eventing;

/// <summary>
/// Behavior tests for the generic EventSub notification dispatcher over the REAL append-only journal (SQLite,
/// with its actual unique constraints) and a capturing bus. Each test proves a consequence of dispatching a
/// notification — the journal row that lands (tenant, type, raw payload), the position it consumes, the
/// idempotent collapse of a redelivery, and the journaled event published — not merely that a call returned.
/// </summary>
public sealed class NotificationDispatcherTests
{
    private static readonly FakeTimeProvider Clock = new(new(2026, 6, 20, 12, 0, 0, TimeSpan.Zero));

    private static DuplicateNotificationSuppressor NewSuppressor(EventStoreTestDbContext db) =>
        new(db);

    private static EventJournalService NewJournal(EventStoreTestDbContext db)
    {
        EventStoreTestUnitOfWork uow = new(db);
        TenantSequenceAllocator allocator = new(db);
        return new(
            db,
            allocator,
            uow,
            Clock,
            new PassthroughEventPayloadProtector(),
            Substitute.For<ICurrentUserService>()
        );
    }

    private static EventSubNotification Notification(
        Guid tenant,
        string messageId,
        string type = "channel.follow",
        string payload = """{"user_id":"42","user_name":"alice"}"""
    )
    {
        using JsonDocument doc = JsonDocument.Parse(payload);
        return new()
        {
            MessageId = messageId,
            MessageTimestamp = new(2026, 6, 20, 11, 30, 0, TimeSpan.Zero),
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
            new EventSubTranslatorRegistry([]),
            NewSuppressor(db),
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
        record.ActorExternalUserId.Should().Be("twitch-123");
        record
            .ActorProvider.Should()
            .Be("twitch", "an EventSub-sourced actor is attributed to the twitch platform");
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
            new EventSubTranslatorRegistry([]),
            NewSuppressor(db),
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
            new EventSubTranslatorRegistry([]),
            NewSuppressor(db),
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

    [Fact]
    public async Task Dispatch_NewNotification_FansOutToTranslator_ButNotOnRedelivery()
    {
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        RecordingTranslator translator = new("channel.follow");

        await using EventStoreTestDbContext db = database.NewContext();
        NotificationDispatcher dispatcher = new(
            NewJournal(db),
            bus,
            new EventSubTranslatorRegistry([translator]),
            NewSuppressor(db),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        await dispatcher.DispatchAsync(Notification(tenant, "follow-msg"));
        translator
            .Calls.Should()
            .Be(1, "the genuinely-new notification fans out to its registered translator");

        // The redelivery is deduped (same message-id ⇒ same EventId) and must NOT fan out a second time.
        await dispatcher.DispatchAsync(Notification(tenant, "follow-msg"));
        translator.Calls.Should().Be(1, "a duplicate already fanned out on its first delivery");
    }

    [Fact]
    public async Task Dispatch_SameRealEvent_TwoDifferentMessageIds_FansOutOnlyOnce()
    {
        // S-DUPE root cause: Twitch's ~1-minute reconnect grace window can hand ONE real occurrence to both a
        // dying session's subscription and its freshly re-homed replacement — two genuine wire deliveries, two
        // DIFFERENT message ids, identical payload bytes. The message-id journal dedupe (proven above) cannot
        // catch this because the ids differ; only the semantic guard can. Reproduces the owner's live report
        // ("2 messages per twitch event") by asserting the count of fan-outs — the real consequence (one more
        // chat send per fan-out downstream) — not a return value.
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        RecordingTranslator translator = new("channel.follow");

        await using EventStoreTestDbContext db = database.NewContext();
        NotificationDispatcher dispatcher = new(
            NewJournal(db),
            bus,
            new EventSubTranslatorRegistry([translator]),
            NewSuppressor(db),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        Result<NotificationDispatchResult> first = await dispatcher.DispatchAsync(
            Notification(tenant, "reconnect-old-session-msg")
        );
        Result<NotificationDispatchResult> second = await dispatcher.DispatchAsync(
            Notification(tenant, "reconnect-new-session-msg") // different message-id, same payload
        );

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        // Different message ids ⇒ different EventIds ⇒ both are genuinely new journal rows (unlike the exact
        // redelivery case above) — the semantic guard operates ON TOP of, not instead of, the journal dedupe.
        second.Value.EventId.Should().NotBe(first.Value.EventId);
        second
            .Value.StreamPosition.Should()
            .Be(2, "both wire deliveries are journaled — dedupe is fan-out only");
        second
            .Value.WasDuplicate.Should()
            .BeTrue("the second delivery is the same real event, semantically");

        translator
            .Calls.Should()
            .Be(1, "one real-world event must produce exactly one fan-out, not two");
    }

    [Fact]
    public async Task Dispatch_TwoDistinctEvents_SameViewerSameText_BothFanOut()
    {
        // The false-positive guard: two genuinely separate occurrences that merely LOOK alike (same viewer,
        // same visible text) must never collapse into one. Twitch stamps distinguishing detail into the
        // payload itself (here: a chat message's own message_id differs between two real sends) — the
        // semantic guard keys on the full payload, so distinct payloads are never suppressed.
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        RecordingTranslator translator = new("channel.chat.message");

        await using EventStoreTestDbContext db = database.NewContext();
        NotificationDispatcher dispatcher = new(
            NewJournal(db),
            bus,
            new EventSubTranslatorRegistry([translator]),
            NewSuppressor(db),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        await dispatcher.DispatchAsync(
            Notification(
                tenant,
                "chat-msg-1",
                type: "channel.chat.message",
                payload: """{"chatter_user_id":"42","message":{"text":"hi"},"message_id":"wire-msg-1"}"""
            )
        );
        Result<NotificationDispatchResult> second = await dispatcher.DispatchAsync(
            Notification(
                tenant,
                "chat-msg-2",
                type: "channel.chat.message",
                payload: """{"chatter_user_id":"42","message":{"text":"hi"},"message_id":"wire-msg-2"}"""
            )
        );

        second
            .Value.WasDuplicate.Should()
            .BeFalse("a genuinely distinct send must not be suppressed");
        translator
            .Calls.Should()
            .Be(2, "two deliberate sends must produce two fan-outs, not one collapsed");
    }

    [Fact]
    public async Task Dispatch_SameRealEvent_AcrossTwoSeparateDispatcherInstances_FansOutOnlyOnce()
    {
        // The actual reported shape (S-DUPE): scripts/switchover.ps1 runs the incoming and outgoing deploy
        // colour SIDE BY SIDE, so two separate OS processes -- two separate NotificationDispatcher instances,
        // each with its OWN DuplicateNotificationSuppressor and its OWN DbContext -- can both receive the same
        // real Twitch event under different message ids. An in-process-only guard is blind to this; only a
        // claim durable in the store BOTH processes share (here: two independent connections against the same
        // shared-cache SQLite database, mirroring two containers against one Postgres/SQLite instance) can
        // catch it. This is the test an in-process dictionary cannot pass.
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        CapturingEventBus busColourA = new();
        RecordingTranslator translatorColourA = new("channel.follow");
        await using EventStoreTestDbContext dbColourA = database.NewContext();
        NotificationDispatcher dispatcherColourA = new(
            NewJournal(dbColourA),
            busColourA,
            new EventSubTranslatorRegistry([translatorColourA]),
            NewSuppressor(dbColourA),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        CapturingEventBus busColourB = new();
        RecordingTranslator translatorColourB = new("channel.follow");
        await using EventStoreTestDbContext dbColourB = database.NewContext();
        NotificationDispatcher dispatcherColourB = new(
            NewJournal(dbColourB),
            busColourB,
            new EventSubTranslatorRegistry([translatorColourB]),
            NewSuppressor(dbColourB),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        Result<NotificationDispatchResult> fromColourA = await dispatcherColourA.DispatchAsync(
            Notification(tenant, "colour-a-delivery")
        );
        Result<NotificationDispatchResult> fromColourB = await dispatcherColourB.DispatchAsync(
            Notification(tenant, "colour-b-delivery") // different message-id, identical payload
        );

        fromColourA.IsSuccess.Should().BeTrue();
        fromColourB.IsSuccess.Should().BeTrue();
        fromColourB
            .Value.WasDuplicate.Should()
            .BeTrue("colour B received the same real event colour A already claimed");

        (translatorColourA.Calls + translatorColourB.Calls)
            .Should()
            .Be(
                1,
                "one real-world event delivered to two live processes must still fan out exactly once"
            );
    }

    [Fact]
    public async Task Dispatch_TwoDistinctEvents_AcrossTwoSeparateDispatcherInstances_BothFanOut()
    {
        // The false-positive guard, proven across processes: two separate live colours each seeing a
        // genuinely distinct event must not have one clobber the other's claim.
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();

        CapturingEventBus busColourA = new();
        RecordingTranslator translatorColourA = new("channel.chat.message");
        await using EventStoreTestDbContext dbColourA = database.NewContext();
        NotificationDispatcher dispatcherColourA = new(
            NewJournal(dbColourA),
            busColourA,
            new EventSubTranslatorRegistry([translatorColourA]),
            NewSuppressor(dbColourA),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        CapturingEventBus busColourB = new();
        RecordingTranslator translatorColourB = new("channel.chat.message");
        await using EventStoreTestDbContext dbColourB = database.NewContext();
        NotificationDispatcher dispatcherColourB = new(
            NewJournal(dbColourB),
            busColourB,
            new EventSubTranslatorRegistry([translatorColourB]),
            NewSuppressor(dbColourB),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        await dispatcherColourA.DispatchAsync(
            Notification(
                tenant,
                "colour-a-1",
                type: "channel.chat.message",
                payload: "{\"chatter_user_id\":\"42\",\"message\":{\"text\":\"hi\"},\"message_id\":\"wire-a\"}"
            )
        );
        await dispatcherColourB.DispatchAsync(
            Notification(
                tenant,
                "colour-b-1",
                type: "channel.chat.message",
                payload: "{\"chatter_user_id\":\"42\",\"message\":{\"text\":\"hi\"},\"message_id\":\"wire-b\"}"
            )
        );

        (translatorColourA.Calls + translatorColourB.Calls)
            .Should()
            .Be(2, "two genuinely distinct sends across two live processes must both fan out");
    }

    [Fact]
    public async Task Dispatch_InboundWhisper_IsJournaled_AndPublishesWhisperReceivedEvent()
    {
        // The bot can only SEND whispers via IPlatformDirectMessageSender/!whisper; there is no chat-channel
        // handler for a whisper a viewer sends TO the bot. This proves the generic dispatcher still journals
        // it (never silently dropped) and fans out to the typed domain event, using the real Twitch sample
        // payload shape (EventSamplePayloads["chat.whisper.received"]).
        using SqliteTestDatabase database = SqliteTestDatabase.Open();
        Guid tenant = Guid.NewGuid();
        CapturingEventBus bus = new();
        UserWhisperMessageTranslator translator = new(bus, Clock);

        await using EventStoreTestDbContext db = database.NewContext();
        NotificationDispatcher dispatcher = new(
            NewJournal(db),
            bus,
            new EventSubTranslatorRegistry([translator]),
            NewSuppressor(db),
            Clock,
            NullLogger<NotificationDispatcher>.Instance
        );

        Result<NotificationDispatchResult> result = await dispatcher.DispatchAsync(
            Notification(
                tenant,
                "whisper-msg-1",
                type: "user.whisper.message",
                payload: """
                {
                    "from_user_id": "12826",
                    "from_user_login": "twitch",
                    "from_user_name": "Twitch",
                    "to_user_id": "141981764",
                    "to_user_login": "twitchdev",
                    "to_user_name": "TwitchDev",
                    "whisper_id": "3c4719ba-fe16-4c75-8f00-78142a375cf1",
                    "whisper": { "text": "I have a secret to tell you!" }
                }
                """
            )
        );

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);

        // The raw whisper payload is a real, queryable journal row — not silently dropped.
        Result<EventRecord> stored = await NewJournal(db).GetByEventIdAsync(result.Value.EventId);
        stored.IsSuccess.Should().BeTrue();
        stored.Value.EventType.Should().Be("user.whisper.message");
        stored.Value.BroadcasterId.Should().Be(tenant);
        JsonDocument
            .Parse(stored.Value.PayloadJson)
            .RootElement.GetProperty("whisper")
            .GetProperty("text")
            .GetString()
            .Should()
            .Be("I have a secret to tell you!", "the raw whisper body is persisted verbatim");

        // It also fans out to the typed domain event carrying the whisper content.
        WhisperReceivedEvent whisper = bus.EventsOf<WhisperReceivedEvent>()
            .Should()
            .ContainSingle()
            .Subject;
        whisper.WhisperId.Should().Be("3c4719ba-fe16-4c75-8f00-78142a375cf1");
        whisper.FromUserLogin.Should().Be("twitch");
        whisper.Text.Should().Be("I have a secret to tell you!");
    }

    /// <summary>A translator that records how often it was invoked, for the fan-out routing test.</summary>
    private sealed class RecordingTranslator(string subscriptionType) : IEventSubEventTranslator
    {
        public int Calls { get; private set; }

        public string SubscriptionType => subscriptionType;

        public Task TranslateAsync(
            EventSubNotification notification,
            CancellationToken ct = default
        )
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
