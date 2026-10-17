// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
