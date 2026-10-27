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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>Argument and text-element readers shared by the text groups (catalog section 3).</summary>
internal static class TextArguments
{
    /// <summary>The most repeats or pad width a helper accepts: the resolver cuts output at 4 KB anyway.</summary>
    public const int MaxCount = 1000;

    /// <summary>Applies the argument escapes the parser leaves alone: <c>\s</c> is a space, <c>\n</c> a new line.</summary>
    public static string Literal(string value) =>
        value
            .Replace(@"\s", " ", StringComparison.Ordinal)
            .Replace(@"\n", "\n", StringComparison.Ordinal);

    /// <summary>A non-negative whole number up to <see cref="MaxCount"/>.</summary>
    public static bool TryCount(string value, out int count) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out count)
        && count <= MaxCount;

    /// <summary>
    /// The separator or pad argument at <paramref name="index"/>. The resolver trims arguments, so an argument
    /// that is empty after trimming stands for one space (<c>{text.repeat:3|ha| }</c>).
    /// </summary>
    public static string TextAt(IReadOnlyList<string> args, int index, string fallback) =>
        args.Count <= index ? fallback
        : args[index].Length == 0 ? " "
        : Literal(args[index]);

    /// <summary>The value of a <c>name=value</c> argument after the first one; null when absent.</summary>
    public static string? Named(IReadOnlyList<string> args, string name)
    {
        string prefix = name + "=";
        string? match = args.Skip(1)
            .FirstOrDefault(a => a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        return match?[prefix.Length..];
    }

    /// <summary>Grapheme-safe split: a surrogate-pair emoji or a combining-mark letter is one element.</summary>
    public static List<string> Elements(string text)
    {
        List<string> elements = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        while (enumerator.MoveNext())
            elements.Add((string)enumerator.Current);
        return elements;
    }

    /// <summary>The words of the text: runs between whitespace.</summary>
    public static List<string> Words(string text) =>
        [.. text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)];

    /// <summary>The words joined with single spaces.</summary>
    public static string Join(IEnumerable<string> words) => string.Join(' ', words);

    /// <summary>Repeats <paramref name="pad"/> to fill <paramref name="count"/> text elements.</summary>
    public static string Fill(string pad, int count)
    {
        List<string> unit = Elements(pad);
        if (unit.Count == 0 || count <= 0)
            return string.Empty;
        return string.Concat(Enumerable.Range(0, count).Select(i => unit[i % unit.Count]));
    }
}
