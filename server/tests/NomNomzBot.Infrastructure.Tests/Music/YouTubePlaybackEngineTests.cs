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
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Playback;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The YouTube engine adapter (music-playback-determinism.md section 11.2): Start maps to PlayNow, a missing
/// player becomes the same typed block as a dead Spotify token, Stop reaches the dispatcher, and the Spotify-only
/// controls answer NotSupported without touching the provider.
/// </summary>
public sealed class YouTubePlaybackEngineTests
{
    private const string VideoId = "dQw4w9WgXcQ";
    private const string WatchUrl = $"https://www.youtube.com/watch?v={VideoId}";
    private static readonly Guid Channel = PlaybackDirectorTestKit.Channel;

    private readonly IMusicProvider _provider = Substitute.For<
        IMusicProvider,
        IMusicProviderPlayNow,
        IMusicProviderPlayerGate
    >();
    private readonly IYouTubePlayerDispatcher _dispatcher =
        Substitute.For<IYouTubePlayerDispatcher>();
    private readonly IYouTubePlayerStateStore _store = Substitute.For<IYouTubePlayerStateStore>();
    private readonly YouTubePlaybackEngine _engine;
    private readonly List<EngineSignal> _signals = [];

    public YouTubePlaybackEngineTests()
    {
        _provider.Capabilities.Returns(
            MusicProviderCapabilities.PlaybackControl | MusicProviderCapabilities.Seek
        );
        PlayerOpen(true);
        _engine = new(Channel, () => EngineLease.Of(_provider, _dispatcher), _store);
        _engine.Reports += signal =>
        {
            _signals.Add(signal);
            return Task.CompletedTask;
        };
    }

    private IMusicProviderPlayNow PlayNow => (IMusicProviderPlayNow)_provider;

    private void PlayerOpen(bool open) =>
        ((IMusicProviderPlayerGate)_provider)
            .HasPlayerAsync(Channel, Arg.Any<CancellationToken>())
            .Returns(open);

    private static PlaybackItem Item() =>
        PlaybackDirectorTestKit.Item("y1", EngineKind.YouTube) with
        {
            Uri = VideoId,
        };

    [Fact]
    public async Task Start_maps_to_PlayNow_and_touches_no_queue_skip_or_previous()
    {
        PlaybackItem item = Item();
        PlayNow.PlayNowAsync(Channel, item.Uri, Arg.Any<CancellationToken>()).Returns(true);

        EngineStartResult result = await _engine.StartAsync(item);

        result.Should().Be(EngineStartResult.Ok);
        await PlayNow.Received(1).PlayNowAsync(Channel, item.Uri, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().AddToQueueAsync(default, default!);
        await _provider.DidNotReceiveWithAnyArgs().SkipAsync(default);
        await _provider.DidNotReceiveWithAnyArgs().PreviousAsync(default);
        await _dispatcher.DidNotReceiveWithAnyArgs().HandOverNextAsync(default);
    }

    [Fact]
    public async Task Start_with_no_player_open_is_Blocked_NoOutput_and_never_calls_PlayNow()
    {
        PlayerOpen(false);

        EngineStartResult result = await _engine.StartAsync(Item());

        result.Accepted.Should().BeFalse();
        result.Block.Should().Be(EngineBlockReason.NoOutput);
        await PlayNow.DidNotReceiveWithAnyArgs().PlayNowAsync(default, default!);
    }

    [Fact]
    public async Task Start_turns_the_provider_NoActiveDevice_throw_into_the_same_NoOutput_block()
    {
        PlaybackItem item = Item();
        PlayNow
            .PlayNowAsync(Channel, item.Uri, Arg.Any<CancellationToken>())
            .ThrowsAsync(new NoActiveDeviceException("youtube"));

        EngineStartResult result = await _engine.StartAsync(item);

        result.Block.Should().Be(EngineBlockReason.NoOutput);
    }

    [Fact]
    public async Task Start_that_PlayNow_declines_is_Refused_not_Blocked()
    {
        PlaybackItem item = Item();
        PlayNow.PlayNowAsync(Channel, item.Uri, Arg.Any<CancellationToken>()).Returns(false);

        EngineStartResult result = await _engine.StartAsync(item);

        result.Accepted.Should().BeFalse();
        result.Block.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Previous_is_Seek_zero_then_Start_and_the_previous_member_is_never_called()
    {
        PlaybackItem item = Item();
        PlayNow.PlayNowAsync(Channel, item.Uri, Arg.Any<CancellationToken>()).Returns(true);

        await _engine.SeekAsync(0);
        await _engine.StartAsync(item);

        await _provider.Received(1).SeekAsync(Channel, 0, Arg.Any<CancellationToken>());
        await PlayNow.Received(1).PlayNowAsync(Channel, item.Uri, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().PreviousAsync(default);
    }

    [Fact]
    public async Task Stop_sends_youtube_stop_through_the_dispatcher_and_not_pause_or_skip()
    {
        await _engine.StopAsync();

        await _dispatcher.Received(1).StopAsync(Channel, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().PauseAsync(default);
        await _provider.DidNotReceiveWithAnyArgs().SkipAsync(default);
    }

    [Fact]
    public async Task Resume_plays_this_engine_only()
    {
        await _engine.ResumeAsync();

        await _provider.Received(1).PlayAsync(Channel, Arg.Any<CancellationToken>());
        await _provider.DidNotReceiveWithAnyArgs().PauseAsync(default);
        await _dispatcher.DidNotReceiveWithAnyArgs().StopAsync(default);
    }

    [Fact]
    public async Task Pause_with_no_player_throws_Blocked_NoOutput_and_sends_nothing()
    {
        PlayerOpen(false);

        Func<Task> act = () => _engine.PauseAsync();

        (await act.Should().ThrowAsync<PlaybackEngineBlockedException>())
            .Which.Should()
            .Match<PlaybackEngineBlockedException>(e =>
                e.Reason == EngineBlockReason.NoOutput && e.Engine == EngineKind.YouTube
            );
        await _provider.DidNotReceiveWithAnyArgs().PauseAsync(default);
    }

    [Fact]
    public async Task Seek_sends_whole_seconds_and_volume_goes_to_the_provider()
    {
        await _engine.SeekAsync(12_900);
        await _engine.SetVolumeAsync(40);

        await _provider.Received(1).SeekAsync(Channel, 12, Arg.Any<CancellationToken>());
        await _provider.Received(1).SetVolumeAsync(Channel, 40, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Shuffle_repeat_devices_transfer_and_token_are_NotSupported_and_never_reach_the_provider()
    {
        EngineControlResult shuffle = await _engine.SetShuffleAsync(true);
        EngineControlResult repeat = await _engine.SetRepeatAsync(MusicRepeatMode.Track);
        EngineControlResult<IReadOnlyList<MusicDeviceInfo>> devices =
            await _engine.GetDevicesAsync();
        EngineControlResult transfer = await _engine.TransferAsync("d", true);
        EngineControlResult<string> token = await _engine.GetEmbeddedTokenAsync();

        shuffle.Should().Be(EngineControlResult.NotSupported);
        repeat.Should().Be(EngineControlResult.NotSupported);
        transfer.Should().Be(EngineControlResult.NotSupported);
        devices.Outcome.Should().Be(EngineControlOutcome.NotSupported);
        token.Outcome.Should().Be(EngineControlOutcome.NotSupported);
        await _provider.DidNotReceiveWithAnyArgs().SetShuffleAsync(default, default);
        await _provider.DidNotReceiveWithAnyArgs().SetRepeatAsync(default, default);
        await _provider.DidNotReceiveWithAnyArgs().GetDevicesAsync(default);
        await _provider
            .DidNotReceiveWithAnyArgs()
            .TransferPlaybackAsync(default, default!, default);
        await _provider.DidNotReceiveWithAnyArgs().GetEmbeddedPlaybackTokenAsync(default);
    }

    [Fact]
    public void Kind_is_YouTube_and_Capabilities_come_from_the_provider()
    {
        _engine.Kind.Should().Be(EngineKind.YouTube);
        _engine
            .Capabilities.Should()
            .Be(MusicProviderCapabilities.PlaybackControl | MusicProviderCapabilities.Seek);
    }

    [Fact]
    public async Task A_player_report_is_pushed_in_the_shared_report_shape_with_the_pushed_length()
    {
        _store
            .PushedTrack(Channel, VideoId)
            .Returns(new YouTubeQueuedTrack(VideoId, "T", "A", 212_000));

        await _engine.ReportAsync(Report(YouTubePlayerState.Playing, 15_000));

        _signals
            .Should()
            .Equal(new EngineReport(EngineKind.YouTube, WatchUrl, true, 15_000, 212_000));
    }

    [Fact]
    public async Task A_paused_report_says_not_playing()
    {
        await _engine.ReportAsync(Report(YouTubePlayerState.Paused, 9_000));

        _signals
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<EngineReport>()
            .Which.IsPlaying.Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(YouTubePlayerState.Ended)]
    [InlineData(YouTubePlayerState.Error)]
    public async Task An_ended_or_errored_player_adds_TrackEnded_after_the_report(
        YouTubePlayerState state
    )
    {
        await _engine.ReportAsync(Report(state, 211_000));

        _signals.Should().HaveCount(2);
        _signals[0].Should().BeOfType<EngineReport>();
        _signals[1].Should().Be(new TrackEnded(EngineKind.YouTube, WatchUrl));
    }

    [Fact]
    public async Task A_report_keeps_the_uri_the_item_was_started_with()
    {
        PlaybackItem item = Item() with { Uri = $"https://www.youtube.com/watch?v={VideoId}" };
        PlayNow.PlayNowAsync(Channel, item.Uri, Arg.Any<CancellationToken>()).Returns(true);
        await _engine.StartAsync(item);

        await _engine.ReportAsync(Report(YouTubePlayerState.Playing, 1_000));

        _signals.Should().ContainSingle().Which.Uri.Should().Be(item.Uri);
    }

    private static YouTubePlayerReport Report(YouTubePlayerState state, long positionMs) =>
        new(VideoId, state, positionMs, DateTimeOffset.UnixEpoch);
}
