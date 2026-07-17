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
/// Proves the poll broadcasters forward begin/progress/end to dashboard clients over the generic
/// <c>ChannelEvent</c> taxonomy (<see cref="IDashboardNotifier.NotifyChannelAsync"/>) AND fan the SAME alert dto
/// to the overlays (generic feed + widgets subscribed to <c>poll_begin</c>/<c>poll_progress</c>/<c>poll_end</c>) —
/// the gap this closes: the <c>poll_prediction</c> overlay widget bound those event types but no handler ever
/// pushed them to the overlay surface.
/// </summary>
public sealed class PollBroadcastHandlersTests
{
    private static readonly PollChoice[] Choices =
    [
        new("c1", "Cats", 10, 2),
        new("c2", "Dogs", 7, 1),
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
    public async Task PollBegan_MapsChoicesAndWindow_AsPollBeginChannelEvent()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PollBeganBroadcastHandler handler = new(notifier, db, widgets);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset endsAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new PollBeganEvent
            {
                BroadcasterId = channel,
                PollId = "poll-1",
                Title = "Cats or dogs?",
                Choices = Choices,
                DurationSeconds = 300,
                EndsAt = endsAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "poll_begin",
                Arg.Is<object>(data =>
                    data is PollBeganAlertDto
                    && ((PollBeganAlertDto)data).PollId == "poll-1"
                    && ((PollBeganAlertDto)data).Title == "Cats or dogs?"
                    && ((PollBeganAlertDto)data).DurationSeconds == 300
                    && ((PollBeganAlertDto)data).EndsAt == endsAt
                    && ((PollBeganAlertDto)data).Choices.Count == 2
                    && ((PollBeganAlertDto)data).Choices[0]
                        == new PollChoiceDto("c1", "Cats", 10, 2)
                    && ((PollBeganAlertDto)data).Choices[1] == new PollChoiceDto("c2", "Dogs", 7, 1)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PollBegan_reaches_the_overlay_feed_and_a_subscribed_widget()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        Guid channel = Guid.CreateVersion7();
        Widget widget = new()
        {
            Id = Guid.NewGuid(),
            BroadcasterId = channel,
            Name = "Poll bar",
            IsEnabled = true,
            EventSubscriptions = ["poll_begin", "poll_progress", "poll_end"],
        };
        db.Widgets.Add(widget);
        await db.SaveChangesAsync();
        PollBeganBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new PollBeganEvent
            {
                BroadcasterId = channel,
                PollId = "poll-1",
                Title = "Cats or dogs?",
                Choices = Choices,
                DurationSeconds = 300,
                EndsAt = new DateTimeOffset(2026, 7, 1, 12, 5, 0, TimeSpan.Zero),
            }
        );

        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "poll_begin"
                    && evt.Payload.Contains("\"pollId\":\"poll-1\"")
                    && evt.Payload.Contains("\"title\":\"Cats or dogs?\"")
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .SendWidgetEventAsync(
                channel.ToString(),
                widget.Id.ToString(),
                Arg.Is<WidgetEventDto>(evt =>
                    evt.EventType == "poll_begin"
                    && evt.Data is PollBeganAlertDto
                    && ((PollBeganAlertDto)evt.Data!).PollId == "poll-1"
                    && ((PollBeganAlertDto)evt.Data!).Choices.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PollProgress_MapsRunningTallies_AsPollProgressChannelEvent_AndOverlayEvent()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PollProgressBroadcastHandler handler = new(notifier, db, widgets);
        Guid channel = Guid.CreateVersion7();
        DateTimeOffset endsAt = new(2026, 7, 1, 12, 5, 0, TimeSpan.Zero);

        await handler.HandleAsync(
            new PollProgressEvent
            {
                BroadcasterId = channel,
                PollId = "poll-1",
                Title = "Cats or dogs?",
                Choices = Choices,
                EndsAt = endsAt,
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "poll_progress",
                Arg.Is<object>(data =>
                    data is PollProgressAlertDto
                    && ((PollProgressAlertDto)data).PollId == "poll-1"
                    && ((PollProgressAlertDto)data).EndsAt == endsAt
                    && ((PollProgressAlertDto)data).Choices.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "poll_progress" && evt.Payload.Contains("\"pollId\":\"poll-1\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PollEnded_MapsStatusAndWinner_AsPollEndChannelEvent_AndOverlayEvent()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PollEndedBroadcastHandler handler = new(notifier, db, widgets);
        Guid channel = Guid.CreateVersion7();

        await handler.HandleAsync(
            new PollEndedEvent
            {
                BroadcasterId = channel,
                PollId = "poll-1",
                Title = "Cats or dogs?",
                Status = "completed",
                Choices = Choices,
                WinningChoiceId = "c1",
            }
        );

        await notifier
            .Received(1)
            .NotifyChannelAsync(
                channel.ToString(),
                "poll_end",
                Arg.Is<object>(data =>
                    data is PollEndedAlertDto
                    && ((PollEndedAlertDto)data).Status == "completed"
                    && ((PollEndedAlertDto)data).WinningChoiceId == "c1"
                    && ((PollEndedAlertDto)data).Choices.Count == 2
                ),
                Arg.Any<CancellationToken>()
            );
        await widgets
            .Received(1)
            .BroadcastOverlayEventAsync(
                channel.ToString(),
                Arg.Is<OverlayEventDto>(evt =>
                    evt.Type == "poll_end"
                    && evt.Payload.Contains("\"status\":\"completed\"")
                    && evt.Payload.Contains("\"winningChoiceId\":\"c1\"")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PollBegan_PlatformSentinelChannel_DoesNotNotify()
    {
        (IDashboardNotifier notifier, IWidgetNotifier widgets, WidgetTestDbContext db) = Build();
        await using WidgetTestDbContext _ = db;
        PollBeganBroadcastHandler handler = new(notifier, db, widgets);

        await handler.HandleAsync(
            new PollBeganEvent
            {
                BroadcasterId = Guid.Empty,
                PollId = "poll-1",
                Title = "t",
                Choices = Choices,
                DurationSeconds = 60,
                EndsAt = DateTimeOffset.UtcNow,
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
