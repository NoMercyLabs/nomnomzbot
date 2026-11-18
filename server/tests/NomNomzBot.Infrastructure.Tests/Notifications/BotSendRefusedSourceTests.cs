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
using Newtonsoft.Json;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.Chat.Events;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Platform;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// When Twitch refuses the bot's chat message in a channel (the bot is not a moderator there, or no
/// <c>channel:bot</c> grant), that channel's inbox must say so in plain words and name the fix. One refusal
/// stream is ONE item per channel, a recovery clears it, a dismissal hides it, and a day ends it.
/// </summary>
public sealed class BotSendRefusedSourceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000d2"
    );
    private static readonly DateTime T0 = new(2026, 11, 17, 18, 0, 0, DateTimeKind.Utc);

    private static EventJournal Row(
        Guid channelId,
        long position,
        DateTime occurredAt,
        DomainEventBase domainEvent
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channelId,
            StreamPosition = position,
            EventType = domainEvent.GetType().Name,
            EventVersion = 1,
            Source = "domain",
            Payload = JsonConvert.SerializeObject(domainEvent),
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };

    private static EventJournal Refused(
        Guid channelId,
        long position,
        DateTime occurredAt,
        string bot = "nomz_bot"
    ) =>
        Row(
            channelId,
            position,
            occurredAt,
            new BotChatSendRefusedEvent
            {
                BroadcasterId = channelId,
                BotUsername = bot,
                OccurredAt = new DateTimeOffset(occurredAt),
            }
        );

    private static EventJournal Recovered(Guid channelId, long position, DateTime occurredAt) =>
        Row(
            channelId,
            position,
            occurredAt,
            new BotChatSendRecoveredEvent
            {
                BroadcasterId = channelId,
                OccurredAt = new DateTimeOffset(occurredAt),
            }
        );

    private static async Task<ActionRequiredInboxServiceTestDbContext> SeedAsync(
        params EventJournal[] rows
    )
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                Name = "ModdedStreamer",
                NameNormalized = "moddedstreamer",
                TwitchChannelId = "100",
                IsOnboarded = true,
            }
        );
        db.EventJournals.AddRange(rows);
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        ActionRequiredInboxServiceTestDbContext db,
        TimeSpan sinceT0,
        Guid? channelId = null
    ) =>
        (
            await ActionRequiredInboxHarness
                .Create(db, new FakeTimeProvider(new DateTimeOffset(T0 + sinceT0)))
                .GetItemsAsync(channelId ?? ChannelId)
        ).Value;

    [Fact]
    public async Task A_refused_send_is_one_warning_that_names_the_channel_and_the_fix()
    {
        EventJournal refusal = Refused(ChannelId, 1, T0);
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            refusal,
            Refused(OtherChannelId, 1, T0, "other_bot")
        );

        ActionRequiredItemDto item = (await ItemsAsync(db, TimeSpan.FromMinutes(1)))
            .Should()
            .ContainSingle()
            .Subject;

        item.Id.Should().Be($"bot-send-refused:{ChannelId}:{T0.Ticks}");
        item.Kind.Should().Be("bot_send_refused");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_bot_send_refused_title");
        item.MessageKey.Should().Be("attention_bot_send_refused_message");
        item.Parameters.Should().Contain("botName", "nomz_bot");
        item.Parameters.Should().Contain("channelName", "ModdedStreamer");
        item.DeepLinkRoute.Should().Be("people/overview");
        item.DetectedAt.Should().Be(T0);
        item.Count.Should().Be(1);
    }

    [Fact]
    public async Task A_second_refusal_adds_no_second_item_and_keeps_the_first_identity()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            Refused(ChannelId, 1, T0),
            Refused(ChannelId, 2, T0.AddHours(2))
        );

        ActionRequiredItemDto item = (await ItemsAsync(db, TimeSpan.FromHours(3)))
            .Should()
            .ContainSingle()
            .Subject;

        item.Id.Should().Be($"bot-send-refused:{ChannelId}:{T0.Ticks}");
        item.DetectedAt.Should().Be(T0.AddHours(2));
    }

    [Fact]
    public async Task A_send_that_worked_afterwards_clears_the_item()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            Refused(ChannelId, 1, T0),
            Recovered(ChannelId, 2, T0.AddMinutes(10))
        );

        (await ItemsAsync(db, TimeSpan.FromMinutes(20))).Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_after_a_recovery_is_a_new_item()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            Refused(ChannelId, 1, T0),
            Recovered(ChannelId, 2, T0.AddMinutes(10)),
            Refused(ChannelId, 3, T0.AddMinutes(30))
        );

        (await ItemsAsync(db, TimeSpan.FromHours(1)))
            .Should()
            .ContainSingle()
            .Which.Id.Should()
            .Be($"bot-send-refused:{ChannelId}:{T0.AddMinutes(30).Ticks}");
    }

    [Fact]
    public async Task A_dismissed_item_stays_hidden_while_the_same_refusal_goes_on()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            Refused(ChannelId, 1, T0),
            Refused(ChannelId, 2, T0.AddHours(2))
        );
        ActionRequiredInboxService inbox = ActionRequiredInboxHarness.Create(
            db,
            new FakeTimeProvider(new DateTimeOffset(T0.AddHours(3)))
        );
        string id = (await inbox.GetItemsAsync(ChannelId)).Value.Single().Id;

        await inbox.DismissAsync(ChannelId, Guid.NewGuid(), [id]);

        (await inbox.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_refusal_older_than_a_day_is_gone()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            Refused(ChannelId, 1, T0)
        );

        (await ItemsAsync(db, TimeSpan.FromHours(25))).Should().BeEmpty();
    }

    [Fact]
    public void The_refusal_and_the_recovery_refresh_the_inbox_live()
    {
        BotSendRefusedSource source = new(
            ActionRequiredInboxServiceTestDbContext.New(),
            new FakeTimeProvider(new DateTimeOffset(T0))
        );

        source
            .InvalidatingEventTypes.Should()
            .BeEquivalentTo(["BotChatSendRefusedEvent", "BotChatSendRecoveredEvent"]);
    }
}
