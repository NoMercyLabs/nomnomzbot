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

namespace NomNomzBot.Infrastructure.Platform.Transport.LivePlaylist;

/// <summary>
/// Rewrites one HLS playlist for the proxy. A nested playlist (a URI line or a <c>URI="..."</c> attribute whose
/// path ends in <c>.m3u8</c>) becomes <c>../{encoded}/index.m3u8</c>, which the player resolves against the
/// proxy address it just read, so variant playlists come through the proxy too. Every other URI (a video
/// segment) is made absolute against the source, so it loads straight from Twitch's CDN.
/// </summary>
internal static partial class HlsPlaylistRewriter
{
    [GeneratedRegex("URI=\"([^\"]+)\"")]
    private static partial Regex UriAttribute();

    public static string Rewrite(string playlist, Uri source)
    {
        string[] lines = playlist.Split('\n');
        IEnumerable<string> rewritten = lines.Select(line => RewriteLine(line, source));
        return string.Join('\n', rewritten);
    }

    private static string RewriteLine(string line, Uri source)
    {
        string trimmed = line.TrimEnd('\r');
        if (trimmed.Length == 0)
            return trimmed;

        if (trimmed.StartsWith('#'))
            return UriAttribute()
                .Replace(trimmed, match => $"URI=\"{RewriteUri(match.Groups[1].Value, source)}\"");

        return RewriteUri(trimmed.Trim(), source);
    }

    private static string RewriteUri(string uri, Uri source)
    {
        if (!Uri.TryCreate(source, uri, out Uri? absolute))
            return uri;

        return absolute.AbsolutePath.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase)
            ? $"../{TwitchLivePlaylistAddress.Encode(absolute)}/index.m3u8"
            : absolute.AbsoluteUri;
    }
}
