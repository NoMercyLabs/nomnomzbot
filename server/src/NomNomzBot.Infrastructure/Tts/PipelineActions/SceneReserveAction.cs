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
/// Pipeline action <c>scene_reserve</c>: reserves this channel's one TTS playback slot for a scripted,
/// timed overlay sequence (a BSOD-style reward takeover) so an unrelated <c>play_tts</c> call elsewhere
/// (a shoutout, a chat <c>!tts</c>) cannot insert its clip ahead of or between the sequence's own
/// remaining lines (see <see cref="ITtsSceneReservation"/>). Sets <c>{scene.token}</c>, which every
/// <c>play_tts</c> call belonging to this sequence must pass as its own <c>scene_token</c> field, and
/// which <c>scene_release</c> needs to end the reservation early. Pair with <c>scene_release</c> in a
/// <c>finally</c>/always-run step — the reservation auto-expires after <c>max_hold_seconds</c> regardless,
/// so a failed sequence cannot mute the channel's TTS forever.
/// </summary>
public sealed class SceneReserveAction : ICommandAction
{
    private const int DefaultMaxHoldSeconds = 60;
    private const int MaxMaxHoldSeconds = 120;

    private readonly ITtsSceneReservation _reservation;

    public string ActionType => "scene_reserve";

    public LocalizedText Category => new("pipeline.category.tts");

    public LocalizedText Description => new("pipeline.scene_reserve.description");

    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "max_hold_seconds",
                PipelineActionFieldKind.Number,
                Description: new("pipeline.scene_reserve.max_hold_seconds.help")
            ),
        ];

    public SceneReserveAction(ITtsSceneReservation reservation) => _reservation = reservation;

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        int maxHoldSeconds = Math.Clamp(
            action.GetInt("max_hold_seconds", DefaultMaxHoldSeconds),
            1,
            MaxMaxHoldSeconds
        );
        string token = await _reservation.ReserveAsync(
            ctx.BroadcasterId,
            TimeSpan.FromSeconds(maxHoldSeconds),
            ctx.CancellationToken
        );
        ctx.Variables["scene.token"] = token;
        return ActionResult.Success($"scene_reserve:{token}");
    }
}
