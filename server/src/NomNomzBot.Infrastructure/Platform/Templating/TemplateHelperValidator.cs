// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// Save-time implementation of <see cref="ITemplateHelperValidator"/> (S042) — extracts every
/// <c>{{helper}}</c> placeholder from the template with the SAME pattern <see cref="TemplateResolver"/>
/// uses to find them, then checks each against <see cref="TemplateHelperRegistry"/> for the given
/// context. A key valid in another context (e.g. <c>args.1</c> saved on an event response) or unknown
/// entirely (a typo) is rejected by name, with the nearest valid key suggested when one is close.
/// </summary>
public sealed partial class TemplateHelperValidator : ITemplateHelperValidator
{
    // Mirrors TemplateResolver's own VariablePattern exactly — a single-brace placeholder with no nested
    // braces (the same shape TemplateResolver.ExtractPlaceholders scans for).
    [GeneratedRegex(@"\{([^{}]+)\}")]
    private static partial Regex PlaceholderPattern();

    public Result Validate(string? template, TemplateHelperContext context)
    {
        if (string.IsNullOrEmpty(template))
            return Result.Success();

        IReadOnlyList<TemplateHelperEntry> validForContext = TemplateHelperRegistry.ForContext(
            context
        );

        List<string> unknownKeys = [];
        foreach (Match match in PlaceholderPattern().Matches(template))
        {
            string key = match.Groups[1].Value.Trim();
            if (key.Length == 0)
                continue;

            bool valid = validForContext.Any(entry => entry.Matches(key));
            if (!valid && !unknownKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                unknownKeys.Add(key);
        }

        if (unknownKeys.Count == 0)
            return Result.Success();

        List<string> messages =
        [
            .. unknownKeys.Select(key => DescribeUnknownKey(key, validForContext)),
        ];
        return Errors.ValidationFailed(
            $"Unknown template helper(s): {string.Join("; ", messages)}."
        );
    }

    private static string DescribeUnknownKey(
        string key,
        IReadOnlyList<TemplateHelperEntry> validForContext
    )
    {
        string? nearest = FindNearestKey(key, validForContext);
        return nearest is null
            ? $"'{{{key}}}' is not a recognized template helper"
            : $"'{{{key}}}' is not a recognized template helper (did you mean '{{{nearest}}}'?)";
    }

    /// <summary>Cheapest-possible nearest-match: the registered literal key with the smallest Levenshtein
    /// distance, capped so an unrelated key is never suggested as a "did you mean".</summary>
    private static string? FindNearestKey(string key, IReadOnlyList<TemplateHelperEntry> candidates)
    {
        const int maxDistance = 3;
        string? best = null;
        int bestDistance = int.MaxValue;

        foreach (TemplateHelperEntry entry in candidates.Where(e => e.Prefix is null))
        {
            int distance = LevenshteinDistance(key, entry.Key);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = entry.Key;
            }
        }

        return bestDistance <= maxDistance ? best : null;
    }

    private static int LevenshteinDistance(string a, string b)
    {
        int[,] d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++)
            d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++)
            d[0, j] = j;

        for (int i = 1; i <= a.Length; i++)
        for (int j = 1; j <= b.Length; j++)
        {
            int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
            d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
        }

        return d[a.Length, b.Length];
    }
}
