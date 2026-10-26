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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Dtos;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Domain.Community.Events;
using NomNomzBot.Domain.Widgets.Entities;

namespace NomNomzBot.Api.Tests.Hubs;

/// <summary>
/// A reloaded poll_prediction widget shows the running poll at once: the newest poll frame is replayed, with
/// its live tallies. A finished poll comes back only while the widget would still show it (8 seconds).
/// </summary>
public sealed class PollPredictionSeedProviderTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly DateTimeOffset Start = new(2026, 10, 26, 20, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<PollChoice> Tallies =
    [
        new("a", "Yes", 7, 0),
        new("b", "No", 3, 0),
    ];

    private static Widget PollWidget => new() { BroadcasterId = Broadcaster };

    [Fact]
    public async Task A_running_poll_comes_back_with_its_latest_tallies()
    {
        FakeTimeProvider clock = new(Start);
        PollPredictionStateStore store = new(clock);
        PollPredictionStateRecorder recorder = new(store);
        await recorder.HandleAsync(
            new PollBeganEvent
            {
                BroadcasterId = Broadcaster,
                PollId = "p1",
                Title = "Best snack?",
                Choices = [new("a", "Yes", 0, 0), new("b", "No", 0, 0)],
                DurationSeconds = 60,
                EndsAt = Start.AddSeconds(60),
            }
        );
        await recorder.HandleAsync(
            new PollProgressEvent
            {
                BroadcasterId = Broadcaster,
                PollId = "p1",
                Title = "Best snack?",
                Choices = Tallies,
                EndsAt = Start.AddSeconds(60),
            }
        );

        IReadOnlyList<WidgetSeedFrame> frames = await new PollPredictionSeedProvider(
            store,
            clock
        ).SeedAsync(Broadcaster, PollWidget, CancellationToken.None);

        frames.Should().ContainSingle();
        frames[0].EventType.Should().Be("poll_progress");
        ((PollProgressAlertDto)frames[0].Data!).Choices.Select(c => c.Votes).Should().Equal(7, 3);
    }

    [Fact]
    public async Task A_finished_poll_comes_back_only_while_the_widget_still_shows_it()
    {
        FakeTimeProvider clock = new(Start);
        PollPredictionStateStore store = new(clock);
        await new PollPredictionStateRecorder(store).HandleAsync(
            new PollEndedEvent
            {
                BroadcasterId = Broadcaster,
                PollId = "p1",
                Title = "Best snack?",
                Status = "completed",
                Choices = Tallies,
                WinningChoiceId = "a",
            }
        );
        PollPredictionSeedProvider sut = new(store, clock);

        clock.Advance(TimeSpan.FromSeconds(5));
        (await sut.SeedAsync(Broadcaster, PollWidget, CancellationToken.None))
            .Select(f => f.EventType)
            .Should()
            .Equal("poll_end");

        clock.Advance(TimeSpan.FromSeconds(5));
        (await sut.SeedAsync(Broadcaster, PollWidget, CancellationToken.None)).Should().BeEmpty();
    }
}
