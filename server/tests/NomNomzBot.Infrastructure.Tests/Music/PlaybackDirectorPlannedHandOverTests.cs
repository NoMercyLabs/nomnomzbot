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
/// Stoney, 2026-11-10: the video must be audible before the switch, and Spotify's next song must never be
/// heard. An engine with a hand-over lead (Spotify's 3 second crossfade) is handed over ahead of its track
/// end: the video starts first, Spotify is cut when the video plays or at the last safe moment, and it moves
/// on to its next song when it gets the music back.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    /// <summary>A 200 second song. The plan starts at 7.5 s left (3 s lead + 1.5 s margin + 3 s to start).</summary>
    private static async Task<(Rig Rig, PlaybackItem Video)> SpotifyNearsItsEndUnderAVideo()
    {
        Rig rig = new();
        rig.Spotify.HandOverLeadMs = 3_000;
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem video = Item("yt1", EngineKind.YouTube);
        await rig.Request(video);
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 192_000);

        rig.YouTube.Calls.Should().BeEmpty("8 seconds left is before the plan");
        rig.Spotify.Calls.Should().BeEmpty();

        await rig.OwnerPlays(rig.Spotify, OwnTrack, 193_000);

        return (rig, video);
    }

    [Fact]
    public async Task PlannedHandOver_TheVideoStartsAheadOfTheEndAndSpotifyIsCutOnlyWhenTheVideoPlays()
    {
        (Rig rig, PlaybackItem video) = await SpotifyNearsItsEndUnderAVideo();

        rig.YouTube.Calls.Should().Equal(new EngineCall("Start", "yt1"));
        rig.Spotify.Calls.Should().BeEmpty("Spotify plays on while the video loads");
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, video));

        await rig.OwnerPlays(rig.Spotify, OwnTrack, 194_000);

        rig.Spotify.Calls.Should().BeEmpty("the same song still playing is expected");

        await rig.Plays(rig.YouTube, video);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
        rig.QueueIds.Should().BeEmpty();

        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, video.Uri));

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"), new EngineCall("Skip"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        Trail(rig)
            .Should()
            .ContainInOrder(
                "planned_handover yt1",
                "planned_cut yt1",
                "started yt1",
                "spotify_skipped_to_request"
            );
    }

    [Fact]
    public async Task PlannedHandOver_AVideoThatLoadsSlowlyStillCutsSpotifyBeforeItsNextSongIsAudible()
    {
        (Rig rig, PlaybackItem video) = await SpotifyNearsItsEndUnderAVideo();

        rig.Clock.Advance(TimeSpan.FromMilliseconds(2_400));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().BeEmpty("4.6 seconds are left, the cut is at 4.5");

        rig.Clock.Advance(TimeSpan.FromMilliseconds(200));
        await rig.Director.CheckDeadlinesAsync();

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, video));

        await rig.Plays(rig.YouTube, video);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task PlannedHandOver_AVideoThatIsRefusedNeverTouchesSpotify()
    {
        Rig rig = new();
        rig.Spotify.HandOverLeadMs = 3_000;
        rig.YouTube.StartResult = EngineStartResult.Refused("player error");
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        await rig.Request(Item("yt1", EngineKind.YouTube));

        await rig.OwnerPlays(rig.Spotify, OwnTrack, 193_000);

        rig.YouTube.Calls.Should().Contain(new EngineCall("Start", "yt1"));
        rig.Spotify.Calls.Should().BeEmpty("Spotify was never paused, so it owes no resume");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task PlannedHandOver_SpotifyThatMovedOnEarlyIsCutAtOnceAndResumesThatSongLater()
    {
        (Rig rig, PlaybackItem video) = await SpotifyNearsItsEndUnderAVideo();

        await rig.OwnerPlays(rig.Spotify, OwnNextTrack, 300);

        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"), new EngineCall("Seek", "0"));

        await rig.Plays(rig.YouTube, video);
        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, video.Uri));

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Stop"), new EngineCall("Seek", "0"), new EngineCall("Resume"));
    }

    [Fact]
    public async Task PlannedHandOver_AnEngineWithNoLeadKeepsTheHandOverAtItsTrackEnd()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        await rig.Request(Item("yt1", EngineKind.YouTube));

        await rig.OwnerPlays(rig.Spotify, OwnTrack, 199_000);

        rig.YouTube.Calls.Should().BeEmpty();
        rig.Spotify.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }
}
