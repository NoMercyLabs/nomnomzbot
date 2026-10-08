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
using Newtonsoft.Json;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Notifications;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Proves a chat filter action that Twitch refused reaches the streamer's action-required inbox: one item per
/// (filter, action) so a raid does not flood it, with the count, the newest chatter and the platform's reason;
/// another channel's failures never leak in; a dismissal hides the item and a NEW failure surfaces it again.
/// </summary>
public sealed class ChatFilterActionFailureSourceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000d1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000d2"
    );
    private static readonly Guid LinkFilter = Guid.Parse("0192b000-0000-7000-8000-00000000f001");
    private static readonly Guid SpamFilter = Guid.Parse("0192b000-0000-7000-8000-00000000f002");
    private static readonly DateTime T0 = new(2026, 10, 8, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ARefusedTimeout_SurfacesAnItemNamingTheFilterTheViewerTheActionAndTheReason()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            Failed(ChannelId, 1, T0, LinkFilter, "No links", "troll", "timeout", "not a moderator"),
            Failed(OtherChannelId, 1, T0, LinkFilter, "No links", "other", "timeout", "x")
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (
            await ActionRequiredInboxHarness
                .Create(db, new FakeTimeProvider(T0.AddHours(1)))
                .GetItemsAsync(ChannelId)
        )
            .Value.Should()
            .ContainSingle()
            .Subject;

        item.Kind.Should().Be("chat_filter_action_failed");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_filter_action_failed_title");
        item.MessageKey.Should().Be("attention_filter_action_failed_message");
        item.Count.Should().Be(1);
        item.Parameters.Should()
            .HaveCount(4)
            .And.Contain("filter", "No links")
            .And.Contain("username", "troll")
            .And.Contain("action", "timeout")
            .And.Contain("reason", "not a moderator");
        item.DeepLinkRoute.Should().Be("moderation");
        item.DetectedAt.Should().Be(T0);
    }

    [Fact]
    public async Task ARaid_IsOneItemPerFilterAndAction_WithTheCountAndTheNewestChatter()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            Failed(ChannelId, 1, T0, LinkFilter, "No links", "first", "timeout", "denied"),
            Failed(
                ChannelId,
                2,
                T0.AddMinutes(1),
                LinkFilter,
                "No links",
                "second",
                "timeout",
                "denied"
            ),
            Failed(
                ChannelId,
                3,
                T0.AddMinutes(2),
                LinkFilter,
                "No links",
                "third",
                "timeout",
                "denied"
            ),
            Failed(
                ChannelId,
                4,
                T0.AddMinutes(3),
                LinkFilter,
                "No links",
                "fourth",
                "delete",
                "denied"
            ),
            Failed(ChannelId, 5, T0.AddMinutes(4), SpamFilter, "Spam", "fifth", "timeout", "denied")
        );
        await db.SaveChangesAsync();

        List<ActionRequiredItemDto> items = (
            await ActionRequiredInboxHarness
                .Create(db, new FakeTimeProvider(T0.AddHours(1)))
                .GetItemsAsync(ChannelId)
        ).Value;

        items.Should().HaveCount(3);
        ActionRequiredItemDto raid = items.Single(i =>
            i.Parameters["filter"] == "No links" && i.Parameters["action"] == "timeout"
        );
        raid.Count.Should().Be(3);
        raid.Parameters.Should().Contain("username", "third");
        items.Single(i => i.Parameters["action"] == "delete").Count.Should().Be(1);
        items.Single(i => i.Parameters["filter"] == "Spam").Count.Should().Be(1);
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task AFailureOlderThanTheWindow_NoLongerShows()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.Add(Failed(ChannelId, 1, T0, LinkFilter, "No links", "old", "ban", "x"));
        await db.SaveChangesAsync();

        (
            await ActionRequiredInboxHarness
                .Create(db, new FakeTimeProvider(T0.AddHours(30)))
                .GetItemsAsync(ChannelId)
        )
            .Value.Should()
            .BeEmpty();
    }

    [Fact]
    public async Task ANewFailure_SurfacesAgainAfterDismissingTheOldOnes()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        FakeTimeProvider clock = new(T0.AddHours(2));
        db.EventJournals.Add(
            Failed(ChannelId, 1, T0, LinkFilter, "No links", "first", "timeout", "denied")
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db, clock);

        ActionRequiredItemDto first = (await sut.GetItemsAsync(ChannelId)).Value.Single();
        await sut.DismissAsync(ChannelId, Guid.NewGuid(), [first.Id]);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();

        db.EventJournals.Add(
            Failed(
                ChannelId,
                2,
                T0.AddHours(1),
                LinkFilter,
                "No links",
                "second",
                "timeout",
                "denied"
            )
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto again = (await sut.GetItemsAsync(ChannelId)).Value.Single();
        again.Count.Should().Be(2);
        again.Parameters.Should().Contain("username", "second");
        again.Id.Should().NotBe(first.Id);
    }

    private static EventJournal Failed(
        Guid channelId,
        long position,
        DateTime occurredAt,
        Guid filterId,
        string filterName,
        string username,
        string action,
        string error
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channelId,
            StreamPosition = position,
            EventType = nameof(ChatFilterActionFailedEvent),
            EventVersion = 1,
            Source = "domain",
            Payload = JsonConvert.SerializeObject(
                new
                {
                    FilterId = filterId,
                    FilterName = filterName,
                    SubjectTwitchUserId = "tw-" + username,
                    SubjectUsername = username,
                    Action = action,
                    Error = error,
                }
            ),
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };
}
