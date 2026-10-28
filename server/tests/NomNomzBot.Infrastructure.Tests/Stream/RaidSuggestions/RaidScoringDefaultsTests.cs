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

using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Raids;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Stream.RaidSuggestions;

namespace NomNomzBot.Infrastructure.Tests.Stream.RaidSuggestions;

/// <summary>
/// Every raid scoring setting has one default (<see cref="RaidScoringDefaults"/>). A channel with no saved
/// row, a null blob, a damaged blob or a blob with missing keys resolves to those defaults and the read
/// writes nothing; every field is saved and read back; every weight is range checked.
/// </summary>
public sealed class RaidScoringDefaultsTests : IDisposable
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-0000000dd011");

    private static readonly JsonSerializerOptions Web = JsonSerializerOptions.Web;

    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly AppDbContext _db;
    private readonly RaidScoringRulesStore _store;

    public RaidScoringDefaultsTests()
    {
        _connection.Open();
        _db = new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _db.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");
        _store = new(_db);
    }

    private async Task StoreBlobAsync(string? json)
    {
        _db.Configurations.Add(
            new()
            {
                BroadcasterId = Channel,
                Key = RaidScoringRulesStore.ConfigKey,
                Value = json,
            }
        );
        await _db.SaveChangesAsync();
    }

    private static void AssertEveryDefault(RaidScoringRules rules)
    {
        rules.PreferredCategories.Should().BeEquivalentTo(RaidScoringDefaults.PreferredCategories);
        rules.OtherCategoryWeight.Should().Be(RaidScoringDefaults.OtherCategoryWeight);
        rules.OtherCategoryLabel.Should().Be(RaidScoringDefaults.OtherCategoryLabel);
        rules.Keywords.Should().BeEquivalentTo(RaidScoringDefaults.Keywords);
        rules.ViewerBands.Should().BeEquivalentTo(RaidScoringDefaults.ViewerBands);
        rules.Recency.NeverRaidedWeight.Should().Be(RaidScoringDefaults.NeverRaidedWeight);
        rules.Recency.Steps.Should().BeEquivalentTo(RaidScoringDefaults.RecencySteps);
        rules.Recency.OlderWeight.Should().Be(RaidScoringDefaults.OlderWeight);
        rules.Recency.OlderNote.Should().Be(RaidScoringDefaults.OlderNote);
        rules.Balance.OwedBaseWeight.Should().Be(RaidScoringDefaults.OwedBaseWeight);
        rules.Balance.OwedExtraWeight.Should().Be(RaidScoringDefaults.OwedExtraWeight);
        rules.Balance.ReciprocalWeight.Should().Be(RaidScoringDefaults.ReciprocalWeight);
        rules.FollowedWeight.Should().Be(RaidScoringDefaults.FollowedWeight);
    }

    [Fact]
    public void Neutral_is_built_from_the_single_source()
    {
        AssertEveryDefault(RaidScoringRules.Neutral);
    }

    [Fact]
    public async Task A_channel_with_no_saved_row_reads_every_default_and_writes_nothing()
    {
        RaidScoringRules rules = await _store.GetAsync(Channel);

        AssertEveryDefault(rules);
        (await _db.Configurations.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("this is not json")]
    public async Task A_null_empty_or_damaged_blob_resolves_to_the_defaults(string? blob)
    {
        await StoreBlobAsync(blob);

        RaidScoringRules rules = await _store.GetAsync(Channel);

        AssertEveryDefault(rules);
        (await _db.Configurations.SingleAsync()).Value.Should().Be(blob);
    }

    [Fact]
    public async Task A_blob_with_missing_keys_keeps_its_keys_and_defaults_the_rest()
    {
        await StoreBlobAsync("""{"followedWeight":7,"recency":{"olderWeight":3}}""");

        RaidScoringRules rules = await _store.GetAsync(Channel);

        rules.FollowedWeight.Should().Be(7);
        rules.Recency.OlderWeight.Should().Be(3);
        rules.Recency.NeverRaidedWeight.Should().Be(RaidScoringDefaults.NeverRaidedWeight);
        rules.Recency.Steps.Should().BeEmpty();
        rules.Balance.Should().NotBeNull();
        rules.Balance.OwedBaseWeight.Should().Be(RaidScoringDefaults.OwedBaseWeight);
        rules.PreferredCategories.Should().BeEmpty();
        rules.OtherCategoryLabel.Should().Be(RaidScoringDefaults.OtherCategoryLabel);
    }

    [Fact]
    public async Task A_put_body_with_missing_keys_stores_the_defaults_for_them()
    {
        RaidScoringRules partial = JsonSerializer.Deserialize<RaidScoringRules>(
            """{"otherCategoryWeight":-5}""",
            Web
        )!;

        Result<RaidScoringRules> saved = await _store.SaveAsync(Channel, partial);

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        saved.Value.OtherCategoryWeight.Should().Be(-5);
        saved.Value.FollowedWeight.Should().Be(RaidScoringDefaults.FollowedWeight);
        saved.Value.Recency.Steps.Should().BeEmpty();
        RaidScoringRules read = await _store.GetAsync(Channel);
        read.Should().BeEquivalentTo(saved.Value);
    }

    public static TheoryData<string, RaidScoringRules> EachField() =>
        new()
        {
            { "OtherCategoryWeight", RaidScoringRules.Neutral with { OtherCategoryWeight = -12 } },
            { "OtherCategoryLabel", RaidScoringRules.Neutral with { OtherCategoryLabel = "off" } },
            { "FollowedWeight", RaidScoringRules.Neutral with { FollowedWeight = 33 } },
            {
                "NeverRaidedWeight",
                RaidScoringRules.Neutral with
                {
                    Recency = RaidScoringRules.Neutral.Recency with { NeverRaidedWeight = 21 },
                }
            },
            {
                "OlderWeight",
                RaidScoringRules.Neutral with
                {
                    Recency = RaidScoringRules.Neutral.Recency with { OlderWeight = 4 },
                }
            },
            {
                "OlderNote",
                RaidScoringRules.Neutral with
                {
                    Recency = RaidScoringRules.Neutral.Recency with { OlderNote = "long ago" },
                }
            },
            {
                "RecencySteps",
                RaidScoringRules.Neutral with
                {
                    Recency = RaidScoringRules.Neutral.Recency with
                    {
                        Steps = [new RaidRecencyStep(3, -9, "this week")],
                    },
                }
            },
            {
                "OwedBaseWeight",
                RaidScoringRules.Neutral with
                {
                    Balance = RaidScoringRules.Neutral.Balance with { OwedBaseWeight = 8 },
                }
            },
            {
                "OwedExtraWeight",
                RaidScoringRules.Neutral with
                {
                    Balance = RaidScoringRules.Neutral.Balance with { OwedExtraWeight = 6 },
                }
            },
            {
                "ReciprocalWeight",
                RaidScoringRules.Neutral with
                {
                    Balance = RaidScoringRules.Neutral.Balance with { ReciprocalWeight = 2 },
                }
            },
            {
                "PreferredCategories",
                RaidScoringRules.Neutral with
                {
                    PreferredCategories = [new RaidCategoryRule("Art", 40, "art")],
                }
            },
            {
                "Keywords",
                RaidScoringRules.Neutral with
                {
                    Keywords = [new RaidKeywordRule("cozy", 15, ["cozy"], true, true)],
                }
            },
            {
                "ViewerBands",
                RaidScoringRules.Neutral with
                {
                    ViewerBands = [new RaidViewerBand(0, 50, 25, "small")],
                }
            },
        };

    [Theory]
    [MemberData(nameof(EachField))]
    public async Task Each_field_is_saved_and_read_back(string _, RaidScoringRules changed)
    {
        Result<RaidScoringRules> saved = await _store.SaveAsync(Channel, changed);

        saved.IsSuccess.Should().BeTrue(saved.ErrorMessage);
        RaidScoringRules read = await _store.GetAsync(Channel);
        read.Should().BeEquivalentTo(changed);
        read.Should().NotBeEquivalentTo(RaidScoringRules.Neutral);
    }

    public static TheoryData<string, RaidScoringRules> OutOfRange() =>
        new()
        {
            { "OtherCategoryWeight", RaidScoringRules.Neutral with { OtherCategoryWeight = 1001 } },
            { "FollowedWeight", RaidScoringRules.Neutral with { FollowedWeight = -1001 } },
            {
                "NeverRaidedWeight",
                RaidScoringRules.Neutral with
                {
                    Recency = RaidScoringRules.Neutral.Recency with { NeverRaidedWeight = 5000 },
                }
            },
            {
                "OlderWeight",
                RaidScoringRules.Neutral with
                {
                    Recency = RaidScoringRules.Neutral.Recency with { OlderWeight = -5000 },
                }
            },
            {
                "OwedBaseWeight",
                RaidScoringRules.Neutral with
                {
                    Balance = RaidScoringRules.Neutral.Balance with { OwedBaseWeight = 1001 },
                }
            },
            {
                "OwedExtraWeight",
                RaidScoringRules.Neutral with
                {
                    Balance = RaidScoringRules.Neutral.Balance with { OwedExtraWeight = 1001 },
                }
            },
            {
                "ReciprocalWeight",
                RaidScoringRules.Neutral with
                {
                    Balance = RaidScoringRules.Neutral.Balance with { ReciprocalWeight = -1001 },
                }
            },
            {
                "RecencyStepDays",
                RaidScoringRules.Neutral with
                {
                    Recency = RaidScoringRules.Neutral.Recency with
                    {
                        Steps = [new RaidRecencyStep(0, 1, "now")],
                    },
                }
            },
        };

    [Theory]
    [MemberData(nameof(OutOfRange))]
    public async Task An_out_of_range_value_is_refused_and_stores_nothing(
        string _,
        RaidScoringRules bad
    )
    {
        Result<RaidScoringRules> saved = await _store.SaveAsync(Channel, bad);

        saved.IsFailure.Should().BeTrue();
        saved.ErrorCode.Should().Be(RaidScoringRulesValidator.ErrorCode);
        (await _db.Configurations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task A_refused_save_hands_the_per_problem_list_to_the_caller()
    {
        RaidScoringRules bad = RaidScoringRules.Neutral with
        {
            FollowedWeight = 5000,
            ViewerBands = [new RaidViewerBand(50, 10, 1, "mid")],
        };

        Result<RaidScoringRules> saved = await _store.SaveAsync(Channel, bad);

        RaidRulesProblems data = saved.ErrorData.Should().BeOfType<RaidRulesProblems>().Subject;
        data.Problems.Should()
            .BeEquivalentTo([
                new RaidRulesProblem(RaidRulesProblem.BandMinAboveMax, "band", "mid", 50, 10),
                new RaidRulesProblem(RaidRulesProblem.WeightOutOfRange, "followed", "", 5000, 1000),
            ]);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
