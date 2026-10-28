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

using NomNomzBot.Application.Music.Services;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Music;

/// <summary>How far the playing track is: length, position, time left, percent and the progress bar.</summary>
internal sealed class MusicProgressHelpers()
    : MusicHelperGroup(
        MusicRow.Now(
            "music.now.duration",
            "3:30",
            (now, _) => now.DurationMs > 0 ? MusicText.Clock(now.DurationMs) : null
        ),
        MusicRow.Now("music.now.progress", "1:23", (now, _) => MusicText.Clock(now.ProgressMs)),
        MusicRow.Now(
            "music.now.remaining",
            "2:07",
            (now, _) => now.DurationMs > 0 ? MusicText.Clock(now.DurationMs - now.ProgressMs) : null
        ),
        MusicRow.Now(
            "music.now.percent",
            "40%",
            (now, _) =>
                MusicText.Fraction(now.ProgressMs, now.DurationMs) is { } fraction
                    ? MusicText.Percent(fraction)
                    : null
        ),
        MusicRow.Now("music.now.bar", "▬▬▬▬🔘▬▬▬▬▬▬ 1:23 / 3:30", PrintBar, maxArgs: 1)
    )
{
    /// <summary><c>music.now.bar</c> adds the clock; <c>music.now.bar:5</c> is the bare bar of that width.</summary>
    private static string? PrintBar(NowPlaying now, MusicCall call)
    {
        bool sized = call.Args.Count == 1;
        int width = MusicText.DefaultBarWidth;
        if (sized && !MusicText.TryWidth(call.Args[0], out width))
            return null;

        if (MusicText.Fraction(now.ProgressMs, now.DurationMs) is not { } fraction)
            return null;

        string bar = MusicText.ProgressBar(fraction, width);
        return sized
            ? bar
            : $"{bar} {MusicText.Clock(now.ProgressMs)} / {MusicText.Clock(now.DurationMs)}";
    }
}
