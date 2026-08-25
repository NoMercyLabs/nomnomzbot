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
using NomNomzBot.Application.Common.Models;

namespace NomNomzBot.Infrastructure.Platform.Templating;

/// <summary>
/// The generic text-transform primitives behind the <c>{transform.&lt;name&gt;:&lt;text&gt;}</c> template
/// helper (and its <c>{transform.truncate.&lt;length&gt;:&lt;text&gt;}</c> argument form). Pure string-in,
/// string-out functions — no template/variable knowledge lives here, so this catalog is reusable and testable
/// on its own, and <see cref="TemplateResolver"/> only owns the placeholder syntax around it.
///
/// Every transform operates on Unicode text elements (<see cref="StringInfo.GetTextElementEnumerator"/>),
/// never raw UTF-16 chars — a surrogate-pair emoji or a combining-mark grapheme is one element, so it can
/// never be split, and casing never mangles a non-Latin (e.g. Dutch accented) letter because
/// <c>ToUpperInvariant</c>/<c>ToLowerInvariant</c> already handle those correctly.
///
/// An unrecognized transform name is a <see cref="Result{T}.Failure"/> — never a silent pass-through of the
/// input — so an author's typo is a recorded, visible failure rather than a command that quietly does nothing.
/// </summary>
public static class TextTransformCatalog
{
    /// <summary>
    /// Applies the named transform to <paramref name="input"/>. <paramref name="argument"/> carries the
    /// transform's own parameter when it has one (currently only <c>truncate</c>'s target length); unused by
    /// every other transform. Unknown names fail with a descriptive, caller-visible error message.
    /// </summary>
    public static Result<string> Apply(string name, string input, string? argument)
    {
        return name.ToLowerInvariant() switch
        {
            "upper" => Result<string>.Success(input.ToUpperInvariant()),
            "lower" => Result<string>.Success(input.ToLowerInvariant()),
            "title" => Result<string>.Success(ToTitleCase(input)),
            "spaced" => Result<string>.Success(ToSpaced(input)),
            "alternating" => Result<string>.Success(ToAlternating(input)),
            "reverse" => Result<string>.Success(ToReverse(input)),
            "trim" => Result<string>.Success(input.Trim()),
            "truncate" => ToTruncate(input, argument),
            _ => Result<string>.Failure(
                $"Unknown text transform '{name}'.",
                "TEMPLATE_UNKNOWN_TRANSFORM"
            ),
        };
    }

    /// <summary>Grapheme-safe text-element split — the unit every transform below iterates over.</summary>
    private static List<string> TextElements(string input)
    {
        List<string> elements = [];
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(input);
        while (enumerator.MoveNext())
            elements.Add((string)enumerator.Current);
        return elements;
    }

    /// <summary>Capitalizes the first text element of every whitespace-delimited word, lowercases the rest.</summary>
    private static string ToTitleCase(string input)
    {
        string[] words = input.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            List<string> elements = TextElements(words[i]);
            if (elements.Count == 0)
                continue;
            elements[0] = elements[0].ToUpperInvariant();
            for (int e = 1; e < elements.Count; e++)
                elements[e] = elements[e].ToLowerInvariant();
            words[i] = string.Concat(elements);
        }
        return string.Join(' ', words);
    }

    /// <summary>Interleaves a single space between every text element — "yell" (!slow) style spacing-out.</summary>
    private static string ToSpaced(string input) => string.Join(' ', TextElements(input));

    /// <summary>
    /// sPoNgEbOb case: alternates upper/lower across letter text elements only — whitespace and
    /// non-letter elements (punctuation, emoji) pass through untouched and never consume a toggle, so
    /// "hello world" -> "hElLo WoRlD" rather than losing the alternation at the space.
    /// </summary>
    private static string ToAlternating(string input)
    {
        StringBuilder builder = new(input.Length);
        bool upper = true;
        foreach (string element in TextElements(input))
        {
            if (!char.IsLetter(element, 0))
            {
                builder.Append(element);
                continue;
            }
            builder.Append(upper ? element.ToUpperInvariant() : element.ToLowerInvariant());
            upper = !upper;
        }
        return builder.ToString();
    }

    /// <summary>Reverses by text element, so a surrogate-pair emoji or combining-mark grapheme stays intact.</summary>
    private static string ToReverse(string input)
    {
        List<string> elements = TextElements(input);
        elements.Reverse();
        return string.Concat(elements);
    }

    /// <summary>
    /// Cuts to at most <paramref name="argument"/> text elements; when it actually cuts, appends a single
    /// "…" so the result is visibly truncated rather than silently shortened. A missing/invalid length
    /// argument is a failure, same as an unknown transform name — no silent fallback.
    /// </summary>
    private static Result<string> ToTruncate(string input, string? argument)
    {
        if (
            !int.TryParse(
                argument,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out int maxLength
            )
        )
            return Result<string>.Failure(
                "The 'truncate' transform requires a non-negative integer length argument (e.g. {transform.truncate.20:…}).",
                "TEMPLATE_TRANSFORM_BAD_ARGUMENT"
            );

        List<string> elements = TextElements(input);
        return Result<string>.Success(
            elements.Count <= maxLength ? input : string.Concat(elements.Take(maxLength)) + "…"
        );
    }
}
