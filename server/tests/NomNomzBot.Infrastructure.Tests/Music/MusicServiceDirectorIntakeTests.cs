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

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// A song request goes to the playback director as a RequestAdded input and to nothing else. The director
/// starts the head on its engine exactly once, and a start the engine refuses reaches the requester as the
/// typed error that says why, with the refused entry taken back out of the queue.
/// </summary>
public sealed class MusicServiceDirectorIntakeTests
{
    internal static readonly Guid SpotifyChannel = Guid.Parse(
        "0192a000-0000-7000-8000-000000007c01"
    );

    internal static string SearchJson(string id) =>
        """
            {"tracks":{"items":[{"name":"Song __ID__","uri":"spotify:track:__ID__","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}}]}}
            """.Replace("__ID__", id);

    [Fact]
    public async Task A_request_on_an_idle_channel_starts_the_song_once_and_the_director_is_Starting()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify();
        FakeEngine spotify = rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify);

        Result result = await RequestAsync(sut, handler, "a1", "viewer1");

        result.IsSuccess.Should().BeTrue();
        spotify.Calls.Where(c => c.Name == "Start").Should().HaveCount(1);
        PlaybackState state = rig.Registry.StateOf(SpotifyChannel);
        state.Kind.Should().Be(PlaybackStateKind.Starting);
        state.Item!.Uri.Should().Be("spotify:track:a1");
        rig.Bus.Published.OfType<PlaybackTransitionedEvent>()
            .Should()
            .ContainSingle(e =>
                e.From == "Idle" && e.To == "Starting" && e.Input == "RequestAdded"
            );
        (await sut.GetQueueAsync(SpotifyChannel.ToString()))
            .Queue.Select(i => i.TrackName)
            .Should()
            .Equal("Song a1");
        QueuePushCount(handler).Should().Be(0, "the director's engine starts it, not a queue push");
    }

    [Fact]
    public async Task A_second_request_while_the_first_is_Starting_does_not_start_anything_again()
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify();
        FakeEngine spotify = rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify);
        await RequestAsync(sut, handler, "b1", "viewer1");

        Result second = await RequestAsync(sut, handler, "b2", "viewer2");

        second.IsSuccess.Should().BeTrue();
        spotify.Calls.Where(c => c.Name == "Start").Should().HaveCount(1);
        rig.Registry.StateOf(SpotifyChannel)
            .Item!.Uri.Should()
            .Be("spotify:track:b1", "the second request waits behind the one that is starting");
        (await sut.GetQueueAsync(SpotifyChannel.ToString()))
            .Queue.Select(i => i.TrackName)
            .Should()
            .Equal("Song b1", "Song b2");
    }

    [Theory]
    [InlineData(EngineBlockReason.NotConnected, "MUSIC_AUTH_FAILED")]
    [InlineData(EngineBlockReason.Forbidden, "MUSIC_FORBIDDEN")]
    [InlineData(EngineBlockReason.NoOutput, "NO_ACTIVE_DEVICE")]
    public async Task A_spotify_start_the_engine_refuses_returns_the_reason_and_leaves_no_entry(
        EngineBlockReason block,
        string expectedCode
    )
    {
        (MusicService sut, RecordingHttpHandler handler, DirectorRig rig) = BuildSpotify();
        rig.Engines.EngineOf(SpotifyChannel, EngineKind.Spotify).StartResult =
            EngineStartResult.BlockedBy(block);

        Result result = await RequestAsync(sut, handler, "c1", "viewer1");

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(expectedCode);
        (await sut.GetQueueAsync(SpotifyChannel.ToString()))
            .Queue.Should()
            .BeEmpty("a refused request is rolled back, the requester was told it failed");
    }

    [Theory]
    [InlineData(EngineBlockReason.NoOutput, "NO_OVERLAY_PLAYER")]
    [InlineData(EngineBlockReason.NotConnected, "MUSIC_AUTH_FAILED")]
    [InlineData(EngineBlockReason.Forbidden, "MUSIC_FORBIDDEN")]
    public async Task A_youtube_start_the_engine_refuses_returns_the_reason_and_leaves_no_entry(
        EngineBlockReason block,
        string expectedCode
    )
    {
        YouTubeSongRequestIntake intake = new();
        intake
            .Rig.Engines.EngineOf(YouTubeSongRequestIntake.ChannelId, EngineKind.YouTube)
            .StartResult = EngineStartResult.BlockedBy(block);
        intake.RouteVideos(
            YouTubeSongRequestIntake.Video("dQw4w9WgXcQ", "Never Gonna Give You Up")
        );

        Result<MusicTrack> result = await intake.Music.RequestTrackAsync(
            YouTubeSongRequestIntake.ChannelId.ToString(),
            "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
            "viewer1"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be(expectedCode);
        (await intake.QueuedTitlesAsync()).Should().BeEmpty();
    }

    private static int QueuePushCount(RecordingHttpHandler handler) =>
        handler.RequestUrls.Count(url =>
            url.StartsWith("POST", StringComparison.Ordinal)
            && url.Contains("/me/player/queue", StringComparison.Ordinal)
        );

    internal static async Task<Result> RequestAsync(
        MusicService sut,
        RecordingHttpHandler handler,
        string trackId,
        string requestedBy
    )
    {
        handler.ClearRoutes();
        handler.RespondWhen(
            r => r.RequestUri!.AbsolutePath.EndsWith("/search", StringComparison.Ordinal),
            HttpStatusCode.OK,
            SearchJson(trackId)
        );
        handler.RespondWhen(
            r =>
                r.Method == HttpMethod.Post
                && r.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player/queue",
                    StringComparison.Ordinal
                ),
            HttpStatusCode.NoContent
        );
        return await sut.AddToQueueAsync(SpotifyChannel.ToString(), $"song {trackId}", requestedBy);
    }

    internal static (MusicService Sut, RecordingHttpHandler Handler, DirectorRig Rig) BuildSpotify(
        NowPlayingCache? nowPlaying = null,
        RecordingEventBus? bus = null,
        Func<DirectorRig, IPlaybackInputSink>? inputSink = null
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = SpotifyChannel,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(SpotifyChannel);

        RecordingHttpHandler handler = new();
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SpotifyRateLimitCooldowns(new NullActionRequiredChangeNotifier()),
            new SingleHandlerClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance),
            new OutboundSanctionAccessor()
        );

        SongRequestQueueStore store = new();
        DirectorRig rig = DirectorRig.Of(store);
        MusicService sut = new(
            [spotify],
            db,
            bus ?? new RecordingEventBus(),
            new BlockedTrackService(db),
            store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            nowPlaying ?? new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            inputSink?.Invoke(rig) ?? rig.Registry,
            rig.Registry
        );
        return (sut, handler, rig);
    }
}
