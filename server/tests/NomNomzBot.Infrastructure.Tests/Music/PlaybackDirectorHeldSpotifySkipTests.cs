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
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A skip never leaves Spotify paused for good (Stoney, 2026-11-09: "i NEED it to not break on me while i
/// stream"). A song already in Spotify's own queue gets no start command, so a held Spotify needs its own.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    private const string HeldOwnTrack = "spotify:track:made-up-own";

    /// <summary>Spotify is held for a playing video, with the Spotify request behind it lined up.</summary>
    private static async Task<(Rig Rig, PlaybackItem Next)> VideoHoldsSpotifyWithALinedUpSong(
        Func<Rig, PlaybackItem, Task> spotifyMovesOn,
        Func<PlaybackItem, string> pausedUri
    )
    {
        Rig rig = new();
        PlaybackItem video = Item("yt1", EngineKind.YouTube);
        PlaybackItem next = Item("sp1");
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, HeldOwnTrack, true, 10_000, 200_000)
        );
        await rig.Request(video);
        await rig.Request(next);

        await spotifyMovesOn(rig, next);
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, pausedUri(next), false, 0, 200_000)
        );
        await rig.Plays(rig.YouTube, video);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));

        return (rig, next);
    }

    private static Task OwnSongEnds(Rig rig, PlaybackItem _) =>
        rig.Spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, HeldOwnTrack));

    private static Task SpotifyReachesTheSong(Rig rig, PlaybackItem next) =>
        rig.Spotify.EmitAsync(new EngineReport(EngineKind.Spotify, next.Uri, true, 0, 200_000));

    [Fact]
    public async Task Skip_OfAVideoWithALinedUpSongBehindIt_SkipsSpotifyToThatSong()
    {
        (Rig rig, PlaybackItem next) = await VideoHoldsSpotifyWithALinedUpSong(
            OwnSongEnds,
            _ => HeldOwnTrack
        );

        await rig.Director.HandleAsync(new OwnerSkip());
        await rig.YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, "YouTube:yt1"));

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("Stop"),
                new EngineCall("Seek", "0"),
                new EngineCall("Skip")
            );
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, next));

        await rig.Plays(rig.Spotify, next);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, next));
        rig.QueueIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Skip_OfAVideoThatPausedTheLinedUpSongItself_ResumesSpotifyOnThatSong()
    {
        (Rig rig, PlaybackItem next) = await VideoHoldsSpotifyWithALinedUpSong(
            SpotifyReachesTheSong,
            song => song.Uri
        );

        await rig.Director.HandleAsync(new OwnerSkip());
        await rig.YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, "YouTube:yt1"));

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "sp1"),
                new EngineCall("Stop"),
                new EngineCall("Seek", "0"),
                new EngineCall("Resume")
            );
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, next));
    }

    [Fact]
    public async Task Skip_OfAVideo_EndsTheHold_SoAPausedSpotifyGetsNoLineUp()
    {
        (Rig rig, PlaybackItem next) = await VideoHoldsSpotifyWithALinedUpSong(
            OwnSongEnds,
            _ => HeldOwnTrack
        );
        await rig.Director.HandleAsync(new OwnerSkip());
        await rig.YouTube.EmitAsync(new TrackEnded(EngineKind.YouTube, "YouTube:yt1"));
        await rig.Plays(rig.Spotify, next);
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, next.Uri, false, 2_000, 200_000)
        );

        await rig.Request(Item("sp2"));

        rig.Spotify.Calls.Should().NotContain(new EngineCall("QueueNext", "sp2"));
    }

    [Fact]
    public async Task Starting_WhenSpotifyStaysPausedWithTheSongInItsQueue_ResumesWithin20Seconds()
    {
        Rig rig = new();
        PlaybackItem song = Item("a");
        await rig.Request(song);
        rig.Spotify.Held.Add("a");
        EngineReport paused = new(EngineKind.Spotify, HeldOwnTrack, false, 5_000, 200_000);

        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Spotify.EmitAsync(paused);
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Spotify.EmitAsync(paused);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Resume"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("a");
        rig.Journal.Should().NotContain(e => e.Reason == "start_timeout");
        rig.Bus.Published.Should().NotContain(e => e is SongPlaybackFailedEvent);
    }

    [Fact]
    public async Task Starting_AfterSpotifyResumedOnItsOwnQueue_TheWaitingSongStillPlays()
    {
        Rig rig = new();
        PlaybackItem song = Item("a");
        await rig.Request(song);
        rig.Spotify.Held.Add("a");
        EngineReport paused = new(EngineKind.Spotify, HeldOwnTrack, false, 5_000, 200_000);
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Spotify.EmitAsync(paused);
        rig.Clock.Advance(TimeSpan.FromSeconds(10));
        await rig.Spotify.EmitAsync(paused);

        await rig.Plays(rig.Spotify, song);

        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, song));
        rig.QueueIds.Should().BeEmpty();
        rig.Spotify.Calls.Count(c => c.Name == "Start").Should().Be(1);
    }
}
