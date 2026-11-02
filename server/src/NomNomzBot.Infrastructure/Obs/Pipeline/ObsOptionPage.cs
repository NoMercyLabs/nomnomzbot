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
using NomNomzBot.Application.Contracts.Pipeline;

namespace NomNomzBot.Infrastructure.Obs.Pipeline;

/// <summary>
/// The one place the OBS name pickers search, order and page their options, so every kind answers a saved-value
/// lookup, a search and a page the same way. OBS names are unique per kind, so a value lookup is an exact match on
/// the name and a search is a case-insensitive contains on the label.
/// </summary>
internal static class ObsOptionPage
{
    /// <summary>The page of <paramref name="options"/> that <paramref name="request"/> asks for.</summary>
    public static Result<PipelineOptionListResult> Of(
        IEnumerable<PipelineOption> options,
        PipelineOptionQuery request
    )
    {
        IEnumerable<PipelineOption> wanted = options;
        if (request.IsValueLookup)
            wanted = wanted.Where(o => o.Value == request.Value);
        else if (!string.IsNullOrWhiteSpace(request.Search))
            wanted = wanted.Where(o =>
                o.Label.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
            );

        List<PipelineOption> matches =
        [
            .. wanted.OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase),
        ];
        List<PipelineOption> page =
        [
            .. matches
                .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
                .Take(request.Pagination.PageSize),
        ];

        return Result.Success(PipelineOptionListResult.Of(page, matches.Count));
    }

    /// <summary>One selectable option whose value is its label, the way OBS addresses a thing by name.</summary>
    public static PipelineOption Named(string name, string? secondaryText) =>
        new(name, name, secondaryText, ImageUrl: null, PipelineOptionState.Selectable);

    /// <summary>The list is unavailable, with the reason OBS gave, rather than empty.</summary>
    public static Result<PipelineOptionListResult> Unavailable(string reason) =>
        Result.Success(PipelineOptionListResult.Unavailable(reason));
}
