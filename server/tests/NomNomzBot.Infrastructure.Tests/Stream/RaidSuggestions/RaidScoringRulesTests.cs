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
using NomNomzBot.Application.Raids;
using NomNomzBot.Infrastructure.Stream.RaidSuggestions;

namespace NomNomzBot.Infrastructure.Tests.Stream.RaidSuggestions;

/// <summary>
/// Raid scoring is per channel: every preference comes from the rules object the scorer is given,
/// and the scorer owns no rule of its own.
/// </summary>
public sealed class RaidScoringRulesTests
{
    private static readonly DateTimeOffset Now = RaidScoringTests.Now;

    private static readonly IReadOnlyDictionary<string, RaidStats> NoStats =
        new Dictionary<string, RaidStats>();

    private static RaidScoringRules WithRules(
        IReadOnlyList<RaidCategoryRule>? categories = null,
        IReadOnlyList<RaidKeywordRule>? keywords = null,
        IReadOnlyList<RaidViewerBand>? bands = null
    ) =>
        RaidScoringRules.Neutral with
        {
            PreferredCategories = categories ?? [],
            Keywords = keywords ?? [],
            ViewerBands = bands ?? [],
        };

    private static List<string> Order(List<RaidScoreResult> ranked) =>
        ranked.Select(r => r.Stream.UserId).ToList();

    [Fact]
    public void Two_channels_with_different_rules_rank_the_same_candidates_differently()
    {
        RaidCandidate chatting = RaidScoringTests.Candidate("1", "Just Chatting");
        RaidCandidate knitter = RaidScoringTests.Candidate("2", "Art", tags: ["Knitting"]);
        RaidCandidate crafter = RaidScoringTests.Candidate("3", "Art", title: "Cozy Crafts night");
        RaidCandidate[] candidates = [chatting, knitter, crafter];

        RaidScoringRules prefersCategory = WithRules(
            categories: [new RaidCategoryRule("Just Chatting", 50, "chatting cat")]
        );
        RaidScoringRules prefersKeywords = WithRules(
            keywords:
            [
                new RaidKeywordRule("craft signal", 70, ["knitting", "cozy crafts"], true, true),
            ]
        );

        List<RaidScoreResult> a = RaidScoring.Rank(
            candidates,
            NoStats,
            NoStats,
            prefersCategory,
            Now
        );
        List<RaidScoreResult> b = RaidScoring.Rank(
            candidates,
            NoStats,
            NoStats,
            prefersKeywords,
            Now
        );

        Order(a).Should().Equal("1", "2", "3");
        a.Select(r => r.Score).Should().Equal(50, 0, 0);
        a[0].Reason.Should().Be("+50 chatting cat");

        Order(b).Should().Equal("2", "3", "1");
        b.Select(r => r.Score).Should().Equal(70, 70, 0);
        b[0].Reason.Should().Be("+70 craft signal");
    }

    [Fact]
    public void Two_channels_with_different_viewer_bands_score_the_same_stream_differently()
    {
        RaidCandidate stream = RaidScoringTests.Candidate("1", viewers: 80);
        RaidScoringRules smallLover = WithRules(bands: [new RaidViewerBand(1, 100, 40, "small")]);
        RaidScoringRules bigLover = WithRules(bands: [new RaidViewerBand(50, 500, 15, "sizable")]);

        RaidScoreResult a = RaidScoring.Score(stream, null, null, smallLover, Now);
        RaidScoreResult b = RaidScoring.Score(stream, null, null, bigLover, Now);

        a.Reasons.Should().Equal(new RaidScoreReason(40, "small (80v)"));
        b.Reasons.Should().Equal(new RaidScoreReason(15, "sizable (80v)"));
    }

    [Fact]
    public void Neutral_rules_add_no_preference_points()
    {
        RaidCandidate[] candidates =
        [
            RaidScoringTests.Candidate("1", "Software and Game Development", 5, tags: ["godot"]),
            RaidScoringTests.Candidate("2", "Fortnite", 9000, followed: true),
            RaidScoringTests.Candidate("3", "Just Chatting", 0, title: "python api"),
        ];
        Dictionary<string, RaidStats> outgoing = new() { ["1"] = new(2, Now.AddDays(-3)) };
        Dictionary<string, RaidStats> incoming = new() { ["2"] = new(4, Now.AddDays(-50)) };

        List<RaidScoreResult> ranked = RaidScoring.Rank(
            candidates,
            outgoing,
            incoming,
            RaidScoringRules.Neutral,
            Now
        );

        ranked.Select(r => r.Score).Should().Equal(0, 0, 0);
        ranked.Should().OnlyContain(r => r.Reasons.Count == 0 && r.Reason == "");
        Order(ranked).Should().Equal("1", "2", "3");
    }

    [Fact]
    public void A_phrase_matches_whole_words_only_and_ignores_case()
    {
        RaidScoringRules rules = WithRules(
            keywords: [new RaidKeywordRule("craft", 10, ["cozy crafts"], false, true)]
        );

        RaidScoring
            .Score(
                RaidScoringTests.Candidate(title: "COZY  Crafts, tonight"),
                null,
                null,
                rules,
                Now
            )
            .Score.Should()
            .Be(10);
        RaidScoring
            .Score(RaidScoringTests.Candidate(title: "uncozy craftsmen"), null, null, rules, Now)
            .Score.Should()
            .Be(0);
    }

    [Fact]
    public void A_keyword_limited_to_titles_ignores_tags_and_the_first_matching_rule_wins()
    {
        RaidScoringRules rules = WithRules(
            keywords:
            [
                new RaidKeywordRule("title only", 20, ["hello"], false, true),
                new RaidKeywordRule("tag rule", 5, ["hello"], true, false),
            ]
        );

        RaidScoring
            .Score(RaidScoringTests.Candidate(tags: ["hello"]), null, null, rules, Now)
            .Reasons.Should()
            .Equal(new RaidScoreReason(5, "tag rule"));
        RaidScoring
            .Score(
                RaidScoringTests.Candidate(title: "hello", tags: ["hello"]),
                null,
                null,
                rules,
                Now
            )
            .Reasons.Should()
            .Equal(new RaidScoreReason(20, "title only"));
    }

    [Fact]
    public void Recency_and_balance_weights_come_from_the_rules()
    {
        RaidScoringRules rules = RaidScoringRules.Neutral with
        {
            Recency = new RaidRecencyRules(7, [new RaidRecencyStep(10, -3, "soon")], 9, "due"),
            Balance = new RaidBalanceWeights(100, 1, 2),
            FollowedWeight = 4,
        };
        RaidCandidate candidate = RaidScoringTests.Candidate(followed: true);
        RaidStats seen = new(1, Now.AddDays(-5));

        RaidScoring
            .Score(candidate, null, null, rules, Now)
            .Reasons.Should()
            .Equal(new RaidScoreReason(7, "never raided"), new RaidScoreReason(4, "followed"));
        RaidScoring
            .Score(candidate, seen, null, rules, Now)
            .Reasons.Should()
            .Contain(new RaidScoreReason(-3, "raided 5d ago (soon)"));
        RaidScoring
            .Score(candidate, new RaidStats(1, Now.AddDays(-20)), new RaidStats(3, Now), rules, Now)
            .Reasons.Should()
            .Contain(new RaidScoreReason(9, "raided 2w ago (due)"))
            .And.Contain(new RaidScoreReason(101, "owed 2 raids (in:3/out:1)"));
    }

    [Theory]
    [InlineData("RaidScoring.cs")]
    [InlineData("RaidTagSignals.cs")]
    public void The_scorer_source_holds_no_streamer_specific_rule(string file)
    {
        string text = File.ReadAllText(Path.Combine(ScorerFolder(), file));

        string[] forbidden =
        [
            "Software and Game Development",
            "Science & Technology",
            "Sci&Tech",
            "non-dev",
            "softwaredev",
            "(elopment)?",
            "deckbuilder",
            "godot",
            "kubernetes",
            "RegexOptions.IgnoreCase | RegexOptions.Compiled",
        ];
        foreach (string word in forbidden)
            text.Should().NotContain(word, because: file + " must read rules from data");
    }

    private static string ScorerFolder()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(
                dir.FullName,
                "src",
                "NomNomzBot.Infrastructure",
                "Stream",
                "RaidSuggestions"
            );
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("RaidSuggestions source folder not found");
    }
}
