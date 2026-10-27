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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.LiveEvents;

public sealed class PollHelpersTableTests
{
    // Channel A: chips 12, cookies 8, 120 s poll that started 30 s before the 14:30:00Z clock.
    public static TheoryData<string, string> Rows =>
        new()
        {
            { "{poll.question}", "Best snack?" },
            { "{if:{poll.isActive}|vote now!|no poll}", "vote now!" },
            { "{poll.isActive}", "true" },
            { "{poll.options}", "chips, cookies" },
            { "{poll.options.numbered}", "1. chips 2. cookies" },
            { "{poll.leading}", "chips" },
            { "{poll.leading.votes}", "12" },
            { "{poll.leading.percent}", "60%" },
            { "{poll.votes.total}", "20" },
            { "{poll.option:2}", "cookies" },
            { "{poll.option.votes:2}", "8" },
            { "{poll.option.percent:2}", "40%" },
            { "{poll.results.bars}", "chips ██████░░░░ 60% · cookies ████░░░░░░ 40%" },
            { "{poll.timeLeft}", "1m 30s" },
            { "{poll.endsAt}", "15:32" },
            { "{if:{poll.isTie}|it's a tie|{poll.leading} leads}", "chips leads" },
            { "{poll.last.winner}", "chips" },
            { "{poll.last.question}", "Best snack?" },
            // An option number past the list is empty; a number that is not one prints the token as written.
            { "{poll.option:5}", "" },
            { "{poll.option:x}", "{poll.option:x}" },
            { "{poll.option}", "{poll.option}" },
        };

    [Theory]
    [MemberData(nameof(Rows))]
    public async Task A_running_poll_renders_the_exact_catalog_output(
        string template,
        string expected
    )
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, PollPredictionHelperFixture.ChannelA);

        rendered.Should().Be(expected);
    }

    // Channel B: no poll running and no history.
    public static TheoryData<string, string> NoPollRows =>
        new()
        {
            { "{poll.question}", "" },
            { "{poll.isActive}", "false" },
            { "{if:{poll.isActive}|vote now!|no poll}", "no poll" },
            { "{poll.options}", "" },
            { "{poll.leading}", "" },
            { "{poll.votes.total}", "" },
            { "{poll.timeLeft}", "" },
            { "{poll.endsAt}", "" },
            { "{poll.last.winner}", "" },
            { "{poll.last.question}", "" },
        };

    [Theory]
    [MemberData(nameof(NoPollRows))]
    public async Task A_channel_without_a_poll_renders_empty_or_the_fallback_branch(
        string template,
        string expected
    )
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, PollPredictionHelperFixture.ChannelB);

        rendered.Should().Be(expected);
    }

    // Channel C: chips 5, cookies 5.
    public static TheoryData<string, string> TieRows =>
        new()
        {
            { "{if:{poll.isTie}|it's a tie|{poll.leading} leads}", "it's a tie" },
            { "{poll.leading.percent}", "50%" },
            { "{poll.results.bars}", "chips █████░░░░░ 50% · cookies █████░░░░░ 50%" },
        };

    [Theory]
    [MemberData(nameof(TieRows))]
    public async Task A_tied_poll_reports_the_tie(string template, string expected)
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(template, PollPredictionHelperFixture.ChannelC);

        rendered.Should().Be(expected);
    }

    [Fact]
    public async Task One_render_asks_the_polls_api_and_the_history_once_however_many_tokens_it_holds()
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync(
            "{poll.question} {poll.leading} {poll.votes.total} {poll.timeLeft} {poll.last.winner} {poll.last.question}",
            PollPredictionHelperFixture.ChannelA
        );

        rendered.Should().Be("Best snack? chips 20 1m 30s chips Best snack?");
        fixture.PollCalls.Should().Be(1);
        fixture.HistoryCalls.Should().Be(1);
        fixture.PredictionCalls.Should().Be(0);
    }

    [Fact]
    public async Task A_render_without_a_channel_prints_every_poll_token_as_written()
    {
        await using PollPredictionHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{poll.question} {poll.isActive}", null);

        rendered.Should().Be("{poll.question} {poll.isActive}");
    }
}
