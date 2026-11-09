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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The registry runs each channel's deadlines on its own timer, so a dead Music Player page gives the music
/// back with no poll, report or user input to drive it.
/// </summary>
public sealed class PlaybackDirectorRegistryDeadlineTimerTests
{
    private const string OwnTrack = "spotify:track:own1";
    private const string OwnNextTrack = "spotify:track:own2";
    private const string VideoUri = "https://www.youtube.com/watch?v=madeUpVideo";

    private sealed class Rig
    {
        private readonly SongRequestQueueStore _store = new();

        public Rig()
        {
            Gate.IsActiveInstance.Returns(true);
            Registry = new(_store, Engines, new RecordingBus(), Clock, null, Gate, Logger);
        }

        public ILogger<PlaybackDirectorRegistry> Logger { get; } =
            Substitute.For<ILogger<PlaybackDirectorRegistry>>();

        public FakeTimeProvider Clock { get; } = NewClock();

        public FakeEngineFactory Engines { get; } = new();

        public IActiveInstanceGate Gate { get; } = Substitute.For<IActiveInstanceGate>();

        public PlaybackDirectorRegistry Registry { get; }

        public FakeEngine Spotify => Engines.EngineOf(Channel, EngineKind.Spotify);

        public FakeEngine YouTube => Engines.EngineOf(Channel, EngineKind.YouTube);

        /// <summary>A video has taken over from the owner's Spotify and reported playing once.</summary>
        public async Task VideoHoldsSpotifyAsync()
        {
            await Registry.SendAsync(Channel, new RequestAdded());
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnTrack, true));
            _store
                .GetOrCreate(Channel.ToString())
                .Enqueue(
                    "viewer",
                    new SongRequestEntry(
                        VideoUri,
                        "Made up video",
                        "Artist",
                        null,
                        200_000,
                        "viewer",
                        0,
                        null,
                        ""
                    )
                );
            await Registry.SendAsync(Channel, new RequestAdded());
            await Spotify.EmitAsync(new TrackEnded(EngineKind.Spotify, OwnTrack));
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, true));
            await Spotify.EmitAsync(Report(EngineKind.Spotify, OwnNextTrack, false));
            await YouTube.EmitAsync(Report(EngineKind.YouTube, VideoUri, true));
            Registry.StateOf(Channel).Engine.Should().Be(EngineKind.YouTube);
        }

        private static EngineReport Report(EngineKind engine, string uri, bool playing) =>
            new(engine, uri, playing, 1_000, 200_000);
    }

    [Fact]
    public async Task A_silent_video_gives_Spotify_back_with_no_input_at_all()
    {
        Rig rig = new();
        await rig.VideoHoldsSpotifyAsync();

        rig.Clock.Advance(TimeSpan.FromSeconds(16));

        rig.Spotify.Calls.Should().Contain(new EngineCall("Resume"));
        rig.YouTube.Calls.Should().Contain(new EngineCall("Stop"));
        rig.Registry.StateOf(Channel).Should().Be(PlaybackState.Playing(EngineKind.Spotify, null));
    }

    [Fact]
    public async Task A_standby_instance_leaves_the_video_to_the_active_one()
    {
        Rig rig = new();
        await rig.VideoHoldsSpotifyAsync();
        rig.Gate.IsActiveInstance.Returns(false);

        rig.Clock.Advance(TimeSpan.FromSeconds(16));

        rig.Spotify.Calls.Should().NotContain(new EngineCall("Resume"));
        rig.YouTube.Calls.Should().NotContain(new EngineCall("Stop"));
        rig.Registry.StateOf(Channel).Engine.Should().Be(EngineKind.YouTube);
    }

    [Fact]
    public async Task A_standby_instance_ignores_a_detached_page()
    {
        Rig rig = new();
        await rig.VideoHoldsSpotifyAsync();
        rig.Gate.IsActiveInstance.Returns(false);

        PlaybackInputOutcome outcome = await rig.Registry.SendAsync(Channel, new OutputDetached());

        outcome.Should().Be(PlaybackInputOutcome.Ignored);
        rig.Registry.StateOf(Channel).Engine.Should().Be(EngineKind.YouTube);
    }

    [Fact]
    public async Task A_disposed_registry_stops_the_timer()
    {
        Rig rig = new();
        await rig.VideoHoldsSpotifyAsync();

        rig.Registry.Dispose();
        rig.Clock.Advance(TimeSpan.FromSeconds(16));

        rig.Spotify.Calls.Should().NotContain(new EngineCall("Resume"));
        rig.YouTube.Calls.Should().NotContain(new EngineCall("Stop"));
        // A tick that still ran would fail on the disposed director and log it.
        rig.Logger.ReceivedCalls().Should().BeEmpty();
    }
}
