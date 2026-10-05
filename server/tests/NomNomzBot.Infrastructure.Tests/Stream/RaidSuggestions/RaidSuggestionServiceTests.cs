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
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Raids;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Stream.RaidSuggestions;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Stream.RaidSuggestions;

/// <summary>
/// Raid suggestions use the rules stored for the channel that asks: two channels with different stored
/// rules rank the same live candidates differently, and a channel with no stored rules gets
/// <see cref="RaidScoringRules.Neutral"/> (every candidate 0, input order kept).
/// </summary>
public sealed class RaidSuggestionServiceTests : IDisposable
{
    private const string Us = "1";
    private static readonly Guid ChannelA = Guid.Parse("0192a000-0000-7000-8000-0000000aaa01");
    private static readonly Guid ChannelB = Guid.Parse("0192a000-0000-7000-8000-0000000bbb01");
    private static readonly Guid ChannelC = Guid.Parse("0192a000-0000-7000-8000-0000000ccc01");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly RaidScoringRulesStore _store;
    private readonly RaidSuggestionService _service;

    public RaidSuggestionServiceTests()
    {
        _connection.Open();
        _db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        _store = new RaidScoringRulesStore(_db);

        IRaidHistoryReader history = Substitute.For<IRaidHistoryReader>();
        history
            .GetHistoryAsync(Arg.Any<Guid>(), Us, Arg.Any<CancellationToken>())
            .Returns(
                Result.Success(
                    new RaidHistory(
                        new Dictionary<string, RaidStats>(),
                        new Dictionary<string, RaidStats>()
                    )
                )
            );
        _service = new RaidSuggestionService(history, _store);
    }

    private static RaidCandidate[] Candidates() =>
        [
            RaidScoringTests.Candidate("10", "Just Chatting"),
            RaidScoringTests.Candidate("20", "Art", tags: ["Knitting"]),
            RaidScoringTests.Candidate("30", "Art", title: "Cozy Crafts night"),
        ];

    private static List<string> Order(List<RaidScoreResult> ranked) =>
        ranked.Select(r => r.Stream.UserId).ToList();

    [Fact]
    public async Task Two_channels_with_different_stored_rules_rank_the_same_candidates_differently()
    {
        RaidScoringRules prefersChatting = RaidScoringRules.Neutral with
        {
            PreferredCategories = [new RaidCategoryRule("Just Chatting", 50, "chatting cat")],
        };
        RaidScoringRules prefersCrafts = RaidScoringRules.Neutral with
        {
            Keywords =
            [
                new RaidKeywordRule("craft signal", 70, ["knitting", "cozy crafts"], true, true),
            ],
        };
        (await _store.SaveAsync(ChannelA, prefersChatting)).IsSuccess.Should().BeTrue();
        (await _store.SaveAsync(ChannelB, prefersCrafts)).IsSuccess.Should().BeTrue();

        Result<List<RaidScoreResult>> a = await _service.RankAsync(
            ChannelA,
            Us,
            Candidates(),
            RaidScoringTests.Now
        );
        Result<List<RaidScoreResult>> b = await _service.RankAsync(
            ChannelB,
            Us,
            Candidates(),
            RaidScoringTests.Now
        );

        Order(a.Value).Should().Equal("10", "20", "30");
        a.Value[0].Score.Should().Be(50);
        a.Value[0].Reason.Should().Be("+50 chatting cat");

        Order(b.Value).Should().Equal("20", "30", "10");
        b.Value[0].Score.Should().Be(70);
        b.Value[2].Score.Should().Be(0);
    }

    [Fact]
    public async Task A_channel_with_no_stored_rules_gets_neutral()
    {
        RaidScoringRules loaded = await _store.GetAsync(ChannelC);
        loaded.Should().Be(RaidScoringRules.Neutral);

        Result<List<RaidScoreResult>> ranked = await _service.RankAsync(
            ChannelC,
            Us,
            Candidates(),
            RaidScoringTests.Now
        );

        Order(ranked.Value).Should().Equal("10", "20", "30");
        ranked.Value.Should().OnlyContain(r => r.Score == 0 && r.Reasons.Count == 0);
    }

    [Fact]
    public async Task Saved_rules_round_trip_through_the_store_with_their_full_shape()
    {
        RaidScoringRules rules = new(
            PreferredCategories: [new RaidCategoryRule("Art", 40, "art cat")],
            OtherCategoryWeight: -10,
            OtherCategoryLabel: "other cat",
            Keywords: [new RaidKeywordRule("cozy", 15, ["cozy", "chill vibes"], true, false)],
            ViewerBands: [new RaidViewerBand(0, 50, 25, "small")],
            Recency: new RaidRecencyRules(
                30,
                [new RaidRecencyStep(7, -40, "raided this week")],
                5,
                "a while ago"
            ),
            Balance: new RaidBalanceWeights(20, 10, 5),
            FollowedWeight: 50
        );

        (await _store.SaveAsync(ChannelA, rules)).IsSuccess.Should().BeTrue();
        (await _store.SaveAsync(ChannelA, rules with { FollowedWeight = 60 }))
            .IsSuccess.Should()
            .BeTrue();

        RaidScoringRules loaded = await _store.GetAsync(ChannelA);
        loaded.Should().BeEquivalentTo(rules with { FollowedWeight = 60 });
        (await _db.Configurations.CountAsync(c => c.BroadcasterId == ChannelA)).Should().Be(1);
    }

    [Fact]
    public async Task Saving_channel_A_rules_leaves_channel_B_rules_unchanged()
    {
        RaidScoringRules rulesB = RaidScoringRules.Neutral with { FollowedWeight = 7 };
        (await _store.SaveAsync(ChannelB, rulesB)).IsSuccess.Should().BeTrue();

        RaidScoringRules rulesA = RaidScoringRules.Neutral with { FollowedWeight = 99 };
        (await _store.SaveAsync(ChannelA, rulesA)).IsSuccess.Should().BeTrue();

        (await _store.GetAsync(ChannelB)).Should().BeEquivalentTo(rulesB);
        (await _store.GetAsync(ChannelA)).Should().BeEquivalentTo(rulesA);
        (await _db.Configurations.CountAsync(c => c.Key == RaidScoringRulesStore.ConfigKey))
            .Should()
            .Be(2);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
