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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Raids;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Stream.RaidSuggestions;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// The raid scoring rules endpoints store and read back one channel's rules through the real store over a
/// real SQLite database: PUT then GET round-trips the full shape, and a PUT that breaks a rule is refused
/// with the project's error envelope and leaves nothing stored.
/// </summary>
public sealed class LiveOpsRaidScoringRulesTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000dd001");

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly LiveOpsController _controller;

    public LiveOpsRaidScoringRulesTests()
    {
        _connection.Open();
        _db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        _controller = new LiveOpsController(
            Substitute.For<ITwitchPollsApi>(),
            Substitute.For<ITwitchPredictionsApi>(),
            Substitute.For<ITwitchRaidsApi>(),
            Substitute.For<ITwitchAdsApi>(),
            Substitute.For<ITwitchClipsApi>(),
            Substitute.For<ITwitchScheduleApi>(),
            Substitute.For<ITwitchStreamsApi>(),
            Substitute.For<IChannelService>(),
            new RaidScoringRulesStore(_db)
        );
    }

    private static RaidScoringRules FullRules() =>
        new(
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

    [Fact]
    public async Task Put_then_get_round_trips_the_rules_shape()
    {
        IActionResult put = await _controller.UpdateRaidScoringRules(
            Channel.ToString(),
            FullRules(),
            CancellationToken.None
        );
        OkObjectResult putOk = put.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<RaidScoringRules>)putOk.Value!)
            .Data.Should()
            .BeEquivalentTo(FullRules());

        IActionResult get = await _controller.GetRaidScoringRules(
            Channel.ToString(),
            CancellationToken.None
        );
        OkObjectResult getOk = get.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<RaidScoringRules>)getOk.Value!)
            .Data.Should()
            .BeEquivalentTo(FullRules());
    }

    [Fact]
    public async Task Get_without_stored_rules_returns_neutral()
    {
        IActionResult get = await _controller.GetRaidScoringRules(
            Channel.ToString(),
            CancellationToken.None
        );
        OkObjectResult ok = get.Should().BeOfType<OkObjectResult>().Subject;
        ((StatusResponseDto<RaidScoringRules>)ok.Value!).Data.Should().Be(RaidScoringRules.Neutral);
    }

    public static TheoryData<string, RaidScoringRules> BadRules() =>
        new()
        {
            {
                "band Min above Max",
                FullRules() with
                {
                    ViewerBands = [new RaidViewerBand(60, 50, 25, "small")],
                }
            },
            {
                "empty word",
                FullRules() with
                {
                    Keywords = [new RaidKeywordRule("cozy", 15, ["cozy", " "], true, false)],
                }
            },
            {
                "weight out of range",
                FullRules() with
                {
                    PreferredCategories = [new RaidCategoryRule("Art", 5000, "art cat")],
                }
            },
        };

    [Theory]
    [MemberData(nameof(BadRules))]
    public async Task A_bad_put_is_refused_with_a_reason_and_stores_nothing(
        string _,
        RaidScoringRules bad
    )
    {
        IActionResult put = await _controller.UpdateRaidScoringRules(
            Channel.ToString(),
            bad,
            CancellationToken.None
        );

        BadRequestObjectResult refused = put.Should().BeOfType<BadRequestObjectResult>().Subject;
        StatusResponseDto<object> envelope = (StatusResponseDto<object>)refused.Value!;
        envelope.Status.Should().Be("error");
        envelope.Code.Should().Be("VALIDATION_FAILED");
        envelope.Message.Should().NotBeNullOrWhiteSpace();

        (await _db.Configurations.CountAsync(c => c.BroadcasterId == Channel)).Should().Be(0);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
