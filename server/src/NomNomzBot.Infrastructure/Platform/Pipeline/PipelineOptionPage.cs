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
using NomNomzBot.Domain.Platform;

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// The one place a picker kind that holds its options in memory searches, orders and pages them, so every such kind
/// answers a saved-value lookup, a search and a page the same way. A search is a case-insensitive contains on the
/// label or the secondary text; a value lookup is an exact match on the saved value.
/// </summary>
internal static class PipelineOptionPage
{
    /// <summary>The page of <paramref name="options"/> that <paramref name="request"/> asks for.</summary>
    /// <param name="options">Every option the source offers.</param>
    /// <param name="request">The search, saved value and page window.</param>
    /// <param name="inSourceOrder">True keeps the order the source gave (a relevance or recency order); false sorts by label.</param>
    public static Result<PipelineOptionListResult> Of(
        IEnumerable<PipelineOption> options,
        PipelineOptionQuery request,
        bool inSourceOrder = false
    )
    {
        IEnumerable<PipelineOption> wanted = options;
        if (request.IsValueLookup)
            wanted = wanted.Where(o => o.Value == request.Value);
        else if (!string.IsNullOrWhiteSpace(request.Search))
            wanted = wanted.Where(o => Matches(o, request.Search));

        List<PipelineOption> matches = inSourceOrder
            ? [.. wanted]
            : [.. wanted.OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase)];

        return Result.Success(PipelineOptionListResult.Of(PageOf(matches, request), matches.Count));
    }

    /// <summary>The rows of <paramref name="matches"/> that fall in the page window of <paramref name="request"/>.</summary>
    public static List<PipelineOption> PageOf(
        IReadOnlyList<PipelineOption> matches,
        PipelineOptionQuery request
    ) =>
        [
            .. matches
                .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
                .Take(request.Pagination.PageSize),
        ];

    /// <summary>The list is unavailable, with the reason the source gave, rather than empty.</summary>
    public static Result<PipelineOptionListResult> Unavailable(string reason) =>
        Result.Success(PipelineOptionListResult.Unavailable(reason));

    /// <summary>A real, empty list from a source that was read.</summary>
    public static Result<PipelineOptionListResult> Empty() =>
        Result.Success(PipelineOptionListResult.Of([], 0));

    /// <summary>One selectable option.</summary>
    public static PipelineOption Option(
        string value,
        string label,
        string? secondaryText = null,
        string? imageUrl = null
    ) => new(value, label, secondaryText, imageUrl, PipelineOptionState.Selectable);

    /// <summary>
    /// <paramref name="request"/> with a saved owned id in its canonical Guid text, so a value saved in the 26-character
    /// ULID wire form finds the option whose value is the Guid. Any other value is left as it is and matches nothing.
    /// </summary>
    public static PipelineOptionQuery WithOwnedIdValue(PipelineOptionQuery request) =>
        request.IsValueLookup
            ? request with
            {
                Value = OwnedIdCodec.FromWire(request.Value),
            }
            : request;

    private static bool Matches(PipelineOption option, string term) =>
        option.Label.Contains(term, StringComparison.OrdinalIgnoreCase)
        || (
            option.SecondaryText is not null
            && option.SecondaryText.Contains(term, StringComparison.OrdinalIgnoreCase)
        );
}
