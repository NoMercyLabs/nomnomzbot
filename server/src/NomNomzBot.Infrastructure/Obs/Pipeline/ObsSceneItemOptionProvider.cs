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
/// Supplies the sources placed in scenes of the channel's connected OBS for the <c>obs_scene_item</c> picker kind.
/// The step's saved <c>scene</c> narrows the list to that scene and the secondary text says whether the source is
/// visible there. With no scene saved it lists every source of every scene once, with no secondary text because
/// visibility differs per scene. With OBS not connected the list is unavailable, with the reason OBS gave.
/// </summary>
internal sealed class ObsSceneItemOptionProvider : IPipelineOptionProvider
{
    private readonly IObsControlService _obs;

    public ObsSceneItemOptionProvider(IObsControlService obs)
    {
        _obs = obs;
    }

    public PipelineActionFieldKind Kind => PipelineActionFieldKind.ObsSceneItem;

    public async Task<Result<PipelineOptionListResult>> GetOptionsAsync(
        Guid broadcasterId,
        PipelineOptionQuery request,
        CancellationToken ct = default
    )
    {
        string? scene = request.ContextValue("scene");
        Result<IReadOnlyList<ObsSceneItemDto>> items = await ObsSceneItems.ReadAsync(
            _obs,
            broadcasterId,
            scene,
            ct
        );
        if (items.IsFailure)
            return ObsOptionPage.Unavailable(items.ErrorMessage!);

        IEnumerable<PipelineOption> options = items
            .Value.GroupBy(i => i.SourceName)
            .Select(g => ObsOptionPage.Named(g.Key, Visibility(scene, g.First())));

        return ObsOptionPage.Of(options, request);
    }

    // Visibility is per scene, so it is only truthful when the list is for one scene.
    private static string? Visibility(string? scene, ObsSceneItemDto item)
    {
        if (scene is null)
            return null;

        return item.Enabled ? "visible" : "hidden";
    }
}
