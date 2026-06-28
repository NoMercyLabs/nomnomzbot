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
