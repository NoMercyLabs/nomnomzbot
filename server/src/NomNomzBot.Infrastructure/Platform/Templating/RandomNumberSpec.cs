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

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// A whole-number range with a step, as written in a template: <c>min.max</c> or <c>min.max.step</c>.
/// A draw is <c>min + k * step</c> for a random k, so every value is on the step and both ends can occur
/// (the max only when it sits on the step).
/// </summary>
internal readonly record struct RandomNumberSpec(int Min, int Max, int Step)
{
    /// <summary>Parses 2 parts (<c>min</c>, <c>max</c>) or 3 parts (<c>min</c>, <c>max</c>, <c>step</c>).</summary>
    public static bool TryParse(ReadOnlySpan<string> parts, out RandomNumberSpec spec)
    {
        spec = default;
        if (parts.Length is not (2 or 3))
            return false;
        if (!int.TryParse(parts[0], out int min) || !int.TryParse(parts[1], out int max))
            return false;

        int step = 1;
        if (parts.Length == 3 && !int.TryParse(parts[2], out step))
            return false;
        if (step < 1 || max < min)
            return false;

        spec = new(min, max, step);
        return true;
    }

    public int Draw() => Draw(Random.Shared);

    /// <summary>Draws from <paramref name="random"/>, so a test can script every reachable step.</summary>
    public int Draw(Random random)
    {
        int steps = (int)(((long)Max - Min) / Step);
        return Min + (random.Next(0, steps + 1) * Step);
    }
}
