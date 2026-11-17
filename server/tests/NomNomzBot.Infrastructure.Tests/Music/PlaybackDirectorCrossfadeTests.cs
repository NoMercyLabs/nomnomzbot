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
/// Stoney, 2026-11-17 (Bamo16's ask): a crossfade between Spotify and YouTube requests, both ways, over the
/// channel's 3 second crossfade. The video ramps in the page; Spotify takes whole-second volume steps; the
/// next Spotify song is never heard before a video ends; a fade that fails leaves today's hand-over.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    /// <summary>A 200 second song at 190 s: the crossfade plan starts at 10.5 s left (3 s fade + 3 s lead + 1.5 s margin + 3 s start).</summary>
    private static async Task<(Rig Rig, PlaybackItem Video)> SpotifyUnderAVideoWithACrossfade()
    {
        Rig rig = new();
        rig.Spotify.HandOverLeadMs = 3_000;
        rig.Spotify.CanCrossfade = true;
        rig.YouTube.CanCrossfade = true;
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem video = Item("yt1", EngineKind.YouTube);
        await rig.Request(video);
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 190_000);

        return (rig, video);
    }

    private static async Task Tick(Rig rig, int ms)
    {
        rig.Clock.Advance(TimeSpan.FromMilliseconds(ms));
        await rig.Director.CheckDeadlinesAsync();
    }

    [Fact]
    public async Task Crossfade_SpotifyToYouTube_TheVideoStartsSilentFadesInAndSpotifyStepsDownToTheCut()
    {
        (Rig rig, PlaybackItem video) = await SpotifyUnderAVideoWithACrossfade();

        rig.YouTube.Calls.Should()
            .Equal(new EngineCall("SetVolume", "0"), new EngineCall("Start", "yt1"));
        rig.Spotify.Calls.Should().BeEmpty("Spotify plays on at its level while the video loads");

        await rig.Plays(rig.YouTube, video);

        rig.YouTube.Calls.Should().EndWith(new EngineCall("Fade", "100/3000"));
        rig.Spotify.Calls.Should().BeEmpty("the fade has not taken its first second yet");

        await Tick(rig, 1_000);
        await Tick(rig, 1_000);
        rig.Spotify.Calls.Select(c => c.Name).Should().OnlyContain(n => n == "SetVolume");
        rig.Spotify.Calls.Select(c => c.Argument).Should().Equal("66", "33");

        await Tick(rig, 1_000);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("SetVolume", "66"),
                new EngineCall("SetVolume", "33"),
                new EngineCall("SetVolume", "0"),
                new EngineCall("Stop"),
                new EngineCall("SetVolume", "100")
            );
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
        rig.QueueIds.Should().BeEmpty();
    }

    [Fact]
    public async Task Crossfade_SpotifyToYouTube_SpotifyIsNeverAboveTheRampBeforeTheVideoIsAudible()
    {
        (Rig rig, PlaybackItem video) = await SpotifyUnderAVideoWithACrossfade();

        await Tick(rig, 1_000);
        await Tick(rig, 1_000);

        rig.Spotify.Calls.Should().BeEmpty("no Spotify step runs before the video plays");
        rig.YouTube.Calls.Where(c => c.Name == "SetVolume")
            .Should()
            .Equal(new EngineCall("SetVolume", "0"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task Crossfade_SpotifyToYouTube_ARefusedSpotifyStepFallsBackToTheCutAndRestoresTheVolume()
    {
        (Rig rig, PlaybackItem video) = await SpotifyUnderAVideoWithACrossfade();
        await rig.Plays(rig.YouTube, video);
        rig.Spotify.VolumeThrows = () =>
        {
            rig.Spotify.VolumeThrows = null;

            return new InvalidOperationException("429");
        };

        await Tick(rig, 1_000);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Stop"), new EngineCall("SetVolume", "100"));
        rig.YouTube.Calls.Should().EndWith(new EngineCall("SetVolume", "100"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task Crossfade_SpotifyToYouTube_AFailedPageFadeCutsSpotifyAtOnceAndRestoresTheVideoLevel()
    {
        (Rig rig, PlaybackItem video) = await SpotifyUnderAVideoWithACrossfade();
        rig.YouTube.FadeThrows = () => new InvalidOperationException("page not connected");

        await rig.Plays(rig.YouTube, video);

        rig.Spotify.Calls.Should().Contain(new EngineCall("Stop"));
        rig.Spotify.Calls.Where(c => c.Name == "SetVolume").Should().BeEmpty("Spotify never moved");
        rig.YouTube.Calls.Should().EndWith(new EngineCall("SetVolume", "100"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task Crossfade_SpotifyToYouTube_ARefusedSilentStartLeavesTheHandOverAsItWasWithNoFade()
    {
        Rig rig = new();
        rig.Spotify.HandOverLeadMs = 3_000;
        rig.Spotify.CanCrossfade = true;
        rig.YouTube.CanCrossfade = true;
        rig.YouTube.VolumeThrows = () => new InvalidOperationException("page not connected");
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 10_000);
        PlaybackItem video = Item("yt1", EngineKind.YouTube);
        await rig.Request(video);
        await rig.OwnerPlays(rig.Spotify, OwnTrack, 190_000);

        await rig.Plays(rig.YouTube, video);

        rig.YouTube.Calls.Should().NotContain(c => c.Name == "Fade");
        rig.Spotify.Calls.Should().Equal(new EngineCall("Stop"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.YouTube, video));
    }

    [Fact]
    public async Task Crossfade_YouTubeToSpotify_SpotifyResumesSilentRisesInStepsAndEndsAtTheOwnerLevel()
    {
        (Rig rig, PlaybackItem video) = await SpotifyUnderAVideoWithACrossfade();
        await rig.Plays(rig.YouTube, video);
        await Tick(rig, 3_000);
        int spotifyBefore = rig.Spotify.Calls.Count;
        int youTubeBefore = rig.YouTube.Calls.Count;

        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, video.Uri));

        rig.YouTube.Calls.Skip(youTubeBefore).Should().Equal(new EngineCall("Fade", "0/3000"));
        rig.Spotify.Calls.Skip(spotifyBefore)
            .Should()
            .Equal(new EngineCall("SetVolume", "0"), new EngineCall("Skip"));

        await Tick(rig, 1_000);
        await Tick(rig, 1_000);
        await Tick(rig, 1_000);

        rig.Spotify.Calls.Skip(spotifyBefore)
            .Should()
            .Equal(
                new EngineCall("SetVolume", "0"),
                new EngineCall("Skip"),
                new EngineCall("SetVolume", "33"),
                new EngineCall("SetVolume", "66"),
                new EngineCall("SetVolume", "100")
            );
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task Crossfade_YouTubeToSpotify_AFailedPageFadeRestoresSpotifyAndKeepsTheHandBack()
    {
        (Rig rig, PlaybackItem video) = await SpotifyUnderAVideoWithACrossfade();
        await rig.Plays(rig.YouTube, video);
        await Tick(rig, 3_000);
        int spotifyBefore = rig.Spotify.Calls.Count;
        rig.YouTube.FadeThrows = () => new InvalidOperationException("page not connected");

        await rig.YouTube.EmitAsync(new TrackEnding(EngineKind.YouTube, video.Uri));

        rig.Spotify.Calls.Skip(spotifyBefore)
            .Should()
            .Equal(
                new EngineCall("SetVolume", "0"),
                new EngineCall("SetVolume", "100"),
                new EngineCall("Skip")
            );
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }
}
