// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using NomNomzBot.Infrastructure.Music;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Who asked for the track that is playing — the field <c>NowPlaying.RequestedBy</c> exposes and, until now,
/// never carried (it was hardcoded null).
///
/// <para>
/// The whole risk is attributing a track to the wrong person. Exactly one request is ever in flight to the
/// provider, but the provider moves on by itself the moment a track ends, so the in-flight entry outlives the
/// track it describes. Reading it without checking WHICH track is playing credits the last requester for
/// every song that follows — including the streamer's own playlist. That is also what decides whether
/// <c>!wrongsong</c> is allowed to skip.
/// </para>
/// </summary>
public sealed class NowPlayingRequesterTests
{
    private const string Channel = "0192a000-0000-7000-8000-0000000ac901";

    private static SongRequestEntry Entry(string uri, string requestedBy) =>
        new(uri, "Track", "Artist", null, 100_000, requestedBy, 0, null, "");

    private static ISongRequestQueueStore StoreWithInFlight(SongRequestEntry? entry)
    {
        SongRequestQueueStore store = new();
        store.SetInFlight(Channel, entry);
        return store;
    }

    [Fact]
    public void The_requester_is_returned_while_their_own_track_is_the_one_playing()
    {
        ISongRequestQueueStore store = StoreWithInFlight(Entry("spotify:track:abc", "Bamo"));

        MusicService
            .RequesterOfPlayingTrack(store, Channel, "spotify:track:abc")
            .Should()
            .Be("Bamo");
    }

    [Fact]
    public void The_requester_is_dropped_once_the_provider_has_moved_on_to_another_track()
    {
        // The bug this guards: the in-flight entry is still there after the requested track finished, so
        // trusting it blindly would put Bamo's name on the streamer's next three songs.
        ISongRequestQueueStore store = StoreWithInFlight(Entry("spotify:track:abc", "Bamo"));

        MusicService
            .RequesterOfPlayingTrack(store, Channel, "spotify:track:something-else")
            .Should()
            .BeNull();
    }

    [Fact]
    public void Nothing_in_flight_means_nobody_requested_what_is_playing()
    {
        ISongRequestQueueStore store = StoreWithInFlight(null);

        MusicService.RequesterOfPlayingTrack(store, Channel, "spotify:track:abc").Should().BeNull();
    }

    [Fact]
    public void A_provider_that_reports_no_track_uri_attributes_nothing()
    {
        // Without a uri there is no way to tell whether the in-flight request is what is playing. Guessing
        // yes would attribute by nothing more than timing.
        ISongRequestQueueStore store = StoreWithInFlight(Entry("spotify:track:abc", "Bamo"));

        MusicService.RequesterOfPlayingTrack(store, Channel, null).Should().BeNull();
        MusicService.RequesterOfPlayingTrack(store, Channel, "").Should().BeNull();
    }

    [Fact]
    public void One_channels_in_flight_request_never_attributes_another_channels_track()
    {
        // The store is keyed per channel; a lookup for a channel that has nothing in flight must not see a
        // different channel's requester.
        ISongRequestQueueStore store = StoreWithInFlight(Entry("spotify:track:abc", "Bamo"));

        MusicService
            .RequesterOfPlayingTrack(
                store,
                "0192a000-0000-7000-8000-0000000ac902",
                "spotify:track:abc"
            )
            .Should()
            .BeNull();
    }
}
