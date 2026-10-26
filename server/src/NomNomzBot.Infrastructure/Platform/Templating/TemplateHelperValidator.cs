// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Infrastructure.Platform.Templating.Parsing;
using NomNomzBot.Infrastructure.Platform.Templating.Rendering;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// Save-time implementation of <see cref="ITemplateHelperValidator"/> (S042) — parses the template with the
/// SAME <see cref="TemplateParser"/> <see cref="TemplateResolver"/> uses, takes every innermost token, then
/// checks each against the injected <see cref="ITemplateHelperRegistry"/> for the given
/// context. A key valid in another context (e.g. <c>args.1</c> saved on an event response) or unknown
/// entirely (a typo) is rejected by name, with the nearest valid key suggested when one is close.
/// </summary>
public sealed class TemplateHelperValidator(ITemplateHelperRegistry registry)
    : ITemplateHelperValidator
{
    /// <summary>A validator that knows the as-built helpers only (no provider).</summary>
    public TemplateHelperValidator()
        : this(TemplateHelperRegistryAggregate.LegacyOnly) { }

    public Result Validate(string? template, TemplateHelperContext context) =>
        Validate(template, context, []);

    public Result Validate(
        string? template,
        TemplateHelperContext context,
        IReadOnlyCollection<string> extraKeys
    )
    {
        if (string.IsNullOrEmpty(template))
            return Result.Success();

        IReadOnlyList<TemplateHelperEntry> validForContext = registry.ForContext(context);

        // The innermost tokens: the ones the resolver knows before it renders (same as the resolver's fetch).
        List<string> unknownKeys = [];
        foreach (
            PlaceholderNode node in TemplateTree.StaticPlaceholders(TemplateParser.Parse(template))
        )
        {
            RenderedToken token = RenderedToken.FromStatic(node);
            string key = token.Content.Trim();
            if (key.Length == 0)
                continue;

            // A provider's helper is valid by its key alone; its arguments are its own business.
            string helperKey = token.Key.Trim();
            bool providerHelper =
                registry.ProviderFor(helperKey) is not null
                && validForContext.Any(entry => entry.Matches(helperKey));

            bool valid =
                providerHelper
                || extraKeys.Contains(key, StringComparer.OrdinalIgnoreCase)
                || validForContext.Any(entry => entry.Matches(key));
            if (!valid && !unknownKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                unknownKeys.Add(key);
        }

        if (unknownKeys.Count == 0)
            return Result.Success();

        List<string> messages =
        [
            .. unknownKeys.Select(key => DescribeUnknownKey(key, validForContext, extraKeys)),
        ];
        return Errors.ValidationFailed(
            $"Unknown template helper(s): {string.Join("; ", messages)}."
        );
    }

    private static string DescribeUnknownKey(
        string key,
        IReadOnlyList<TemplateHelperEntry> validForContext,
        IReadOnlyCollection<string> extraKeys
    )
    {
        string? nearest = FindNearestKey(
            key,
            [.. extraKeys, .. validForContext.Where(e => e.Prefix is null).Select(e => e.Key)]
        );
        return nearest is null
            ? $"'{{{key}}}' is not a recognized template helper"
            : $"'{{{key}}}' is not a recognized template helper (did you mean '{{{nearest}}}'?)";
    }

    /// <summary>Cheapest-possible nearest-match: the registered literal key with the smallest Levenshtein
    /// distance, capped so an unrelated key is never suggested as a "did you mean".</summary>
    private static string? FindNearestKey(string key, IReadOnlyList<string> candidates)
    {
        const int maxDistance = 3;
        string? best = null;
        int bestDistance = int.MaxValue;

        foreach (string candidate in candidates)
        {
            int distance = LevenshteinDistance(key, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
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
