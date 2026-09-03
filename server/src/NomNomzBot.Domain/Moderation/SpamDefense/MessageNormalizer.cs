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

namespace NomNomzBot.Domain.Moderation.SpamDefense;

/// <summary>
/// L0 of the spam-defense stack (spam-defense.md §L0) — the foundation every layer above consumes, so
/// they all get evasion resistance for free.
///
/// <para>The problem it solves: a blocklist entry for <c>viewers</c> does not match <c>VI EWERS</c>,
/// <c>vie̟wers</c>, <c>ѵiewers</c>, <c>ｖｉｅｗｅｒｓ</c> or <c>v13w3r5</c>, and each evasion is cheap for the
/// attacker while each blocklist line is expensive for the operator. Normalizing BEFORE matching collapses
/// all of them onto one skeleton, so a single corpus entry covers every future respacing of a phrase.</para>
///
/// <para>Pure and deterministic — no clock, no I/O, no state. That is what lets the corpus tests pin exact
/// skeletons and lets every layer above be tested without a database.</para>
///
/// <para><b>It never mutates the message shown in chat.</b> It exists only to decide.</para>
/// </summary>
public static class MessageNormalizer
{
    /// <summary>
    /// Confusables folded to their Latin skeleton. This is a CURATED subset of UTS #39
    /// <c>confusables.txt</c>, not the whole table: NFKD (step 1) already folds fullwidth,
    /// math-alphanumeric and compatibility forms, which leaves the cross-script homoglyphs that actually
    /// appear in chat spam — Cyrillic and Greek letters wearing Latin faces. Adding a row here is the
    /// extension point; vendoring the full table would be the alternative if the corpus ever needs it.
    /// </summary>
    private static readonly Dictionary<char, char> Confusables = new()
    {
        // ─ Cyrillic → Latin ─
        ['а'] = 'a', // а
        ['е'] = 'e', // е
        ['о'] = 'o', // о
        ['р'] = 'p', // р
        ['с'] = 'c', // с
        ['у'] = 'y', // у
        ['х'] = 'x', // х
        ['ѕ'] = 's', // ѕ
        ['і'] = 'i', // і
        ['ј'] = 'j', // ј
        ['һ'] = 'h', // һ
        ['ѵ'] = 'v', // ѵ izhitsa — caught by the corpus test, absent from the first draft
        ['ԁ'] = 'd', // ԁ
        ['ɡ'] = 'g', // ɡ Latin script-g
        ['ӏ'] = 'l', // ӏ palochka
        ['ԛ'] = 'q', // ԛ
        ['ԝ'] = 'w', // ԝ
        ['ż'] = 'z', // ż (survives NFKD as z + mark, but the precomposed form appears in lists)
        ['в'] = 'b', // в
        ['к'] = 'k', // к
        ['м'] = 'm', // м
        ['н'] = 'h', // н
        ['т'] = 't', // т
        ['А'] = 'a', // А
        ['В'] = 'b', // В
        ['Е'] = 'e', // Е
        ['К'] = 'k', // К
        ['М'] = 'm', // М
        ['Н'] = 'h', // Н
        ['О'] = 'o', // О
        ['Р'] = 'p', // Р
        ['С'] = 'c', // С
        ['Т'] = 't', // Т
        ['Х'] = 'x', // Х
        ['Ѕ'] = 's', // Ѕ
        ['І'] = 'i', // І
        ['Ј'] = 'j', // Ј
        // ─ Stroked / barred Latin (Windows Win+. → Symbols → Latin) ─
        // NFKD does NOT decompose these: a stroke is part of the letter, not a combining mark. They are
        // two clicks away on the on-screen keyboard, which makes them the cheapest disguise available.
        ['ø'] = 'o',
        ['Ø'] = 'o',
        ['đ'] = 'd',
        ['Đ'] = 'd',
        ['ð'] = 'd',
        ['ł'] = 'l',
        ['Ł'] = 'l',
        ['ħ'] = 'h',
        ['Ħ'] = 'h',
        ['ŧ'] = 't',
        ['Ŧ'] = 't',
        ['ɨ'] = 'i',
        ['Ɨ'] = 'i',
        ['ƶ'] = 'z',
        ['Ƶ'] = 'z',
        ['ɓ'] = 'b',
        ['ǥ'] = 'g',
        ['ɇ'] = 'e',
        ['ɏ'] = 'y',
        ['ꝑ'] = 'p',
        ['ı'] = 'i', // dotless i
        ['ȷ'] = 'j', // dotless j
        ['ſ'] = 's', // long s
        ['þ'] = 'p',
        ['Þ'] = 'p',
        ['ß'] = 'b',
        ['æ'] = 'a',
        ['Æ'] = 'a',
        ['œ'] = 'o',
        ['Œ'] = 'o',
        // ─ Currency (Win+. → Symbols → Currency) ─ each one wears a letter's face.
        ['¢'] = 'c',
        ['£'] = 'l',
        ['¥'] = 'y',
        ['₩'] = 'w',
        ['₽'] = 'p',
        ['€'] = 'e',
        ['₡'] = 'c',
        ['₺'] = 't',
        ['₴'] = 'e',
        ['ƒ'] = 'f',
        // ─ General symbols reachable from the same panel ─
        ['°'] = 'o',
        ['∅'] = 'o',
        ['✕'] = 'x',
        ['✗'] = 'x',
        ['✘'] = 'x',
        ['×'] = 'x',
        ['∆'] = 'a',
        ['∑'] = 'e',
        ['℮'] = 'e',
        ['№'] = 'n',
        ['Ω'] = 'o',
        ['µ'] = 'u', // micro sign
        ['μ'] = 'u', // Greek mu
        // ─ Greek → Latin ─
        ['ο'] = 'o', // ο
        ['α'] = 'a', // α
        ['ε'] = 'e', // ε
        ['ι'] = 'i', // ι
        ['κ'] = 'k', // κ
        ['ν'] = 'v', // ν
        ['ρ'] = 'p', // ρ
        ['σ'] = 'o', // σ
        ['υ'] = 'u', // υ
        ['χ'] = 'x', // χ
        ['Α'] = 'a', // Α
        ['Β'] = 'b', // Β
        ['Ε'] = 'e', // Ε
        ['Η'] = 'h', // Η
        ['Ι'] = 'i', // Ι
        ['Κ'] = 'k', // Κ
        ['Μ'] = 'm', // Μ
        ['Ν'] = 'n', // Ν
        ['Ο'] = 'o', // Ο
        ['Ρ'] = 'p', // Ρ
        ['Τ'] = 't', // Τ
        ['Υ'] = 'y', // Υ
        ['Χ'] = 'x', // Χ
    };

    /// <summary>Leetspeak digits and symbols to the letters they impersonate (§L0 step 5).</summary>
    private static readonly Dictionary<char, char> Leet = new()
    {
        ['0'] = 'o',
        ['1'] = 'i',
        ['3'] = 'e',
        ['4'] = 'a',
        ['5'] = 's',
        ['7'] = 't',
        ['@'] = 'a',
        ['$'] = 's',
    };

    /// <summary>
    /// Run the full §L0 pipeline. Steps are applied in the spec's order — the order matters: folding
    /// before stripping marks would leave combining marks attached to already-folded characters, and
    /// de-leeting before homoglyph folding would turn a Cyrillic о into an o that the leet map never sees.
    /// </summary>
    public static NormalizedMessage Normalize(string? message)
    {
        string original = message ?? string.Empty;

        // The SD2 signal is judged on what the SENDER actually typed, never on the decomposed form.
        // NFKD manufactures combining marks out of innocent characters — `¯` (U+00AF) decomposes to
        // space + combining macron — so scanning after decomposition reports zalgo in a kaomoji and
        // punishes ordinary chat, the exact SD0 failure. Scan the original; decompose only to match.
        bool cosmeticAbuse = HasCosmeticAbuse(original);

        // 1. NFKD — separates base characters from combining marks and folds fullwidth /
        //    math-alphanumeric / compatibility forms toward ASCII.
        string decomposed = original.Normalize(NormalizationForm.FormKD);

        // 2 + 3. Strip the marks and invisibles themselves so they cannot break up a match.
        StringBuilder visible = new(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (IsCosmeticAbuse(c))
                continue;
            visible.Append(IsCollapsibleWhitespace(c) ? ' ' : c);
        }

        // 4. Record mixed-script tokens BEFORE folding — after it, the evidence is gone.
        string visibleText = visible.ToString();
        List<string> mixedScriptTokens = FindMixedScriptTokens(visibleText);

        // 4 (cont.) + 5. Fold confusables, lowercase, de-leet.
        StringBuilder folded = new(visibleText.Length);
        foreach (char c in visibleText)
        {
            char mapped =
                Confusables.TryGetValue(c, out char latin) ? latin
                : DeriveLatinSkeleton(c) is char derived ? derived
                : char.ToLowerInvariant(c);
            folded.Append(Leet.TryGetValue(mapped, out char letter) ? letter : mapped);
        }

        // 6. Collapse runs to two, then strip everything that is not a letter or digit.
        string skeleton = BuildSkeleton(folded.ToString());

        return new NormalizedMessage(original, skeleton, cosmeticAbuse, mixedScriptTokens);
    }

    /// <summary>
    /// Combining marks (zalgo, <c>B̟est</c>) and format/invisible characters — zero-width space, joiners,
    /// BOM, word joiner, bidi overrides, and the U+E0000–E007F tag block. No legitimate chat message
    /// needs any of them.
    /// </summary>
    private static bool IsCosmeticAbuse(char c)
    {
        UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(c);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format)
            return true;

        return c switch
        {
            '​' or '‌' or '‍' => true, // ZWSP / ZWNJ / ZWJ
            '﻿' or '⁠' => true, // BOM / word joiner
            '‪' or '‫' or '‬' or '‭' or '‮' => true, // bidi overrides
            >= '\uDB40' and <= '\uDB43' => true, // high surrogates of the U+E0000–E007F tag block
            _ => false,
        };
    }

    /// <summary>
    /// The SD2 signal, judged positionally rather than per character.
    ///
    /// <para>A zero-width joiner between two LETTERS is evasion — there is no reason to write
    /// <c>f​ree</c>. A zero-width joiner between two EMOJI is how emoji are built: the family
    /// <c>👨‍👩</c> and every skin-tone and profession sequence on the Windows
    /// emoji keyboard is a joined sequence, and so is every variation selector. Treating those as an
    /// attack would delete a large share of ordinary chat, so a format character next to an emoji is
    /// not a signal.</para>
    ///
    /// <para>Stripping is still unconditional — the skeleton wants none of these characters either.
    /// Only the SIGNAL is context-aware, because only the signal can hurt somebody.</para>
    /// </summary>
    private static bool HasCosmeticAbuse(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!IsCosmeticAbuse(text[i]))
                continue;

            bool isFormat =
                CharUnicodeInfo.GetUnicodeCategory(text[i]) == UnicodeCategory.Format
                || text[i] is '​' or '‌' or '‍' or '﻿' or '⁠';
            if (isFormat && (IsEmojiLike(text, i - 1) || IsEmojiLike(text, i + 1)))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// True when the character at <paramref name="index"/> is part of an emoji. Surrogates cover the
    /// astral-plane emoji blocks; <see cref="UnicodeCategory.OtherSymbol"/> covers the BMP ones
    /// (❤, ☀) that the on-screen keyboard also offers.
    /// </summary>
    private static bool IsEmojiLike(string text, int index)
    {
        if (index < 0 || index >= text.Length)
            return false;

        char c = text[index];
        return char.IsSurrogate(c)
            || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherSymbol;
    }

    /// <summary>Any Unicode whitespace that is not a plain space — folded to U+0020 rather than dropped.</summary>
    private static bool IsCollapsibleWhitespace(char c) => c != ' ' && char.IsWhiteSpace(c);

    /// <summary>
    /// Derives a Latin letter from a character's own Unicode decomposition, so accented, circled,
    /// parenthesised, superscript and other compatibility forms fold WITHOUT anyone hand-listing them.
    ///
    /// <para>This exists because a hand-maintained confusable table is the wrong shape for the part of the
    /// problem that Unicode already answers: every <c>é ⓐ ⑴ ᵃ ǽ</c> the on-screen keyboard can produce
    /// decomposes to its base letter, and deriving that is a rule rather than 400 more rows. The
    /// <see cref="Confusables"/> table is then only responsible for what NO algorithm derives —
    /// cross-script homoglyphs (Cyrillic <c>о</c> is a genuinely different letter that merely looks Latin)
    /// and stroked letters (<c>ø đ ł</c> carry no decomposition at all). Those two classes are irreducible
    /// data; everything else is generated here.</para>
    /// </summary>
    private static char? DeriveLatinSkeleton(char c)
    {
        if (c < 128)
            return null; // already ASCII — nothing to derive

        // A lone surrogate half is not a valid string on its own: normalizing one throws
        // ArgumentException. Emoji are surrogate pairs, so without this guard the normalizer crashed on
        // ordinary chat — every message carrying 🎉 — which the corpus test caught immediately. Emoji
        // carry no Latin skeleton anyway; the skeleton builder drops them as non-alphanumeric.
        if (char.IsSurrogate(c))
            return null;

        foreach (char part in c.ToString().Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(part) is UnicodeCategory.NonSpacingMark)
                continue;
            char lower = char.ToLowerInvariant(part);
            // Only accept a decomposition that lands on a plain ASCII letter; anything else (a second
            // letter of a ligature, punctuation, another script) is left to the table or kept as-is.
            return lower is >= 'a' and <= 'z' ? lower : null;
        }

        return null;
    }

    /// <summary>
    /// A token is mixed-script when it contains letters from two different scripts — the signature of
    /// <c>ѕtream</c> (Cyrillic ѕ + Latin). A token written ENTIRELY in one non-Latin script is ordinary
    /// chat in a lot of channels and is deliberately not reported here.
    /// </summary>
    private static List<string> FindMixedScriptTokens(string text)
    {
        List<string> mixed = [];
        foreach (string token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            bool latin = false;
            bool nonLatin = false;
            foreach (char c in token)
            {
                if (!char.IsLetter(c))
                    continue;
                if (c < 128)
                    latin = true;
                else if (Confusables.ContainsKey(c))
                    nonLatin = true;
            }
            if (latin && nonLatin)
                mixed.Add(token);
        }
        return mixed;
    }

    /// <summary>
    /// Collapse runs of the same character to two (<c>heeeeey</c> → <c>heey</c>), then keep only letters
    /// and digits. Dropping spaces is what makes <c>VI EWERS</c> and <c>viewers</c> the same skeleton, and
    /// it is why one corpus entry covers every future respacing of a phrase.
    /// </summary>
    private static string BuildSkeleton(string folded)
    {
        StringBuilder skeleton = new(folded.Length);
        char previous = '\0';
        int runLength = 0;

        foreach (char c in folded)
        {
            if (!char.IsLetterOrDigit(c))
            {
                previous = '\0';
                runLength = 0;
                continue;
            }

            if (c == previous)
            {
                if (runLength >= 2)
                    continue;
                runLength++;
            }
            else
            {
                previous = c;
                runLength = 1;
            }
            skeleton.Append(c);
        }

        return skeleton.ToString();
    }
}
