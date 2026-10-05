// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

    public int Draw()
    {
        int steps = (int)(((long)Max - Min) / Step);
        return Min + (Random.Shared.Next(0, steps + 1) * Step);
    }
}
