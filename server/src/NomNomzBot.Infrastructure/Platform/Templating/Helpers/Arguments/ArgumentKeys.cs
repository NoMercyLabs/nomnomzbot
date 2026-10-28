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
/// The key shapes with a position in them: <c>args.2</c>, <c>args.2.upper</c>, <c>args.2+</c>, <c>args.2-3</c>.
/// A position is a whole number from 1 (the first argument is <c>args.1</c>, as the seed counts).
/// </summary>
internal static class ArgumentKeys
{
    private const string Prefix = "args.";

    /// <summary>True for <c>args.&lt;n&gt;&lt;suffix&gt;</c>; <paramref name="n"/> is the position.</summary>
    public static bool TryPosition(string key, string suffix, out int n)
    {
        n = 0;
        return key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            && key.Length > Prefix.Length + suffix.Length
            && TryNumber(key[Prefix.Length..^suffix.Length], out n);
    }

    public static bool IsPosition(string key, string suffix) => TryPosition(key, suffix, out _);

    /// <summary>True for <c>args.&lt;a&gt;-&lt;b&gt;</c> with both positions at least 1.</summary>
    public static bool TryRange(string key, out int first, out int last)
    {
        first = 0;
        last = 0;
        if (!key.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        string[] parts = key[Prefix.Length..].Split('-');
        return parts.Length == 2 && TryNumber(parts[0], out first) && TryNumber(parts[1], out last);
    }

    public static bool IsRange(string key) => TryRange(key, out _, out _);

    private static bool TryNumber(string text, out int n) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1;
}
