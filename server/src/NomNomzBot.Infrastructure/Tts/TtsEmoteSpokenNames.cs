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

namespace NomNomzBot.Infrastructure.Tts;

/// <summary>
/// How the voice says a channel's Twitch emotes: the name of the emote, not the channel prefix in front of it.
/// An affiliate's prefix is the first letters of the login plus a collision number (so no, one or two digits), and a
/// partner picks a prefix or none. The prefix cannot be told from the shape of one code (a partner's own
/// "gachiGASM" has none), so it is read from the whole set: the prefix is what EVERY emote of the channel starts
/// with, up to the first capital. When the set does not agree, or holds one emote, nothing is removed and the
/// emote is spoken as written.
/// </summary>
internal static class TtsEmoteSpokenNames
{
    /// <summary>A prefix is at least this long (Twitch builds it from letters of the login).</summary>
    private const int MinPrefixLength = 2;

    /// <summary>Code → the words the voice says, for every emote whose prefix could be read from the set.</summary>
    public static Dictionary<string, string> Derive(
        IReadOnlyList<string> codes,
        Func<string, string> format
    )
    {
        Dictionary<string, string> spoken = new(StringComparer.Ordinal);
        string? prefix = SharedPrefix(codes);
        if (prefix is null)
            return spoken;

        foreach (string code in codes)
        {
            string name = code[prefix.Length..];
            if (name.Length > 0)
                spoken[code] = format(name);
        }

        return spoken;
    }

    /// <summary>
    /// The lowercase-and-digit start every code shares, or null when the codes do not agree. A code with no capital
    /// has no boundary between prefix and name, so one such code settles that there is no prefix.
    /// </summary>
    private static string? SharedPrefix(IReadOnlyList<string> codes)
    {
        if (codes.Count < 2)
            return null;

        string? shared = null;
        foreach (string code in codes)
        {
            string? start = PrefixCandidate(code);
            if (start is null)
                return null;

            shared = shared is null ? start : CommonStart(shared, start);
            if (shared.Length < MinPrefixLength)
                return null;
        }

        return char.IsAsciiLetterLower(shared![0]) ? shared : null;
    }

    /// <summary>The run of lowercase letters and digits before the first capital; null when there is no capital after it.</summary>
    private static string? PrefixCandidate(string code)
    {
        int end = 0;
        while (
            end < code.Length
            && (char.IsAsciiLetterLower(code[end]) || char.IsAsciiDigit(code[end]))
        )
            end++;

        bool hasName = end < code.Length && char.IsAsciiLetterUpper(code[end]);

        return hasName ? code[..end] : null;
    }

    private static string CommonStart(string a, string b)
    {
        int length = 0;
        while (length < a.Length && length < b.Length && a[length] == b[length])
            length++;

        return a[..length];
    }
}
