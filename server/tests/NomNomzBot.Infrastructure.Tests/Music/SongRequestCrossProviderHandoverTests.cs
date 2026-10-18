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
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// music-sr.md §3.5.2: with Spotify and YouTube both on, the queue is interleaved and each item plays on
/// its own engine, one at a time. The outgoing engine is stopped and seen stopped before the incoming one
/// starts, and an engine still playing a request of ours is never cut off.
/// </summary>
public sealed class SongRequestCrossProviderHandoverTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-00000000ab21");
    private const string SpotifyUri = "spotify:track:abc";
    private const string YouTubeUri = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
    private const MusicProviderCapabilities Transport =
        MusicProviderCapabilities.PlaybackControl
        | MusicProviderCapabilities.Skip
        | MusicProviderCapabilities.AcceptsSongRequests;

    private readonly IMusicProvider _spotify = Provider("spotify", SpotifyUri);
    private readonly IMusicProvider _youTube = Provider("youtube", YouTubeUri);
    private readonly SongRequestQueueStore _store = new();

    private static IMusicProvider Provider(string key, string ownedUri)
    {
        IMusicProvider provider =
            key == "spotify"
                ? Substitute.For<IMusicProvider, IMusicProviderStartTrack>()
                : Substitute.For<IMusicProvider>();
        provider.Provider.Returns(key);
        if (provider is IMusicProviderStartTrack starter)
            starter
                .StartTrackAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(true);
        provider.Capabilities.Returns(Transport);
        provider.OwnsTrackUri(Arg.Any<string>()).Returns(call => (string)call[0] == ownedUri);
        provider
            .WorksWithoutConnectionAsync(Arg.Any<CancellationToken>())
            .Returns(key == "youtube");
        provider
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return provider;
    }

    private static TrackInfo Playing(string uri, bool isPlaying, int progressMs = 196_000) =>
        new()
        {
            TrackName = "t",
            Artist = "a",
            Album = "",
            TrackUri = uri,
            Provider = "x",
            IsPlaying = isPlaying,
            DurationMs = 200_000,
            ProgressMs = progressMs,
        };

    private static SongRequestEntry Entry(string uri) =>
        new(uri, "Track", "Artist", null, 200_000, "viewer", 0, null, "");

    private MusicService NewService()
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
        return new(
            [_spotify, _youTube],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            _store,
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache(),
            new OutboundSanctionAccessor(),
            Substitute.For<IUserIdentityService>(),
            Substitute.For<IForeignLinkTitleLookup>()
        );
    }

    private void Enqueue(string uri) =>
        _store.GetOrCreate(ChannelId.ToString()).Enqueue("viewer", Entry(uri));

    [Fact]
    public async Task A_youtube_request_is_handed_to_youtube_not_to_the_active_spotify()
    {
        Enqueue(YouTubeUri);

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await _youTube
            .Received(1)
            .AddToQueueAsync(ChannelId, YouTubeUri, Arg.Any<CancellationToken>());
        await _spotify
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.GetInFlight(ChannelId.ToString())!.TrackUri.Should().Be(YouTubeUri);
    }

    [Fact]
    public async Task Spotify_playing_its_own_music_is_paused_and_seen_stopped_before_youtube_plays()
    {
        Enqueue(YouTubeUri);
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(
                Playing("spotify:track:playlist", true),
                Playing("spotify:track:playlist", false)
            );

        await NewService().HandOverNextAsync(ChannelId.ToString());

        Received.InOrder(() =>
        {
            _spotify.GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>());
            _spotify.PauseAsync(ChannelId, Arg.Any<CancellationToken>());
            _spotify.GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>());
            _youTube.AddToQueueAsync(ChannelId, YouTubeUri, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Youtube_waits_when_spotify_does_not_stop()
    {
        Enqueue(YouTubeUri);
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", true));

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await _youTube
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        _store.GetOrCreate(ChannelId.ToString()).Peek()!.TrackUri.Should().Be(YouTubeUri);
    }

    [Fact]
    public async Task A_new_spotify_request_never_jumps_a_youtube_request_that_waits_its_turn()
    {
        // Live 2026-10-18: the YouTube head waited for Spotify's song to end; the next Spotify request
        // went straight into Spotify's queue, played first, and the reconciler then dropped the head.
        Enqueue(YouTubeUri);
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", true, progressMs: 50_000));
        _spotify
            .ResolveTrackAsync(ChannelId, SpotifyUri, Arg.Any<CancellationToken>())
            .Returns((Playing(SpotifyUri, false), MusicProviderFailureReason.None));

        Result admitted = await NewService()
            .AddToQueueAsync(ChannelId.ToString(), SpotifyUri, "second-viewer");

        admitted.IsSuccess.Should().BeTrue();
        await _spotify
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, SpotifyUri, Arg.Any<CancellationToken>());
        _store.GetInFlight(ChannelId.ToString()).Should().BeNull();
        List<string> pending =
        [
            .. _store.GetOrCreate(ChannelId.ToString()).GetSnapshot().Select(e => e.Item.TrackUri),
        ];
        pending.Should().Equal(YouTubeUri, SpotifyUri);
    }

    [Fact]
    public async Task A_youtube_request_still_playing_is_never_cut_off_by_the_next_spotify_item()
    {
        Enqueue(SpotifyUri);
        _youTube
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing(YouTubeUri, true));
        _store.SetPlayingRequest(ChannelId.ToString(), Entry(YouTubeUri));

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await _youTube.DidNotReceive().PauseAsync(ChannelId, Arg.Any<CancellationToken>());
        await _spotify
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task After_a_failed_youtube_item_the_next_spotify_item_is_the_track_that_starts()
    {
        const string stray = "spotify:track:pizza-calzone";
        Enqueue(SpotifyUri);
        _store.SetLastEngine(ChannelId.ToString(), "youtube");
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", false));
        _spotify
            .GetQueueAsync(ChannelId, Arg.Any<CancellationToken>())
            .Returns(new List<TrackInfo> { Playing(stray, false) });

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await ((IMusicProviderStartTrack)_spotify)
            .Received(1)
            .StartTrackAsync(ChannelId, SpotifyUri, Arg.Any<CancellationToken>());
        await _spotify.DidNotReceive().SkipAsync(ChannelId, Arg.Any<CancellationToken>());
        await _spotify
            .DidNotReceive()
            .AddToQueueAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        _store.GetLastEngine(ChannelId.ToString()).Should().Be("spotify");
        _store.GetInFlight(ChannelId.ToString())!.TrackUri.Should().Be(SpotifyUri);
    }

    [Fact]
    public async Task An_engine_switch_falls_back_to_push_and_skip_when_play_now_is_refused()
    {
        Enqueue(SpotifyUri);
        _store.SetLastEngine(ChannelId.ToString(), "youtube");
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", false));
        ((IMusicProviderStartTrack)_spotify)
            .StartTrackAsync(ChannelId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(false);

        await NewService().HandOverNextAsync(ChannelId.ToString());

        Received.InOrder(() =>
        {
            _spotify.AddToQueueAsync(ChannelId, SpotifyUri, Arg.Any<CancellationToken>());
            _spotify.SkipAsync(ChannelId, Arg.Any<CancellationToken>());
        });
        _store.GetLastEngine(ChannelId.ToString()).Should().Be("spotify");
    }

    [Fact]
    public async Task Back_to_back_spotify_items_are_drip_fed_without_a_skip()
    {
        Enqueue(SpotifyUri);
        _store.SetLastEngine(ChannelId.ToString(), "spotify");
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:other", false));

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await _spotify
            .Received(1)
            .AddToQueueAsync(ChannelId, SpotifyUri, Arg.Any<CancellationToken>());
        await _spotify.DidNotReceive().SkipAsync(ChannelId, Arg.Any<CancellationToken>());
    }

    private void EmptyQueueAfter(string lastEngine, bool spotifyPlaying)
    {
        _store.GetOrCreate(ChannelId.ToString());
        _store.SetLastEngine(ChannelId.ToString(), lastEngine);
        _spotify
            .GetCurrentTrackAsync(ChannelId, Arg.Any<CancellationToken>(), Arg.Any<bool>())
            .Returns(Playing("spotify:track:playlist", spotifyPlaying));
    }

    [Fact]
    public async Task A_dry_queue_after_a_youtube_request_resumes_spotify_once()
    {
        EmptyQueueAfter("youtube", spotifyPlaying: false);
        MusicService service = NewService();

        await service.HandOverNextAsync(ChannelId.ToString());
        await service.HandOverNextAsync(ChannelId.ToString());

        await _spotify.Received(1).PlayAsync(ChannelId, Arg.Any<CancellationToken>());
        _store.GetLastEngine(ChannelId.ToString()).Should().Be("spotify");
    }

    [Fact]
    public async Task A_dry_queue_after_a_spotify_request_changes_nothing()
    {
        EmptyQueueAfter("spotify", spotifyPlaying: false);

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await _spotify.DidNotReceive().PlayAsync(ChannelId, Arg.Any<CancellationToken>());
        await _spotify.DidNotReceive().PauseAsync(ChannelId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dry_queue_does_not_resume_spotify_that_is_already_playing()
    {
        EmptyQueueAfter("youtube", spotifyPlaying: true);

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await _spotify.DidNotReceive().PlayAsync(ChannelId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_dry_queue_waits_while_a_youtube_request_is_still_playing()
    {
        EmptyQueueAfter("youtube", spotifyPlaying: false);
        _store.SetPlayingRequest(ChannelId.ToString(), Entry(YouTubeUri));

        await NewService().HandOverNextAsync(ChannelId.ToString());

        await _spotify.DidNotReceive().PlayAsync(ChannelId, Arg.Any<CancellationToken>());
    }
}
