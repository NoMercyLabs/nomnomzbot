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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using static NomNomzBot.Infrastructure.Tests.Music.MusicServiceDirectorIntakeTests;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Spec rows 18, 19 and 20: pause, resume, previous, seek and volume reach the playback director as owner
/// inputs. The director commands the engine that plays, and only that engine. The provider is never called
/// for them, so Spotify and YouTube get the same treatment from every control surface (they all call
/// <see cref="MusicService"/>).
/// </summary>
public sealed class MusicServiceOwnerControlsThroughDirectorTests
{
    public static TheoryData<EngineKind> Engines =>
        new() { EngineKind.Spotify, EngineKind.YouTube };

    private static string UriOf(EngineKind engine) =>
        engine == EngineKind.Spotify
            ? "spotify:track:own1"
            : "https://www.youtube.com/watch?v=own1";

    /// <summary>The owner plays music on the given engine; the director observes it and goes Playing(engine, null).</summary>
    private static async Task<(
        MusicService Sut,
        RecordingHttpHandler Handler,
        DirectorRig Rig,
        FakeEngine Playing,
        FakeEngine Other
    )> OwnerPlayingAsync(EngineKind engine)
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify();
        FakeEngine playing = rig.Engines.EngineOf(SpotifyChannel, engine);
        FakeEngine other = rig.Engines.EngineOf(
            SpotifyChannel,
            engine == EngineKind.Spotify ? EngineKind.YouTube : EngineKind.Spotify
        );
        // The director is created on its first input; it only hears engine reports after that.
        await rig.Registry.SendAsync(SpotifyChannel, new OutputAttached());
        await playing.EmitAsync(new EngineReport(engine, UriOf(engine), true, 1_000, 200_000));
        rig.Registry.StateOf(SpotifyChannel).Should().Be(PlaybackState.Playing(engine, null));

        return (sut, handler, rig, playing, other);
    }

    private static bool CalledProviderControl(RecordingHttpHandler handler) =>
        handler.RequestUrls.Any(url =>
            (
                url.StartsWith("PUT", StringComparison.Ordinal)
                || url.StartsWith("POST", StringComparison.Ordinal)
            ) && url.Contains("/me/player", StringComparison.Ordinal)
        );

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Pause_then_resume_command_only_the_playing_engine_and_journal_both_moves(
        EngineKind engine
    )
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            DirectorRig rig,
            FakeEngine playing,
            FakeEngine other
        ) = await OwnerPlayingAsync(engine);
        string channel = SpotifyChannel.ToString();

        Result pause = await sut.PauseAsync(channel);
        PlaybackState paused = rig.Registry.StateOf(SpotifyChannel);
        Result resume = await sut.PlayAsync(channel);

        pause.IsSuccess.Should().BeTrue();
        resume.IsSuccess.Should().BeTrue();
        paused.Should().Be(PlaybackState.PausedByOwner(engine, null));
        rig.Registry.StateOf(SpotifyChannel).Should().Be(PlaybackState.Playing(engine, null));
        playing.Calls.Select(c => c.Name).Should().Equal("Pause", "Resume");
        other.Calls.Should().BeEmpty("only the engine that plays is commanded");
        CalledProviderControl(handler).Should().BeFalse("the director commands, not the provider");
        rig.Bus.Published.OfType<PlaybackTransitionedEvent>()
            .Where(e => e.Input is "OwnerPause" or "OwnerResume")
            .Select(e => (e.Input, e.From, e.To, e.Engine))
            .Should()
            .Equal(
                ("OwnerPause", "Playing", "PausedByOwner", engine.ToString()),
                ("OwnerResume", "PausedByOwner", "Playing", engine.ToString())
            );
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Seek_and_volume_reach_only_the_playing_engine_with_the_asked_values(
        EngineKind engine
    )
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            DirectorRig rig,
            FakeEngine playing,
            FakeEngine other
        ) = await OwnerPlayingAsync(engine);
        string channel = SpotifyChannel.ToString();

        Result seek = await sut.SeekAsync(channel, 45_000);
        Result volume = await sut.SetVolumeAsync(channel, 40);

        seek.IsSuccess.Should().BeTrue();
        volume.IsSuccess.Should().BeTrue();
        playing
            .Calls.Should()
            .Equal(new EngineCall("Seek", "45000"), new EngineCall("SetVolume", "40"));
        other.Calls.Should().BeEmpty();
        rig.Registry.StateOf(SpotifyChannel).Should().Be(PlaybackState.Playing(engine, null));
        CalledProviderControl(handler).Should().BeFalse();
        rig.Bus.Published.OfType<PlaybackTransitionedEvent>()
            .Select(e => e.Reason)
            .Should()
            .Contain(["seek", "volume"]);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Previous_early_in_the_song_restarts_it_on_the_playing_engine(
        EngineKind engine
    )
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            DirectorRig rig,
            FakeEngine playing,
            FakeEngine other
        ) = await OwnerPlayingAsync(engine);

        Result result = await sut.PreviousAsync(SpotifyChannel.ToString());

        result.IsSuccess.Should().BeTrue();
        playing.Calls.Should().Equal(new EngineCall("Seek", "0"));
        other.Calls.Should().BeEmpty();
        CalledProviderControl(handler).Should().BeFalse();
        rig.Bus.Published.OfType<PlaybackTransitionedEvent>()
            .Should()
            .Contain(e => e.Input == "OwnerPrevious" && e.Reason == "previous_restart");
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Every_control_gives_the_same_engine_calls_and_journal_moves_on_both_engines(
        EngineKind engine
    )
    {
        (MusicService sut, _, DirectorRig rig, FakeEngine playing, _) = await OwnerPlayingAsync(
            engine
        );
        string channel = SpotifyChannel.ToString();

        await sut.PauseAsync(channel);
        await sut.PlayAsync(channel);
        await sut.SeekAsync(channel, 5_000);
        await sut.SetVolumeAsync(channel, 70);
        await sut.PreviousAsync(channel);

        playing
            .Calls.Select(c => c.Name)
            .Should()
            .Equal("Pause", "Resume", "Seek", "SetVolume", "Seek");
        rig.Bus.Published.OfType<PlaybackTransitionedEvent>()
            .Count(e => e.Input.StartsWith("Owner", StringComparison.Ordinal))
            .Should()
            .Be(5, "one journal move per control, whichever engine plays");
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task An_engine_that_refuses_a_control_returns_the_typed_error_and_the_director_is_Blocked(
        EngineKind engine
    )
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig, FakeEngine playing, _) =
            await OwnerPlayingAsync(engine);
        playing.BlockCommands = EngineBlockReason.Forbidden;

        Result result = await sut.PauseAsync(SpotifyChannel.ToString());

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("MUSIC_FORBIDDEN");
        rig.Registry.StateOf(SpotifyChannel).Kind.Should().Be(PlaybackStateKind.Blocked);
        CalledProviderControl(handler).Should().BeFalse();
    }

    [Fact]
    public async Task Controls_with_nothing_playing_return_nothing_playing_and_command_no_engine()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify();
        string channel = SpotifyChannel.ToString();

        Result pause = await sut.PauseAsync(channel);
        Result volume = await sut.SetVolumeAsync(channel, 30);

        pause.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        volume.ErrorCode.Should().Be("NO_ACTIVE_DEVICE");
        rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify).Calls.Should().BeEmpty();
        rig.Engines.EngineOf(SpotifyChannel, EngineKind.YouTube).Calls.Should().BeEmpty();
        CalledProviderControl(handler).Should().BeFalse();
    }
}
