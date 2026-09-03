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
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
using NomNomzBot.Domain.Notifications.Entities;
using NomNomzBot.Infrastructure.Notifications;

namespace NomNomzBot.Infrastructure.Tests.Notifications;

/// <summary>
/// Proves the action-required inbox (S071a) aggregates REAL, already-persisted signals rather than a
/// hardcoded list: a dead/expired <see cref="IntegrationConnection"/> and a pending AutoMod-held
/// <see cref="ModerationQueueItem"/> each surface with the right <c>Kind</c>/<c>Severity</c>, a healthy
/// connection and a resolved queue item do NOT, and everything is scoped to the requested channel only.
/// </summary>
public sealed class ActionRequiredInboxServiceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192b000-0000-7000-8000-0000000000e1");
    private static readonly Guid OtherChannelId = Guid.Parse(
        "0192b000-0000-7000-8000-0000000000e2"
    );

    [Fact]
    public async Task GetItemsAsync_SurfacesADeadIntegrationConnection_WithCriticalSeverity()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = ChannelId,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.NeedsReauth,
                ConsecutiveFailureCount = 3,
                LastErrorAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc),
            }
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        result.IsSuccess.Should().BeTrue();
        ActionRequiredItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be("integration_token_dead");
        item.Severity.Should().Be("critical");
        item.Title.Should().Contain("spotify");
        item.DetectedAt.Should().Be(new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc));
        item.DeepLinkRoute.Should().Be("/settings/integrations/spotify");
    }

    [Fact]
    public async Task GetItemsAsync_DoesNotSurfaceAHealthyConnection()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = ChannelId,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.Connected,
            }
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetItemsAsync_SurfacesAPendingAutoModHeldMessage_WithWarningSeverity()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.ModerationQueueItems.Add(
            new ModerationQueueItem
            {
                BroadcasterId = ChannelId,
                Source = ModerationQueueSource.AutoMod,
                Status = ModerationQueueStatus.Pending,
                TargetUsernameSnapshot = "chatter123",
                AutoModCategory = "swearing",
            }
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        ActionRequiredItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Kind.Should().Be("held_chat_message");
        item.Severity.Should().Be("warning");
        item.Message.Should().Contain("chatter123");
        item.DeepLinkRoute.Should().Be("/moderation/queue");
    }

    [Fact]
    public async Task GetItemsAsync_DoesNotSurfaceAnAlreadyResolvedQueueItem_OrAnotherChannelsItems()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        db.ModerationQueueItems.Add(
            new ModerationQueueItem
            {
                BroadcasterId = ChannelId,
                Source = ModerationQueueSource.AutoMod,
                Status = ModerationQueueStatus.Approved,
            }
        );
        db.ModerationQueueItems.Add(
            new ModerationQueueItem
            {
                BroadcasterId = OtherChannelId,
                Source = ModerationQueueSource.AutoMod,
                Status = ModerationQueueStatus.Pending,
            }
        );
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                BroadcasterId = OtherChannelId,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.Expired,
            }
        );
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        result.Value.Should().BeEmpty();
    }

    // ── S-OWN22 T2 — identity, per-user grouping, persisted dismissal ──

    private static ModerationQueueItem PendingHold(
        string twitchUserId,
        string username,
        DateTime createdAt
    ) =>
        new()
        {
            BroadcasterId = ChannelId,
            Source = ModerationQueueSource.AutoMod,
            Status = ModerationQueueStatus.Pending,
            TargetTwitchUserId = twitchUserId,
            TargetUsernameSnapshot = username,
            AutoModCategory = "swearing",
            CreatedAt = createdAt,
        };

    [Fact]
    public async Task GetItemsAsync_GroupsPendingHoldsPerUser_IntoOneItemWithCountAndQueueItemIds()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        DateTime t0 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        ModerationQueueItem hold1 = PendingHold("999001", "ashleyflores_01", t0);
        ModerationQueueItem hold2 = PendingHold("999001", "ashleyflores_01", t0.AddMinutes(5));
        ModerationQueueItem hold3 = PendingHold("999001", "ashleyflores_01", t0.AddMinutes(9));
        ModerationQueueItem otherUsersHold = PendingHold("999002", "chatter123", t0.AddMinutes(2));
        db.ModerationQueueItems.AddRange(hold1, hold2, hold3, otherUsersHold);
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        result.Value.Should().HaveCount(2, "3 holds from one user collapse into ONE item");

        ActionRequiredItemDto grouped = result.Value.Single(i => i.Count == 3);
        grouped.Id.Should().Be("held-user:999001");
        grouped.Kind.Should().Be("held_chat_message");
        grouped.SourceUserId.Should().Be("999001");
        grouped.SourceUserName.Should().Be("ashleyflores_01");
        grouped
            .QueueItemIds.Should()
            .BeEquivalentTo([hold1.Id, hold2.Id, hold3.Id], "every pending hold is addressable");
        grouped.Message.Should().Contain("3").And.Contain("ashleyflores_01");
        grouped.DetectedAt.Should().Be(t0.AddMinutes(9), "the group surfaces at its newest hold");

        ActionRequiredItemDto single = result.Value.Single(i => i.Count == 1);
        single.Id.Should().Be($"held:{otherUsersHold.Id}");
        single.SourceUserId.Should().Be("999002");
        single.SourceUserName.Should().Be("chatter123");
        single.QueueItemIds.Should().BeEquivalentTo([otherUsersHold.Id]);
        single.Message.Should().Contain("chatter123");
    }

    [Fact]
    public async Task DismissAsync_OfAGroupedItem_WritesOneRowPerContainedHeldKey_AndExcludesThem()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        DateTime t0 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        ModerationQueueItem hold1 = PendingHold("999001", "ashleyflores_01", t0);
        ModerationQueueItem hold2 = PendingHold("999001", "ashleyflores_01", t0.AddMinutes(1));
        ModerationQueueItem otherUsersHold = PendingHold("999002", "chatter123", t0.AddMinutes(2));
        db.ModerationQueueItems.AddRange(hold1, hold2, otherUsersHold);
        await db.SaveChangesAsync();
        Guid dismisser = Guid.Parse("0192b000-0000-7000-8000-0000000000a9");
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        Result<int> dismissed = await sut.DismissAsync(ChannelId, dismisser, ["held-user:999001"]);

        dismissed.IsSuccess.Should().BeTrue();
        dismissed.Value.Should().Be(2, "one dismissal row per contained held:{guid} key");
        List<ActionRequiredDismissal> rows = await db.ActionRequiredDismissals.ToListAsync();
        rows.Should().HaveCount(2);
        rows.Select(r => r.ItemKey)
            .Should()
            .BeEquivalentTo([$"held:{hold1.Id}", $"held:{hold2.Id}"]);
        rows.Should()
            .AllSatisfy(r =>
            {
                r.ChannelId.Should().Be(ChannelId);
                r.DismissedByUserId.Should().Be(dismisser);
                r.DismissedAt.Should().NotBe(default);
            });

        Result<List<ActionRequiredItemDto>> requery = await sut.GetItemsAsync(ChannelId);
        ActionRequiredItemDto remaining = requery.Value.Should().ContainSingle().Subject;
        remaining.Id.Should().Be($"held:{otherUsersHold.Id}", "the other user's item survives");
    }

    [Fact]
    public async Task GetItemsAsync_SurfacesANewHoldFromTheSameUser_AfterDismissingTheirGroup()
    {
        // The owner-visible promise: dismissing a per-user GROUP must not mute that user forever. The
        // group id is expanded into its contained held:{guid} keys at dismiss time, so a later hold from
        // the same user is a key no earlier dismissal covers. Dismissing the grouped id LITERALLY (storing
        // "held-user:999001" as the row) would leave the original holds visible here and mute the new one.
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        DateTime t0 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        ModerationQueueItem hold1 = PendingHold("999001", "ashleyflores_01", t0);
        ModerationQueueItem hold2 = PendingHold("999001", "ashleyflores_01", t0.AddMinutes(3));
        db.ModerationQueueItems.AddRange(hold1, hold2);
        await db.SaveChangesAsync();
        Guid dismisser = Guid.Parse("0192b000-0000-7000-8000-0000000000a9");
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        // Dismiss the GROUP as the dashboard sends it — the id the grouped row actually carries.
        ActionRequiredItemDto grouped = (await sut.GetItemsAsync(ChannelId))
            .Value.Should()
            .ContainSingle()
            .Subject;
        grouped.Id.Should().Be("held-user:999001");
        (await sut.DismissAsync(ChannelId, dismisser, [grouped.Id])).Value.Should().Be(2);
        (await db.ActionRequiredDismissals.Select(r => r.ItemKey).ToListAsync())
            .Should()
            .BeEquivalentTo(
                [$"held:{hold1.Id}", $"held:{hold2.Id}"],
                "the group id is expanded, never stored literally"
            );
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty("both holds were dismissed");

        ModerationQueueItem newHold = PendingHold("999001", "ashleyflores_01", t0.AddHours(1));
        db.ModerationQueueItems.Add(newHold);
        await db.SaveChangesAsync();

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        ActionRequiredItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Id.Should()
            .Be($"held:{newHold.Id}", "a NEW hold is a NEW key the old dismissal cannot hide");
        item.Count.Should().Be(1);
    }

    [Fact]
    public async Task DeadTokenKey_ChangesOnReInvalidation_SoAnOldDismissalDoesNotHideIt()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        DateTime firstDeath = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        IntegrationConnection connection = new()
        {
            BroadcasterId = ChannelId,
            Provider = AuthEnums.IntegrationProvider.Spotify,
            Status = AuthEnums.IntegrationStatus.NeedsReauth,
            ConsecutiveFailureCount = 3,
            LastErrorAt = firstDeath,
        };
        db.IntegrationConnections.Add(connection);
        await db.SaveChangesAsync();
        Guid dismisser = Guid.Parse("0192b000-0000-7000-8000-0000000000a9");
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        string firstKey = $"token:{connection.Id}:{firstDeath.Ticks}";
        (await sut.GetItemsAsync(ChannelId)).Value.Should().ContainSingle(i => i.Id == firstKey);
        (await sut.DismissAsync(ChannelId, dismisser, [firstKey])).Value.Should().Be(1);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().BeEmpty("the dead token was dismissed");

        // The token dies AGAIN after a fix — a later invalidation instant mints a NEW key.
        DateTime secondDeath = firstDeath.AddDays(3);
        connection.LastErrorAt = secondDeath;
        await db.SaveChangesAsync();

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        ActionRequiredItemDto item = result.Value.Should().ContainSingle().Subject;
        item.Id.Should().Be($"token:{connection.Id}:{secondDeath.Ticks}");
        item.Kind.Should().Be("integration_token_dead");
        item.Count.Should().Be(1);
        item.QueueItemIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvingAQueueItem_RemovesItsInboxItem_WithNoDismissalInvolved()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        DateTime t0 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        ModerationQueueItem hold = PendingHold("999001", "ashleyflores_01", t0);
        db.ModerationQueueItems.Add(hold);
        await db.SaveChangesAsync();
        ActionRequiredInboxService sut = new(db, TimeProvider.System);
        (await sut.GetItemsAsync(ChannelId)).Value.Should().ContainSingle();

        hold.Status = ModerationQueueStatus.Approved;
        await db.SaveChangesAsync();

        (await sut.GetItemsAsync(ChannelId))
            .Value.Should()
            .BeEmpty("resolution removes the item from the derived inbox");
        (await db.ActionRequiredDismissals.ToListAsync())
            .Should()
            .BeEmpty("no dismissal row is needed for a resolved item");
    }

    [Fact]
    public async Task DismissAsync_IsIdempotent_AndScopedToTheChannel()
    {
        await using ActionRequiredInboxServiceTestDbContext db =
            ActionRequiredInboxServiceTestDbContext.New();
        DateTime t0 = new(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);
        ModerationQueueItem hold = PendingHold("999001", "ashleyflores_01", t0);
        db.ModerationQueueItems.Add(hold);
        await db.SaveChangesAsync();
        Guid dismisser = Guid.Parse("0192b000-0000-7000-8000-0000000000a9");
        ActionRequiredInboxService sut = new(db, TimeProvider.System);

        (await sut.DismissAsync(ChannelId, dismisser, [$"held:{hold.Id}"])).Value.Should().Be(1);
        (await sut.DismissAsync(ChannelId, dismisser, [$"held:{hold.Id}"]))
            .Value.Should()
            .Be(0, "re-dismissing an already-dismissed key writes nothing");

        (await db.ActionRequiredDismissals.ToListAsync()).Should().ContainSingle();

        // Cross-tenant: ANOTHER channel dismissing the very same item key must not hide this channel's
        // live hold. Reading dismissals unscoped (dropping the ChannelId predicate) would mute it here.
        ModerationQueueItem liveHold = PendingHold("999002", "chatter123", t0.AddMinutes(5));
        db.ModerationQueueItems.Add(liveHold);
        db.ActionRequiredDismissals.Add(
            new ActionRequiredDismissal
            {
                ChannelId = OtherChannelId,
                ItemKey = $"held:{liveHold.Id}",
                DismissedByUserId = dismisser,
                DismissedAt = t0,
            }
        );
        await db.SaveChangesAsync();

        Result<List<ActionRequiredItemDto>> mine = await sut.GetItemsAsync(ChannelId);

        mine.Value.Select(i => i.Id)
            .Should()
            .BeEquivalentTo(
                [$"held:{liveHold.Id}"],
                "another channel's dismissal of the same key never reaches this tenant"
            );
    }
}
