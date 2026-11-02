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
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Pipeline;

/// <summary>
/// Supplies the filters of the channel's connected OBS for the <c>obs_filter</c> picker kind. The step's saved
/// <c>source</c> narrows the list to that source's filters; the secondary text is the filter kind OBS reports. With
/// no source saved it lists each filter name found on any scene source, once. With OBS not connected the list is
/// unavailable, with the reason OBS gave, rather than empty.
/// </summary>
internal sealed class ObsFilterOptionProvider : IPipelineOptionProvider
{
    private readonly IObsControlService _obs;

    public ObsFilterOptionProvider(IObsControlService obs)
    {
        _obs = obs;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ObsFilter;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<List<ObsFilterDto>> filters = await ReadFiltersAsync(
            broadcasterId,
            request.ContextValue("source"),
            ct
        );
        if (filters.IsFailure)
            return ObsOptionPage.Unavailable(filters.ErrorMessage!);

        IEnumerable<PipelineOption> options = filters
            .Value.GroupBy(f => f.Name)
            .Select(g => ObsOptionPage.Named(g.Key, g.First().Kind));

        return ObsOptionPage.Of(options, request);
    }

    private async Task<Result<List<ObsFilterDto>>> ReadFiltersAsync(
        Guid broadcasterId,
        string? source,
        CancellationToken ct
    )
    {
        if (source is not null)
        {
            Result<IReadOnlyList<ObsFilterDto>> one = await _obs.GetSourceFilterListAsync(
                broadcasterId,
                source,
                ct
            );
            return one.IsFailure
                ? Result.Failure<List<ObsFilterDto>>(
                    one.ErrorMessage!,
                    one.ErrorCode ?? "OBS_UNAVAILABLE"
                )
                : Result.Success<List<ObsFilterDto>>([.. one.Value]);
        }

        Result<IReadOnlyList<ObsSceneItemDto>> items = await ObsSceneItems.ReadAsync(
            _obs,
            broadcasterId,
            scene: null,
            ct
        );
        if (items.IsFailure)
            return Result.Failure<List<ObsFilterDto>>(
                items.ErrorMessage!,
                items.ErrorCode ?? "OBS_UNAVAILABLE"
            );

        List<ObsFilterDto> all = [];
        foreach (string name in items.Value.Select(i => i.SourceName).Distinct())
        {
            Result<IReadOnlyList<ObsFilterDto>> onSource = await _obs.GetSourceFilterListAsync(
                broadcasterId,
                name,
                ct
            );
            if (onSource.IsSuccess)
                all.AddRange(onSource.Value);
        }

        return Result.Success(all);
    }
}
