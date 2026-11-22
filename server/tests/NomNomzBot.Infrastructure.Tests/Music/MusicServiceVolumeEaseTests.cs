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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using static NomNomzBot.Infrastructure.Tests.Music.MusicServiceDirectorIntakeTests;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A volume change of more than 10 points eases to its value in steps when the channel's setting for that
/// direction is on, and snaps otherwise. The tests call the real <see cref="MusicService.SetVolumeAsync"/>;
/// only the engine and the clock are fakes, and the assertion is the exact list of volumes the engine got.
/// </summary>
public sealed class MusicServiceVolumeEaseTests
{
    private static readonly TimeSpan StepGap = TimeSpan.FromMilliseconds(300);

    private sealed record Rig(
        MusicService Sut,
        FakeEngine Spotify,
        FakeTimeProvider Time,
        MusicConfigDbFixture Config
    ) : IDisposable
    {
        public void Dispose() => Config.Dispose();
    }

    /// <summary>A channel with the director Starting, the cache reporting <paramref name="current"/> as its volume (null: no report), and the two ease settings saved.</summary>
    private static async Task<Rig> PlayingAtAsync(
        int? current,
        bool? easeUp = null,
        bool? easeDown = null
    )
    {
        MusicConfigDbFixture config = new();
        (
            await config.Service.UpdateConfigAsync(
                SpotifyChannel.ToString(),
                new() { EaseVolumeUp = easeUp, EaseVolumeDown = easeDown }
            )
        )
            .IsSuccess.Should()
            .BeTrue();
        NowPlayingCache cache = new();
        FakeTimeProvider time = new();
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify(
            cache,
            config: config.Service,
            time: time
        );
        (await RequestAsync(sut, handler, "e1", "viewer-e1")).IsSuccess.Should().BeTrue();
        if (current is { } volume)
        {
            cache.Set(
                SpotifyChannel,
                new()
                {
                    TrackName = "Song e1",
                    Artist = "Artist",
                    Album = "Album",
                    TrackUri = "spotify:track:e1",
                    Provider = "spotify",
                    IsPlaying = true,
                    DurationMs = 200_000,
                    VolumePercent = volume,
                },
                DateTimeOffset.UtcNow
            );
        }

        return new(sut, rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify), time, config);
    }

    /// <summary>Moves the fake clock one step gap at a time until the call finishes.</summary>
    private static async Task<Result> FinishAsync(Task<Result> call, FakeTimeProvider time)
    {
        for (int i = 0; i < 200 && !call.IsCompleted; i++)
        {
            time.Advance(StepGap);
            await Task.Delay(5);
        }

        call.IsCompleted.Should().BeTrue("the ease ends after at most five steps");

        return await call;
    }

    private static List<int> VolumesSent(FakeEngine spotify) =>
        spotify
            .Calls.Where(c => c.Name == "SetVolume")
            .Select(c => int.Parse(c.Argument!))
            .ToList();

    [Fact]
    public async Task A_rise_of_more_than_ten_points_eases_up_in_steps_that_end_on_the_target()
    {
        using Rig rig = await PlayingAtAsync(20);

        Result result = await FinishAsync(
            rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 80),
            rig.Time
        );

        result.IsSuccess.Should().BeTrue();
        VolumesSent(rig.Spotify).Should().Equal(32, 44, 56, 68, 80);
    }

    [Fact]
    public async Task A_drop_of_more_than_ten_points_snaps_with_the_default_settings()
    {
        using Rig rig = await PlayingAtAsync(80);

        Result result = await FinishAsync(
            rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 20),
            rig.Time
        );

        result.IsSuccess.Should().BeTrue();
        VolumesSent(rig.Spotify).Should().Equal(20);
    }

    [Fact]
    public async Task A_change_of_exactly_ten_points_is_one_call()
    {
        using Rig rig = await PlayingAtAsync(20);

        await FinishAsync(rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 30), rig.Time);

        VolumesSent(rig.Spotify).Should().Equal(30);
    }

    [Fact]
    public async Task A_drop_eases_down_in_steps_when_ease_volume_down_is_on()
    {
        using Rig rig = await PlayingAtAsync(80, easeDown: true);

        Result result = await FinishAsync(
            rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 20),
            rig.Time
        );

        result.IsSuccess.Should().BeTrue();
        VolumesSent(rig.Spotify).Should().Equal(68, 56, 44, 32, 20);
    }

    [Fact]
    public async Task A_rise_is_one_call_when_ease_volume_up_is_off()
    {
        using Rig rig = await PlayingAtAsync(20, easeUp: false);

        await FinishAsync(rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 80), rig.Time);

        VolumesSent(rig.Spotify).Should().Equal(80);
    }

    [Fact]
    public async Task A_second_change_during_a_running_ease_stops_the_first_and_ends_on_its_own_target()
    {
        using Rig rig = await PlayingAtAsync(20);
        Task<Result> first = rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 80);
        for (int i = 0; i < 200 && VolumesSent(rig.Spotify).Count == 0; i++)
            await Task.Delay(5);
        VolumesSent(rig.Spotify).Should().Equal(32);

        Result second = await FinishAsync(
            rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 50),
            rig.Time
        );
        Result firstResult = await FinishAsync(first, rig.Time);

        second.IsSuccess.Should().BeTrue();
        firstResult.IsSuccess.Should().BeTrue("a replaced ease is not a failure");
        VolumesSent(rig.Spotify).Should().Equal(32, 30, 40, 50);
    }

    [Fact]
    public async Task An_unknown_current_volume_is_one_call()
    {
        using Rig rig = await PlayingAtAsync(null);

        await FinishAsync(rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 80), rig.Time);

        VolumesSent(rig.Spotify).Should().Equal(80);
    }

    [Fact]
    public async Task A_step_the_engine_refuses_returns_its_failure_and_stops_the_ease()
    {
        using Rig rig = await PlayingAtAsync(20);
        rig.Spotify.BlockCommands = EngineBlockReason.Forbidden;

        Result result = await FinishAsync(
            rig.Sut.SetVolumeAsync(SpotifyChannel.ToString(), 80),
            rig.Time
        );

        result.IsFailure.Should().BeTrue();
        VolumesSent(rig.Spotify).Should().Equal([32], "a refused first step ends the ease");
    }

    [Fact]
    public async Task The_two_ease_settings_default_to_up_on_and_down_off_and_save_what_is_sent()
    {
        using MusicConfigDbFixture fixture = new();
        string channel = SpotifyChannel.ToString();

        MusicConfigDto defaults = (await fixture.Service.GetConfigAsync(channel)).Value;
        MusicConfigDto saved = (
            await fixture.Service.UpdateConfigAsync(
                channel,
                new() { EaseVolumeUp = false, EaseVolumeDown = true }
            )
        ).Value;

        MusicConfigDefaults.EaseVolumeUp.Should().BeTrue();
        MusicConfigDefaults.EaseVolumeDown.Should().BeFalse();
        defaults.EaseVolumeUp.Should().BeTrue();
        defaults.EaseVolumeDown.Should().BeFalse();
        saved.EaseVolumeUp.Should().BeFalse();
        saved.EaseVolumeDown.Should().BeTrue();
        (await fixture.Service.GetConfigAsync(channel)).Value.Should().BeEquivalentTo(saved);
    }
}
