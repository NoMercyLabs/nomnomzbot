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

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Text;

/// <summary>
/// Fancy alphabets: the same words in small caps, upside down, full width, math bold, italic, monospace,
/// script, circled, squared and superscript letters, plus the combining strike and underline and the NFKC
/// <c>text.plain</c> that folds them all back. Each look is a <see cref="TextAlphabet"/>.
/// </summary>
internal sealed class FancyTextHelpers()
    : TextHelperGroup(
        TextHelperRow.OfText("text.smallcaps", "ʜᴇʟʟᴏ", SmallCaps.Apply),
        TextHelperRow.OfText("text.upsidedown", "ollǝɥ", UpsideDown),
        TextHelperRow.OfText("text.fullwidth", "ｈｉ", FullWidth.Apply),
        TextHelperRow.OfText("text.bold", "𝐡𝐢", Bold.Apply),
        TextHelperRow.OfText("text.italic", "𝘩𝘪", Italic.Apply),
        TextHelperRow.OfText("text.monospace", "𝚑𝚒", Monospace.Apply),
        TextHelperRow.OfText("text.script", "𝒽𝒾", Script.Apply),
        TextHelperRow.OfText("text.bubble", "ⓗⓘ", Bubble.Apply),
        TextHelperRow.OfText("text.squared", "🄷🄸", Squared.Apply),
        TextHelperRow.OfText("text.strike", "h̶i̶", text => Combine(text, "̶")),
        TextHelperRow.OfText("text.underline", "h̲i̲", text => Combine(text, "̲")),
        TextHelperRow.OfText("text.superscript", "ʰⁱ", Superscript.Apply),
        TextHelperRow.OfText(
            "text.plain",
            "hi hi",
            text => text.Normalize(NormalizationForm.FormKC)
        )
    )
{
    private static readonly TextAlphabet SmallCaps = new(
        TextAlphabet.Lower,
        TextAlphabet.Of("ᴀʙᴄᴅᴇꜰɢʜɪᴊᴋʟᴍɴᴏᴘǫʀsᴛᴜᴠᴡxʏᴢ"),
        TextAlphabet.Fold.Lower
    );

    private static readonly TextAlphabet UpsideDownLetters = new(
        TextAlphabet.Lower + TextAlphabet.Digits + ".,?!'()[]{}_&",
        TextAlphabet.Of("ɐqɔpǝɟƃɥᴉɾʞlɯuodbɹsʇnʌʍxʎz" + "0ƖᄅƐㄣϛ9ㄥ86" + "˙'¿¡,)(][}{‾⅋"),
        TextAlphabet.Fold.Lower
    );

    private static readonly TextAlphabet FullWidth = new(
        " " + string.Concat(Enumerable.Range(0x21, 0x5E).Select(c => (char)c)),
        [.. TextAlphabet.Run(0x3000, 1), .. TextAlphabet.Run(0xFF01, 0x5E)]
    );

    private static readonly TextAlphabet Bold = MathAlphabet(0x1D400, 0x1D41A, 0x1D7CE);

    private static readonly TextAlphabet Italic = MathAlphabet(0x1D608, 0x1D622, null);

    private static readonly TextAlphabet Monospace = MathAlphabet(0x1D670, 0x1D68A, 0x1D7F6);

    private static readonly TextAlphabet Script = MathAlphabet(
        0x1D49C,
        0x1D4B6,
        null,
        new Dictionary<char, string>
        {
            ['B'] = "ℬ",
            ['E'] = "ℰ",
            ['F'] = "ℱ",
            ['H'] = "ℋ",
            ['I'] = "ℐ",
            ['L'] = "ℒ",
            ['M'] = "ℳ",
            ['R'] = "ℛ",
            ['e'] = "ℯ",
            ['g'] = "ℊ",
            ['o'] = "ℴ",
        }
    );

    private static readonly TextAlphabet Bubble = new(
        TextAlphabet.Upper + TextAlphabet.Lower + TextAlphabet.Digits,
        [
            .. TextAlphabet.Run(0x24B6, 26),
            .. TextAlphabet.Run(0x24D0, 26),
            .. TextAlphabet.Run(0x24EA, 1),
            .. TextAlphabet.Run(0x2460, 9),
        ]
    );

    private static readonly TextAlphabet Squared = new(
        TextAlphabet.Upper,
        TextAlphabet.Run(0x1F130, 26),
        TextAlphabet.Fold.Upper
    );

    private static readonly TextAlphabet Superscript = new(
        TextAlphabet.Lower + TextAlphabet.Digits + "+-=()",
        TextAlphabet.Of("ᵃᵇᶜᵈᵉᶠᵍʰⁱʲᵏˡᵐⁿᵒᵖqʳˢᵗᵘᵛʷˣʸᶻ" + "⁰¹²³⁴⁵⁶⁷⁸⁹" + "⁺⁻⁼⁽⁾"),
        TextAlphabet.Fold.Lower
    );

    /// <summary>A math alphabet: the capitals, the small letters and optionally the digits sit in runs of code points.</summary>
    private static TextAlphabet MathAlphabet(
        int upperStart,
        int lowerStart,
        int? digitStart,
        IReadOnlyDictionary<char, string>? exceptions = null
    )
    {
        string source = TextAlphabet.Upper + TextAlphabet.Lower;
        List<string> target =
        [
            .. TextAlphabet.Run(upperStart, 26),
            .. TextAlphabet.Run(lowerStart, 26),
        ];
        if (digitStart is int digits)
        {
            source += TextAlphabet.Digits;
            target.AddRange(TextAlphabet.Run(digits, 10));
        }
        for (int i = 0; i < source.Length; i++)
            if (exceptions is not null && exceptions.TryGetValue(source[i], out string? special))
                target[i] = special;
        return new TextAlphabet(source, target);
    }

    private static string UpsideDown(string text) =>
        string.Concat(
            TextArguments.Elements(text).AsEnumerable().Reverse().Select(UpsideDownLetters.Map)
        );

    /// <summary>Adds a combining mark after every element that is not white space.</summary>
    private static string Combine(string text, string mark) =>
        string.Concat(
            TextArguments.Elements(text).Select(e => string.IsNullOrWhiteSpace(e) ? e : e + mark)
        );
}
