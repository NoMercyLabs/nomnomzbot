// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Domain.Music.Interfaces;

namespace NomNomzBot.Infrastructure.Music.Playback;

/// <summary>Builds the Spotify and YouTube engines of a channel over the registered providers.</summary>
public sealed class PlaybackEngineFactory(
    IEnumerable<IMusicProvider> providers,
    IYouTubePlayerDispatcher dispatcher,
    IYouTubePlayerStateStore store
) : IPlaybackEngineFactory
{
    private const string SpotifyKey = "spotify";
    private const string YouTubeKey = "youtube";

    public IReadOnlyList<IPlaybackEngine> Create(Guid broadcasterId)
    {
        IMusicProvider spotify = Provider(SpotifyKey);
        IMusicProvider youTube = Provider(YouTubeKey);

        return
        [
            new SpotifyPlaybackEngine(
                broadcasterId,
                spotify,
                (IMusicProviderStartTrack)spotify,
                (IMusicProviderConnectionGate)spotify
            ),
            new YouTubePlaybackEngine(
                broadcasterId,
                youTube,
                (IMusicProviderPlayNow)youTube,
                (IMusicProviderPlayerGate)youTube,
                dispatcher,
                store
            ),
        ];
    }

    private IMusicProvider Provider(string key) =>
        providers.Single(provider => provider.Provider == key);
}
