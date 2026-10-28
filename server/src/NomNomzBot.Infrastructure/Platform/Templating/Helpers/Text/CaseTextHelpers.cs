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

/// <summary>Letter case: upper, lower, title, sentence, first-letter and swapped forms, alternating forms.</summary>
internal sealed class CaseTextHelpers()
    : TextHelperGroup(
        Transform("text.upper", "HELLO", "upper"),
        Transform("text.lower", "hello", "lower"),
        Transform("text.title", "Big World", "title"),
        Transform("text.alternating", "HeLlO", "alternating"),
        Transform("text.mocking", "hElLo", "mocking"),
        Transform("text.spongecase", "hElLo WoRlD", "mocking"),
        TextHelperRow.OfText("text.sentence", "Hello world. Bye", Sentence),
        TextHelperRow.OfText("text.capitalize", "Hello world", t => FirstElement(t, upper: true)),
        TextHelperRow.OfText("text.decapitalize", "hello", t => FirstElement(t, upper: false)),
        TextHelperRow.OfText("text.swapcase", "hELLO", Swap)
    )
{
    private static TextHelperRow Transform(string key, string sample, string transform) =>
        TextHelperRow.OfText(
            key,
            sample,
            text => TextTransformCatalog.Apply(transform, text, null).Value
        );

    /// <summary>Lowercases everything, then capitalizes the first letter of each sentence.</summary>
    private static string Sentence(string text)
    {
        StringBuilder builder = new(text.Length);
        bool startOfSentence = true;
        foreach (string element in TextArguments.Elements(text.ToLowerInvariant()))
        {
            bool isLetter = char.IsLetter(element, 0);
            builder.Append(startOfSentence && isLetter ? element.ToUpperInvariant() : element);
            if (isLetter || char.IsDigit(element, 0))
                startOfSentence = false;
            else if (element is "." or "!" or "?")
                startOfSentence = true;
        }
        return builder.ToString();
    }

    /// <summary>Changes the case of the first text element only and keeps the rest as written.</summary>
    private static string FirstElement(string text, bool upper)
    {
        List<string> elements = TextArguments.Elements(text);
        if (elements.Count == 0)
            return text;
        elements[0] = upper ? elements[0].ToUpperInvariant() : elements[0].ToLowerInvariant();
        return string.Concat(elements);
    }

    /// <summary>Upper becomes lower and lower becomes upper, element by element.</summary>
    private static string Swap(string text) =>
        string.Concat(
            TextArguments
                .Elements(text)
                .Select(e => char.IsUpper(e, 0) ? e.ToLowerInvariant() : e.ToUpperInvariant())
        );
}
