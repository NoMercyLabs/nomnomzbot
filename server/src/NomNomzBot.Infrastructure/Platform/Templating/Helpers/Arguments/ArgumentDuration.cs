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

using System.Globalization;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Arguments;

/// <summary>
/// Reads a duration word as seconds: <c>45s</c>, <c>5m</c>, <c>2h</c>, <c>3d</c>, <c>1w</c>, a chain of them
/// (<c>1h30m</c>) or a bare number of seconds (<c>90</c>).
/// </summary>
internal static class ArgumentDuration
{
    /// <summary>The longest span read: 100000 days, so the sum cannot overflow.</summary>
    private const long MaxSeconds = 8_640_000_000;

    private static readonly Dictionary<char, long> UnitSeconds = new()
    {
        ['s'] = 1,
        ['m'] = 60,
        ['h'] = 3_600,
        ['d'] = 86_400,
        ['w'] = 604_800,
    };

    public static bool TrySeconds(string word, out long seconds)
    {
        seconds = 0;
        string text = word.ToLowerInvariant();
        int at = 0;
        while (at < text.Length)
        {
            int start = at;
            while (at < text.Length && char.IsAsciiDigit(text[at]))
                at++;

            bool hasUnit = at < text.Length;
            if (
                at == start
                || at - start > 10
                || !long.TryParse(
                    text.AsSpan(start, at - start),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out long amount
                )
            )
                return false;

            long unit = 1;
            if (hasUnit && !UnitSeconds.TryGetValue(text[at++], out unit))
                return false;
            if (!hasUnit && start > 0)
                return false;

            seconds += amount * unit;
            if (seconds > MaxSeconds)
                return false;
        }
        return text.Length > 0;
    }
}
