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
using NomNomzBot.Application.ViewerData.Dtos;
using NomNomzBot.Application.ViewerData.Services;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.ViewerData.Pipeline;

/// <summary>
/// Supplies the viewer-data keys that viewers of the asking channel hold, for the <c>viewer_data_key</c> picker kind.
/// The saved value is the key; the secondary text is how many viewers hold it. The search and the page go to the
/// viewer-data service. A saved key is found by an exact match, so a key nobody holds any more finds nothing and the
/// step form keeps showing the saved text. A key list that cannot be read is unavailable with the service's reason.
/// </summary>
internal sealed class ViewerDataKeyOptionProvider : IPipelineOptionProvider
{
    private readonly IViewerDataService _viewerData;

    public ViewerDataKeyOptionProvider(IViewerDataService viewerData)
    {
        _viewerData = viewerData;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ViewerDataKey;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        string? term = request.IsValueLookup ? request.Value : request.Search;
        PaginationParams window = request.IsValueLookup
            ? new PaginationParams(1, PaginationParams.MaxPageSize)
            : request.Pagination;
        Result<PagedList<ViewerDataKeySummary>> read = await _viewerData.ListKeysAsync(
            broadcasterId,
            term,
            window,
            ct
        );
        if (read.IsFailure)
            return PipelineOptionPage.Unavailable(read.ErrorMessage!);

        if (request.IsValueLookup)
        {
            List<PipelineOption> exact =
            [
                .. read
                    .Value.Items.Where(k =>
                        string.Equals(k.Key, request.Value, StringComparison.OrdinalIgnoreCase)
                    )
                    .Select(ToOption),
            ];
            return Result.Success(PipelineOptionListResult.Of(exact, exact.Count));
        }

        return Result.Success(
            PipelineOptionListResult.Of(
                [.. read.Value.Items.Select(ToOption)],
                read.Value.TotalCount
            )
        );
    }

    private static PipelineOption ToOption(ViewerDataKeySummary key) =>
        PipelineOptionPage.Option(
            key.Key,
            key.Key,
            key.ViewerCount == 1 ? "1 viewer" : $"{key.ViewerCount} viewers"
        );
}
