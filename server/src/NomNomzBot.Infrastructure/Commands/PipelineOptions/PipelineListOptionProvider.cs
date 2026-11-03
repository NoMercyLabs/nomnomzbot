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
using NomNomzBot.Application.Commands.Dtos;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Infrastructure.Platform.Pipeline;

namespace NomNomzBot.Infrastructure.Commands.PipelineOptions;

/// <summary>
/// The shared read of the asking channel's pipelines for the two pipeline pickers. It reads every page of the
/// service, so a channel with more pipelines than one page is never cut short, and a list that cannot be read is
/// unavailable with the service's reason. The subclasses choose which field of a pipeline is the saved value.
/// </summary>
internal abstract class PipelineListOptionProvider : IPipelineOptionProvider
{
    private readonly IPipelineService _pipelines;

    protected PipelineListOptionProvider(IPipelineService pipelines)
    {
        _pipelines = pipelines;
    }

    public abstract PipelineActionFieldKind Kind { get; }

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        List<PipelineListItemDto> items = [];
        int page = 1;
        bool more = true;
        while (more)
        {
            Result<PagedList<PipelineListItemDto>> read = await _pipelines.ListAsync(
                broadcasterId.ToString(),
                new PaginationParams(page, PaginationParams.MaxPageSize),
                ct
            );
            if (read.IsFailure)
                return PipelineOptionPage.Unavailable(read.ErrorMessage!);

            items.AddRange(read.Value.Items);
            more = read.Value.HasNextPage;
            page++;
        }

        return PipelineOptionPage.Of(items.Select(ToOption), Normalize(request));
    }

    /// <summary>The request as this kind's saved value should be looked up.</summary>
    protected virtual PipelineOptionQuery Normalize(PipelineOptionQuery request) => request;

    /// <summary>The value this kind saves for <paramref name="pipeline"/>.</summary>
    protected abstract string ValueOf(PipelineListItemDto pipeline);

    private PipelineOption ToOption(PipelineListItemDto pipeline) =>
        PipelineOptionPage.Option(ValueOf(pipeline), pipeline.Name, pipeline.Description);
}
