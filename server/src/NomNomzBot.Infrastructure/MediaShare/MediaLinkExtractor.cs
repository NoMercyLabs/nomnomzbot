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

namespace NomNomzBot.Infrastructure.MediaShare;

/// <summary>
/// Finds the first Twitch clip or YouTube link in a chat line. It only locates the link: the resolver
/// still decides whether it is accepted.
/// </summary>
public static partial class MediaLinkExtractor
{
    public static string? FirstLink(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        Match match = LinkPattern().Match(text);
        return match.Success ? match.Value : null;
    }

    [GeneratedRegex(
        @"https?://(?:(?:www\.|m\.)?(?:youtube\.com|twitch\.tv)|youtu\.be|clips\.twitch\.tv)/\S+",
        RegexOptions.IgnoreCase,
        matchTimeoutMilliseconds: 250
    )]
    private static partial Regex LinkPattern();
}
