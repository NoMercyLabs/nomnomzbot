// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using FluentAssertions;

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

public sealed class PredictionHelpersTableTests
{
    // Channel A: Yes 3,000 points (9 users), No 1,000 (3 users), window 60 s that opened 15 s before the clock.
    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{prediction.question}", "Win?" },
            { "{if:{prediction.isActive}|bet now|}", "bet now" },
            { "{prediction.isActive}", "true" },
            { "{prediction.status.now}", "open" },
            { "{prediction.outcomes.list}", "Yes, No" },
            { "{prediction.leading}", "Yes" },
            { "{prediction.leading.points}", "3,000" },
            { "{prediction.leading.percent}", "75%" },
            { "{prediction.points.total}", "4,000" },
            { "{prediction.users.total}", "12" },
            { "{prediction.outcome:2}", "No" },
            { "{prediction.outcome.points:2}", "1,000" },
            { "{prediction.outcome.percent:2}", "25%" },
            { "{prediction.outcome.odds:2}", "1:4" },
            { "{prediction.outcome.odds:1}", "1:1.33" },
            { "{prediction.topPredictor}", "ViewerOne (1,000)" },
            { "{prediction.timeLeft}", "45s" },
            { "{prediction.last.winner}", "Yes" },
            { "{prediction.outcome:9}", "" },
            { "{prediction.outcome:0}", "{prediction.outcome:0}" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task An_open_prediction_renders_the_exact_catalog_output(
        string template,
        string expected
    )
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, PollPredictionHelperFixture.ChannelA);

        rendered.Should().Be(expected);
    }

    // Channel B: nothing running.
    public static TheoryData<string, string> NoPredictionRows =>
        new()
        {
            { "{prediction.question}", "" },
            { "{prediction.isActive}", "false" },
            { "{if:{prediction.isActive}|bet now|}", "" },
            { "{prediction.status.now}", "" },
            { "{prediction.points.total}", "" },
            { "{prediction.timeLeft}", "" },
            { "{prediction.last.winner}", "" },
        };

    [Theory]
    [MemberData(nameof(NoPredictionRows))]
    public async Task A_channel_without_a_prediction_renders_empty_or_the_fallback_branch(
        string template,
        string expected
    )
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, PollPredictionHelperFixture.ChannelB);

        rendered.Should().Be(expected);
    }

    // Channel C: the same prediction, locked; the channel language is Dutch.
    public static TheoryData<string, string> LockedRows =>
        new()
        {
            { "{prediction.isActive}", "false" },
            { "{prediction.status.now}", "locked" },
            { "{prediction.timeLeft}", "" },
            { "{prediction.leading.points}", "3.000" },
        };

    [Theory]
    [MemberData(nameof(LockedRows))]
    public async Task A_locked_prediction_is_no_longer_open_and_has_no_time_left(
        string template,
        string expected
    )
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, PollPredictionHelperFixture.ChannelC);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task One_render_asks_the_predictions_api_and_the_history_once()
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{prediction.question} {prediction.leading} {prediction.points.total} {prediction.last.winner}",
            PollPredictionHelperFixture.ChannelA
        );

        rendered.Should().Be("Win? Yes 4,000 Yes");
        fixture.PredictionCalls.Should().Be(1);
        fixture.HistoryCalls.Should().Be(1);
        fixture.PollCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_render_without_a_channel_prints_every_prediction_token_as_written()
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{prediction.question} {prediction.isActive}",
            null
        );

        rendered.Should().Be("{prediction.question} {prediction.isActive}");
    }
}
