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

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Contracts.Twitch;

namespace NomNomzBot.Infrastructure.Stream.Pipeline;

/// <summary>
/// Supplies stream categories for the <c>stream_category</c> picker kind by searching the platform catalogue
/// (the same Twitch category search the stream page's category picker uses). The option value is the category
/// NAME, the form <c>set_stream_info</c> saves; the platform resolves the name to its own id when it applies
/// the update. A saved value is answered with its exact match only, so the picker can label it.
/// </summary>
internal sealed class StreamCategoryOptionProvider : IPipelineOptionProvider
{
    private readonly ITwitchSearchApi _search;

    public StreamCategoryOptionProvider(ITwitchSearchApi search)
    {
        _search = search;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.StreamCategory;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        string term = request.IsValueLookup
            ? request.Value!
            : request.Search?.Trim() ?? string.Empty;
        if (term.Length == 0)
            return Result.Success(PipelineOptionListResult.Of([], 0));

        Result<TwitchPage<TwitchSearchCategory>> search = await _search.SearchCategoriesAsync(
            term,
            new(PageSize: request.Pagination.PageSize),
            ct
        );
        if (search.IsFailure)
            return Result.Success(
                PipelineOptionListResult.Unavailable(
                    $"The category search failed: {search.ErrorMessage}"
                )
            );

        IEnumerable<TwitchSearchCategory> found = search.Value.Items;
        if (request.IsValueLookup)
            found = found.Where(c =>
                string.Equals(c.Name, term, StringComparison.OrdinalIgnoreCase)
            );

        List<PipelineOption> options =
        [
            .. found.Select(c => new PipelineOption(
                c.Name,
                c.Name,
                SecondaryText: null,
                c.BoxArtUrl,
                PipelineOptionState.Selectable
            )),
        ];
        return Result.Success(PipelineOptionListResult.Of(options, options.Count));
    }
}
