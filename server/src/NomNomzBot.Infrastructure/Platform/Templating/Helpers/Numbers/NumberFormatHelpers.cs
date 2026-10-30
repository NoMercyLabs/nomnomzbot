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
using Humanizer;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Numbers;

/// <summary>Writing a number: with group marks in the channel culture, in short form, as a size, and reading a short form back.</summary>
internal sealed class NumberFormatHelpers()
    : NumberHelperGroup(
        NumberHelperRow.WithCulture("num.format", "1,234", Format),
        NumberHelperRow.OfNumber("num.compact", "1.2k", NumberText.Compact),
        NumberHelperRow.Of("num.parse", "1200", Parse),
        NumberHelperRow.OfNumber("num.bytes", "1.5 KB", Bytes)
    )
{
    private static readonly Dictionary<char, decimal> Suffixes = new()
    {
        ['k'] = 1_000m,
        ['m'] = 1_000_000m,
        ['b'] = 1_000_000_000m,
        ['t'] = 1_000_000_000_000m,
    };

    /// <summary><c>num.format:n[|places]</c>: group marks and decimal mark of the channel language.</summary>
    private static string? Format(IReadOnlyList<string> args, CultureInfo culture)
    {
        if (
            args.Count is 0 or > 2
            || !NumberArguments.TryNumber(args[0], out decimal value)
            || !NumberArguments.TryPlacesAt(args, 1, -1, out int places)
        )
            return null;

        return places < 0
            ? value.ToString("#,##0.##########", culture)
            : NumberText.Round(value, places).ToString("N" + places, culture);
    }

    /// <summary><c>num.parse:text</c>: a number with an optional k, M, B or T suffix, as the plain number.</summary>
    private static string? Parse(IReadOnlyList<string> args)
    {
        if (args.Count != 1 || ParsedArgument.Word(args[0]).Length == 0)
            return null;

        string text = ParsedArgument.Word(args[0]);
        decimal multiplier = 1;
        if (Suffixes.TryGetValue(char.ToLowerInvariant(text[^1]), out decimal factor))
        {
            multiplier = factor;
            text = text[..^1];
        }

        return NumberArguments.TryNumber(text, out decimal value)
            ? NumberText.Plain(value * multiplier)
            : null;
    }

    /// <summary><c>num.bytes:n</c>: a size in the largest fitting unit, <c>1.5 KB</c>.</summary>
    private static string? Bytes(decimal value) =>
        value < 0
            ? null
            : ByteSize.FromBytes((double)value).ToString("0.##", CultureInfo.InvariantCulture);
}
