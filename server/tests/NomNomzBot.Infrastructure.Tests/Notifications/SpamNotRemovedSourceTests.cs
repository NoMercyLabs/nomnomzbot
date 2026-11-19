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
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.SpamDefense;
using NomNomzBot.Infrastructure.Notifications;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// A channel that did not onboard, where the bot saw spam and left it in chat because no moderator said it may
/// act in their name, must say so in that channel's inbox: one item, how much spam, who sent the last one, and
/// a route to the place the moderator turns removal on. A yes on record ends it.
/// </summary>
public sealed class SpamNotRemovedSourceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000e2"
    );
    private static readonly Guid ModeratorId = Guid.Parse("0192b000-0000-7000-8000-0000000000e3");
    private const string TwitchChannelId = "1403633423";
    private static readonly DateTime T0 = new(2026, 11, 19, 18, 0, 0, DateTimeKind.Utc);

    private static SpamDetection Detection(
        DateTime detectedAt,
        string sender,
        SpamOutcome outcome = SpamOutcome.DeleteAndEscalate,
        bool dryRun = false,
        Guid? channelId = null
    ) =>
        new()
        {
            BroadcasterId = channelId ?? ChannelId,
            SubjectPlatformUserId = sender,
            SubjectDisplayName = sender,
            Provider = "twitch",
            MessageId = Guid.NewGuid().ToString(),
            MessageText = "Cheap viewers on streamboo .com",
            Outcome = outcome,
            WouldHaveBeen = outcome,
            WasDryRun = dryRun,
            DetectedAt = detectedAt,
        };

    private static ChannelMembership Membership(bool removesSpamInOwnName) =>
        new()
        {
            BroadcasterId = ChannelId,
            UserId = ModeratorId,
            ManagementRole = ManagementRole.Moderator,
            GrantedAt = T0.AddDays(-3),
            RemovesSpamInOwnName = removesSpamInOwnName,
        };

    private static async Task<ActionRequiredInboxServiceTestDbContext> SeedAsync(
        bool onboarded,
        params object[] rows
    )
    {
        ActionRequiredInboxServiceTestDbContext db = ActionRequiredInboxServiceTestDbContext.New();
        db.Channels.Add(
            new()
            {
                Id = ChannelId,
                Name = "Resident_Meeper",
                NameNormalized = "resident_meeper",
                TwitchChannelId = TwitchChannelId,
                IsOnboarded = onboarded,
            }
        );
        db.AddRange(rows);
        await db.SaveChangesAsync();
        return db;
    }

    private static ActionRequiredInboxService InboxAt(
        ActionRequiredInboxServiceTestDbContext db,
        DateTime now
    ) => ActionRequiredInboxHarness.Create(db, new FakeTimeProvider(new DateTimeOffset(now)));

    [Fact]
    public async Task Spam_left_in_chat_is_one_warning_that_counts_it_and_names_the_last_sender()
    {
        SpamDetection latest = Detection(T0.AddMinutes(5), "streamboo_bot_2");
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            onboarded: false,
            Membership(removesSpamInOwnName: false),
            Detection(T0, "streamboo_bot_1", SpamOutcome.DeleteAndQueue),
            latest,
            Detection(T0.AddHours(-30), "too_old"),
            Detection(T0, "dry_run_only", dryRun: true),
            Detection(T0, "only_flagged", SpamOutcome.Flag),
            Detection(T0, "other_channel", channelId: OtherChannelId)
        );

        ActionRequiredItemDto item = (await InboxAt(db, T0.AddMinutes(10)).GetItemsAsync(ChannelId))
            .Value.Should()
            .ContainSingle()
            .Subject;

        item.Id.Should().Be($"spam-not-removed:{latest.Id}");
        item.Kind.Should().Be("spam_not_removed");
        item.Severity.Should().Be("warning");
        item.TitleKey.Should().Be("attention_spam_not_removed_title");
        item.MessageKey.Should().Be("attention_spam_not_removed_message");
        item.Parameters.Should().Contain("username", "streamboo_bot_2");
        item.Parameters.Should().Contain("count", "2");
        item.Count.Should().Be(2);
        item.DetectedAt.Should().Be(T0.AddMinutes(5));
        item.DeepLinkRoute.Should().Be("chat/one-channel");
    }

    [Fact]
    public async Task A_moderator_who_turned_removal_on_ends_the_notice()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            onboarded: false,
            Membership(removesSpamInOwnName: true),
            Detection(T0, "streamboo_bot_1")
        );

        (await InboxAt(db, T0.AddMinutes(10)).GetItemsAsync(ChannelId)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_streamer_yes_on_record_ends_the_notice()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            onboarded: false,
            Membership(removesSpamInOwnName: false),
            new ModeratorMassBanOptIn
            {
                OperatorUserId = ModeratorId,
                BroadcasterTwitchId = TwitchChannelId,
                BroadcasterLogin = "resident_meeper",
                RecordedAt = T0.AddDays(-1),
            },
            Detection(T0, "streamboo_bot_1")
        );

        (await InboxAt(db, T0.AddMinutes(10)).GetItemsAsync(ChannelId)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task An_onboarded_channel_never_shows_the_notice()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            onboarded: true,
            Detection(T0, "streamboo_bot_1")
        );

        (await InboxAt(db, T0.AddMinutes(10)).GetItemsAsync(ChannelId)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task A_dismissed_notice_stays_hidden_until_new_spam_arrives()
    {
        await using ActionRequiredInboxServiceTestDbContext db = await SeedAsync(
            onboarded: false,
            Membership(removesSpamInOwnName: false),
            Detection(T0, "streamboo_bot_1")
        );
        ActionRequiredInboxService inbox = InboxAt(db, T0.AddHours(1));
        string id = (await inbox.GetItemsAsync(ChannelId)).Value.Single().Id;

        await inbox.DismissAsync(ChannelId, ModeratorId, [id]);
        (await inbox.GetItemsAsync(ChannelId)).Value.Should().BeEmpty();

        SpamDetection next = Detection(T0.AddMinutes(30), "streamboo_bot_2");
        db.SpamDetections.Add(next);
        await db.SaveChangesAsync();

        ActionRequiredItemDto item = (await inbox.GetItemsAsync(ChannelId))
            .Value.Should()
            .ContainSingle()
            .Subject;
        item.Id.Should().Be($"spam-not-removed:{next.Id}");
        item.Count.Should().Be(2);
    }
}
