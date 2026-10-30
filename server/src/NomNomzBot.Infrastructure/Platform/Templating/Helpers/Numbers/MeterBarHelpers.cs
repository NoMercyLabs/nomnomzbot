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

using NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>The <c>meter</c> rows: the bar with its number, the plain bar, hearts and stars.</summary>
internal sealed class MeterBarHelpers()
    : NumberHelperGroup(
        NumberHelperRow.Of("meter", "██████░░░░ 60%", Meter) with
        {
            AmplifierArgument = WidthArgument,
        },
        NumberHelperRow.Of("meter.plain", "██████░░░░", Plain) with
        {
            AmplifierArgument = WidthArgument,
        },
        NumberHelperRow.Of("meter.hearts", Heart + Heart + Heart + BlackHeart + BlackHeart, Hearts),
        NumberHelperRow.Of("meter.stars", "★★★☆☆", Stars)
    )
{
    private const int WidthArgument = 1;
    private const int HeartCount = 5;
    private const int MaxStars = 20;
    private const string Heart = "❤️";
    private const string BlackHeart = "🖤";
    private const string Star = "★";
    private const string EmptyStar = "☆";

    /// <summary><c>meter:percent[|width[|full|empty]]</c>: the bar, a space and the clamped percent.</summary>
    private static string? Meter(IReadOnlyList<string> args)
    {
        if (
            args.Count == 3
            || !TryPercent(args, out decimal percent)
            || !TryWidth(args, out int width)
        )
            return null;

        string full = TextArguments.TextAt(args, 2, MeterBars.Full);
        string empty = TextArguments.TextAt(args, 3, MeterBars.Empty);
        string bar = MeterBars.Draw(percent, width, full, empty);
        return $"{bar} {NumberText.Plain(MeterBars.Clamp(percent))}%";
    }

    /// <summary><c>meter.plain:percent[|width]</c>: the bar without the number.</summary>
    private static string? Plain(IReadOnlyList<string> args) =>
        args.Count <= 2 && TryPercent(args, out decimal percent) && TryWidth(args, out int width)
            ? MeterBars.Draw(percent, width, MeterBars.Full, MeterBars.Empty)
            : null;

    /// <summary><c>meter.hearts:percent</c>: five hearts, the full ones red, the rest black.</summary>
    private static string? Hearts(IReadOnlyList<string> args) =>
        args.Count == 1 && NumberArguments.TryNumber(args[0], out decimal percent)
            ? MeterBars.Draw(percent, HeartCount, Heart, BlackHeart)
            : null;

    /// <summary><c>meter.stars:n|max</c>: <c>n</c> filled stars out of <c>max</c>.</summary>
    private static string? Stars(IReadOnlyList<string> args)
    {
        if (
            args.Count != 2
            || !NumberArguments.TryNumbers(args, out List<decimal> values)
            || values[1] < 1
            || values[1] > MaxStars
            || values[1] != decimal.Truncate(values[1])
        )
            return null;

        decimal percent = values[0] / values[1] * 100;
        return MeterBars.Draw(percent, (int)values[1], Star, EmptyStar);
    }

    private static bool TryPercent(IReadOnlyList<string> args, out decimal percent)
    {
        percent = 0;
        return args.Count > 0 && NumberArguments.TryNumber(args[0], out percent);
    }

    private static bool TryWidth(IReadOnlyList<string> args, out int width)
    {
        width = MeterBars.DefaultWidth;
        return args.Count < 2 || MeterBars.TryWidth(args[1], out width);
    }
}
