// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
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
            new ModeratorAddedEvent
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
            new ModeratorRemovedEvent
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
            new VipAddedEvent
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
            new VipRemovedEvent
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
            new ModeratorAddedEvent
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
            new ModeratorAddedEvent
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
            new ModeratorAddedEvent
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
}
