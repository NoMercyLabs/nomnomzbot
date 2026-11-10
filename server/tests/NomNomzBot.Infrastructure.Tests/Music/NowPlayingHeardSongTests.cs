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
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Music.ValueObjects;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Stoney, 2026-11-10: "the youtube music is playing but the state is only reflecting spotify" and "the !song
/// command needs to reply with the correct song too". While a video request plays, Spotify waits on pause.
/// Every reader of now playing gets the video that is heard, not the Spotify song that waits.
/// </summary>
public sealed class NowPlayingHeardSongTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000be42");
    private const string VideoUri = "https://www.youtube.com/watch?v=EQAUupOFarw";

    private sealed class Harness
    {
        public required MusicService Sut { get; init; }
        public required NowPlayingCache Cache { get; init; }
        public required IPlaybackStateReader State { get; init; }
        public string Channel => ChannelId.ToString();
    }

    private static Harness Build()
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
        db.SaveChanges();

        IMusicProvider spotify = Provider("spotify", WaitingSpotifySong());
        IMusicProvider youtube = Provider("youtube", PlayingVideo());
        youtube.WorksWithoutConnectionAsync(Arg.Any<CancellationToken>()).Returns(true);

        IPlaybackStateReader state = Substitute.For<IPlaybackStateReader>();
        state.StateOf(ChannelId).Returns(PlaybackState.Idle);
        NowPlayingCache cache = new();
        return new()
        {
            Sut = new(
                [spotify, youtube],
                db,
                new RecordingEventBus(),
                new BlockedTrackService(db),
                DirectorRig.NewStore(out SongRequestQueueStore directorStore),
                new NoOpSongRequestQueuePersistence(),
                NullLogger<MusicService>.Instance,
                new InMemoryIntegrationCapabilityStore(),
                PermissiveMusicConfigService.Instance,
                Substitute.For<ICurrencyAccountService>(),
                cache,
                new OutboundSanctionAccessor(),
                Substitute.For<IUserIdentityService>(),
                Substitute.For<IForeignLinkTitleLookup>(),
                DirectorRig.Of(directorStore).Registry,
                state
            ),
            Cache = cache,
            State = state,
        };
    }

    private static IMusicProvider Provider(string name, TrackInfo current)
    {
        IMusicProvider provider = Substitute.For<IMusicProvider>();
        provider.Provider.Returns(name);
        provider.Capabilities.Returns(MusicProviderCapabilities.NowPlaying);
        provider
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Task.FromResult<TrackInfo?>(current));
        return provider;
    }

    private static TrackInfo WaitingSpotifySong() =>
        new()
        {
            TrackName = "Waiting Song",
            Artist = "Spotify Artist",
            Album = "Album",
            TrackUri = "spotify:track:waiting",
            Provider = "spotify",
            DurationMs = 200_000,
            ProgressMs = 0,
            IsPlaying = false,
        };

    private static TrackInfo PlayingVideo() =>
        new()
        {
            TrackName = "Why Can't We Be Friends",
            Artist = "The Big Push",
            Album = string.Empty,
            TrackUri = VideoUri,
            Provider = "youtube",
            DurationMs = 240_000,
            ProgressMs = 42_000,
            IsPlaying = true,
        };

    private static PlaybackItem VideoRequest() =>
        new(
            "row-1",
            "marcipopsis",
            EngineKind.YouTube,
            VideoUri,
            "Why Can't We Be Friends",
            ["The Big Push"],
            null,
            240_000,
            "MarciPopsis"
        );

    [Theory]
    [InlineData(PlaybackStateKind.Playing)]
    [InlineData(PlaybackStateKind.PausedByOwner)]
    public async Task A_reader_gets_the_video_that_is_heard_while_the_Spotify_song_waits(
        PlaybackStateKind kind
    )
    {
        Harness h = Build();
        h.Cache.Set(ChannelId, WaitingSpotifySong(), DateTimeOffset.UtcNow);
        h.State.StateOf(ChannelId)
            .Returns(new PlaybackState(kind, EngineKind.YouTube, VideoRequest()));

        NowPlaying? now = await h.Sut.GetNowPlayingAsync(h.Channel);

        now.Should().NotBeNull();
        now.TrackName.Should().Be("Why Can't We Be Friends");
        now.Artist.Should().Be("The Big Push");
        now.Provider.Should().Be("youtube");
        now.TrackUri.Should().Be(VideoUri);
        now.ProgressMs.Should().Be(42_000);
        now.IsPlaying.Should().BeTrue();
    }

    [Theory]
    [InlineData(PlaybackStateKind.Playing, true)]
    [InlineData(PlaybackStateKind.PausedByOwner, false)]
    public async Task The_play_pause_hint_follows_the_video_not_the_Spotify_song_that_waits(
        PlaybackStateKind kind,
        bool expected
    )
    {
        Harness h = Build();
        h.Cache.Set(ChannelId, WaitingSpotifySong(), DateTimeOffset.UtcNow);
        h.State.StateOf(ChannelId)
            .Returns(new PlaybackState(kind, EngineKind.YouTube, VideoRequest()));

        bool? isPlaying = await h.Sut.TryGetCachedIsPlayingAsync(h.Channel);

        isPlaying.Should().Be(expected);
    }

    [Fact]
    public async Task The_background_poll_still_reads_the_Spotify_song_that_waits_so_its_engine_is_fed()
    {
        Harness h = Build();
        h.State.StateOf(ChannelId)
            .Returns(PlaybackState.Playing(EngineKind.YouTube, VideoRequest()));

        NowPlaying? polled = await h.Sut.GetNowPlayingAsync(h.Channel, isBackgroundPoll: true);

        polled.Should().NotBeNull();
        polled.TrackUri.Should().Be("spotify:track:waiting");
        polled.Provider.Should().Be("spotify");
    }

    [Fact]
    public async Task A_video_that_is_still_starting_does_not_replace_the_Spotify_song_that_is_heard()
    {
        Harness h = Build();
        h.State.StateOf(ChannelId)
            .Returns(PlaybackState.Starting(EngineKind.YouTube, VideoRequest()));

        NowPlaying? now = await h.Sut.GetNowPlayingAsync(h.Channel);

        now.Should().NotBeNull();
        now.TrackUri.Should().Be("spotify:track:waiting");
    }
}
