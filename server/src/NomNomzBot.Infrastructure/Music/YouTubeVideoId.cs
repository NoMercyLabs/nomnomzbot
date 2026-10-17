// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Reads the 11-character video id out of whatever YouTube link shape chat pastes: watch, short,
/// embed, live, music/m./gaming. hosts, no-cookie embeds, attribution links, with or without a scheme.
/// </summary>
internal static class YouTubeVideoId
{
    private static readonly string[] VideoPathPrefixes =
    [
        "shorts",
        "embed",
        "v",
        "e",
        "live",
        "watch",
    ];

    /// <summary>The 11-char video id in a bare id or a YouTube link; null when there is none.</summary>
    public static string? Extract(string uriOrId)
    {
        if (string.IsNullOrWhiteSpace(uriOrId))
            return null;

        string value = ChatLinkText.Normalize(uriOrId);

        if (IsVideoId(value))
            return value;

        Uri? url = YouTubeUrl(value);
        if (url is null)
            return null;

        if (IsShortHost(url))
        {
            string shortId = url.AbsolutePath.Trim('/');
            return IsVideoId(shortId) ? shortId : null;
        }

        string? watchId = QueryValue(url.Query, "v");
        if (watchId is not null && IsVideoId(watchId))
            return watchId;

        // attribution_link?u=/watch?v=<id>&feature=share: the real target is the encoded "u" value.
        string? target = QueryValue(url.Query, "u");
        if (target is not null && target.StartsWith('/'))
            return Extract($"https://www.youtube.com{target}");

        string[] segments = url.AbsolutePath.Trim('/').Split('/');
        if (
            segments.Length == 2
            && VideoPathPrefixes.Contains(segments[0], StringComparer.OrdinalIgnoreCase)
            && IsVideoId(segments[1])
        )
            return segments[1];

        return null;
    }

    /// <summary>The absolute URL when it is on a YouTube host; else null.</summary>
    public static Uri? YouTubeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url))
            return null;
        if (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps)
            return null;

        string host = url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? url.Host[4..]
            : url.Host;
        bool isYouTubeHost =
            string.Equals(host, "youtu.be", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "youtube.com", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "youtube-nocookie.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".youtube.com", StringComparison.OrdinalIgnoreCase);
        return isYouTubeHost ? url : null;
    }

    public static bool IsShortHost(Uri url) =>
        url.Host.EndsWith("youtu.be", StringComparison.OrdinalIgnoreCase);

    public static bool IsVideoId(string value) =>
        value.Length == 11 && value.All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    /// <summary>Reads a single query-string value (leading '?' tolerated) without an ASP.NET dependency.</summary>
    private static string? QueryValue(string query, string key)
    {
        foreach (
            string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
        )
        {
            int equals = pair.IndexOf('=');
            if (equals <= 0)
                continue;

            if (string.Equals(pair[..equals], key, StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(pair[(equals + 1)..]);
        }

        return null;
    }
}
