// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out position)
        && position >= 1;

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
