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

using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// The text helpers that read the channel's language: the joining word of <c>text.join.and</c> and
/// <c>text.join.or</c> ("and"/"en", "or"/"of") and the English-only <c>text.article</c>. This group implements
/// <see cref="ITextHelperGroup"/> itself because a <see cref="TextHelperRow"/> does not see the render scope.
/// A channel language the group has no word for gets the English word.
/// </summary>
internal sealed class LanguageTextHelpers : ITextHelperGroup
{
    private const string English = "en";

    private static readonly Dictionary<string, (string And, string Or)> JoinWords = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        [English] = ("and", "or"),
        ["nl"] = ("en", "of"),
    };

    private static readonly string[] SoundsLikeA = ["uni", "use", "usu", "uti", "eu", "one", "ubi"];

    private static readonly string[] SilentH = ["hour", "honest", "honor", "honour", "heir"];

    private readonly Dictionary<string, Func<IReadOnlyList<string>, string, string?>> _rows = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        ["text.article"] = Article,
        ["text.join.and"] = (args, language) => JoinList(args, JoinWord(language).And),
        ["text.join.or"] = (args, language) => JoinList(args, JoinWord(language).Or),
    };

    public IReadOnlyList<TemplateHelperEntry> Entries { get; } =
    [
        new TextHelperRow("text.article", "an apple", _ => null).ToEntry(),
        new TextHelperRow("text.join.and", "a, b and c", _ => null).ToEntry(),
        new TextHelperRow("text.join.or", "a, b or c", _ => null).ToEntry(),
    ];

    public string? TryRender(HelperNode node, TemplateRenderScope scope) =>
        _rows.TryGetValue(node.Key, out Func<IReadOnlyList<string>, string, string?>? render)
            ? render(node.Arguments, LanguageOf(scope))
            : null;

    private static string LanguageOf(TemplateRenderScope scope) =>
        (scope.Language ?? English).Split('-', '_')[0];

    private static (string And, string Or) JoinWord(string language) =>
        JoinWords.GetValueOrDefault(language, JoinWords[English]);

    /// <summary><c>a</c> or <c>an</c> for English by the first sound; other languages get the word back.</summary>
    private static string? Article(IReadOnlyList<string> args, string language)
    {
        if (args.Count != 1 || args[0].Length == 0)
            return null;
        if (!string.Equals(language, English, StringComparison.OrdinalIgnoreCase))
            return args[0];

        string lower = args[0].ToLowerInvariant();
        bool vowel = "aeiou".Contains(lower[0]) && !SoundsLikeA.Any(lower.StartsWith);
        bool silentH = SilentH.Any(lower.StartsWith);
        return (vowel || silentH ? "an " : "a ") + args[0];
    }

    /// <summary><c>a, b and c</c>: every item but the last with a comma, then the joining word.</summary>
    private static string? JoinList(IReadOnlyList<string> items, string word) =>
        items.Count switch
        {
            0 => null,
            1 => items[0],
            _ => string.Join(", ", items.Take(items.Count - 1)) + " " + word + " " + items[^1],
        };
}
