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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A widget's YouTube player report names its state as one of four words (playing, paused, ended, error, any
/// case). Anything else, including a number or a comma list that .NET enum parsing would read, is refused and
/// stores nothing.
/// </summary>
public sealed class YouTubePlayerReportStateTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192b000-0000-7000-8000-0000000a7e01");
    private static readonly Guid Player = Guid.Parse("0192b000-0000-7000-8000-0000000a7e02");
    private const string Video = "dQw4w9WgXcQ";

    private static (YouTubePlayerReportService Reports, YouTubePlayerStateStore Store) Build()
    {
        IOverlayPresenceRegistry presence = Substitute.For<IOverlayPresenceRegistry>();
        presence.IsWidgetAttached(Broadcaster, Arg.Any<Guid>()).Returns(true);
        YouTubePlayerStateStore store = new(
            new FakeTimeProvider(DateTimeOffset.Parse("2026-10-04T12:00:00Z"))
        );
        YouTubePlayerDispatcher players = new(
            AuthTestBuilder.NewContext(),
            presence,
            Substitute.For<IWidgetEventNotifier>(),
            store
        );
        YouTubePlayerReportService reports = new(
            store,
            players,
            new RecordingEventBus(),
            new PlayOnceResumeTracker()
        );
        return (reports, store);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("1")]
    [InlineData("Playing, Paused")]
    [InlineData("buffering")]
    public async Task A_state_that_is_not_one_of_the_four_words_is_refused_and_stores_nothing(
        string state
    )
    {
        (YouTubePlayerReportService reports, YouTubePlayerStateStore store) = Build();

        Result result = await reports.ReportAsync(Broadcaster, Player, Video, state, 1000);

        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("INVALID_STATE");
        store.GetFresh(Broadcaster).Should().BeNull();
    }

    [Theory]
    [InlineData("PLAYING", YouTubePlayerState.Playing)]
    [InlineData("paused", YouTubePlayerState.Paused)]
    [InlineData("Ended", YouTubePlayerState.Ended)]
    [InlineData("ERROR", YouTubePlayerState.Error)]
    public async Task Each_of_the_four_words_is_stored_in_any_case(
        string state,
        YouTubePlayerState expected
    )
    {
        (YouTubePlayerReportService reports, YouTubePlayerStateStore store) = Build();

        Result result = await reports.ReportAsync(Broadcaster, Player, Video, state, 1000);

        result.IsSuccess.Should().BeTrue();
        store.GetFresh(Broadcaster)!.State.Should().Be(expected);
    }
}
