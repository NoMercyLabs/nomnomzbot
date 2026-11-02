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
/// Supplies the scenes of the channel's connected OBS for the <c>obs_scene</c> picker kind. The scene on air is
/// marked "live". With OBS not connected the list is unavailable, with the reason OBS gave, rather than empty.
/// </summary>
internal sealed class ObsSceneOptionProvider : IPipelineOptionProvider
{
    private readonly IObsControlService _obs;

    public ObsSceneOptionProvider(IObsControlService obs)
    {
        _obs = obs;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ObsScene;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        Result<IReadOnlyList<ObsSceneDto>> scenes = await _obs.GetScenesAsync(broadcasterId, ct);
        if (scenes.IsFailure)
            return ObsOptionPage.Unavailable(scenes.ErrorMessage!);

        return ObsOptionPage.Of(
            scenes.Value.Select(s => ObsOptionPage.Named(s.Name, s.IsCurrent ? "live" : null)),
            request
        );
    }
}
