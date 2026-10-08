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
/// Proves a shared ban that was refused or skipped reaches the streamer's action-required inbox: one item per
/// reason for the channel that should have banned, naming the viewer and why; another channel's failures never
/// leak in; a dismissal hides the item and a NEW failure surfaces it again.
/// </summary>
public sealed class SharedBanNotAppliedSourceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000c1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000c2"
    );
    private static readonly DateTime T0 = new(2026, 10, 8, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ARefusedBan_SurfacesAnItemNamingTheViewerAndTheReason()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            Failed(
                ChannelId,
                1,
                T0,
                "twitch_ban_failed",
                "Troll",
                "TWITCH_MISSING_SCOPE: no scope"
            ),
            Failed(OtherChannelId, 1, T0, "twitch_ban_failed", "Other", "x")
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

        item.Kind.Should().Be("shared_ban_not_applied");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_shared_ban_title");
        item.MessageKey.Should().Be("attention_shared_ban_twitch_ban_failed_message");
        item.Count.Should().Be(1);
        item.Parameters.Should()
            .Contain("targetName", "Troll")
            .And.Contain("detail", "TWITCH_MISSING_SCOPE: no scope");
        item.DeepLinkRoute.Should().Be("moderation");
    }

    [Fact]
    public async Task EachReason_IsItsOwnItem_AndFailuresOfOneReasonAreGrouped()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.AddRange(
            Failed(ChannelId, 1, T0, "no_shared_session", "A", null),
            Failed(ChannelId, 2, T0.AddMinutes(5), "no_shared_session", "B", null),
            Failed(ChannelId, 3, T0.AddMinutes(6), "origin_not_trusted", "C", null)
        );
        await db.SaveChangesAsync();

        List<ActionRequiredItemDto> items = (
            await ActionRequiredInboxHarness
                .Create(db, new FakeTimeProvider(T0.AddHours(1)))
                .GetItemsAsync(ChannelId)
        ).Value;

        items.Should().HaveCount(2);
        items
            .Single(i => i.MessageKey == "attention_shared_ban_no_shared_session_message")
            .Count.Should()
            .Be(2);
        items
            .Single(i => i.MessageKey == "attention_shared_ban_origin_not_trusted_message")
            .Severity.Should()
            .Be("info");
    }

    [Fact]
    public async Task AFailureOlderThanTheWindow_NoLongerShows()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.EventJournals.Add(Failed(ChannelId, 1, T0, "twitch_ban_failed", "Old", null));
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
        db.EventJournals.Add(Failed(ChannelId, 1, T0, "twitch_ban_failed", "First", null));
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = ActionRequiredInboxHarness.Create(db, clock);

        ActionRequiredItemDto first = (await sut.GetItemsAsync(ChannelId)).Value.Single();
        await sut.DismissAsync(ChannelId, Guid.NewGuid(), [first.Id]);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();

        db.EventJournals.Add(
            Failed(ChannelId, 2, T0.AddHours(1), "twitch_ban_failed", "Second", null)
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto again = (await sut.GetItemsAsync(ChannelId)).Value.Single();
        again.Count.Should().Be(2);
        again.Parameters.Should().Contain("targetName", "Second");
    }

    private static EventJournal Failed(
        Guid channelId,
        long position,
        DateTime occurredAt,
        string reason,
        string targetName,
        string? detail
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channelId,
            StreamPosition = position,
            EventType = nameof(SharedChatBanNotAppliedEvent),
            EventVersion = 1,
            Source = "domain",
            Payload = JsonConvert.SerializeObject(
                new
                {
                    Reason = reason,
                    Origin = "shared_chat",
                    OriginChannelId = Guid.NewGuid(),
                    TargetTwitchUserId = "troll-42",
                    TargetDisplayName = targetName,
                    Detail = detail,
                }
            ),
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };
}
