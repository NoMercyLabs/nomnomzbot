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

namespace NomNomzBot.Infrastructure.Tests.Platform.Templating.Helpers.Fun;

/// <summary>What the random and hashed rows promise beyond one seeded value: ranges, consistency and stability.</summary>
public sealed class FunHelpersBehaviorTests
{
    private static readonly DateTimeOffset Monday = new(2026, 10, 26, 14, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_coin_follows_the_seeded_draw_and_lands_on_both_sides()
    {
        await using FunHelperFixture fixture = new();
        Random mirror = new(1);
        HashSet<string> seen = [];

        for (int i = 0; i < 100; i++)
        {
            string rendered = await fixture.RenderAsync("{fun.coin}");

            rendered.Should().Be(mirror.Next(2) == 0 ? "heads" : "tails");
            seen.Add(rendered);
        }

        seen.Should().BeEquivalentTo("heads", "tails");
    }

    [Fact]
    public async Task Dice_stay_inside_their_range_and_the_detail_adds_up_to_the_total()
    {
        await using FunHelperFixture fixture = new();
        HashSet<int> totals = [];

        for (int i = 0; i < 300; i++)
        {
            int total = int.Parse(await fixture.RenderAsync("{fun.dice:2d6}"));
            total.Should().BeInRange(2, 12);
            totals.Add(total);

            string detail = await fixture.RenderAsync("{fun.dice.detail:3d4}");
            string[] sides = detail.Split(" = ");
            int[] rolls = [.. sides[0].Split(" + ").Select(int.Parse)];
            rolls.Should().HaveCount(3).And.OnlyContain(r => r >= 1 && r <= 4);
            rolls.Sum().Should().Be(int.Parse(sides[1]));
        }

        totals.Should().HaveCountGreaterThan(8);
    }

    [Fact]
    public async Task A_single_die_defaults_to_six_sides_and_a_dice_emoji_is_a_die_face()
    {
        await using FunHelperFixture fixture = new();
        const string Faces = "⚀⚁⚂⚃⚄⚅";

        for (int i = 0; i < 100; i++)
        {
            int roll = int.Parse(await fixture.RenderAsync("{fun.dice}"));
            roll.Should().BeInRange(1, 6);

            string face = await fixture.RenderAsync("{fun.dice.emoji}");
            Faces.Should().Contain(face);

            string[] three = (await fixture.RenderAsync("{fun.dice.emoji:3}")).Split(' ');
            three.Should().HaveCount(3).And.OnlyContain(f => Faces.Contains(f));
        }
    }

    [Fact]
    public async Task The_slots_report_a_win_only_when_all_three_symbols_match()
    {
        await using FunHelperFixture fixture = new();
        HashSet<string> outcomes = [];

        for (int i = 0; i < 800; i++)
        {
            string[] parts = (await fixture.RenderAsync("{fun.slots}|{fun.slots.win}")).Split('|');
            string[] symbols = parts[0].Split(' ');

            symbols.Should().HaveCount(3);
            parts[1].Should().Be(symbols.Distinct().Count() == 1 ? "true" : "false");
            outcomes.Add(parts[1]);
        }

        outcomes.Should().BeEquivalentTo("true", "false");
    }

    [Fact]
    public async Task The_slots_win_reads_false_when_the_render_pulled_no_slots()
    {
        await using FunHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{if:{fun.slots.win}|jackpot|try again}");

        rendered.Should().Be("try again");
    }

    [Theory]
    [InlineData("rock")]
    [InlineData("paper")]
    [InlineData("scissors")]
    public async Task Rock_paper_scissors_names_the_bot_choice_and_judges_by_the_rules(
        string choice
    )
    {
        await using FunHelperFixture fixture = new();
        Dictionary<string, string> beats = new()
        {
            ["rock"] = "scissors",
            ["paper"] = "rock",
            ["scissors"] = "paper",
        };
        HashSet<string> results = [];

        for (int i = 0; i < 90; i++)
        {
            string[] parts = (await fixture.RenderAsync("{fun.rps.vs:" + choice + "}")).Split(
                " — "
            );
            string expected =
                parts[0] == choice ? "a draw"
                : beats[choice] == parts[0] ? "you win"
                : "you lose";

            beats.Keys.Should().Contain(parts[0]);
            parts[1].Should().Be(expected);
            results.Add(parts[1]);
        }

        results.Should().BeEquivalentTo("a draw", "you win", "you lose");
    }

    [Fact]
    public async Task A_card_is_one_of_the_52_and_the_deck_is_covered()
    {
        await using FunHelperFixture fixture = new();
        HashSet<string> cards = [];

        for (int i = 0; i < 2000; i++)
            cards.Add(await fixture.RenderAsync("{fun.card}"));

        cards.Should().HaveCount(52);
        cards.Should().Contain(["A♠", "10♥", "K♣", "7♦"]);
    }

    [Fact]
    public async Task The_roulette_clicks_five_times_in_six_and_goes_bang_once()
    {
        await using FunHelperFixture fixture = new();
        int bangs = 0;

        for (int i = 0; i < 6000; i++)
            if (await fixture.RenderAsync("{fun.roulette}") == "bang")
                bangs++;

        bangs.Should().BeInRange(850, 1150);
    }

    [Fact]
    public async Task The_eight_ball_answers_from_the_channels_own_list_and_never_mixes_channels()
    {
        await using FunHelperFixture fixture = new();
        string first = await fixture.RenderAsync("{fun.8ball}", FunHelperFixture.ChannelA);

        first.Should().Be(FunHelperFixture.AnswersA[new Random(1).Next(3)]);

        HashSet<string> fromA = [];
        HashSet<string> fromB = [];
        for (int i = 0; i < 200; i++)
        {
            fromA.Add(await fixture.RenderAsync("{fun.8ball}", FunHelperFixture.ChannelA));
            fromB.Add(await fixture.RenderAsync("{fun.8ball}", FunHelperFixture.ChannelB));
        }

        fromA.Should().BeEquivalentTo(FunHelperFixture.AnswersA);
        fromB.Should().BeEquivalentTo(FunHelperFixture.AnswersB);
    }

    [Fact]
    public async Task The_eight_ball_prints_the_token_when_the_channel_has_no_list_or_there_is_no_channel()
    {
        await using FunHelperFixture fixture = new();

        (await fixture.RenderAsync("{fun.8ball}", FunHelperFixture.ChannelC))
            .Should()
            .Be("{fun.8ball}");
        (await fixture.RenderAsync("{fun.8ball}")).Should().Be("{fun.8ball}");
    }

    [Fact]
    public async Task A_love_score_is_one_number_for_the_pair_whatever_the_order_and_stays_inside_zero_to_100()
    {
        await using FunHelperFixture fixture = new();

        string forward = await fixture.RenderAsync("{fun.love:Ann|Bob}");
        string backward = await fixture.RenderAsync("{fun.love:bob|ANN}");

        backward.Should().Be(forward);
        int percent = int.Parse(forward.TrimEnd('%'));
        percent.Should().BeInRange(0, 100);
        forward.Should().EndWith("%");
    }

    [Fact]
    public async Task A_rating_is_stable_within_the_day_and_changes_on_another_day()
    {
        await using FunHelperFixture morning = new(now: Monday);
        await using FunHelperFixture evening = new(now: Monday.AddHours(8));
        await using FunHelperFixture otherDay = new(now: Monday.AddDays(1));
        string[] things = ["pizza", "tea", "cats", "mondays", "bugs", "sleep", "coffee", "rain"];

        List<string> sameDay = [];
        List<string> nextDay = [];
        foreach (string thing in things)
        {
            string template = "{fun.rate:" + thing + "}|{fun.love:" + thing + "|Zed}";
            string first = await morning.RenderAsync(template);
            sameDay.Add(await evening.RenderAsync(template));
            nextDay.Add(await otherDay.RenderAsync(template));

            sameDay[^1].Should().Be(first);
            int rating = int.Parse(first.Split('|')[0].Split('/')[0]);
            rating.Should().BeInRange(1, 10);
            first.Split('|')[0].Should().EndWith("/10");
        }

        nextDay.Should().NotEqual(sameDay);
    }

    [Fact]
    public async Task A_percent_is_stable_per_viewer_and_differs_between_viewers()
    {
        await using FunHelperFixture one = new(userLogin: "viewerone");
        await using FunHelperFixture again = new(userLogin: "viewerone");
        await using FunHelperFixture two = new(userLogin: "viewertwo");
        string[] things = ["luck", "skill", "chaos", "style", "rage", "focus", "mood", "aim"];

        List<string> ones = [];
        List<string> twos = [];
        foreach (string thing in things)
        {
            string template = "{fun.percent:" + thing + "}";
            ones.Add(await one.RenderAsync(template));
            (await again.RenderAsync(template)).Should().Be(ones[^1]);
            twos.Add(await two.RenderAsync(template));
            int.Parse(ones[^1].TrimEnd('%')).Should().BeInRange(0, 100);
        }

        twos.Should().NotEqual(ones);
    }

    [Fact]
    public async Task A_ship_name_blends_the_two_names_and_shows_both()
    {
        await using FunHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{fun.ship:Alexander|Bo}");

        rendered.Should().Be("Alexander + Bo = Alexo");
    }

    [Fact]
    public async Task The_countdown_counts_down_to_one()
    {
        await using FunHelperFixture fixture = new();

        string rendered = await fixture.RenderAsync("{fun.countdown:5}");

        rendered.Should().Be("5… 4… 3… 2… 1…");
    }

    [Fact]
    public async Task The_random_picks_come_from_their_sets_with_the_name_after_the_emoji()
    {
        await using FunHelperFixture fixture = new();
        HashSet<string> animals = [];
        HashSet<string> colors = [];

        for (int i = 0; i < 400; i++)
        {
            animals.Add(await fixture.RenderAsync("{fun.random.animal}"));
            colors.Add(await fixture.RenderAsync("{fun.random.color.name}"));
        }

        animals.Should().Contain("🦊 fox").And.OnlyContain(a => a.Contains(' '));
        colors.Should().Contain("teal").And.OnlyContain(c => !c.Contains(' '));
        animals.Should().HaveCountGreaterThan(5);
        colors.Should().HaveCountGreaterThan(5);
    }
}
