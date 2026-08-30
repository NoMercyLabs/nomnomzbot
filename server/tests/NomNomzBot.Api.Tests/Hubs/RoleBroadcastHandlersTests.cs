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
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the moderator/VIP role broadcasters forward grants and revocations to dashboard clients over the
/// generic <c>ChannelEvent</c> taxonomy AND fan the SAME decorated <see cref="RoleChangedAlertDto"/> to the
/// overlays (generic feed + subscribed widgets).
/// </summary>
public sealed class RoleBroadcastHandlersTests
{
    [Fact]
    public async Task ModeratorAdded_MapsUser_AsModeratorAddedChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        ModeratorAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "UserOne",
                UserLogin = "userone",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "moderator_added",
                Arg.Is<object>(data =>
                    data is RoleChangedAlertDto
                    && ((RoleChangedAlertDto)data).UserId == "u1"
                    && ((RoleChangedAlertDto)data).UserDisplayName == "UserOne"
                    && ((RoleChangedAlertDto)data).UserLogin == "userone"
                ),
                Arg.Any<CancellationToken>(),
                userId: "u1",
                userDisplayName: "UserOne"
            );
    }

    [Fact]
    public async Task ModeratorRemoved_MapsUser_AsModeratorRemovedChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        ModeratorRemovedBroadcastHandler handler = new(notifier, enricher, db, widgets);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "UserOne",
                UserLogin = "userone",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "moderator_removed",
                Arg.Is<object>(data =>
                    data is RoleChangedAlertDto && ((RoleChangedAlertDto)data).UserId == "u1"
                ),
                Arg.Any<CancellationToken>(),
                userId: "u1",
                userDisplayName: "UserOne"
            );
    }

    [Fact]
    public async Task VipAdded_MapsUser_AsVipAddedChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        VipAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                UserId = "u2",
                UserDisplayName = "UserTwo",
                UserLogin = "usertwo",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "vip_added",
                Arg.Is<object>(data =>
                    data is RoleChangedAlertDto
                    && ((RoleChangedAlertDto)data).UserId == "u2"
                    && ((RoleChangedAlertDto)data).UserDisplayName == "UserTwo"
                    && ((RoleChangedAlertDto)data).UserLogin == "usertwo"
                ),
                Arg.Any<CancellationToken>(),
                userId: "u2",
                userDisplayName: "UserTwo"
            );
    }

    [Fact]
    public async Task VipRemoved_MapsUser_AsVipRemovedChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        VipRemovedBroadcastHandler handler = new(notifier, enricher, db, widgets);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                UserId = "u2",
                UserDisplayName = "UserTwo",
                UserLogin = "usertwo",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "vip_removed",
                Arg.Is<object>(data =>
                    data is RoleChangedAlertDto && ((RoleChangedAlertDto)data).UserId == "u2"
                ),
                Arg.Any<CancellationToken>(),
                userId: "u2",
                userDisplayName: "UserTwo"
            );
    }

    [Fact]
    public async Task ModeratorAdded_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        ModeratorAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = Guid.Empty,
                UserId = "u1",
                UserDisplayName = "x",
                UserLogin = "x",
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>(),
                userId: Arg.Any<string?>(),
                userDisplayName: Arg.Any<string?>()
            );
        await widgets
            .DidNotReceiveWithAnyArgs()
            .BroadcastOverlayEventAsync(default!, default!, default);
    }

    [Fact]
    public async Task ModeratorAdded_WithKnownUser_CarriesTheEnrichedFields()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns(
                new HubUserEnrichment("UserOne", "https://cdn/avatar.png", "he/him", "Moderator")
            );
        ModeratorAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "UserOne",
                UserLogin = "userone",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "moderator_added",
                Arg.Is<object>(data =>
                    data is RoleChangedAlertDto
                    && ((RoleChangedAlertDto)data).AvatarUrl == "https://cdn/avatar.png"
                    && ((RoleChangedAlertDto)data).Pronouns == "he/him"
                    && ((RoleChangedAlertDto)data).CommunityStanding == "Moderator"
                ),
                Arg.Any<CancellationToken>(),
                userId: "u1",
                userDisplayName: "UserOne"
            );
    }

    [Fact]
    public async Task ModeratorAdded_is_also_pushed_to_overlays_as_a_decorated_event()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        enricher
            .EnrichAsync(channel, "u1", Arg.Any<CancellationToken>())
            .Returns(
                new HubUserEnrichment("UserOne", "https://cdn/avatar.png", "he/him", "Moderator")
            );
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Role alert",
            IsEnabled = true,
            EventSubscriptions = ["moderator_added"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        ModeratorAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new()
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "UserOne",
                UserLogin = "userone",
            }
        );

        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "moderator_added"
                    && evt.Payload.Contains("\"avatarUrl\":\"https://cdn/avatar.png\"")
                    && evt.Payload.Contains("\"communityStanding\":\"Moderator\"")
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "moderator_added" && evt.Data is RoleChangedAlertDto
                ),
                Arg.Any<CancellationToken>()
            );
    }

    /// <summary>
    /// S-REPLAY-VIPSHOUTOUT-CHANNELEVENT's done-when proof: unlike follow/sub/cheer/raid, VIP grants have no
    /// sibling <c>TwitchAlertHandlerBase</c> handler logging the activity-feed row — this broadcast handler IS
    /// the only consumer of <see cref="NomNomzBot.Domain.Moderation.Events.VipAddedEvent"/>, so it must write
    /// the <see cref="ChannelEvent"/> itself. Proves a VIP action produces BOTH a queryable ChannelEvent row
    /// (the same way DashboardController.GetActivity queries them) AND a RenderedAlertCapture correlated to
    /// that same ChannelEvent.Id (not null) — the existing ChannelEventId threading now has something real to
    /// point at.
    /// </summary>
    [Fact]
    public async Task VipAdded_LogsChannelEvent_AndCorrelatesTheWidgetCapture_ToItsId()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "VIP alert",
                IsEnabled = true,
                EventSubscriptions = ["vip_added"],
            }
        );
        await db.SaveChangesAsync();

        VipAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new()
            {
                EventId = eventId,
                BroadcasterId = channel,
                UserId = "u2",
                UserDisplayName = "UserTwo",
                UserLogin = "usertwo",
            }
        );

        // (a) A real, queryable ChannelEvent row — same table/columns DashboardController.GetActivity reads —
        // keyed by the domain event's own EventId.
        ChannelEvent feedRow = await db.ChannelEvents.SingleAsync(e => e.ChannelId == channel);
        feedRow.Id.Should().Be(eventId.ToString());
        feedRow.Type.Should().Be("channel.vip.add");

        // (b) The RenderedAlertCapture the widget dispatch wrote is correlated to that SAME ChannelEvent.Id —
        // not null — proving the existing correlation threading now resolves to something real.
        RenderedAlertCapture capture = await db.RenderedAlertCaptures.SingleAsync(c =>
            c.BroadcasterId == channel
        );
        capture.ChannelEventId.Should().Be(feedRow.Id);
        capture.ChannelEventId.Should().NotBeNull();
    }

    /// <summary>
    /// S-REPLAY-MODERATOR-CHANNELEVENT's done-when proof: unlike follow/sub/cheer/raid, moderator grants have
    /// no sibling <c>TwitchAlertHandlerBase</c> handler logging the activity-feed row — this broadcast handler
    /// IS the only consumer of <see cref="ModeratorAddedEvent"/>, so it must write the <see cref="ChannelEvent"/>
    /// itself. Proves a moderator-add action produces BOTH a queryable ChannelEvent row (the same way
    /// DashboardController.GetActivity queries them) AND a RenderedAlertCapture correlated to that same
    /// ChannelEvent.Id (not null).
    /// </summary>
    [Fact]
    public async Task ModeratorAdded_LogsChannelEvent_AndCorrelatesTheWidgetCapture_ToItsId()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Moderator alert",
                IsEnabled = true,
                EventSubscriptions = ["moderator_added"],
            }
        );
        await db.SaveChangesAsync();

        ModeratorAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new()
            {
                EventId = eventId,
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "UserOne",
                UserLogin = "userone",
            }
        );

        ChannelEvent feedRow = await db.ChannelEvents.SingleAsync(e => e.ChannelId == channel);
        feedRow.Id.Should().Be(eventId.ToString());
        feedRow.Type.Should().Be("channel.moderator.add");

        RenderedAlertCapture capture = await db.RenderedAlertCaptures.SingleAsync(c =>
            c.BroadcasterId == channel
        );
        capture.ChannelEventId.Should().Be(feedRow.Id);
        capture.ChannelEventId.Should().NotBeNull();
    }

    [Fact]
    public async Task ModeratorAdded_ReDelivery_DoesNotDoubleLogTheChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        ModeratorAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);
        ModeratorAddedEvent moderatorAdded = new()
        {
            EventId = eventId,
            BroadcasterId = channel,
            UserId = "u1",
            UserDisplayName = "UserOne",
            UserLogin = "userone",
        };

        await handler.HandleAsync(moderatorAdded);
        await handler.HandleAsync(moderatorAdded);

        (await db.ChannelEvents.CountAsync(e => e.ChannelId == channel)).Should().Be(1);
    }

    /// <summary>
    /// Same S-REPLAY-MODERATOR-CHANNELEVENT proof, for the revoke side: <see cref="ModeratorRemovedEvent"/>
    /// has the identical gap as the add side, fixed identically.
    /// </summary>
    [Fact]
    public async Task ModeratorRemoved_LogsChannelEvent_AndCorrelatesTheWidgetCapture_ToItsId()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        db.Widgets.Add(
            new()
            {
                Id = Guid.NewGuid(),
                BroadcasterId = channel,
                Name = "Moderator alert",
                IsEnabled = true,
                EventSubscriptions = ["moderator_removed"],
            }
        );
        await db.SaveChangesAsync();

        ModeratorRemovedBroadcastHandler handler = new(notifier, enricher, db, widgets);

        await handler.HandleAsync(
            new()
            {
                EventId = eventId,
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "UserOne",
                UserLogin = "userone",
            }
        );

        ChannelEvent feedRow = await db.ChannelEvents.SingleAsync(e => e.ChannelId == channel);
        feedRow.Id.Should().Be(eventId.ToString());
        feedRow.Type.Should().Be("channel.moderator.remove");

        RenderedAlertCapture capture = await db.RenderedAlertCaptures.SingleAsync(c =>
            c.BroadcasterId == channel
        );
        capture.ChannelEventId.Should().Be(feedRow.Id);
        capture.ChannelEventId.Should().NotBeNull();
    }

    [Fact]
    public async Task ModeratorRemoved_ReDelivery_DoesNotDoubleLogTheChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        ModeratorRemovedBroadcastHandler handler = new(notifier, enricher, db, widgets);
        ModeratorRemovedEvent moderatorRemoved = new()
        {
            EventId = eventId,
            BroadcasterId = channel,
            UserId = "u1",
            UserDisplayName = "UserOne",
            UserLogin = "userone",
        };

        await handler.HandleAsync(moderatorRemoved);
        await handler.HandleAsync(moderatorRemoved);

        (await db.ChannelEvents.CountAsync(e => e.ChannelId == channel)).Should().Be(1);
    }

    [Fact]
    public async Task VipAdded_ReDelivery_DoesNotDoubleLogTheChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        IHubUserEnricher enricher = Substitute.For<IHubUserEnricher>();
        IWidgetNotifier widgets = Substitute.For<IWidgetNotifier>();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Guid channel = Guid.CreateVersion7();
        Guid eventId = Guid.CreateVersion7();
        VipAddedBroadcastHandler handler = new(notifier, enricher, db, widgets);
        VipAddedEvent vipAdded = new()
        {
            EventId = eventId,
            BroadcasterId = channel,
            UserId = "u2",
            UserDisplayName = "UserTwo",
            UserLogin = "usertwo",
        };

        await handler.HandleAsync(vipAdded);
        await handler.HandleAsync(vipAdded);

        (await db.ChannelEvents.CountAsync(e => e.ChannelId == channel)).Should().Be(1);
    }
}
