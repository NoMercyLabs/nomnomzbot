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
using NomNomzBot.Application.Obs.Dtos;
using NomNomzBot.Application.Obs.Services;

namespace NomNomzBot.Infrastructure.Obs.Pipeline;

/// <summary>
/// Reads the scene items the source and filter pickers narrow on. With a scene name it is that scene's items; with
/// none it is every scene's items, so a picker still works before the scene is chosen.
/// </summary>
internal static class ObsSceneItems
{
    public static async Task<Result<IReadOnlyList<ObsSceneItemDto>>> ReadAsync(
        IObsControlService obs,
        Guid broadcasterId,
        string? scene,
        CancellationToken ct
    )
    {
        if (scene is not null)
            return await obs.GetSceneItemListAsync(broadcasterId, scene, ct);

        Result<IReadOnlyList<ObsSceneDto>> scenes = await obs.GetScenesAsync(broadcasterId, ct);
        if (scenes.IsFailure)
            return Result.Failure<IReadOnlyList<ObsSceneItemDto>>(
                scenes.ErrorMessage!,
                scenes.ErrorCode ?? "OBS_UNAVAILABLE"
            );

        List<ObsSceneItemDto> items = [];
        foreach (ObsSceneDto each in scenes.Value)
        {
            Result<IReadOnlyList<ObsSceneItemDto>> inScene = await obs.GetSceneItemListAsync(
                broadcasterId,
                each.Name,
                ct
            );
            if (inScene.IsSuccess)
                items.AddRange(inScene.Value);
        }

        return Result.Success<IReadOnlyList<ObsSceneItemDto>>(items);
    }
}
