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

namespace NomNomzBot.Infrastructure.Commands.Builtins;

/// <summary>
/// Turns a provider track uri into a link a chat client can open. YouTube's uri is already a real
/// <c>https://</c> watch URL; Spotify's is the internal <c>spotify:track:&lt;id&gt;</c> scheme, which chat clients
/// cannot open. Any other provider that already hands back a real link passes through unchanged.
/// </summary>
internal static class TrackLinks
{
    private const string SpotifyUriPrefix = "spotify:track:";

    public static string ToWebLink(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return string.Empty;

        return uri.StartsWith(SpotifyUriPrefix, StringComparison.Ordinal)
            ? $"https://open.spotify.com/track/{uri[SpotifyUriPrefix.Length..]}"
            : uri;
    }
}
