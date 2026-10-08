// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Moderation.SpamDefense;

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// Cleans a name for the voice in fixed steps: strip xX decoration, turn separators and symbols into
/// spaces, decode leetspeak digits and symbols that sit INSIDE a run of letters, then split camelCase and
/// letter/digit joins. Numeric suffixes stay numbers except a decorative _99 on an already-leet name.
/// </summary>
public sealed class SpokenNameFormatter : ISpokenNameFormatter
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    private static readonly Regex WrappedDecoration = new(
        @"^(?:xX|Xx)(?<inner>.+?)(?:xX|Xx)$",
        RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex DecorationToken = new(
        @"^(?:xX|Xx|xXx|XxX)$",
        RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex LeetRun = new(
        @"(?<=\p{L})[0-9@$]+(?=\p{L})",
        RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex LeadingLeetRun = new(
        @"^[0134578@$]+(?=\p{Ll})",
        RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex WordSplit = new(
        @"(?<=\p{Ll})(?=\p{Lu})|(?<=\p{Lu})(?=\p{Lu}\p{Ll})|(?<=\p{L})(?=\d)|(?<=\d)(?=\p{L})",
        RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private const string NameChars = @"[\p{L}\p{N}_]";

    public string Format(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return name;

        List<string> tokens = Tokenize(StripWrappedDecoration(name.Trim().TrimStart('@')));
        bool hasInternalLeet = tokens.Any(token =>
            LeetRun.Matches(token).Any(run => run.Value.All(IsLeet))
        );
        List<string> words = [];
        for (int index = 0; index < tokens.Count; index++)
        {
            string token = tokens[index];
            if (hasInternalLeet && index == tokens.Count - 1 && IsDecorativeNumber(token))
                continue;
            words.AddRange(SplitWords(DecodeLeet(token, hasInternalLeet)));
        }

        return words.Count == 0 ? name : string.Join(' ', words);
    }

    public string ApplyToText(string text, IReadOnlyList<string>? names)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        Regex pattern = BuildTextPattern(names);
        return pattern.Replace(
            text,
            match =>
                Format(
                    match.Groups["mention"].Success ? match.Groups["mention"].Value : match.Value
                )
        );
    }

    private static Regex BuildTextPattern(IReadOnlyList<string>? names)
    {
        string known = string.Join(
            "|",
            (names ?? [])
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim().TrimStart('@'))
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(n => n.Length)
                .Select(Regex.Escape)
        );
        string alternatives = @"@(?<mention>" + NameChars + "+)";
        if (known.Length > 0)
            alternatives += "|" + known;
        return new(
            @"(?<!" + NameChars + "|@)(?:" + alternatives + ")(?!" + NameChars + ")",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            RegexTimeout
        );
    }

    private static string StripWrappedDecoration(string name)
    {
        Match match = WrappedDecoration.Match(name);
        return match.Success && match.Groups["inner"].Value.Any(char.IsLetterOrDigit)
            ? match.Groups["inner"].Value
            : name;
    }

    private static List<string> Tokenize(string name)
    {
        StringBuilder cleaned = new(name.Length);
        for (int i = 0; i < name.Length; i++)
            cleaned.Append(KeepsAsCharacter(name, i) ? name[i] : ' ');

        List<string> tokens = cleaned
            .ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        if (tokens.All(DecorationToken.IsMatch))
            return [];

        TrimDecorationToken(tokens, 0);
        TrimDecorationToken(tokens, tokens.Count - 1);
        return tokens;
    }

    private static bool KeepsAsCharacter(string name, int index)
    {
        char c = name[index];
        if (char.IsLetterOrDigit(c))
            return true;
        return IsLeet(c)
            && index > 0
            && index < name.Length - 1
            && char.IsLetter(name[index - 1])
            && char.IsLetter(name[index + 1]);
    }

    private static void TrimDecorationToken(List<string> tokens, int index)
    {
        if (tokens.Count > 1 && DecorationToken.IsMatch(tokens[index]))
            tokens.RemoveAt(index);
    }

    // A leading digit is a number ("1Gamer") unless the rest of the name is already leetspeak and it opens a
    // lowercase word: "5p3c7r4l" is "spectral", not "5 pectral".
    private static string DecodeLeet(string token, bool hasInternalLeet)
    {
        string decoded = LeetRun.Replace(token, run => DecodeRun(token, run));
        // A short trailing glyph belongs to the word when another part of this name proves leetspeak.
        // A plain word followed by a number (Dark1, Anna1990) keeps its number.
        if (
            hasInternalLeet
            && token.Length <= 3
            && token.Length > 1
            && char.IsLetter(token[^2])
            && IsLeet(token[^1])
        )
            decoded = decoded[..^1] + Decode(token[^1].ToString());

        if (decoded == token)
            return decoded;

        Match leading = LeadingLeetRun.Match(decoded);
        return leading.Success ? Decode(leading.Value) + decoded[leading.Length..] : decoded;
    }

    // "Player1Gamer": a lowercase letter before the run and a capital after it is a word break, not
    // leetspeak. A run between two capitals ("L33T") decodes in capitals.
    private static string DecodeRun(string token, Match run)
    {
        char before = token[run.Index - 1];
        char after = token[run.Index + run.Length];
        if (!run.Value.All(IsLeet) || (char.IsLower(before) && char.IsUpper(after)))
            return run.Value;

        string decoded = Decode(run.Value);
        return char.IsUpper(before) && char.IsUpper(after) ? decoded.ToUpperInvariant() : decoded;
    }

    private static string Decode(string run)
    {
        StringBuilder decoded = new(run.Length);
        foreach (char c in run)
        {
            MessageNormalizer.TryFoldLeet(c, out char letter);
            decoded.Append(letter);
        }
        return decoded.ToString();
    }

    private static bool IsLeet(char c) => MessageNormalizer.TryFoldLeet(c, out _);

    private static bool IsDecorativeNumber(string token) => token == "99";

    private static string[] SplitWords(string token) =>
        WordSplit
            .Replace(token, " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word =>
                word.Equals("haxor", StringComparison.OrdinalIgnoreCase)
                    ? word.All(char.IsUpper)
                        ? "HACKER"
                        : char.IsUpper(word[0])
                            ? "Hacker"
                            : "hacker"
                    : word
            )
            .ToArray();
}
