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
using NomNomzBot.Domain.Music.ValueObjects;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The music is never left silent (audit 2026-10-19). A skip on Spotify moves Spotify's own queue on instead of
/// pausing it; a hand-over never pauses the owner's music for an engine that cannot start; a failed hand-over
/// resumes the music it paused; a Blocked director follows music that plays.
/// </summary>
public sealed partial class PlaybackDirectorTests
{
    [Fact]
    public async Task Skip_OnTheLastSpotifyRequest_MovesSpotifyOnToTheOwnersMusicAndNeverPausesIt()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.Spotify);

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Spotify.Calls.Should().Equal(new EngineCall("Start", "a"), new EngineCall("Skip"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task Skip_OnTheOwnersOwnSpotifyMusicWithNothingWaiting_SkipsTheOwnerTrack()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 50_000);

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Spotify.Calls.Should().Equal(new EngineCall("Skip"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.Journal.Last().Reason.Should().Be("owner_skip");
    }

    [Fact]
    public async Task Skip_ToASpotifyRequestNotYetQueued_StartsItWithoutPausingSpotifyFirst()
    {
        Rig rig = new();
        await rig.StartPlaying(EngineKind.Spotify);
        PlaybackItem b = Item("b", owner: "bob");
        rig.Queue.Enqueue(b);

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("Start", "b"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, b));
    }

    [Fact]
    public async Task Skip_WhileTheNextRequestIsLinedUpInSpotify_MovesSpotifyOnToIt()
    {
        Rig rig = new();
        PlaybackItem a = await rig.StartPlaying(EngineKind.Spotify);
        PlaybackItem b = Item("b", owner: "owner-b");
        await rig.Request(b);
        await rig.Spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, a.Uri, true, a.DurationMs - 12_000, a.DurationMs)
        );

        await rig.Director.HandleAsync(new OwnerSkip());
        await rig.Plays(rig.Spotify, b);

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("Start", "a"),
                new EngineCall("QueueNext", "b"),
                new EngineCall("Skip")
            );
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, b));
    }

    [Fact]
    public async Task Skip_OverOwnerMusicWhileSpotifyHoldsTheRequest_NeverPausesSpotify()
    {
        Rig rig = new();
        PlaybackItem a = await OwnerMusicWithTheRequestInSpotifysQueue(rig);

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Spotify.Calls.Should().Equal(new EngineCall("QueueNext", "a"), new EngineCall("Skip"));
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, a));
    }

    [Fact]
    public async Task Skip_WhilePausedAndSpotifyHoldsTheRequest_MovesSpotifyOnToIt()
    {
        Rig rig = new();
        PlaybackItem a = await OwnerMusicWithTheRequestInSpotifysQueue(rig);
        await rig.Director.HandleAsync(new OwnerPause());

        await rig.Director.HandleAsync(new OwnerSkip());

        rig.Spotify.Calls.Should()
            .Equal(
                new EngineCall("QueueNext", "a"),
                new EngineCall("Pause"),
                new EngineCall("Skip")
            );
        rig.Director.State.Should().Be(PlaybackState.Starting(EngineKind.Spotify, a));
    }

    [Fact]
    public async Task HandOverToYouTube_WithNoPlayerOpen_NeverPausesTheOwnersSpotify()
    {
        Rig rig = new();
        rig.YouTube.CanStart = false;
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 196_000);

        await rig.Request(Item("y1", EngineKind.YouTube));

        rig.Spotify.Calls.Should().BeEmpty("the owner's song plays on");
        rig.YouTube.Calls.Should().BeEmpty();
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("y1");
    }

    [Fact]
    public async Task HandOverToYouTube_ThatFailsAfterSpotifyPaused_ResumesTheOwnersSpotify()
    {
        Rig rig = new();
        rig.YouTube.StartResult = EngineStartResult.Refused("player closed");
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 196_000);
        await rig.Request(Item("y1", EngineKind.YouTube));
        await rig.Spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, "spotify:track:mine"));
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next", 1_000);

        await rig.Paused(rig.Spotify, "spotify:track:next", 0);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Stop"), new EngineCall("Seek", "0"), new EngineCall("Resume"));
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("y1");
    }

    [Fact]
    public async Task Blocked_APlayingReport_FollowsThatMusicAndLinesTheRequestUpAtItsEnd()
    {
        Rig rig = new();
        rig.Spotify.StartResult = EngineStartResult.Refused("no device");
        await rig.Request(Item("a"));
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Blocked);
        rig.Spotify.StartResult = EngineStartResult.Ok;

        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 10_000);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 190_000);

        rig.Spotify.Calls.Should()
            .Equal(new EngineCall("Start", "a"), new EngineCall("QueueNext", "a"));
    }

    [Fact]
    public async Task Blocked_ARequestWhileTheOwnersMusicPlays_NeverStartsOverIt()
    {
        Rig rig = new();
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 10_000);
        rig.Spotify.BlockCommands = EngineBlockReason.NotConnected;
        await rig.Director.HandleAsync(new OwnerVolume(50));
        rig.Director.State.Kind.Should().Be(PlaybackStateKind.Blocked);
        rig.Spotify.BlockCommands = null;

        await rig.Request(Item("a"));

        rig.Spotify.Calls.Should().NotContain(c => c.Name == "Start", "the owner's song plays on");
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
        rig.QueueIds.Should().Equal("a");
    }

    /// <summary>
    /// The owner's music plays and request a sits in Spotify's own queue, but its start timed out, so the
    /// director follows the owner's music again.
    /// </summary>
    private static async Task<PlaybackItem> OwnerMusicWithTheRequestInSpotifysQueue(Rig rig)
    {
        await rig.OwnerPlays(rig.Spotify, "spotify:track:mine", 196_000);
        PlaybackItem a = Item("a");
        await rig.Request(a);
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 2_000);
        rig.Clock.Advance(TimeSpan.FromSeconds(15));
        await rig.OwnerPlays(rig.Spotify, "spotify:track:next-in-playlist", 17_000);
        rig.Director.State.Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));

        return a;
    }
}
