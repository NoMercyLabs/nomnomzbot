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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.EventStore.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Notifications.Sources;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// A message AutoMod could not delete is still on screen. The streamer must see that in the attention
/// inbox, with the rule and the chatter, and must be able to dismiss it; a NEW failure must surface again.
/// </summary>
public sealed class AutoModDeleteFailedSourceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000c1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000c2"
    );
    private static readonly DateTime T0 = new(2026, 10, 8, 18, 0, 0, DateTimeKind.Utc);

    private static async Task<ActionRequiredInboxServiceTestDbContext> NewDbAsync()
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        await db.SaveChangesAsync();
        return db;
    }

    private static EventJournal Failure(
        Guid channelId,
        long position,
        DateTime occurredAt,
        string rule,
        string user
    ) =>
        new()
        {
            EventId = Guid.NewGuid(),
            BroadcasterId = channelId,
            StreamPosition = position,
            EventType = nameof(AutoModDeleteFailedEvent),
            EventVersion = 1,
            Source = "domain",
            Payload =
                $"{{\"MessageId\":\"m-{position}\",\"UserId\":\"1\",\"UserLogin\":\"{user}\",\"RuleName\":\"{rule}\",\"Error\":\"forbidden\"}}",
            Metadata = "{}",
            OccurredAt = occurredAt,
            RecordedAt = occurredAt,
        };

    private static async Task<List<ActionRequiredItemDto>> ItemsAsync(
        ActionRequiredInboxServiceTestDbContext db,
        DateTime now,
        params string[] dismissed
    )
    {
        AutoModDeleteFailedSource source = new(db, new FakeTimeProvider(now));
        Result<List<ActionRequiredItemDto>> result = await source.GetItemsAsync(
            ChannelId,
            new HashSet<string>(dismissed)
        );
        return result.Value;
    }

    [Fact]
    public async Task AFailedDeleteInTheWindow_NamesTheRuleAndTheChatter()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        db.EventJournals.AddRange(
            Failure(ChannelId, 1, T0, "no links", "spammer_one"),
            Failure(ChannelId, 2, T0.AddHours(-30), "no links", "too_old"),
            Failure(OtherChannelId, 1, T0, "no links", "someone_else")
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db, T0.AddHours(1)))
            .Should()
            .ContainSingle()
            .Subject;

        item.Kind.Should().Be("automod_delete_failed");
        item.Severity.Should().Be("warning");
        item.Count.Should().Be(1);
        item.TitleKey.Should().Be("attention_automod_delete_failed_title");
        item.MessageKey.Should().Be("attention_automod_delete_failed_message");
        item.Parameters.Should()
            .Contain("ruleName", "no links")
            .And.Contain("userName", "spammer_one")
            .And.Contain("count", "1");
        item.DeepLinkRoute.Should().Be("moderation");
    }

    [Fact]
    public async Task SeveralFailuresAreOneItemWithACount()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        db.EventJournals.AddRange(
            Failure(ChannelId, 1, T0, "no links", "a"),
            Failure(ChannelId, 2, T0.AddMinutes(5), "no links", "b")
        );
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await ItemsAsync(db, T0.AddHours(1)))
            .Should()
            .ContainSingle()
            .Subject;

        item.Count.Should().Be(2);
        item.Parameters.Should().Contain("userName", "b");
    }

    [Fact]
    public async Task ADismissalHidesTheItemAndANewFailureSurfacesAgain()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await NewDbAsync();
        db.EventJournals.Add(Failure(ChannelId, 1, T0, "no links", "a"));
        await db.SaveChangesAsync();

        ActionRequiredItemDto first = (await ItemsAsync(db, T0.AddHours(1))).Single();
        (await ItemsAsync(db, T0.AddHours(1), first.Id)).Should().BeEmpty();

        db.EventJournals.Add(Failure(ChannelId, 2, T0.AddMinutes(10), "no links", "b"));
        await db.SaveChangesAsync();

        ActionRequiredItemDto again = (await ItemsAsync(db, T0.AddHours(1), first.Id))
            .Should()
            .ContainSingle()
            .Subject;
        again.Id.Should().NotBe(first.Id);
    }

    [Fact]
    public void TheSourceIsInvalidatedByTheFailureEvent()
    {
        AutoModDeleteFailedSource source = new(null!, TimeProvider.System);

        source.InvalidatingEventTypes.Should().Equal(nameof(AutoModDeleteFailedEvent));
        source.KeyPrefixes.Should().Equal("automod-delete-failed:");
    }
}
