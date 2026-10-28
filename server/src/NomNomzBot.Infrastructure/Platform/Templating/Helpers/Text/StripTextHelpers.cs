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

using System.Text;
using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// Removing or hiding parts of the text: links, mentions, everything but digits or letters, vowels, a banned
/// word and the start of a secret. Removing a link or mention also squeezes the gap it leaves to one space.
/// (<c>text.emotes.strip</c> needs the channel's emote set and is not in this pure group.)
/// </summary>
internal sealed partial class StripTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText("text.links.strip", "see now", text => Remove(Link(), text)),
        TextHelperRow.OfText("text.mentions.strip", "hi", text => Remove(Mention(), text)),
        TextHelperRow.OfText("text.digits", "122", text => Keep(text, Rune.IsDigit)),
        TextHelperRow.OfText("text.letters", "ab", text => Keep(text, Rune.IsLetter)),
        TextHelperRow.OfText("text.vowels.remove", "hll", text => Keep(text, r => !IsVowel(r))),
        new TextHelperRow("text.censor", "you ****", Censor),
        new TextHelperRow("text.mask", "****et", Mask)
    )
{
    private const char Star = '*';

    [GeneratedRegex(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    [GeneratedRegex(@"@\w+")]
    private static partial Regex Mention();

    private static string Remove(Regex pattern, string text) =>
        TextArguments.Join(TextArguments.Words(pattern.Replace(text, " ")));

    private static string Keep(string text, Func<Rune, bool> keep) =>
        string.Concat(text.EnumerateRunes().Where(keep));

    private static bool IsVowel(Rune rune) =>
        rune.IsAscii && "aeiouAEIOU".Contains((char)rune.Value);

    /// <summary><c>{text.censor:t|word}</c>: every case-insensitive match of word becomes stars of the same length.</summary>
    private static string? Censor(IReadOnlyList<string> args)
    {
        if (args.Count != 2 || args[1].Length == 0)
            return null;

        return Regex.Replace(
            args[0],
            Regex.Escape(args[1]),
            match => new string(Star, TextArguments.Elements(match.Value).Count),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
    }

    /// <summary><c>{text.mask:t|keep}</c>: all but the last keep elements become stars.</summary>
    private static string? Mask(IReadOnlyList<string> args)
    {
        if (args.Count != 2 || !TextArguments.TryCount(args[1], out int keep))
            return null;

        List<string> elements = TextArguments.Elements(args[0]);
        int hidden = Math.Max(0, elements.Count - keep);
        return new string(Star, hidden) + string.Concat(elements.Skip(hidden));
    }
}
