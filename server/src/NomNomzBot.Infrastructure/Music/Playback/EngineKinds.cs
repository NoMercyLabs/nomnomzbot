// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.ValueObjects;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>Which engine plays a track: a spotify: URI is Spotify, anything else is the YouTube overlay player.</summary>
public static class EngineKinds
{
    public static EngineKind ForTrack(string? provider, string trackUri)
    {
        if (string.Equals(provider, "youtube", StringComparison.OrdinalIgnoreCase))
            return EngineKind.YouTube;

        if (string.Equals(provider, "spotify", StringComparison.OrdinalIgnoreCase))
            return EngineKind.Spotify;

        return trackUri.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase)
            ? EngineKind.Spotify
            : EngineKind.YouTube;
    }
}
