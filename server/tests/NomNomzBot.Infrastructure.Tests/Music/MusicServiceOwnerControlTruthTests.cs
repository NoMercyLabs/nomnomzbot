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
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Commands.Builtin.Personality;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Commands.Builtins;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.RateLimiting;
using NomNomzBot.Infrastructure.Tests.Commands.Builtins;
using static NomNomzBot.Infrastructure.Tests.Music.MusicServiceDirectorIntakeTests;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// An owner control the playback director ignores is reported as not done, and a control it applies is
/// reported as done. Chat and the dashboard never hear "done" for a change that did not happen (live
/// 2026-10-30: <c>!volume</c> answered "Volume set" while the director, Starting, ignored it).
/// </summary>
public sealed class MusicServiceOwnerControlTruthTests
{
    private const string Skipper = "0192a000-0000-7000-8000-000000007c99";

    /// <summary>A request is admitted on an idle channel; Spotify accepted the start, so the director is Starting.</summary>
    private static async Task<(
        MusicService Sut,
        DirectorRig Rig,
        FakeEngine Spotify,
        NowPlayingCache Cache
    )> StartingAsync()
    {
        NowPlayingCache cache = new();
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify(cache);
        FakeEngine spotify = rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify);
        (await RequestAsync(sut, handler, "t1", "viewer-t1")).IsSuccess.Should().BeTrue();
        rig.Registry.StateOf(SpotifyChannel).Kind.Should().Be(PlaybackStateKind.Starting);
        cache.Set(
            SpotifyChannel,
            new()
            {
                TrackName = "Song t1",
                Artist = "Artist",
                Album = "Album",
                TrackUri = "spotify:track:t1",
                Provider = "spotify",
                IsPlaying = true,
                DurationMs = 200_000,
            },
            DateTimeOffset.UtcNow
        );

        return (sut, rig, spotify, cache);
    }

    private static Task<Result> RunAsync(MusicService sut, string control)
    {
        string channel = SpotifyChannel.ToString();

        return control switch
        {
            "pause" => sut.PauseAsync(channel),
            "resume" => sut.PlayAsync(channel),
            "skip" => sut.SkipAsync(channel, Skipper),
            "previous" => sut.PreviousAsync(channel),
            "seek" => sut.SeekAsync(channel, 30_000),
            "volume" => sut.SetVolumeAsync(channel, 40),
            _ => throw new ArgumentOutOfRangeException(nameof(control), control, null),
        };
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("resume")]
    [InlineData("skip")]
    [InlineData("previous")]
    [InlineData("seek")]
    public async Task A_control_the_Starting_director_ignores_returns_a_failure_and_commands_no_engine(
        string control
    )
    {
        (MusicService sut, DirectorRig rig, FakeEngine spotify, _) = await StartingAsync();

        Result result = await RunAsync(sut, control);

        result.IsFailure.Should().BeTrue($"the director ignored {control}, so it was not done");
        result.ErrorCode.Should().Be("INVALID_STATE");
        spotify.Calls.Select(c => c.Name).Should().Equal("Start");
        rig.Bus.Published.OfType<PlaybackTransitionedEvent>().Last().Reason.Should().Be("ignored");
    }

    [Fact]
    public async Task Volume_while_Starting_reaches_the_engine_and_returns_success()
    {
        (MusicService sut, DirectorRig rig, FakeEngine spotify, _) = await StartingAsync();

        Result result = await RunAsync(sut, "volume");

        result.IsSuccess.Should().BeTrue();
        spotify.Calls.Select(c => c.Name).Should().Equal("Start", "SetVolume");
        spotify.Calls.Last().Argument.Should().Be("40");
        rig.Bus.Published.OfType<PlaybackTransitionedEvent>().Last().Reason.Should().Be("volume");
        rig.Registry.LevelOf(SpotifyChannel).Should().Be(new PlaybackLevel(40, false));
    }

    [Fact]
    public async Task Resume_while_the_music_already_plays_is_not_reported_as_done()
    {
        (MusicService sut, DirectorRig rig, FakeEngine spotify, _) = await StartingAsync();
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:t1", true, 1_000, 200_000)
        );
        rig.Registry.StateOf(SpotifyChannel).Kind.Should().Be(PlaybackStateKind.Playing);

        Result result = await RunAsync(sut, "resume");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("INVALID_STATE");
        spotify.Calls.Select(c => c.Name).Should().Equal("Start");
    }

    [Fact]
    public async Task Pause_while_the_music_plays_is_applied_and_returns_success()
    {
        (MusicService sut, DirectorRig rig, FakeEngine spotify, _) = await StartingAsync();
        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, "spotify:track:t1", true, 1_000, 200_000)
        );

        Result result = await RunAsync(sut, "pause");

        result.IsSuccess.Should().BeTrue();
        spotify.Calls.Select(c => c.Name).Should().Equal("Start", "Pause");
        rig.Registry.StateOf(SpotifyChannel).Kind.Should().Be(PlaybackStateKind.PausedByOwner);
    }

    [Fact]
    public async Task The_pause_reply_in_chat_is_the_failure_line_not_the_paused_line_while_Starting()
    {
        (MusicService sut, _, FakeEngine spotify, _) = await StartingAsync();
        PauseBuiltin builtin = new(sut, TestBuiltinComposer.Create(), MusicGateTestKit.Gate(true));

        Result<string> reply = await builtin.ExecuteAsync(ChatContext(string.Empty));

        string paused = ToneTemplateCatalog.Get(
            PersonalityTone.Informative,
            BuiltinResponseSlots.Pause.Key,
            BuiltinResponseSlots.Pause.Paused
        )[0];
        string failed = ToneTemplateCatalog.Get(
            PersonalityTone.Informative,
            BuiltinResponseSlots.Pause.Key,
            BuiltinResponseSlots.PlaybackControl.Failed
        )[0];
        reply.Value.Should().NotBe(paused);
        reply.Value.Should().Be(failed);
        spotify.Calls.Select(c => c.Name).Should().Equal("Start");
    }

    [Fact]
    public async Task The_skip_reply_in_chat_is_the_failure_line_not_the_skipped_line_while_Starting()
    {
        (MusicService sut, _, FakeEngine spotify, _) = await StartingAsync();
        SkipBuiltin builtin = new(
            sut,
            TestBuiltinComposer.Create(),
            MusicGateTestKit.Gate(true),
            new CooldownManager(TimeProvider.System)
        );

        Result<string> reply = await builtin.ExecuteAsync(ChatContext(string.Empty));

        reply
            .Value.Should()
            .NotBe(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.Skip.Key,
                    BuiltinResponseSlots.Skip.Skipped
                )[0]
            );
        reply
            .Value.Should()
            .Be(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.Skip.Key,
                    BuiltinResponseSlots.Skip.Failed
                )[0]
            );
        spotify.Calls.Select(c => c.Name).Should().Equal("Start");
    }

    /// <summary>Forwards every input to the real director until told to answer Ignored for the rest, as a director with no row for the control does.</summary>
    private sealed class IgnoringSink(IPlaybackInputSink inner) : IPlaybackInputSink
    {
        public bool Ignore { get; set; }

        public Task<PlaybackInputOutcome> SendAsync(
            Guid broadcasterId,
            PlaybackInput input,
            CancellationToken ct = default
        ) =>
            Ignore
                ? Task.FromResult(PlaybackInputOutcome.Ignored)
                : inner.SendAsync(broadcasterId, input, ct);
    }

    [Fact]
    public async Task The_volume_reply_in_chat_is_the_failure_line_not_Volume_set_when_the_director_ignored_it()
    {
        NowPlayingCache cache = new();
        IgnoringSink? sink = null;
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify(
            cache,
            null,
            director => sink = new(director.Registry)
        );
        FakeEngine spotify = rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify);
        (await RequestAsync(sut, handler, "t1", "viewer-t1")).IsSuccess.Should().BeTrue();
        cache.Set(
            SpotifyChannel,
            new()
            {
                TrackName = "Song t1",
                Artist = "Artist",
                Album = "Album",
                TrackUri = "spotify:track:t1",
                Provider = "spotify",
                IsPlaying = true,
                DurationMs = 200_000,
            },
            DateTimeOffset.UtcNow
        );
        sink!.Ignore = true;
        VolumeBuiltin builtin = new(sut, TestBuiltinComposer.Create(), MusicGateTestKit.Gate(true));

        Result<string> reply = await builtin.ExecuteAsync(ChatContext("40"));

        reply.Value.Should().NotBe("Volume set to 40%.");
        reply
            .Value.Should()
            .Be(
                ToneTemplateCatalog.Get(
                    PersonalityTone.Informative,
                    BuiltinResponseSlots.Volume.Key,
                    BuiltinResponseSlots.Volume.SetFailed
                )[0]
            );
        spotify.Calls.Select(c => c.Name).Should().Equal("Start");
    }

    private static BuiltinCommandContext ChatContext(string args) =>
        new()
        {
            BroadcasterId = SpotifyChannel,
            TriggeringUserId = Skipper,
            TriggeringUserDisplayName = "Bamo",
            RoleLevel = 10,
            Args = args,
        };
}
