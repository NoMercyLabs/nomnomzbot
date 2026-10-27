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
using System.Text;
using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// Naming styles: slug, snake, kebab, camel, Pascal and hashtag. The text is first cut into words at every
/// character that is not a letter or digit and at every lower-to-upper step (<c>helloBig</c>), with accents
/// folded away, so every style gets the same words.
/// </summary>
internal sealed partial class NamingTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText("text.slug", "hello-big-world", text => Join(text, "-")),
        TextHelperRow.OfText("text.snake", "hello_big_world", text => Join(text, "_")),
        TextHelperRow.OfText("text.kebab", "hello-big-world", text => Join(text, "-")),
        TextHelperRow.OfText("text.camel", "helloBigWorld", Camel),
        TextHelperRow.OfText("text.pascal", "HelloBigWorld", Pascal),
        TextHelperRow.OfText("text.hashtag", "#BigWorld", Hashtag)
    )
{
    [GeneratedRegex(@"(?<=\p{Ll})(?=\p{Lu})|[^\p{L}\p{N}]+")]
    private static partial Regex WordBoundary();

    private static string Join(string text, string separator) =>
        string.Join(separator, WordsOf(text).Select(w => w.ToLowerInvariant()));

    private static string Camel(string text)
    {
        List<string> words = WordsOf(text);
        return words.Count == 0
            ? string.Empty
            : words[0].ToLowerInvariant() + string.Concat(words.Skip(1).Select(Capitalized));
    }

    private static string Pascal(string text) => string.Concat(WordsOf(text).Select(Capitalized));

    private static string Hashtag(string text)
    {
        string tag = Pascal(text);
        return tag.Length == 0 ? string.Empty : "#" + tag;
    }

    private static string Capitalized(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    private static List<string> WordsOf(string text) =>
        [.. WordBoundary().Split(StripAccents(text)).Where(w => w.Length > 0)];

    private static string StripAccents(string text) =>
        string.Concat(
            text.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
        );
}
