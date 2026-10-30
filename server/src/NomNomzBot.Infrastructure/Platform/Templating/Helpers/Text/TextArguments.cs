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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>Argument and text-element readers shared by the text groups (catalog section 3).</summary>
internal static class TextArguments
{
    /// <summary>The most repeats or pad width a helper accepts: the resolver cuts output at 4 KB anyway.</summary>
    public const int MaxCount = 1000;

    /// <summary>A non-negative whole number up to <see cref="MaxCount"/>; the spaces around it do not count.</summary>
    public static bool TryCount(string value, out int count) =>
        ParsedArgument.TryWhole(value, out count) && count <= MaxCount;

    /// <summary>
    /// The separator or pad argument at <paramref name="index"/>, exactly as written (the parser already turned
    /// <c>\s</c> and <c>\n</c> into a space and a new line); <paramref name="fallback"/> when it is absent.
    /// </summary>
    public static string TextAt(IReadOnlyList<string> args, int index, string fallback) =>
        args.Count <= index ? fallback : args[index];

    /// <summary>
    /// The value of a <c>name=value</c> argument after the first one; null when absent. The spaces around the
    /// name do not count, the value is returned as written.
    /// </summary>
    public static string? Named(IReadOnlyList<string> args, string name)
    {
        foreach (string argument in args.Skip(1))
        {
            int equals = argument.IndexOf('=', StringComparison.Ordinal);
            if (
                equals > 0
                && ParsedArgument
                    .Word(argument[..equals])
                    .Equals(name, StringComparison.OrdinalIgnoreCase)
            )
                return argument[(equals + 1)..];
        }
        return null;
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
