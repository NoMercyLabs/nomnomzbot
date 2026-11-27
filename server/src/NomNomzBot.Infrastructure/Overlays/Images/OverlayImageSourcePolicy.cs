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

namespace NomNomzBot.Infrastructure.Overlays.Images;

/// <summary>
/// Decides which URLs the image optimizer may fetch. The list is short on purpose: an anonymous endpoint that
/// fetches a URL for a caller is a proxy, so only the hosts overlays really load images from are allowed, and only
/// over HTTPS with no credentials and no custom port. The same check runs on every redirect target.
/// </summary>
internal static class OverlayImageSourcePolicy
{
    private const int MaxUrlLength = 2048;

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "static-cdn.jtvnw.net",
        "cdn.7tv.app",
        "cdn.betterttv.net",
        "cdn.frankerfacez.com",
        "i.scdn.co",
        "i.ytimg.com",
        "cdn.discordapp.com",
        "media.tenor.com",
        "media.giphy.com",
        "media0.giphy.com",
        "media1.giphy.com",
        "media2.giphy.com",
        "media3.giphy.com",
        "media4.giphy.com",
    };

    private const string TwemojiHost = "cdn.jsdelivr.net";

    // Twemoji URLs carry the release in the package segment ("twemoji@15.1.0"), so the segment ends at '/' or '@'.
    private static readonly string[] TwemojiPrefixes =
    [
        "/gh/jdecked/twemoji/",
        "/gh/jdecked/twemoji@",
    ];

    /// <summary>The URL when it may be fetched; null when it is refused.</summary>
    public static Uri? Allow(string? raw, string? ownOrigin)
    {
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxUrlLength)
            return null;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
            return null;
        if (uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || !uri.IsDefaultPort)
            return null;
        if (Uri.UnescapeDataString(uri.AbsolutePath).Contains("..", StringComparison.Ordinal))
            return null;

        return IsAllowedHost(uri) || IsOwnOrigin(uri, ownOrigin) ? uri : null;
    }

    private static bool IsAllowedHost(Uri uri)
    {
        if (AllowedHosts.Contains(uri.Host))
            return true;

        return uri.Host.Equals(TwemojiHost, StringComparison.OrdinalIgnoreCase)
            && TwemojiPrefixes.Any(prefix =>
                uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
            );
    }

    private static bool IsOwnOrigin(Uri uri, string? ownOrigin) =>
        Uri.TryCreate(ownOrigin, UriKind.Absolute, out Uri? own)
        && own.Scheme == Uri.UriSchemeHttps
        && own.Host.Equals(uri.Host, StringComparison.OrdinalIgnoreCase)
        && own.Port == uri.Port;
}
