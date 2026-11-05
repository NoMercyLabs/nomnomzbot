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

using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Contracts.Tts;

namespace NomNomzBot.Infrastructure.Tts.PipelineActions;

/// <summary>
/// Pipeline action <c>scene_release</c>: ends a reservation started by <c>scene_reserve</c> early, so
/// other TTS requests on the channel can speak again right away instead of waiting out the reservation's
/// <c>max_hold_seconds</c> ceiling. A missing or stale (already superseded) <c>scene_token</c> is a no-op —
/// see <see cref="ITtsSceneReservation.ReleaseAsync"/>.
/// </summary>
public sealed class SceneReleaseAction : ICommandAction
{
    private readonly ITtsSceneReservation _reservation;

    public string ActionType => "scene_release";

    public LocalizedText Category => new("pipeline.category.tts");

    public LocalizedText Description => new("pipeline.scene_release.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "scene_token",
                PipelineActionFieldKind.Text,
                Required: true,
                Templated: true,
                Description: new("pipeline.scene_release.scene_token.help")
            ),
        ];

    public SceneReleaseAction(ITtsSceneReservation reservation) => _reservation = reservation;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string token = action.GetString("scene_token") ?? string.Empty;
        if (token.Length == 0)
            return ActionResult.Success("scene_release:no-token");

        await _reservation.ReleaseAsync(ctx.BroadcasterId, token, ctx.CancellationToken);
        return ActionResult.Success($"scene_release:{token}");
    }
}
