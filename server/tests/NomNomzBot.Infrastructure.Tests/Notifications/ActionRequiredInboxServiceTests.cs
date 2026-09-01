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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Notifications.Dtos;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Moderation.Entities;
using NomNomzBot.Domain.Moderation.Enums;
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
        ActionRequiredInboxService sut = new(db);

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
        ActionRequiredInboxService sut = new(db);

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
        ActionRequiredInboxService sut = new(db);

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
        ActionRequiredInboxService sut = new(db);

        Result<List<ActionRequiredItemDto>> result = await sut.GetItemsAsync(ChannelId);

        result.Value.Should().BeEmpty();
    }
}
