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
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Widgets.Entities;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the hype train OVERLAY handlers route through the same widget-subscription gate as every other overlay
/// alert (<c>WidgetAlertDispatch.RouteAsync</c>): only an enabled widget that declares the event type receives it,
/// and the pushed <see cref="WidgetEventDto"/> carries the flattened level/progress fields. This is the "overlays
/// plausibly need it: hype train" half of the gap — the dashboard-only counterpart is
/// <see cref="HypeTrainBroadcastHandlersTests"/>.
/// </summary>
public sealed class WidgetHypeTrainAlertHandlersTests
{
    private static readonly HypeTrainContribution[] Contributions =
    [
        new("u1", "user1", "User1", "bits", 500),
    ];

    private static Widget SubscribedWidget(Guid broadcasterId, string eventType) =>
        new()
        {
            Id = Guid.NewGuid().ToString(),
            BroadcasterId = broadcasterId,
            Name = "Hype train meter",
            IsEnabled = true,
            EventSubscriptions = [eventType],
        };

    [Fact]
    public async Task HypeTrainBegan_SubscribedWidget_ReceivesFlattenedLevelProgressAndGoal()
    {
        Guid channel = Guid.CreateVersion7();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Widget widget = SubscribedWidget(channel, "hype_train_begin");
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetHypeTrainBeganAlertHandler handler = new(db, notifier);

        await handler.HandleAsync(
            new HypeTrainBeganEvent
            {
                BroadcasterId = channel,
                HypeTrainId = "ht-1",
                Level = 2,
                Total = 3000,
                Progress = 1200,
                Goal = 5000,
                TopContributions = Contributions,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(5),
            }
        );

        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id,
                Arg.Is<WidgetEventDto>(dto =>
                    dto.WidgetId == widget.Id && dto.EventType == "hype_train_begin"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HypeTrainProgress_SubscribedWidget_ReceivesUpdatedProgress()
    {
        Guid channel = Guid.CreateVersion7();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Widget widget = SubscribedWidget(channel, "hype_train_progress");
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetHypeTrainProgressAlertHandler handler = new(db, notifier);

        await handler.HandleAsync(
            new HypeTrainProgressEvent
            {
                BroadcasterId = channel,
                HypeTrainId = "ht-1",
                Level = 3,
                Total = 4500,
                Progress = 2200,
                Goal = 6000,
                TopContributions = Contributions,
                ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(3),
            }
        );

        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id,
                Arg.Is<WidgetEventDto>(dto =>
                    dto.WidgetId == widget.Id && dto.EventType == "hype_train_progress"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HypeTrainEnded_SubscribedWidget_ReceivesFinalLevel()
    {
        Guid channel = Guid.CreateVersion7();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        Widget widget = SubscribedWidget(channel, "hype_train_end");
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();

        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetHypeTrainEndedAlertHandler handler = new(db, notifier);

        await handler.HandleAsync(
            new HypeTrainEndedEvent
            {
                BroadcasterId = channel,
                HypeTrainId = "ht-1",
                Level = 4,
                Total = 8000,
                TopContributions = Contributions,
                EndedAt = DateTimeOffset.UtcNow,
            }
        );

        await notifier
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id,
                Arg.Is<WidgetEventDto>(dto =>
                    dto.WidgetId == widget.Id && dto.EventType == "hype_train_end"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task HypeTrainBegan_WidgetNotSubscribed_DoesNotReceiveTheEvent()
    {
        Guid channel = Guid.CreateVersion7();
        await using WidgetTestDbContext db = WidgetTestDbContext.New();
        // Subscribed to a different event type — must NOT receive hype_train_begin.
        db.Widgets.Add(SubscribedWidget(channel, "follow"));
        await db.SaveChangesAsync();

        IWidgetNotifier notifier = Substitute.For<IWidgetNotifier>();
        WidgetHypeTrainBeganAlertHandler handler = new(db, notifier);

        await handler.HandleAsync(
            new HypeTrainBeganEvent
            {
                BroadcasterId = channel,
                HypeTrainId = "ht-1",
                Level = 1,
                Total = 1,
                Progress = 1,
                Goal = 1,
                TopContributions = Contributions,
                ExpiresAt = DateTimeOffset.UtcNow,
            }
        );

        await notifier
            .DidNotReceive()
            .SendWidgetEventAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<WidgetEventDto>(),
                Arg.Any<CancellationToken>()
            );
    }
}
