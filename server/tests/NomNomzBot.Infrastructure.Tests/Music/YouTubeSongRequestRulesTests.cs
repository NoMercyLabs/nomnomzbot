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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;
using static NomNomzBot.Infrastructure.Tests.Music.PlaybackDirectorTestKit;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// 2026-10-18: a viewer requested a YouTube link on a channel that never allowed YouTube. Spotify was paused
/// in the middle of the song and nothing played. A request follows the channel's sources, waits for the
/// current song, and a YouTube item with no overlay player is refused with a reason instead of silence.
/// </summary>
public sealed class YouTubeSongRequestRulesTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab77");
    private const string SpotifyUri = "spotify:track:abc";
    private const string YouTubeUri = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
    private const MusicProviderCapabilities Transport =
        MusicProviderCapabilities.PlaybackControl
        | MusicProviderCapabilities.Skip
        | MusicProviderCapabilities.AcceptsSongRequests
        | MusicProviderCapabilities.Search
        | MusicProviderCapabilities.Queue;

    private readonly IMusicProvider _spotify = Provider("spotify", SpotifyUri);
    private readonly IMusicProvider _youTube = Provider("youtube", YouTubeUri);
    private readonly SongRequestQueueStore _store = new();

    private sealed class FixedConfigService(MusicConfigDto config) : IMusicConfigService
    {
        public Task<Result<MusicConfigDto>> GetConfigAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(config));

        public Task<Result<MusicConfigDto>> UpdateConfigAsync(
            string broadcasterId,
            UpdateMusicConfigDto request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public MusicConfigDto GetDefaults() => config;
    }

    private static IMusicProvider Provider(string key, string ownedUri)
    {
        IMusicProvider provider = Substitute.For<IMusicProvider, IMusicProviderPlayerGate>();
        provider.Provider.Returns(key);
        provider.Capabilities.Returns(Transport);
        provider.OwnsTrackUri(Arg.Any<string>()).Returns(call => (string)call[0] == ownedUri);
        provider
            .WorksWithoutConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(key == "youtube");
        provider
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        provider
            .ResolveTrackAsync(ChannelId, ownedUri, Arg.Any<CancellationToken>())
            .Returns(
                (
                    new TrackInfo
                    {
                        TrackName = "Song",
                        Artist = "Artist",
                        Album = "",
                        TrackUri = ownedUri,
                        Provider = key,
                        DurationMs = 200_000,
                    },
                    MusicProviderFailureReason.None
                )
            );
        ((IMusicProviderPlayerGate)provider)
            .HasPlayerAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(true);
        return provider;
    }

    private static TrackInfo Playing(string uri, bool isPlaying, int progressMs) =>
        new()
        {
            TrackName = "t",
            Artist = "a",
            Album = "",
            TrackUri = uri,
            Provider = "x",
            IsPlaying = isPlaying,
            ProgressMs = progressMs,
            DurationMs = 200_000,
        };

    private MusicService NewService(
        bool allowYouTube = true,
        IMusicConfigService? configService = null
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.Services.Add(
            new()
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        // These tests are about queue behavior, so the channel has opened YouTube requests to everyone.
        // The per-service role rules live in SongRequestSourceRoleTests.
        db.OpenRequestsToEveryone(ChannelId, "youtube");
        MusicConfigDto config = new(true, "auto", 50, 5, allowYouTube, true, "everyone");
        return new(
            [_spotify, _youTube],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            _store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            configService ?? new FixedConfigService(config),
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>(),
            DirectorRig.Of(_store).Registry,
            DirectorRig.Of(_store).Registry
        );
    }

    [Fact]
    public async Task A_youtube_request_is_refused_and_nothing_is_queued_when_youtube_is_not_allowed()
    {
        MusicService sut = NewService(allowYouTube: false);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "fillz"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("PROVIDER_NOT_ALLOWED");
        _store.GetOrCreate(ChannelId.ToString()).GetSnapshot().Should().BeEmpty();
        await _youTube
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _spotify.DidNotReceive().PauseAsync(ChannelId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_request_that_arrives_while_spotify_plays_a_song_never_pauses_that_song()
    {
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", isPlaying: true, progressMs: 90_000));
        MusicService sut = NewService();
        await SpotifyReportsPlayingAsync(90_000);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "fillz"
        );

        result.IsSuccess.Should().BeTrue();
        await _spotify.DidNotReceive().PauseAsync(ChannelId, Arg.Any<CancellationToken>());
        await _youTube
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store
            .GetOrCreate(ChannelId.ToString())
            .Peek()!
            .TrackUri.Should()
            .Be(YouTubeUri, "the request waits its turn in the fair queue");
        // Spec music-playback-determinism section 4: Playing(spotify, null) + RequestAdded keeps the
        // request waiting. A request before the first Spotify report is covered by the engine probe
        // (PlaybackDirectorFirstReportTests).
        YouTubeStartCount().Should().Be(0, "the request waits for the playing song");
        DirectorRig
            .Of(_store)
            .Registry.StateOf(ChannelId)
            .Should()
            .Be(PlaybackState.Playing(EngineKind.Spotify, null));
        _store.GetInFlight(ChannelId.ToString()).Should().BeNull();
    }

    /// <summary>The Spotify poller's report of the owner's own song, as the director receives it.</summary>
    private Task SpotifyReportsPlayingAsync(int progressMs) =>
        DirectorRig
            .Of(_store)
            .Registry.SendAsync(
                ChannelId,
                new EngineReport(
                    EngineKind.Spotify,
                    "spotify:track:playlist",
                    true,
                    progressMs,
                    200_000
                )
            );

    private FakeEngine YouTubeEngine() =>
        DirectorRig.Of(_store).Engines.EngineOf(ChannelId, EngineKind.YouTube);

    private int YouTubeStartCount() => YouTubeEngine().Calls.Count(c => c.Name == "Start");

    private FakeEngine SpotifyEngine() =>
        DirectorRig.Of(_store).Engines.EngineOf(ChannelId, EngineKind.Spotify);

    [Fact]
    public async Task A_song_that_is_not_near_its_end_is_never_paused_by_the_waiting_request()
    {
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", isPlaying: true, progressMs: 120_000));
        await SpotifyReportsPlayingAsync(120_000);
        _store.GetOrCreate(ChannelId.ToString()).Enqueue("fillz", Entry(YouTubeUri));

        await DirectorRig.Of(_store).Registry.SendAsync(ChannelId, new RequestAdded());

        YouTubeStartCount().Should().Be(0, "the director keeps the request waiting for the song");
        await _spotify.DidNotReceive().PauseAsync(ChannelId, Arg.Any<CancellationToken>());
        await _youTube
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        _store.GetOrCreate(ChannelId.ToString()).Peek()!.TrackUri.Should().Be(YouTubeUri);
    }

    [Fact]
    public async Task The_request_waits_for_the_song_and_starts_once_Spotify_is_paused_at_its_end()
    {
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", isPlaying: true, progressMs: 90_000));
        MusicService sut = NewService();
        await SpotifyReportsPlayingAsync(90_000);
        await sut.RequestTrackAsync(ChannelId.ToString(), YouTubeUri, "fillz");
        SpotifyEngine().Calls.Should().BeEmpty("the song plays on");
        YouTubeStartCount().Should().Be(0, "the request waits while the song still plays");

        // The song ends: the director pauses Spotify's next song, puts it back to its start, and waits
        // until Spotify confirms the pause.
        await DirectorRig
            .Of(_store)
            .Registry.SendAsync(
                ChannelId,
                new TrackEnded(EngineKind.Spotify, "spotify:track:playlist")
            );

        SpotifyEngine().Calls.Select(c => c.Name).Should().Equal("Stop", "Seek");
        YouTubeStartCount().Should().Be(0, "YouTube starts only once Spotify is quiet");

        // Spotify confirms the pause: the director starts the waiting head on YouTube, exactly once.
        await DirectorRig
            .Of(_store)
            .Registry.SendAsync(
                ChannelId,
                new EngineReport(EngineKind.Spotify, "spotify:track:next", false, 0, 200_000)
            );

        YouTubeStartCount()
            .Should()
            .Be(1, "the director starts the request once, when Spotify is paused");
        PlaybackState started = DirectorRig.Of(_store).Registry.StateOf(ChannelId);
        started.Kind.Should().Be(PlaybackStateKind.Starting);
        started.Engine.Should().Be(EngineKind.YouTube);
        started.Item!.Uri.Should().Be(YouTubeUri);
    }

    [Fact]
    public async Task A_channel_that_never_set_the_youtube_toggle_refuses_a_youtube_link()
    {
        using MusicConfigDbFixture fixture = new();
        (await fixture.Service.GetConfigAsync(ChannelId.ToString()))
            .Value.AllowYouTube.Should()
            .BeFalse("YouTube is opt-in");
        MusicService sut = NewService(configService: fixture.Service);

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "fillz"
        );

        result.ErrorCode.Should().Be("PROVIDER_NOT_ALLOWED");
        await _youTube
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_youtube_request_with_no_overlay_player_is_refused_and_spotify_keeps_playing()
    {
        // The YouTube engine refuses the start because no overlay player is open.
        YouTubeEngine().StartResult = EngineStartResult.BlockedBy(EngineBlockReason.NoOutput);
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", isPlaying: false, progressMs: 0));
        MusicService sut = NewService();

        Result<MusicTrack> result = await sut.RequestTrackAsync(
            ChannelId.ToString(),
            YouTubeUri,
            "fillz"
        );

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("NO_OVERLAY_PLAYER");
        YouTubeStartCount().Should().Be(1, "the engine was asked once and refused");
        DirectorRig
            .Of(_store)
            .Registry.StateOf(ChannelId)
            .Kind.Should()
            .Be(PlaybackStateKind.Blocked);
        _store.GetOrCreate(ChannelId.ToString()).GetSnapshot().Should().BeEmpty();
        await _spotify.DidNotReceive().PauseAsync(ChannelId, Arg.Any<CancellationToken>());
        await _youTube
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private static SongRequestEntry Entry(string uri) =>
        new(uri, "Track", "Artist", null, 200_000, "fillz", 0, null, "");
}
