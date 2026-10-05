// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Application.Raids;

/// <summary>
/// The limits a channel's raid scoring rules must keep: every weight within
/// ±<see cref="MaxWeight"/>, every band with <c>Min &lt;= Max</c> and <c>Min &gt;= 0</c>, every word, name
/// and recency step filled in. The first broken rule names itself in the failure message.
/// </summary>
public static class RaidScoringRulesValidator
{
    public const int MaxWeight = 1000;
    public const string ErrorCode = "VALIDATION_FAILED";

    public static Result Validate(RaidScoringRules rules)
    {
        List<string> problems = [];

        foreach (RaidCategoryRule category in rules.PreferredCategories)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
                problems.Add("A preferred category has no name.");
            CheckWeight(problems, category.Weight, $"category '{category.Name}'");
        }
        CheckWeight(problems, rules.OtherCategoryWeight, "other categories");

        foreach (RaidKeywordRule keyword in rules.Keywords)
        {
            if (keyword.Words.Count == 0)
                problems.Add($"Keyword rule '{keyword.Label}' has no words.");
            if (keyword.Words.Any(string.IsNullOrWhiteSpace))
                problems.Add($"Keyword rule '{keyword.Label}' has an empty word.");
            if (keyword is { MatchTags: false, MatchTitle: false })
                problems.Add($"Keyword rule '{keyword.Label}' matches neither tags nor title.");
            CheckWeight(problems, keyword.Weight, $"keyword rule '{keyword.Label}'");
        }

        foreach (RaidViewerBand band in rules.ViewerBands)
        {
            if (band.Min < 0)
                problems.Add($"Viewer band '{band.Label}' starts below 0.");
            if (band.Min > band.Max)
                problems.Add(
                    $"Viewer band '{band.Label}' has Min {band.Min} above Max {band.Max}."
                );
            CheckWeight(problems, band.Weight, $"viewer band '{band.Label}'");
        }

        CheckWeight(problems, rules.Recency.NeverRaidedWeight, "never raided");
        foreach (RaidRecencyStep step in rules.Recency.Steps)
        {
            if (step.WithinDays <= 0)
                problems.Add($"Recency step '{step.Note}' needs at least 1 day.");
            CheckWeight(problems, step.Weight, $"recency step '{step.Note}'");
        }
        CheckWeight(problems, rules.Recency.OlderWeight, "older raids");

        CheckWeight(problems, rules.Balance.OwedBaseWeight, "owed raid");
        CheckWeight(problems, rules.Balance.OwedExtraWeight, "extra owed raid");
        CheckWeight(problems, rules.Balance.ReciprocalWeight, "reciprocal raid");
        CheckWeight(problems, rules.FollowedWeight, "followed");

        return problems.Count == 0
            ? Result.Success()
            : Result.Failure(string.Join(" ", problems), ErrorCode);
    }

    private static void CheckWeight(List<string> problems, int weight, string what)
    {
        if (Math.Abs(weight) > MaxWeight)
            problems.Add($"The weight for {what} must be between -{MaxWeight} and {MaxWeight}.");
    }
}
