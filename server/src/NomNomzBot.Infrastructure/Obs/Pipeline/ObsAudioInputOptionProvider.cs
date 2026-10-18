// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Pipeline;
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Pipeline;

/// <summary>
/// Supplies the audio inputs of the channel's connected OBS for the <c>obs_audio_input</c> picker kind. An input
/// counts as audio when OBS reports a mute state for it (<see cref="ObsInputDto.Muted"/> is not null); browser and
/// image inputs reject the mute probe and are left out. With OBS not connected the list is unavailable, with the
/// reason OBS gave, rather than an empty list.
/// </summary>
internal sealed class ObsAudioInputOptionProvider : IPipelineOptionProvider
{
    private readonly IObsControlService _obs;

    public ObsAudioInputOptionProvider(IObsControlService obs)
    {
        _obs = obs;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ObsAudioInput;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<ObsInputDto>> inputs = await _obs.GetInputsAsync(broadcasterId, ct);
        if (inputs.IsFailure)
            return Result.Success(PipelineOptionListResult.Unavailable(inputs.ErrorMessage!));

        IEnumerable<ObsInputDto> audio = inputs.Value.Where(i => i.Muted is not null);
        if (request.IsValueLookup)
            audio = audio.Where(i => i.Name == request.Value);
        else if (!string.IsNullOrWhiteSpace(request.Search))
            audio = audio.Where(i =>
                i.Name.Contains(request.Search, StringComparison.OrdinalIgnoreCase)
            );

        List<ObsInputDto> matches =
        [
            .. audio.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
        ];
        List<PipelineOption> page =
        [
            .. matches
                .Skip((request.Pagination.Page - 1) * request.Pagination.PageSize)
                .Take(request.Pagination.PageSize)
                .Select(ToOption),
        ];

        return Result.Success(PipelineOptionListResult.Of(page, matches.Count));
    }

    private static PipelineOption ToOption(ObsInputDto input) =>
        new(
            input.Name,
            input.Name,
            input.Muted == true ? "muted" : "live",
            ImageUrl: null,
            PipelineOptionState.Selectable
        );
}
