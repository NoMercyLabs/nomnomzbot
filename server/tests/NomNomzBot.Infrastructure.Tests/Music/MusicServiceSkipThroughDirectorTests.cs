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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using static NomNomzBot.Infrastructure.Tests.Music.MusicServiceDirectorIntakeTests;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A skip goes to the playback director as an OwnerSkip input and to nothing else: the director stops the
/// playing song and starts the next head once, and an engine that refuses reaches the caller as the typed
/// error that says why. The provider's own skip call is never made.
/// </summary>
public sealed class MusicServiceSkipThroughDirectorTests
{
    private const string FirstUri = "spotify:track:s1";

    /// <summary>An internal user id (a Guid), so the skip needs no identity lookup.</summary>
    private static readonly string Skipper = Guid.Parse("0192a000-0000-7000-8000-000000007c99")
        .ToString();

    private static void SeedNowPlaying(NowPlayingCache cache) =>
        cache.Set(
            SpotifyChannel,
            new()
            {
                TrackName = "Song s1",
                Artist = "Artist",
                Album = "Album",
                TrackUri = FirstUri,
                Provider = "spotify",
                IsPlaying = true,
                DurationMs = 200_000,
            },
            DateTimeOffset.UtcNow
        );

    /// <summary>Requests the given tracks on an idle channel and lets the engine report the first one as playing.</summary>
    private static async Task<(
        MusicService Sut,
        RecordingHttpHandler Handler,
        DirectorRig Rig,
        RecordingEventBus Bus,
        FakeEngine Spotify
    )> PlayingAsync(params string[] trackIds)
    {
        NowPlayingCache cache = new();
        RecordingEventBus bus = new();
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify(
            cache,
            bus
        );
        FakeEngine spotify = rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify);
        foreach (string id in trackIds)
            (await RequestAsync(sut, handler, id, $"viewer-{id}")).IsSuccess.Should().BeTrue();

        await spotify.EmitAsync(
            new EngineReport(EngineKind.Spotify, FirstUri, true, 1_000, 200_000)
        );
        rig.Registry.StateOf(SpotifyChannel).Kind.Should().Be(PlaybackStateKind.Playing);
        SeedNowPlaying(cache);

        return (sut, handler, rig, bus, spotify);
    }

    private static bool CalledProviderSkip(RecordingHttpHandler handler) =>
        handler.RequestUrls.Any(url =>
            url.StartsWith("POST", StringComparison.Ordinal)
            && url.Contains("/me/player/next", StringComparison.Ordinal)
        );

    [Fact]
    public async Task Skipping_the_playing_head_moves_Spotify_on_to_the_lined_up_next_head_without_pausing_and_never_calls_the_provider()
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            DirectorRig rig,
            RecordingEventBus bus,
            FakeEngine spotify
        ) = await PlayingAsync("s1", "s2");

        Result result = await sut.SkipAsync(SpotifyChannel.ToString(), Skipper);

        result.IsSuccess.Should().BeTrue();
        spotify.Calls.Select(c => c.Name).Should().Equal("Start", "QueueNext", "Skip");
        PlaybackState state = rig.Registry.StateOf(SpotifyChannel);
        state.Kind.Should().Be(PlaybackStateKind.Starting);
        state.Item!.Uri.Should().Be("spotify:track:s2");
        spotify
            .Calls[1]
            .Argument.Should()
            .Be(state.Item.Id, "the lined-up request is the next head");
        CalledProviderSkip(handler).Should().BeFalse("the director skips, not the provider");
        bus.Published.OfType<SongSkippedEvent>()
            .Should()
            .ContainSingle(e => e.TrackName == "Song s1");
    }

    [Fact]
    public async Task Skipping_with_an_empty_queue_moves_Spotify_on_to_the_owners_music_and_the_skip_is_still_announced()
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            DirectorRig rig,
            RecordingEventBus bus,
            FakeEngine spotify
        ) = await PlayingAsync("s1");

        Result result = await sut.SkipAsync(SpotifyChannel.ToString(), Skipper);

        result.IsSuccess.Should().BeTrue();
        spotify.Calls.Select(c => c.Name).Should().Equal("Start", "Skip");
        rig.Registry.StateOf(SpotifyChannel)
            .Should()
            .Be(PlaybackState.Playing(EngineKind.Spotify, null));
        CalledProviderSkip(handler).Should().BeFalse();
        bus.Published.OfType<SongSkippedEvent>()
            .Should()
            .ContainSingle(e => e.TrackName == "Song s1");
    }

    [Fact]
    public async Task Retracting_the_request_that_plays_right_now_skips_it_at_once_and_says_so()
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            _,
            RecordingEventBus bus,
            FakeEngine spotify
        ) = await PlayingAsync("s1", "s2");
        string channel = SpotifyChannel.ToString();
        // The state seen live: the request is still in the fair queue while the provider already plays it.
        (await RequestAsync(sut, handler, "s1", "viewer-wrong"))
            .IsSuccess.Should()
            .BeTrue();
        int playing = (await sut.GetQueueAsync(channel))
            .Queue.Select((item, index) => (item, index))
            .Single(entry => entry.item.TrackName == "Song s1")
            .index;

        RequestRetraction result = await sut.RetractRequestAsync(channel, playing);

        result.Should().Be(RequestRetraction.RemovedAndSkippedNow);
        spotify.Calls.Select(c => c.Name).Should().Contain("Skip");
        (await sut.GetQueueAsync(channel)).Queue.Should().NotContain(i => i.TrackName == "Song s1");
        bus.Published.OfType<SongSkippedEvent>()
            .Should()
            .ContainSingle(e => e.TrackName == "Song s1");
    }

    [Fact]
    public async Task Retracting_a_request_that_is_not_playing_skips_nothing_now()
    {
        (MusicService sut, _, _, RecordingEventBus bus, FakeEngine spotify) = await PlayingAsync(
            "s1",
            "s2"
        );
        string channel = SpotifyChannel.ToString();
        int waiting = (await sut.GetQueueAsync(channel))
            .Queue.Select((item, index) => (item, index))
            .Single(entry => entry.item.TrackName == "Song s2")
            .index;

        RequestRetraction result = await sut.RetractRequestAsync(channel, waiting);

        result.Should().NotBe(RequestRetraction.RemovedAndSkippedNow);
        spotify.Calls.Select(c => c.Name).Should().NotContain("Skip");
        bus.Published.OfType<SongSkippedEvent>().Should().BeEmpty();
    }

    [Theory]
    [InlineData(EngineBlockReason.Forbidden, "MUSIC_FORBIDDEN")]
    [InlineData(EngineBlockReason.NoOutput, "NO_ACTIVE_DEVICE")]
    public async Task Skipping_when_the_engine_refuses_returns_the_typed_error_and_throws_nothing(
        EngineBlockReason block,
        string expectedCode
    )
    {
        (
            MusicService sut,
            RecordingHttpHandler handler,
            DirectorRig rig,
            RecordingEventBus bus,
            FakeEngine spotify
        ) = await PlayingAsync("s1", "s2");
        spotify.BlockCommands = block;

        Result result = await sut.SkipAsync(SpotifyChannel.ToString(), Skipper);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(expectedCode);
        rig.Registry.StateOf(SpotifyChannel).Kind.Should().Be(PlaybackStateKind.Blocked);
        CalledProviderSkip(handler).Should().BeFalse();
        bus.Published.OfType<SongSkippedEvent>().Should().BeEmpty("a refused skip skipped nothing");
    }

    [Theory]
    [InlineData(EngineBlockReason.NotConnected, "MUSIC_AUTH_FAILED", "reconnected")]
    [InlineData(EngineBlockReason.Forbidden, "MUSIC_FORBIDDEN", "permission")]
    [InlineData(EngineBlockReason.NoOutput, "NO_ACTIVE_DEVICE", "Nothing is playing")]
    public async Task A_blocked_skip_is_worded_as_a_skip_and_never_as_a_queue_request(
        EngineBlockReason block,
        string expectedCode,
        string expectedPhrase
    )
    {
        (MusicService sut, _, _, _, FakeEngine spotify) = await PlayingAsync("s1", "s2");
        spotify.BlockCommands = block;

        Result result = await sut.SkipAsync(SpotifyChannel.ToString(), Skipper);

        result.ErrorCode.Should().Be(expectedCode);
        result.ErrorMessage.Should().NotContainEquivalentOf("queue");
        result.ErrorMessage.Should().Contain(expectedPhrase);
    }

    [Fact]
    public async Task A_blocked_skip_for_any_other_reason_says_the_music_service_had_a_problem_not_queue()
    {
        (MusicService sut, _, _, _, FakeEngine spotify) = await PlayingAsync("s1", "s2");
        spotify.BlockCommands = (EngineBlockReason)99;

        Result result = await sut.SkipAsync(SpotifyChannel.ToString(), Skipper);

        result.ErrorCode.Should().Be("PROVIDER_ERROR");
        result.ErrorMessage.Should().NotContainEquivalentOf("queue");
        result.ErrorMessage.Should().Contain("skip");
    }
}
