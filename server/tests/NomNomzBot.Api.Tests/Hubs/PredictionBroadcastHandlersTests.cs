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
/// Proves the prediction broadcasters forward begin/progress/lock/end to dashboard clients over the generic
/// <c>ChannelEvent</c> taxonomy AND fan the SAME alert dto to the overlays (generic feed + widgets subscribed to
/// <c>prediction_begin</c>/<c>prediction_progress</c>/<c>prediction_lock</c>/<c>prediction_end</c>) — the gap
/// this closes: the <c>poll_prediction</c> overlay widget bound those event types but no handler ever pushed
/// them to the overlay surface.
/// </summary>
public sealed class PredictionBroadcastHandlersTests
{
    private static readonly PredictionOutcome[] Outcomes =
    [
        new("o1", "Yes", 1000, 12, "blue"),
        new("o2", "No", 500, 8, "pink"),
    ];

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
    public async Task PredictionBegan_MapsOutcomesAndWindow_AsPredictionBeginChannelEvent()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PredictionBeganBroadcastHandler handler = new(notifier, db, widgets);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset locksAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new PredictionBeganEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Outcomes = Outcomes,
                WindowSeconds = 300,
                LocksAt = locksAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_begin",
                Arg.Is<object>(data =>
                    data is PredictionBeganAlertDto
                    && ((PredictionBeganAlertDto)data).PredictionId == "pred-1"
                    && ((PredictionBeganAlertDto)data).WindowSeconds == 300
                    && ((PredictionBeganAlertDto)data).LocksAt == locksAt
                    && ((PredictionBeganAlertDto)data).Outcomes.Count == 2
                    && ((PredictionBeganAlertDto)data).Outcomes[0]
                        == new PredictionOutcomeDto("o1", "Yes", 1000, 12, "blue")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionBegan_reaches_the_overlay_feed_and_a_subscribed_widget()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Prediction bar",
            IsEnabled = true,
            EventSubscriptions = ["prediction_begin", "prediction_lock", "prediction_end"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        PredictionBeganBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new PredictionBeganEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Outcomes = Outcomes,
                WindowSeconds = 300,
                LocksAt = new DateTimeOffset(2026, 7, 1, 12, 5, 0, TimeSpan.Zero),
            }
        );

        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "prediction_begin"
                    && evt.Payload.Contains("\"predictionId\":\"pred-1\"")
                    && evt.Payload.Contains("\"title\":\"Will it rain?\"")
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "prediction_begin"
                    && evt.Data is PredictionBeganAlertDto
                    && ((PredictionBeganAlertDto)evt.Data!).PredictionId == "pred-1"
                    && ((PredictionBeganAlertDto)evt.Data!).Outcomes.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionProgress_MapsRunningPools_AsPredictionProgressChannelEvent_AndOverlayEvent()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PredictionProgressBroadcastHandler handler = new(notifier, db, widgets);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset locksAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new PredictionProgressEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Outcomes = Outcomes,
                LocksAt = locksAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_progress",
                Arg.Is<object>(data =>
                    data is PredictionProgressAlertDto
                    && ((PredictionProgressAlertDto)data).PredictionId == "pred-1"
                    && ((PredictionProgressAlertDto)data).LocksAt == locksAt
                    && ((PredictionProgressAlertDto)data).Outcomes.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "prediction_progress"
                    && evt.Payload.Contains("\"predictionId\":\"pred-1\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionLocked_MapsOutcomes_AsPredictionLockChannelEvent_AndOverlayEvent()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PredictionLockedBroadcastHandler handler = new(notifier, db, widgets);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new PredictionLockedEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Outcomes = Outcomes,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_lock",
                Arg.Is<object>(data =>
                    data is PredictionLockedAlertDto
                    && ((PredictionLockedAlertDto)data).PredictionId == "pred-1"
                    && ((PredictionLockedAlertDto)data).Outcomes.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "prediction_lock"
                    && evt.Payload.Contains("\"predictionId\":\"pred-1\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionEnded_MapsStatusAndWinner_AsPredictionEndChannelEvent_AndOverlayEvent()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PredictionEndedBroadcastHandler handler = new(notifier, db, widgets);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new PredictionEndedEvent
            {
                BroadcasterId = channel,
                PredictionId = "pred-1",
                Title = "Will it rain?",
                Status = "resolved",
                Outcomes = Outcomes,
                WinningOutcomeId = "o1",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "prediction_end",
                Arg.Is<object>(data =>
                    data is PredictionEndedAlertDto
                    && ((PredictionEndedAlertDto)data).Status == "resolved"
                    && ((PredictionEndedAlertDto)data).WinningOutcomeId == "o1"
                    && ((PredictionEndedAlertDto)data).Outcomes.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "prediction_end"
                    && evt.Payload.Contains("\"status\":\"resolved\"")
                    && evt.Payload.Contains("\"winningOutcomeId\":\"o1\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PredictionBegan_PlatformSentinelChannel_DoesNotNotify()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PredictionBeganBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new PredictionBeganEvent
            {
                BroadcasterId = Guid.Empty,
                PredictionId = "pred-1",
                Title = "t",
                Outcomes = Outcomes,
                WindowSeconds = 60,
                LocksAt = DateTimeOffset.UtcNow,
            }
        );

        await notifier
            .DidNotReceive()
            .NotifyChannelAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .DidNotReceiveWithAnyArgs()
            .BroadcastOverlayEventAsync(default!, default!, default);
    }
}
