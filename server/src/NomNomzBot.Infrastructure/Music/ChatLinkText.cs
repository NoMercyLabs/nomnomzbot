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

using System.Text.RegularExpressions;

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Reads a link the way viewers actually paste one into chat: wrapped in <c>&lt;…&gt;</c>, followed by
/// the sentence's punctuation, or with no <c>https://</c> in front. Song request intake normalizes a
/// link-shaped request once with this, so every later step sees a plain absolute URL.
/// </summary>
internal static partial class ChatLinkText
{
    private static readonly char[] TrailingPunctuation =
    [
        '.',
        ',',
        ')',
        '!',
        '?',
        ';',
        ':',
        '"',
        '\'',
        '>',
    ];

    private static readonly char[] LeadingWrappers = ['<', '(', '"', '\''];

    /// <summary>True when the text is one link and nothing else — with or without a scheme.</summary>
    public static bool LooksLikeLink(string text)
    {
        string candidate = Unwrap(text);
        if (candidate.Length == 0 || candidate.Any(char.IsWhiteSpace))
            return false;

        return candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || SchemelessHost().IsMatch(candidate);
    }

    /// <summary>The link as an absolute URL: unwrapped, trailing punctuation dropped, <c>https://</c> added
    /// when missing. Text that is not link-shaped comes back trimmed and otherwise untouched.</summary>
    public static string Normalize(string text)
    {
        if (!LooksLikeLink(text))
            return text.Trim();

        string link = Unwrap(text).TrimEnd(TrailingPunctuation);
        return link.Contains("://", StringComparison.Ordinal) ? link : $"https://{link}";
    }

    private static string Unwrap(string text)
    {
        return text.Trim().TrimStart(LeadingWrappers).TrimEnd(TrailingPunctuation).Trim();
    }

    /// <summary>A host with at least one dot followed by a path, e.g. <c>youtu.be/abc</c> or
    /// <c>www.youtube.com/watch?v=…</c> — "Mr.Brightside" or "AC/DC" do not match.</summary>
    [GeneratedRegex(@"^([a-z0-9-]+\.)+[a-z]{2,}/", RegexOptions.IgnoreCase)]
    private static partial Regex SchemelessHost();
}
