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
using NSubstitute;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// Proves the poll broadcasters forward begin/progress/end to dashboard clients over the generic
/// <c>ChannelEvent</c> taxonomy (<see cref="IDashboardNotifier.NotifyChannelAsync"/>), with choices mapped onto
/// <see cref="PollChoiceDto"/> — the gap this closes: the pipeline-trigger handlers fired
/// <c>event_response:poll_begin</c>/<c>poll_end</c> but no hub client ever saw the poll.
/// </summary>
public sealed class PollBroadcastHandlersTests
{
    private static readonly PollChoice[] Choices =
    [
        new("c1", "Cats", 10, 2),
        new("c2", "Dogs", 7, 1),
    ];

    [Fact]
    public async Task PollBegan_MapsChoicesAndWindow_AsPollBeginChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PollBeganBroadcastHandler handler = new(notifier);
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
    public async Task PollProgress_MapsRunningTallies_AsPollProgressChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PollProgressBroadcastHandler handler = new(notifier);
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
    }

    [Fact]
    public async Task PollEnded_MapsStatusAndWinner_AsPollEndChannelEvent()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PollEndedBroadcastHandler handler = new(notifier);
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
    }

    [Fact]
    public async Task PollBegan_PlatformSentinelChannel_DoesNotNotify()
    {
        IDashboardNotifier notifier = Substitute.For<IDashboardNotifier>();
        PollBeganBroadcastHandler handler = new(notifier);

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
    }
}
