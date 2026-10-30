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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Counters;

/// <summary>Text and number rules the list helpers share.</summary>
internal static class ListText
{
    /// <summary>The separator joined lists use when the template gives none.</summary>
    public const string DefaultSeparator = ", ";

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A 1-based position: a whole number of at least 1.</summary>
    public static bool TryPosition(string value, out int position) =>
        ParsedArgument.TryWhole(value, out position) && position >= 1;

    /// <summary>The entry at a 1-based position; empty past the end.</summary>
    public static string ItemAt(IReadOnlyList<string> items, int position) =>
        position <= items.Count ? items[position - 1] : string.Empty;

    /// <summary>
    /// The entries joined with a separator; a separator that is empty after the resolver's trim stands for
    /// one space, and <c>\s</c> is a space (the same rule as <c>text.repeat</c>).
    /// </summary>
    public static string Join(IEnumerable<string> items, string separator) =>
        string.Join(
            separator.Length == 0 ? " " : separator.Replace(@"\s", " ", StringComparison.Ordinal),
            items
        );

    /// <summary>Entries of a <c>;</c> separated inline list: trimmed, empty ones dropped.</summary>
    public static List<string> Inline(string list) =>
        [
            .. list.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            ),
        ];
}
