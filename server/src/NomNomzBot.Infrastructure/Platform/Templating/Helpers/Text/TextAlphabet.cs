// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// A map from plain characters to the look-alike characters of one Unicode alphabet (small caps, math bold, ...).
/// Characters the alphabet does not cover pass through unchanged. Both sides are text elements, so an alphabet
/// whose letters sit outside the basic plane (math bold) works the same as one that does not.
/// </summary>
internal sealed class TextAlphabet
{
    /// <summary>Which case the input is folded to before the lookup, for alphabets with one case only.</summary>
    public enum Fold
    {
        None,
        Lower,
        Upper,
    }

    public const string Lower = "abcdefghijklmnopqrstuvwxyz";
    public const string Upper = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
    public const string Digits = "0123456789";

    private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);
    private readonly Fold _fold;

    /// <param name="source">The plain characters, one text element each.</param>
    /// <param name="target">The look-alike for each source character, in the same order.</param>
    /// <param name="fold">The case the input is folded to first.</param>
    public TextAlphabet(string source, IReadOnlyList<string> target, Fold fold = Fold.None)
    {
        List<string> keys = TextArguments.Elements(source);
        if (keys.Count != target.Count)
            throw new ArgumentException("Source and target must have the same length.");

        for (int i = 0; i < keys.Count; i++)
            _map[keys[i]] = target[i];
        _fold = fold;
    }

    /// <summary>The look-alike of one text element, or the element itself when the alphabet has none.</summary>
    public string Map(string element)
    {
        string key = _fold switch
        {
            Fold.Lower => element.ToLowerInvariant(),
            Fold.Upper => element.ToUpperInvariant(),
            _ => element,
        };
        return _map.GetValueOrDefault(key, element);
    }

    public string Apply(string text) => string.Concat(TextArguments.Elements(text).Select(Map));

    /// <summary><paramref name="count"/> consecutive code points from <paramref name="start"/>, one string each.</summary>
    public static List<string> Run(int start, int count) =>
        [.. Enumerable.Range(start, count).Select(char.ConvertFromUtf32)];

    /// <summary>The text elements of a literal, as the target list of a map.</summary>
    public static List<string> Of(string literal) => TextArguments.Elements(literal);
}
