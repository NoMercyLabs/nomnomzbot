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
