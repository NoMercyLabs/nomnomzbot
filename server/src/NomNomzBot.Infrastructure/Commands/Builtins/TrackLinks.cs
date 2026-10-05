// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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
