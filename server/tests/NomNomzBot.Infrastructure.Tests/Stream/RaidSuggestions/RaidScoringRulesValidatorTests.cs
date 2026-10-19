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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Raids;

namespace NomNomzBot.Infrastructure.Tests.Stream.RaidSuggestions;

/// <summary>
/// A refused raid rule set names every broken rule as data (reason, field, subject, numbers), so the
/// dashboard phrases each one in the owner's language instead of showing the server's English sentence.
/// </summary>
public sealed class RaidScoringRulesValidatorTests
{
    private static RaidRulesProblems ProblemsOf(RaidScoringRules rules)
    {
        Result result = RaidScoringRulesValidator.Validate(rules);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");

        return result.ErrorData.Should().BeOfType<RaidRulesProblems>().Subject;
    }

    [Fact]
    public void A_valid_rule_set_passes()
    {
        RaidScoringRulesValidator
            .Validate(RaidScoringDefaults.Resolve(RaidScoringRules.Neutral))
            .IsSuccess.Should()
            .BeTrue();
    }

    [Fact]
    public void An_out_of_range_weight_names_its_field_subject_value_and_limit()
    {
        RaidScoringRules rules = RaidScoringDefaults.Resolve(
            RaidScoringRules.Neutral with
            {
                PreferredCategories = [new RaidCategoryRule("Art", 5000, "art")],
            }
        );

        RaidRulesProblem problem = ProblemsOf(rules).Problems.Should().ContainSingle().Subject;

        problem
            .Should()
            .Be(
                new RaidRulesProblem(
                    RaidRulesProblem.WeightOutOfRange,
                    "category",
                    "Art",
                    5000,
                    1000
                )
            );
    }

    [Fact]
    public void Every_broken_rule_is_listed_with_its_own_reason()
    {
        RaidScoringRules rules = RaidScoringDefaults.Resolve(
            RaidScoringRules.Neutral with
            {
                Keywords = [new RaidKeywordRule("craft", 1, [], false, false)],
                ViewerBands = [new RaidViewerBand(50, 10, 1, "mid")],
            }
        );

        List<string> reasons = ProblemsOf(rules).Problems.Select(p => p.Reason).ToList();

        reasons
            .Should()
            .BeEquivalentTo([
                RaidRulesProblem.KeywordNoWords,
                RaidRulesProblem.KeywordMatchesNothing,
                RaidRulesProblem.BandMinAboveMax,
            ]);
    }

    [Fact]
    public void The_english_message_stays_for_api_clients()
    {
        RaidScoringRules rules = RaidScoringDefaults.Resolve(
            RaidScoringRules.Neutral with
            {
                ViewerBands = [new RaidViewerBand(50, 10, 1, "mid")],
            }
        );

        RaidScoringRulesValidator
            .Validate(rules)
            .ErrorMessage.Should()
            .Be("Viewer band 'mid' has Min 50 above Max 10.");
    }
}
