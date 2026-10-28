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

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Raids;

/// <summary>
/// The limits a channel's raid scoring rules must keep: every weight within
/// ±<see cref="MaxWeight"/>, every band with <c>Min &lt;= Max</c> and <c>Min &gt;= 0</c>, every word, name
/// and recency step filled in. A refusal lists every broken rule: as English text in the message (for API
/// clients) and as <see cref="RaidRulesProblems"/> data (for the dashboard, which phrases them itself).
/// </summary>
public static class RaidScoringRulesValidator
{
    public const int MaxWeight = 1000;
    public const string ErrorCode = "VALIDATION_FAILED";

    public static Result Validate(RaidScoringRules rules)
    {
        List<(RaidRulesProblem Problem, string Text)> problems = [];

        foreach (RaidCategoryRule category in rules.PreferredCategories)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
                problems.Add(
                    (
                        new(RaidRulesProblem.CategoryNoName, "category", category.Name),
                        "A preferred category has no name."
                    )
                );
            CheckWeight(
                problems,
                category.Weight,
                "category",
                category.Name,
                $"category '{category.Name}'"
            );
        }
        CheckWeight(problems, rules.OtherCategoryWeight, "otherCategory", "", "other categories");

        foreach (RaidKeywordRule keyword in rules.Keywords)
        {
            IReadOnlyList<string>? words = keyword.Words;
            if (words is not { Count: > 0 })
                problems.Add(
                    (
                        new(RaidRulesProblem.KeywordNoWords, "keyword", keyword.Label),
                        $"Keyword rule '{keyword.Label}' has no words."
                    )
                );
            if (words?.Any(string.IsNullOrWhiteSpace) == true)
                problems.Add(
                    (
                        new(RaidRulesProblem.KeywordEmptyWord, "keyword", keyword.Label),
                        $"Keyword rule '{keyword.Label}' has an empty word."
                    )
                );
            if (keyword is { MatchTags: false, MatchTitle: false })
                problems.Add(
                    (
                        new(RaidRulesProblem.KeywordMatchesNothing, "keyword", keyword.Label),
                        $"Keyword rule '{keyword.Label}' matches neither tags nor title."
                    )
                );
            CheckWeight(
                problems,
                keyword.Weight,
                "keyword",
                keyword.Label,
                $"keyword rule '{keyword.Label}'"
            );
        }

        foreach (RaidViewerBand band in rules.ViewerBands)
        {
            if (band.Min < 0)
                problems.Add(
                    (
                        new(RaidRulesProblem.BandBelowZero, "band", band.Label, band.Min, 0),
                        $"Viewer band '{band.Label}' starts below 0."
                    )
                );
            if (band.Min > band.Max)
                problems.Add(
                    (
                        new(
                            RaidRulesProblem.BandMinAboveMax,
                            "band",
                            band.Label,
                            band.Min,
                            band.Max
                        ),
                        $"Viewer band '{band.Label}' has Min {band.Min} above Max {band.Max}."
                    )
                );
            CheckWeight(problems, band.Weight, "band", band.Label, $"viewer band '{band.Label}'");
        }

        CheckWeight(problems, rules.Recency.NeverRaidedWeight, "neverRaided", "", "never raided");
        foreach (RaidRecencyStep step in rules.Recency.Steps)
        {
            if (step.WithinDays <= 0)
                problems.Add(
                    (
                        new(RaidRulesProblem.StepNeedsDay, "step", step.Note, step.WithinDays, 1),
                        $"Recency step '{step.Note}' needs at least 1 day."
                    )
                );
            CheckWeight(problems, step.Weight, "step", step.Note, $"recency step '{step.Note}'");
        }
        CheckWeight(problems, rules.Recency.OlderWeight, "olderRaids", "", "older raids");

        CheckWeight(problems, rules.Balance.OwedBaseWeight, "owedBase", "", "owed raid");
        CheckWeight(problems, rules.Balance.OwedExtraWeight, "owedExtra", "", "extra owed raid");
        CheckWeight(problems, rules.Balance.ReciprocalWeight, "reciprocal", "", "reciprocal raid");
        CheckWeight(problems, rules.FollowedWeight, "followed", "", "followed");

        return problems.Count == 0
            ? Result.Success()
            : Result.Failure(
                string.Join(" ", problems.Select(p => p.Text)),
                ErrorCode,
                errorData: new RaidRulesProblems(problems.Select(p => p.Problem).ToList())
            );
    }

    private static void CheckWeight(
        List<(RaidRulesProblem Problem, string Text)> problems,
        int weight,
        string field,
        string subject,
        string what
    )
    {
        if (Math.Abs(weight) > MaxWeight)
            problems.Add(
                (
                    new(RaidRulesProblem.WeightOutOfRange, field, subject, weight, MaxWeight),
                    $"The weight for {what} must be between -{MaxWeight} and {MaxWeight}."
                )
            );
    }
}
