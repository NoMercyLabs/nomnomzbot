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
using NomNomzBot.Domain.Music.Exceptions;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The Spotify engine adapter (music-playback-determinism.md section 11.2): which provider calls it makes and
/// does not make, and the one typed answer it gives when a precondition is missing.
/// </summary>
public sealed class SpotifyPlaybackEngineTests
{
    private static readonly Guid Channel = PlaybackDirectorTestKit.Channel;

    private readonly IMusicProvider _provider = Substitute.For<
        IMusicProvider,
        IMusicProviderStartTrack,
        IMusicProviderConnectionGate
    >();
    private readonly SpotifyPlaybackEngine _engine;
    private readonly List<EngineSignal> _signals = [];

    public SpotifyPlaybackEngineTests()
    {
        _provider.Capabilities.Returns(
            MusicProviderCapabilities.Shuffle | MusicProviderCapabilities.Repeat
        );
        Connected(true);
        _engine = new(Channel, () => EngineLease.Of(_provider));
        _engine.Reports += signal =>
        {
            _signals.Add(signal);
            return Task.CompletedTask;
        };
    }

    private void Connected(bool connected) =>
        ((IMusicProviderConnectionGate)_provider)
            .IsConnectedAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(connected);

    private IMusicProviderStartTrack StartTrack => (IMusicProviderStartTrack)_provider;

    private static PlaybackItem Item() => PlaybackDirectorTestKit.Item("a1");

    [Fact]
    public async Task Start_maps_to_StartTrack_and_touches_no_queue_skip_or_previous()
    {
        PlaybackItem item = Item();
        StartTrack.StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>()).Returns(true);

        EngineStartResult result = await _engine.StartAsync(item);

        result.Should().Be(EngineStartResult.Ok);
        await StartTrack
            .Received(1)
            .StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().AddToQueueAsync(default, default!);
        await _provider.DidNotReceiveWithAnyArgs().SkipAsync(default);
        await _provider.DidNotReceiveWithAnyArgs().PreviousAsync(default);
    }

    [Fact]
    public async Task Start_refused_by_provider_is_Refused_not_Blocked()
    {
        PlaybackItem item = Item();
        StartTrack.StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>()).Returns(false);

        EngineStartResult result = await _engine.StartAsync(item);

        result.Accepted.Should().BeFalse();
        result.Block.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Start_without_a_token_is_Blocked_NotConnected_and_never_calls_the_provider()
    {
        Connected(false);

        EngineStartResult result = await _engine.StartAsync(Item());

        result.Accepted.Should().BeFalse();
        result.Block.Should().Be(EngineBlockReason.NotConnected);
        await StartTrack.DidNotReceiveWithAnyArgs().StartTrackAsync(default, default!);
    }

    [Theory]
    [MemberData(nameof(ProviderFailures))]
    public async Task Start_turns_every_provider_throw_into_one_typed_block(
        Exception thrown,
        EngineBlockReason expected
    )
    {
        PlaybackItem item = Item();
        StartTrack
            .StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>())
            .ThrowsAsync(thrown);

        EngineStartResult result = await _engine.StartAsync(item);

        result.Accepted.Should().BeFalse();
        result.Block.Should().Be(expected);
    }

    public static TheoryData<Exception, EngineBlockReason> ProviderFailures =>
        new()
        {
            { new NoActiveDeviceException("spotify"), EngineBlockReason.NoOutput },
            { new MusicAuthenticationFailedException("spotify"), EngineBlockReason.NotConnected },
            { new MusicForbiddenException("spotify"), EngineBlockReason.Forbidden },
            { new PremiumRequiredException("spotify"), EngineBlockReason.PremiumRequired },
        };

    [Fact]
    public async Task Start_lets_an_unknown_provider_error_escape()
    {
        PlaybackItem item = Item();
        StartTrack
            .StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        Func<Task> act = () => _engine.StartAsync(item);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Previous_is_Seek_zero_then_Start_and_the_previous_endpoint_is_never_called()
    {
        PlaybackItem item = Item();
        StartTrack.StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>()).Returns(true);

        await _engine.SeekAsync(0);
        await _engine.StartAsync(item);

        await _provider.Received(1).SeekAsync(Channel, 0, Arg.Any<CancellationToken>());
        await StartTrack
            .Received(1)
            .StartTrackAsync(Channel, item.Uri, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().PreviousAsync(default);
    }

    [Fact]
    public async Task Seek_sends_whole_seconds_to_the_provider()
    {
        await _engine.SeekAsync(61_900);

        await _provider.Received(1).SeekAsync(Channel, 61, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_pauses_and_does_not_skip()
    {
        await _engine.StopAsync();

        await _provider.Received(1).PauseAsync(Channel, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().SkipAsync(default);
        await _provider.DidNotReceiveWithAnyArgs().PlayAsync(default);
    }

    [Fact]
    public async Task Resume_plays_this_engine_only()
    {
        await _engine.ResumeAsync();

        await _provider.Received(1).PlayAsync(Channel, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().PauseAsync(default);
    }

    [Fact]
    public async Task Volume_goes_to_the_provider()
    {
        await _engine.SetVolumeAsync(35);

        await _provider.Received(1).SetVolumeAsync(Channel, 35, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pause_without_a_token_throws_the_same_typed_block_and_sends_nothing()
    {
        Connected(false);

        Func<Task> act = () => _engine.PauseAsync();

        (await act.Should().ThrowAsync<PlaybackEngineBlockedException>())
            .Which.Should()
            .Match<PlaybackEngineBlockedException>(e =>
                e.Reason == EngineBlockReason.NotConnected && e.Engine == EngineKind.Spotify
            );
        await _provider.DidNotReceiveWithAnyArgs().PauseAsync(default);
    }

    [Fact]
    public async Task Pause_with_no_device_throws_Blocked_NoOutput()
    {
        _provider
            .PauseAsync(Channel, Arg.Any<CancellationToken>())
            .ThrowsAsync(new NoActiveDeviceException("spotify"));

        Func<Task> act = () => _engine.PauseAsync();

        (await act.Should().ThrowAsync<PlaybackEngineBlockedException>())
            .Which.Reason.Should()
            .Be(EngineBlockReason.NoOutput);
    }

    [Fact]
    public void Kind_is_Spotify_and_Capabilities_come_from_the_provider()
    {
        _engine.Kind.Should().Be(EngineKind.Spotify);
        _engine
            .Capabilities.Should()
            .Be(MusicProviderCapabilities.Shuffle | MusicProviderCapabilities.Repeat);
    }

    [Fact]
    public async Task Shuffle_repeat_transfer_run_on_the_provider_and_answer_Done()
    {
        EngineControlResult shuffle = await _engine.SetShuffleAsync(true);
        EngineControlResult repeat = await _engine.SetRepeatAsync(MusicRepeatMode.Track);
        EngineControlResult transfer = await _engine.TransferAsync("dev-1", true);

        shuffle.Should().Be(EngineControlResult.Done);
        repeat.Should().Be(EngineControlResult.Done);
        transfer.Should().Be(EngineControlResult.Done);
        await _provider.Received(1).SetShuffleAsync(Channel, true, Arg.Any<CancellationToken>());
        await _provider
            .Received(1)
            .SetRepeatAsync(Channel, MusicRepeatMode.Track, Arg.Any<CancellationToken>());
        await _provider
            .Received(1)
            .TransferPlaybackAsync(Channel, "dev-1", true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Devices_and_embedded_token_return_the_provider_value()
    {
        MusicDeviceInfo device = new("dev-1", "Desk", "Computer", true, 40);
        _provider.GetDevicesAsync(Channel, Arg.Any<CancellationToken>()).Returns([device]);
        _provider
            .GetEmbeddedPlaybackTokenAsync(Channel, Arg.Any<CancellationToken>())
            .Returns("tok");

        EngineControlResult<IReadOnlyList<MusicDeviceInfo>> devices =
            await _engine.GetDevicesAsync();
        EngineControlResult<string> token = await _engine.GetEmbeddedTokenAsync();

        devices.Outcome.Should().Be(EngineControlOutcome.Done);
        devices.Value.Should().Equal(device);
        token.Outcome.Should().Be(EngineControlOutcome.Done);
        token.Value.Should().Be("tok");
    }

    [Fact]
    public async Task A_control_without_a_token_is_Blocked_NotConnected_and_sends_nothing()
    {
        Connected(false);

        EngineControlResult result = await _engine.SetShuffleAsync(true);

        result.Should().Be(EngineControlResult.BlockedBy(EngineBlockReason.NotConnected));
        await _provider.DidNotReceiveWithAnyArgs().SetShuffleAsync(default, default);
    }

    [Fact]
    public async Task Observe_reports_the_polled_track_in_the_shared_report_shape()
    {
        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 30_000, playing: true));

        _signals
            .Should()
            .Equal(new EngineReport(EngineKind.Spotify, "spotify:track:a", true, 30_000, 200_000));
    }

    [Fact]
    public async Task Observe_derives_TrackEnded_before_the_report_when_a_near_end_track_is_replaced()
    {
        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 197_000, playing: true));
        _signals.Clear();

        await _engine.ObserveAsync(Track("spotify:track:b", progressMs: 1_000, playing: true));

        _signals
            .Should()
            .Equal(
                new TrackEnded(EngineKind.Spotify, "spotify:track:a"),
                new EngineReport(EngineKind.Spotify, "spotify:track:b", true, 1_000, 200_000)
            );
    }

    [Fact]
    public async Task Observe_derives_TrackEnded_when_the_player_goes_idle_after_a_near_end_track()
    {
        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 198_000, playing: true));
        _signals.Clear();

        await _engine.ObserveAsync(null);

        _signals.Should().Equal(new TrackEnded(EngineKind.Spotify, "spotify:track:a"));
    }

    [Fact]
    public async Task Observe_derives_TrackEnded_when_a_near_end_track_restarts_on_repeat()
    {
        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 199_000, playing: true));
        _signals.Clear();

        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 500, playing: true));

        _signals
            .OfType<TrackEnded>()
            .Should()
            .ContainSingle()
            .Which.Uri.Should()
            .Be("spotify:track:a");
    }

    [Fact]
    public async Task Observe_does_not_call_a_mid_track_change_an_end()
    {
        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 60_000, playing: true));
        _signals.Clear();

        await _engine.ObserveAsync(Track("spotify:track:b", progressMs: 1_000, playing: true));

        _signals.OfType<TrackEnded>().Should().BeEmpty();
        _signals.OfType<EngineReport>().Should().ContainSingle();
    }

    [Fact]
    public async Task Observe_does_not_end_a_track_that_is_merely_paused_near_the_end()
    {
        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 197_000, playing: true));
        _signals.Clear();

        await _engine.ObserveAsync(Track("spotify:track:a", progressMs: 197_000, playing: false));

        _signals.OfType<TrackEnded>().Should().BeEmpty();
        _signals.OfType<EngineReport>().Single().IsPlaying.Should().BeFalse();
    }

    private static TrackInfo Track(string uri, int progressMs, bool playing) =>
        new()
        {
            TrackName = "T",
            Artist = "A",
            Album = "Al",
            TrackUri = uri,
            Provider = "spotify",
            DurationMs = 200_000,
            ProgressMs = progressMs,
            IsPlaying = playing,
        };
}
