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

namespace NomNomzBot.Infrastructure.Music;

/// <summary>
/// Who asked for the track the provider is playing, or null when nobody did (the streamer started it
/// themselves, or it is whatever their playlist rolled onto next). The one place that answers it.
///
/// <para>
/// The playback director is the first source: it removes a queue row the moment it sees that row play, so
/// the item it holds is the request that is playing, however the request reached the player (started by the
/// director, or lined up in Spotify's own queue). The in-flight entry is the second source: it is only set
/// for the one request handed over while the channel was idle, so alone it credits nobody for the second
/// song onward. The request kept aside by the reconciler is the last.
/// </para>
///
/// <para>
/// Every source is matched on the track uri rather than trusted blindly. The provider moves on by itself the
/// moment a track ends, and without that check the last requester's name would stay stuck on every track that
/// followed, crediting them for songs they never asked for.
/// </para>
/// </summary>
internal static class PlayingRequester
{
    public static string? Of(
        ISongRequestQueueStore queueStore,
        PlaybackState directorState,
        string broadcasterId,
        string? playingTrackUri
    )
    {
        if (string.IsNullOrEmpty(playingTrackUri))
            return null;

        if (
            directorState.Item is { RequestedBy: { Length: > 0 } directorRequester } item
            && string.Equals(item.Uri, playingTrackUri, StringComparison.Ordinal)
        )
            return directorRequester;

        SongRequestEntry? inFlight = queueStore.GetInFlight(broadcasterId);
        if (string.Equals(inFlight?.TrackUri, playingTrackUri, StringComparison.Ordinal))
            return inFlight?.RequestedBy;

        SongRequestEntry? playing = queueStore.GetPlayingRequest(broadcasterId);

        return string.Equals(playing?.TrackUri, playingTrackUri, StringComparison.Ordinal)
            ? playing?.RequestedBy
            : null;
    }
}
