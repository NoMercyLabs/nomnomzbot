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
using NomNomzBot.Application.Sound.Services;

namespace NomNomzBot.Infrastructure.Sound.PipelineActions;

/// <summary>
/// Pipeline action <c>play_sound</c> (spec §4). Resolves a clip by id or name, pushes a <c>PlaySound</c>
/// payload to the overlay audio bus via <see cref="ISoundClipOverlayNotifier"/>. When
/// <c>WaitForFinish</c> is true the action awaits the clip's <c>DurationMs</c> (capped at 60 s) before
/// the next action runs. Unknown or disabled clips return a typed failure; no throw.
/// </summary>
public sealed class PlaySoundAction : ICommandAction
{
    private const int MaxWaitMs = 60_000;

    private readonly ISoundClipService _clips;
    private readonly ISoundClipOverlayNotifier _overlay;

    public string ActionType => "play_sound";

    public PlaySoundAction(ISoundClipService clips, ISoundClipOverlayNotifier overlay)
    {
        _clips = clips;
        _overlay = overlay;
    }

    public async Task<ActionResult> ExecuteAsync(
        PipelineExecutionContext ctx,
        ActionDefinition action
    )
    {
        string? clipRef = action.GetString("clip");
        if (string.IsNullOrWhiteSpace(clipRef))
            return ActionResult.Failure("play_sound requires a 'clip' parameter (id or name).");

        int? volumeOverride = action.GetInt("volume", -1) is int v and >= 0 ? v : null;
        bool waitForFinish =
            string.Equals(
                action.GetString("wait_for_finish"),
                "true",
                StringComparison.OrdinalIgnoreCase
            )
            || action.GetInt("wait_for_finish", 0) == 1;
        string? handle = action.GetString("handle");

        Result<SoundPlaybackDto> resolveResult = await _clips.ResolveForPlaybackAsync(
            ctx.BroadcasterId,
            clipRef,
            volumeOverride,
            ctx.CancellationToken
        );

        if (!resolveResult.IsSuccess)
            return ActionResult.Failure(resolveResult.ErrorMessage ?? "Sound clip not found.");

        SoundPlaybackDto playback = resolveResult.Value;
        await _overlay.PlaySoundAsync(ctx.BroadcasterId, playback, ctx.CancellationToken);

        if (waitForFinish && playback.DurationMs > 0)
        {
            int waitMs = Math.Min(playback.DurationMs, MaxWaitMs);
            await Task.Delay(waitMs, ctx.CancellationToken);
        }

        return ActionResult.Success($"play_sound:{playback.ClipId} volume={playback.Volume}");
    }
}
