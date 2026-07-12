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
using NomNomzBot.Domain.Rewards.Events;
using NomNomzBot.Domain.Stream.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the sub / resub / gift / cheer / raid broadcasters push their alert DTO to the dashboard AND fan the SAME
/// dto to the overlays (generic feed + subscribed widgets) — replacing the deleted minimal <c>new { user, ... }</c>
/// widget handlers, so an OBS overlay now receives the same typed alert shape the dashboard consumes rather than a
/// thin anonymous payload.
/// </summary>
public sealed class SubscriptionCheerRaidBroadcastTests
{
    private static (
        IDashboardNotifier Notifier,
        IWidgetNotifier Widgets,
        WidgetTestDbContext Db
    ) Build() =>
        (
            Substitute.For<IDashboardNotifier>(),
            Substitute.For<IWidgetNotifier>(),
            WidgetTestDbContext.New()
        );

    [Fact]
    public async Task NewSubscription_reaches_the_dashboard_and_the_overlay_feed()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        NewSubscriptionBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new NewSubscriptionEvent
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "Subber",
                Tier = "1000",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "subscription",
                Arg.Is<object>(data =>
                    data is SubscriptionAlertDto
                    && ((SubscriptionAlertDto)data).DisplayName == "Subber"
                    && ((SubscriptionAlertDto)data).Tier == "1000"
                ),
                Arg.Any<CancellationToken>(),
                userId: "u1",
                userDisplayName: "Subber"
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "subscription"
                    && evt.Payload.Contains("\"displayName\":\"Subber\"")
                    && evt.Payload.Contains("\"tier\":\"1000\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task NewSubscription_reaches_a_subscribed_widget_with_the_typed_dto()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        Widget widget = new()
        {
            Id = Guid.NewGuid().ToString(),
            BroadcasterId = channel,
            Name = "Sub alert",
            IsEnabled = true,
            EventSubscriptions = ["subscription"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        NewSubscriptionBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new NewSubscriptionEvent
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "Subber",
                Tier = "2000",
            }
        );

        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id,
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "subscription"
                    && evt.Data is SubscriptionAlertDto
                    && ((SubscriptionAlertDto)evt.Data!).Tier == "2000"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Resubscription_reaches_the_dashboard_and_the_overlay_feed()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        ResubscriptionBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new ResubscriptionEvent
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "Loyal",
                Tier = "1000",
                CumulativeMonths = 6,
                StreakMonths = 3,
                Message = "still here",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "resub",
                Arg.Is<object>(data =>
                    data is ResubAlertDto
                    && ((ResubAlertDto)data).Months == 6
                    && ((ResubAlertDto)data).Streak == 3
                ),
                Arg.Any<CancellationToken>(),
                userId: "u1",
                userDisplayName: "Loyal"
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "resub"
                    && evt.Payload.Contains("\"months\":6")
                    && evt.Payload.Contains("\"streak\":3")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task GiftSubscription_reaches_the_dashboard_and_the_overlay_feed()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        GiftSubscriptionBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new GiftSubscriptionEvent
            {
                BroadcasterId = channel,
                GifterUserId = "g1",
                GifterDisplayName = "Generous",
                Tier = "1000",
                GiftCount = 5,
                IsAnonymous = false,
                Recipients = [new GiftRecipient("r1", "Lucky")],
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "gift_sub",
                Arg.Is<object>(data =>
                    data is GiftSubAlertDto
                    && ((GiftSubAlertDto)data).GifterDisplayName == "Generous"
                    && ((GiftSubAlertDto)data).Count == 5
                    && !((GiftSubAlertDto)data).Anonymous
                ),
                Arg.Any<CancellationToken>(),
                userId: "g1",
                userDisplayName: "Generous"
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "gift_sub"
                    && evt.Payload.Contains("\"gifterDisplayName\":\"Generous\"")
                    && evt.Payload.Contains("\"count\":5")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Cheer_reaches_the_dashboard_and_the_overlay_feed()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        CheerBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new CheerEvent
            {
                BroadcasterId = channel,
                UserId = "u1",
                UserDisplayName = "Cheerer",
                Bits = 100,
                Message = "pog",
                IsAnonymous = false,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "cheer",
                Arg.Is<object>(data =>
                    data is CheerAlertDto
                    && ((CheerAlertDto)data).Bits == 100
                    && ((CheerAlertDto)data).DisplayName == "Cheerer"
                ),
                Arg.Any<CancellationToken>(),
                userId: "u1",
                userDisplayName: "Cheerer"
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "cheer"
                    && evt.Payload.Contains("\"bits\":100")
                    && evt.Payload.Contains("\"displayName\":\"Cheerer\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Raid_reaches_the_dashboard_and_the_overlay_feed()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        RaidBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new RaidEvent
            {
                BroadcasterId = channel,
                FromUserId = "u1",
                FromDisplayName = "Raider",
                FromLogin = "raider",
                ViewerCount = 50,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "raid",
                Arg.Is<object>(data =>
                    data is RaidAlertDto
                    && ((RaidAlertDto)data).FromDisplayName == "Raider"
                    && ((RaidAlertDto)data).ViewerCount == 50
                ),
                Arg.Any<CancellationToken>(),
                userId: "u1",
                userDisplayName: "Raider"
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "raid"
                    && evt.Payload.Contains("\"fromDisplayName\":\"Raider\"")
                    && evt.Payload.Contains("\"viewerCount\":50")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Cheer_on_the_platform_sentinel_channel_reaches_neither_surface()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        CheerBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new CheerEvent
            {
                BroadcasterId = Guid.Empty,
                UserId = "u1",
                UserDisplayName = "Cheerer",
                Bits = 100,
                Message = "pog",
                IsAnonymous = false,
            }
        );

        await notifier
            .DidNotReceiveWithAnyArgs()
            .NotifyChannelAsync(default!, default!, default!, default);
        await widgets
            .DidNotReceiveWithAnyArgs()
            .BroadcastOverlayEventAsync(default!, default!, default);
    }
}
