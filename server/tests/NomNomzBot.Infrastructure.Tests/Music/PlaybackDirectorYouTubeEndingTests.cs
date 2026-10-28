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
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Stoney, 2026-10-19 night: the Music Player page sends ENDING at 3 seconds left (input <see cref="TrackEnding"/>)
/// and Spotify resumes then, unless another video follows (music-playback-determinism.md, scenario rows 31-34
/// and 36). The helpers live in PlaybackDirectorOwnerHandOverTests.cs.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task YouTubeEnding_AVideoAheadOfASpotifyRequestTakesOverAtSpotifysNextSongAndResumesOnThatRequest()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem yt1 = Item("yt1", EngineKind.YouTube);
        PlaybackItem sp1 = Item("sp1");
        await rig.Request(yt1);
        await rig.Request(sp1);

        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "sp1"));

        await rig.OwnerPlays(rig.Spotify, OwnTrack, 197_000);

        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "sp1"));
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));

        await SpotifyMovesOn(rig, OwnTrack, sp1.Uri);
        await YouTubeTakesOver(rig, sp1.Uri, yt1);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("Stop"),
                new EngineCall("Seek", "0")
            );
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.QueueIds.Should().Equal("sp1");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, yt1));

        await EndsWithSpotifyBack(rig, yt1, sp1);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("Stop"),
                new EngineCall("Seek", "0"),
                new EngineCall("Resume")
            );
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.QueueIds.Should().BeEmpty();
        rig.Director.LastPlayed!.Id.Should().Be("sp1");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, sp1));
        Trail(rig)
            .Should()
            .ContainInOrder(
                "lined_up sp1",
                "youtube_takeover yt1",
                "started yt1",
                "spotify_resumed",
                "played_externally sp1",
                "youtube_finished yt1"
            );
    }

    [Fact]
    public async Task YouTubeEnding_ThreeVideosPlayBackToBackWhileSpotifyWaitsPausedOnTheLinedUpRequest()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem yt1 = Item("yt1", EngineKind.YouTube);
        PlaybackItem yt2 = Item("yt2", EngineKind.YouTube);
        PlaybackItem yt3 = Item("yt3", EngineKind.YouTube);
        PlaybackItem sp1 = Item("sp1");
        foreach (PlaybackItem item in new[] { yt1, yt2, yt3, sp1 })
            await rig.Request(item);

        rig.QueueIds.Should().Equal("yt1", "yt2", "yt3", "sp1");
        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "sp1"));

        await SpotifyMovesOn(rig, OwnTrack, sp1.Uri);
        await YouTubeTakesOver(rig, sp1.Uri, yt1);
        await NextVideoAtEnded(rig, yt1, yt2, sp1.Uri);
        await NextVideoAtEnded(rig, yt2, yt3, sp1.Uri);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("Stop"),
                new EngineCall("Seek", "0")
            );
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, yt3));
        rig.QueueIds.Should().Equal("sp1");

        await EndsWithSpotifyBack(rig, yt3, sp1);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("Stop"),
                new EngineCall("Seek", "0"),
                new EngineCall("Resume")
            );
        rig.YouTube.Calls.Should()
            .Equal(
                new EngineCall("Start", "yt1"),
                new EngineCall("Start", "yt2"),
                new EngineCall("Start", "yt3")
            );
        rig.QueueIds.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, sp1));
        Trail(rig)
            .Should()
            .ContainInOrder(
                "youtube_takeover yt1",
                "next_item yt2",
                "next_item yt3",
                "spotify_resumed",
                "played_externally sp1"
            );
    }

    [Fact]
    public async Task YouTubeEnding_SpotifyRequestsAroundAVideoEachPlayFromSpotifysOwnQueue()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem sp1 = Item("sp1");
        PlaybackItem yt1 = Item("yt1", EngineKind.YouTube);
        PlaybackItem sp2 = Item("sp2");
        foreach (PlaybackItem item in new[] { sp1, yt1, sp2 })
            await rig.Request(item);

        rig.QueueIds.Should().Equal("sp1", "yt1", "sp2");
        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "sp1"));

        await SpotifyMovesOn(rig, OwnTrack, sp1.Uri);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, sp1));
        rig.QueueIds.Should().Equal("yt1", "sp2");
        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("QueueNext", "sp1"), new EngineCall("QueueNext", "sp2"));

        await SpotifyMovesOn(rig, sp1.Uri, sp2.Uri);
        await YouTubeTakesOver(rig, sp2.Uri, yt1);

        rig.QueueIds.Should().Equal("sp2");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, yt1));

        await EndsWithSpotifyBack(rig, yt1, sp2);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("QueueNext", "sp2"),
                new EngineCall("Stop"),
                new EngineCall("Seek", "0"),
                new EngineCall("Resume")
            );
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.QueueIds.Should().BeEmpty();
        rig.Director.LastPlayed!.Id.Should().Be("sp2");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, sp2));
        Trail(rig)
            .Should()
            .ContainInOrder(
                "lined_up sp1",
                "started sp1",
                "lined_up sp2",
                "youtube_takeover yt1",
                "spotify_resumed",
                "played_externally sp2"
            );
    }

    [Fact]
    public async Task YouTubeEnding_ASpotifyRequestMadeDuringAVideoIsLinedUpAndSpotifySkipsToItAtTheEnding()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem yt1 = Item("yt1", EngineKind.YouTube);
        await rig.Request(yt1);
        await SpotifyMovesOn(rig, OwnTrack, OwnNextTrack);
        await YouTubeTakesOver(rig, OwnNextTrack, yt1);
        PlaybackItem sp1 = Item("sp1");

        await rig.Request(sp1);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("Stop"),
                new EngineCall("Seek", "0"),
                new EngineCall("QueueNext", "sp1")
            );

        await EndsWithSpotifyBack(rig, yt1, sp1);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("Stop"),
                new EngineCall("Seek", "0"),
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("Skip")
            );
        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.QueueIds.Should().BeEmpty();
        rig.Director.LastPlayed!.Id.Should().Be("sp1");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, sp1));
        Trail(rig)
            .Should()
            .ContainInOrder(
                "youtube_takeover yt1",
                "lined_up sp1",
                "spotify_skipped_to_request",
                "played_externally sp1",
                "youtube_finished yt1"
            );
    }

    [Fact]
    public async Task YouTubeEnding_OnlyYouTubeRequestsPlayInTurnAtEachEndAndSpotifyGetsNoCommand()
    {
        Rig rig = new();
        PlaybackItem yt1 = Item("yt1", EngineKind.YouTube);
        PlaybackItem yt2 = Item("yt2", EngineKind.YouTube);
        await rig.Request(yt1);
        await rig.Request(yt2);
        await rig.Plays(rig.YouTube, yt1);

        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, yt1.Uri));

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, yt1));

        await rig.Ends(rig.YouTube, yt1);
        await rig.Plays(rig.YouTube, yt2);
        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, yt2.Uri));
        await rig.Ends(rig.YouTube, yt2);

        rig.Spotify.Calls.Should().BeEmpty();
        rig.YouTube.Calls.Should()
            .Equal(new EngineCall("Start", "yt1"), new EngineCall("Start", "yt2"));
        rig.QueueIds.Should().BeEmpty();
        rig.Director.LastPlayed!.Id.Should().Be("yt2");
        rig.Director.State.Should().Be(PlaybackState.Idle);
        Trail(rig)
            .Should()
            .ContainInOrder(
                "start_head yt1",
                "started yt1",
                "next_item yt2",
                "started yt2",
                "queue_empty"
            );
    }

    /// <summary>The video ends while the queue head is on Spotify: ENDING, its last seconds, Spotify plays, then ENDED.</summary>
    private static async Task EndsWithSpotifyBack(
        Rig rig,
        PlaybackItem video,
        PlaybackItem spotifyRow
    )
    {
        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, video.Uri));
        await rig.Plays(rig.YouTube, video);
        await rig.Plays(rig.Spotify, spotifyRow);
        await rig.Ends(rig.YouTube, video);
    }

    /// <summary>Another video follows: its ENDING leaves Spotify paused, its ENDED starts the next video.</summary>
    private static async Task NextVideoAtEnded(
        Rig rig,
        PlaybackItem video,
        PlaybackItem next,
        string pausedSpotifyUri
    )
    {
        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, video.Uri));
        await rig.Paused(rig.Spotify, pausedSpotifyUri, 0);
        await rig.Ends(rig.YouTube, video);
        await rig.Plays(rig.YouTube, next);
    }
}
