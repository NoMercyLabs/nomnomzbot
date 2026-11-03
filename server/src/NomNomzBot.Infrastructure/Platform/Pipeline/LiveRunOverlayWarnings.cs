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

namespace NomNomzBot.Infrastructure.Platform.Pipeline;

/// <summary>
/// Tells the person who ran a live test which steps showed nothing they could see. A step that only ever shows on a
/// browser source (a sound, a widget event, a spoken line) did run, but with no source attached nobody saw or heard
/// it. The result says so, instead of reporting a silent success.
/// </summary>
internal static class LiveRunOverlayWarnings
{
    private static readonly HashSet<string> OverlayOutputActionTypes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "widget_event",
        "play_sound",
        "stop_sound",
        "play_tts",
        "tts_synthesize",
        "media_playback",
        "stage_effect",
    };

    /// <summary>One warning per executed overlay step when no browser source is attached; empty when one is.</summary>
    public static List<string> For(IEnumerable<StepExecutionLog> stepLogs, bool overlayConnected) =>
        overlayConnected
            ? []
            :
            [
                .. stepLogs
                    .Where(l => OverlayOutputActionTypes.Contains(l.ActionType))
                    .Select(l =>
                        $"Step {l.StepIndex} ({l.ActionType}) shows on a browser source, and none is attached to this channel, so its output could not be seen."
                    ),
            ];
}
