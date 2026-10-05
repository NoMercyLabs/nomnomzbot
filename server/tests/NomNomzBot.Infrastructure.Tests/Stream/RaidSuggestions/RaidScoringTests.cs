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
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Raids;
using NomNomzBot.Infrastructure.Stream.RaidSuggestions;

namespace NomNomzBot.Infrastructure.Tests.Stream.RaidSuggestions;

/// <summary>
/// Pins the raid-target ranking to the legacy bot's numbers: every bucket's points, the reason text,
/// and the order the ranked list comes out in.
/// </summary>
public sealed class RaidScoringTests
{
    internal static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static readonly RaidScoringRules Rules = StoneyRaidRules.Rules;

    internal static RaidCandidate Candidate(
        string userId = "100",
        string game = "Software and Game Development",
        int viewers = 30,
        string title = "stream",
        string[]? tags = null,
        bool followed = false
    ) =>
        new(
            new TwitchStream(
                Id: "s" + userId,
                UserId: userId,
                UserLogin: "login" + userId,
                UserName: "Name" + userId,
                GameId: "g",
                GameName: game,
                Type: "live",
                Title: title,
                Tags: tags ?? [],
                ViewerCount: viewers,
                StartedAt: Now.AddHours(-1),
                Language: "en",
                ThumbnailUrl: "",
                IsMature: false
            ),
            followed
        );

    private static RaidStats Raids(int count, double daysAgo) => new(count, Now.AddDays(-daysAgo));

    [Fact]
    public void Owed_raids_outrank_a_gamedev_signal()
    {
        RaidCandidate owed = Candidate("1", "Just Chatting", tags: ["godot"]);
        RaidCandidate plain = Candidate("2", "Just Chatting", tags: ["python"]);
        Dictionary<string, RaidStats> incoming = new() { ["1"] = Raids(3, 40) };

        List<RaidScoreResult> ranked = RaidScoring.Rank(
            [plain, owed],
            new Dictionary<string, RaidStats>(),
            incoming,
            Rules,
            Now
        );

        ranked.Select(r => r.Stream.UserId).Should().Equal("1", "2");
        ranked[0].Score.Should().Be(74);
        ranked[0]
            .Reason.Should()
            .Be(
                "-20 non-dev cat (Just Chatting), -40 gamedev signal, +25 small (30v), "
                    + "+30 never raided, +79 owed 3 raids (in:3/out:0)"
            );
        ranked[1].Score.Should().Be(70);
    }

    [Theory]
    [InlineData("Software and Game Development", 60, "+60 Software cat")]
    [InlineData("software and game development", 60, "+60 Software cat")]
    [InlineData("Science & Technology", 40, "+40 Sci&Tech cat")]
    [InlineData("Fortnite", -20, "-20 non-dev cat (Fortnite)")]
    public void Category_points_and_label(string game, int delta, string label)
    {
        RaidScoreResult result = RaidScoring.Score(Candidate(game: game), null, null, Rules, Now);

        result.Score.Should().Be(delta + 25 + 30);
        result
            .Reasons[0]
            .Should()
            .Be(new RaidScoreReason(delta, label.Substring(label.IndexOf(' ') + 1)));
        result.Reason.Should().StartWith(label + ", +25 small (30v)");
    }

    [Theory]
    [InlineData(1, 25, "small (1v)")]
    [InlineData(50, 25, "small (50v)")]
    [InlineData(51, 10, "mid (51v)")]
    [InlineData(200, 10, "mid (200v)")]
    [InlineData(201, -5, "medium (201v)")]
    [InlineData(500, -5, "medium (500v)")]
    [InlineData(501, -15, "big (501v)")]
    [InlineData(1000, -15, "big (1000v)")]
    [InlineData(1001, -30, "large (1001v)")]
    [InlineData(5000, -30, "large (5000v)")]
    [InlineData(5001, -50, "giant (5001v)")]
    public void Viewer_bucket_points_and_label(int viewers, int delta, string label)
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(viewers: viewers),
            null,
            null,
            Rules,
            Now
        );

        result.Reasons.Should().Contain(new RaidScoreReason(delta, label));
        result.Score.Should().Be(60 + delta + 30);
    }

    [Theory]
    [InlineData(3, -40, "raided 3d ago (cooldown)")]
    [InlineData(10, -15, "raided 1w ago (recent)")]
    [InlineData(20, 5, "raided 2w ago")]
    [InlineData(40, 25, "raided 1mo ago (stale)")]
    public void Outgoing_raid_cooldown_points_and_label(int daysAgo, int delta, string label)
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(),
            Raids(1, daysAgo),
            null,
            Rules,
            Now
        );

        result.Reasons.Should().Contain(new RaidScoreReason(delta, label));
        result.Score.Should().Be(60 + 25 + delta);
    }

    [Fact]
    public void Never_raided_scores_thirty()
    {
        RaidScoreResult result = RaidScoring.Score(Candidate(), null, null, Rules, Now);

        result.Reasons.Should().Contain(new RaidScoreReason(30, "never raided"));
    }

    [Fact]
    public void Followed_channel_gets_fifty()
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(followed: true),
            null,
            null,
            Rules,
            Now
        );

        result.Reasons.Should().Contain(new RaidScoreReason(50, "followed"));
        result.Score.Should().Be(60 + 25 + 30 + 50);
        result.IsFollowed.Should().BeTrue();
    }

    [Theory]
    [InlineData(1, 0, 55, "owed 1 raid (in:1/out:0)")]
    [InlineData(3, 1, 67, "owed 2 raids (in:3/out:1)")]
    [InlineData(2, 2, 10, "reciprocal (in:2/out:2)")]
    public void Reciprocity_points_and_label(int inCount, int outCount, int delta, string label)
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(),
            Raids(outCount, 40),
            Raids(inCount, 40),
            Rules,
            Now
        );

        result.Reasons.Should().Contain(new RaidScoreReason(delta, label));
        result.Score.Should().Be(60 + 25 + 25 + delta);
    }

    [Fact]
    public void Reciprocity_adds_nothing_when_we_raided_them_more()
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(),
            Raids(3, 40),
            Raids(1, 40),
            Rules,
            Now
        );

        result.Score.Should().Be(60 + 25 + 25);
        result.Reasons.Should().NotContain(r => r.Label.Contains("in:"));
    }

    [Fact]
    public void Reciprocity_adds_nothing_when_they_never_raided_us()
    {
        RaidScoreResult result = RaidScoring.Score(Candidate(), Raids(2, 40), null, Rules, Now);

        result.Score.Should().Be(60 + 25 + 25);
    }

    [Fact]
    public void A_software_tag_adds_thirtyfive()
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(tags: ["Docker"]),
            null,
            null,
            Rules,
            Now
        );

        result.Reasons.Should().Contain(new RaidScoreReason(35, "software signal"));
        result.Score.Should().Be(60 + 35 + 25 + 30);
    }

    [Fact]
    public void A_software_word_in_the_title_counts_as_a_signal()
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(title: "Building a Python REST API!"),
            null,
            null,
            Rules,
            Now
        );

        result.Reasons.Should().Contain(new RaidScoreReason(35, "software signal"));
    }

    [Fact]
    public void Gamedev_signal_beats_a_software_signal()
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(tags: ["programming", "deckbuilder"]),
            null,
            null,
            Rules,
            Now
        );

        result.Reasons.Should().Contain(new RaidScoreReason(-40, "gamedev signal"));
        result.Reasons.Should().NotContain(r => r.Label == "software signal");
        result.Score.Should().Be(60 - 40 + 25 + 30);
    }

    [Fact]
    public void Ambiguous_languages_are_not_a_signal()
    {
        RaidScoreResult result = RaidScoring.Score(
            Candidate(tags: ["csharp", "rust"], title: "csharp and rust"),
            null,
            null,
            Rules,
            Now
        );

        result.Reasons.Should().NotContain(r => r.Label.Contains("signal"));
    }

    [Fact]
    public void Rank_orders_by_score_descending_and_keeps_input_order_on_ties()
    {
        RaidCandidate low = Candidate("1", "Fortnite", 800);
        RaidCandidate tieA = Candidate("2", "Science & Technology");
        RaidCandidate high = Candidate("3", followed: true);
        RaidCandidate tieB = Candidate("4", "Science & Technology");

        List<RaidScoreResult> ranked = RaidScoring.Rank(
            [low, tieA, high, tieB],
            new Dictionary<string, RaidStats>(),
            new Dictionary<string, RaidStats>(),
            Rules,
            Now
        );

        ranked.Select(r => r.Stream.UserId).Should().Equal("3", "2", "4", "1");
        ranked.Select(r => r.Score).Should().Equal(165, 95, 95, -5);
    }

    [Fact]
    public void Rank_uses_each_channels_own_history()
    {
        RaidCandidate recentlyRaided = Candidate("1");
        RaidCandidate staleRaid = Candidate("2");
        Dictionary<string, RaidStats> outgoing = new()
        {
            ["1"] = Raids(1, 2),
            ["2"] = Raids(1, 60),
        };

        List<RaidScoreResult> ranked = RaidScoring.Rank(
            [recentlyRaided, staleRaid],
            outgoing,
            new Dictionary<string, RaidStats>(),
            Rules,
            Now
        );

        ranked.Select(r => r.Stream.UserId).Should().Equal("2", "1");
        ranked.Select(r => r.Score).Should().Equal(110, 45);
    }
}
