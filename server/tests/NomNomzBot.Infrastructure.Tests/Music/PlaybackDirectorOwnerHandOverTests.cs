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

using FluentAssertions;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Stoney, 2026-10-19 night: Spotify keeps its own queue plus ONE lined-up request; a YouTube request takes over
/// when Spotify's next song starts, and Spotify gets the music back when the video ends
/// (music-playback-determinism.md, "Changed 2026-10-19 night", scenario rows 35 and 37; rows 31-34 and 36 are in
/// PlaybackDirectorYouTubeEndingTests.cs). The fake Spotify records the director's stop as "Stop": the real
/// Spotify engine pauses on it (SpotifyPlaybackEngine.StopAsync).
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    private const string OwnTrack = "spotify:track:own1";
    private const string OwnNextTrack = "spotify:track:own2";

    /// <summary>
    /// The song playing belongs to bob, so bob's next request ranks second, and it is already lined up in
    /// Spotify's own queue when stoney's first request takes the top of the fair queue.
    /// </summary>
    private static async Task<(
        Rig Rig,
        PlaybackItem LinedUp,
        PlaybackItem NewHead
    )> FairInsertAheadOfLinedUp()
    {
        FairQueue<PlaybackItem> fairQueue = new();
        fairQueue.SetNowPlayingOwner("bob");
        Rig rig = new(fairQueue);
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem linedUp = Item("bob2", owner: "bob");
        PlaybackItem newHead = Item("stoney1", owner: "stoney");
        await rig.Request(linedUp);
        await rig.Request(newHead);

        rig.QueueIds.Should().Equal("stoney1", "bob2");
        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "bob2"));
        return (rig, linedUp, newHead);
    }

    [Fact]
    public async Task OwnerHandOver_TrackEnd_TheFairHeadPlaysFirstAndTheLinedUpSongKeepsItsPlace()
    {
        (Rig rig, PlaybackItem linedUp, PlaybackItem newHead) = await FairInsertAheadOfLinedUp();

        await SpotifyMovesOn(rig, OwnTrack, linedUp.Uri);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("QueueNext", "bob2"), new EngineCall("Start", "stoney1"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, newHead));
        rig.QueueIds.Should().Equal("stoney1", "bob2");

        // The next poll can still name the displaced song before Spotify has moved on.
        await rig.OwnerPlays(rig.Spotify, linedUp.Uri, 3_000);
        rig.QueueIds.Should().Equal("stoney1", "bob2");

        await rig.OwnerPlays(rig.Spotify, newHead.Uri, 1_000);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, newHead));
        rig.QueueIds.Should().Equal("bob2");
        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "bob2"),
                new EngineCall("Start", "stoney1"),
                new EngineCall("QueueNext", "bob2")
            );

        await SpotifyMovesOn(rig, newHead.Uri, linedUp.Uri);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, linedUp));
        rig.QueueIds.Should().BeEmpty();
    }

    [Fact]
    public async Task A_displaced_song_counts_as_played_only_when_it_really_plays()
    {
        (Rig rig, PlaybackItem linedUp, PlaybackItem newHead) = await FairInsertAheadOfLinedUp();

        await SpotifyMovesOn(rig, OwnTrack, linedUp.Uri);

        rig.Bus.Published.OfType<SongRequestPlayedEvent>().Should().BeEmpty();

        await rig.OwnerPlays(rig.Spotify, newHead.Uri, 1_000);
        await SpotifyMovesOn(rig, newHead.Uri, linedUp.Uri);

        List<SongRequestPlayedEvent> played =
        [
            .. rig.Bus.Published.OfType<SongRequestPlayedEvent>(),
        ];
        played.Select(e => e.ItemId).Should().Equal("stoney1", "bob2");
        played[1].TrackUri.Should().Be(linedUp.Uri);
        played[1].BroadcasterId.Should().Be(Channel);
    }

    [Fact]
    public async Task OwnerHandOver_OwnerSkip_MovesOntoTheLinedUpSongThenStartsTheFairHead()
    {
        (Rig rig, PlaybackItem linedUp, PlaybackItem newHead) = await FairInsertAheadOfLinedUp();

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("QueueNext", "bob2"), new EngineCall("Skip"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, linedUp));

        await rig.OwnerPlays(rig.Spotify, linedUp.Uri, 1_000);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "bob2"),
                new EngineCall("Skip"),
                new EngineCall("Start", "stoney1")
            );
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, newHead));
        rig.QueueIds.Should().Equal("stoney1", "bob2");
    }

    [Fact]
    public async Task OwnerHandOver_OnlySpotify_LinesUpOneRequestAtATimeAndSendsNoPauseSkipOrStart()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem sp1 = Item("sp1");
        PlaybackItem sp2 = Item("sp2");
        await rig.Request(sp1);
        await rig.Request(sp2);

        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "sp1"));

        await SpotifyMovesOn(rig, OwnTrack, sp1.Uri);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, sp1));
        rig.QueueIds.Should().Equal("sp2");
        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("QueueNext", "sp1"), new EngineCall("QueueNext", "sp2"));

        await SpotifyMovesOn(rig, sp1.Uri, sp2.Uri);
        await SpotifyMovesOn(rig, sp2.Uri, OwnNextTrack);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("QueueNext", "sp1"), new EngineCall("QueueNext", "sp2"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.QueueIds.Should().BeEmpty();
        rig.Director.LastPlayed!.Id.Should().Be("sp2");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        Trail(rig)
            .Should()
            .ContainInOrder("lined_up sp1", "started sp1", "lined_up sp2", "started sp2");
    }

    [Fact]
    public async Task OwnerHandOver_NoEndingSignal_SpotifyResumesWhenTheVideoEnds()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem yt1 = Item("yt1", EngineKind.YouTube);
        await rig.Request(yt1);

        rig.Spotify.Calls.Should().BeEmpty();

        await SpotifyMovesOn(rig, OwnTrack, OwnNextTrack);
        await YouTubeTakesOver(rig, OwnNextTrack, yt1);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, yt1));
        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"), new EngineCall("Seek", "0"));

        await rig.Ends(rig.YouTube, yt1);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Stop"), new EngineCall("Seek", "0"), new EngineCall("Resume"));
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.QueueIds.Should().BeEmpty();
        rig.Director.LastPlayed!.Id.Should().Be("yt1");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        Trail(rig)
            .Should()
            .ContainInOrder("youtube_takeover yt1", "started yt1", "spotify_resumed");
    }

    /// <summary>Spotify's poll at a song boundary: the old track ended, then the next one plays (SpotifyPlaybackEngine.ObserveAsync).</summary>
    private static async Task SpotifyMovesOn(Rig rig, string endedUri, string nextUri)
    {
        await rig.Spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, endedUri));
        await rig.OwnerPlays(rig.Spotify, nextUri, 1_000);
    }

    /// <summary>Spotify confirms the takeover's pause at the start of its next song, then the video plays.</summary>
    private static async Task YouTubeTakesOver(Rig rig, string pausedSpotifyUri, PlaybackItem video)
    {
        await rig.Paused(rig.Spotify, pausedSpotifyUri, 0);
        await rig.Plays(rig.YouTube, video);
    }

    /// <summary>The journal as "reason item" lines, in order.</summary>
    private static List<string> Trail(Rig rig) =>
        [.. rig.Journal.Select(e => $"{e.Reason} {e.QueueItemId}".TrimEnd())];
}
