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
using NomNomzBot.Application.Sound.Services;

namespace NomNomzBot.Infrastructure.Sound.PipelineActions;

/// <summary>
/// Pipeline action <c>stop_sound</c> (spec §4). Pushes a stop command to the overlay — targeting a
/// named handle when one is provided, or stopping all playback when <c>All</c> is true.
/// </summary>
public sealed class StopSoundAction : ICommandAction
{
    private readonly ISoundClipOverlayNotifier _overlay;

    public string ActionType => "stop_sound";

    public LocalizedText Category => new("pipeline.category.sound");

    public LocalizedText Description => new("pipeline.stop_sound.description");
    public IReadOnlyList<PipelineActionFieldDescriptor> Fields =>
        [
            new(
                "handle",
                PipelineActionFieldKind.Text,
                Description: new("pipeline.stop_sound.handle.help")
            ),
            new(
                "all",
                PipelineActionFieldKind.Boolean,
                Description: new("pipeline.stop_sound.all.help")
            ),
        ];

    public StopSoundAction(ISoundClipOverlayNotifier overlay)
    {
        _overlay = overlay;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? handle = action.GetString("handle");
        bool all =
            string.Equals(action.GetString("all"), "true", StringComparison.OrdinalIgnoreCase)
            || action.GetInt("all", 0) == 1;

        await _overlay.StopSoundAsync(ctx.BroadcasterId, handle, all, ctx.CancellationToken);
        return ActionResult.Success("stop_sound");
    }
}
