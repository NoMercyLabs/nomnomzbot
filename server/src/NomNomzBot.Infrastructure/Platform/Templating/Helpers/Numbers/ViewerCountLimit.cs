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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>
/// Cuts a count or a width that makes a helper's output grow down to the render's
/// <see cref="Application.Abstractions.Templating.TemplateRenderScope.TextCountLimit"/>, so a viewer's
/// <c>{fun.dice.detail:50d2}</c> rolls the limit instead of being refused or running unbounded. No limit: unchanged.
/// </summary>
internal static class ViewerCountLimit
{
    /// <summary>The count itself, or the limit when the count is above it.</summary>
    public static int Within(int count, int? limit) =>
        limit is { } cap && count > cap ? cap : count;

    /// <summary>
    /// The arguments with the whole number at <paramref name="index"/> cut down to <paramref name="limit"/>. An
    /// argument that is not a whole number is left for the helper to refuse.
    /// </summary>
    public static IReadOnlyList<string> Within(IReadOnlyList<string> args, int? index, int? limit)
    {
        if (
            index is not { } at
            || limit is not { } cap
            || at >= args.Count
            || !ParsedArgument.TryWhole(args[at], out int count)
            || count <= cap
        )
            return args;

        List<string> cut = [.. args];
        cut[at] = cap.ToString(CultureInfo.InvariantCulture);
        return cut;
    }
}
