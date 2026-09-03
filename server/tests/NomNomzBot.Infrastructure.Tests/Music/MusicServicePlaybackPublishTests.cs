// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves E4's mutation-path leg: <see cref="MusicService"/>'s play/pause/skip/play-context actions publish a
/// <see cref="PlaybackStateChangedEvent"/> the instant they succeed — carrying the FRESHLY re-read track/play
/// state, not a guess — so the dashboard/overlay update instantly instead of waiting for the next
/// <c>MusicStatePollingService</c> tick. Exercises the real <see cref="SpotifyMusicProvider"/> resolution path
/// (<c>MusicService.GetActiveProviderAsync</c> matches by concrete provider type), stubbing only the HTTP
/// transport, so the test proves the actual production wiring, not a substitute.
/// </summary>
public sealed class MusicServicePlaybackPublishTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000f0001");

    [Fact]
    public async Task PlayAsync_publishes_the_freshly_read_state_on_success()
    {
        (MusicService sut, RecordingEventBus bus, _) = Build(TrackJson("Song A", isPlaying: true));

        Result ok = await sut.PlayAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        PlaybackStateChangedEvent published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .Single();
        published.BroadcasterId.Should().Be(ChannelId);
        published.IsPlaying.Should().BeTrue();
        published.TrackName.Should().Be("Song A");
        published.Artist.Should().Be("Artist");
        published.Album.Should().Be("Album");
        published.DurationMs.Should().Be(200000);
        published.ProgressMs.Should().Be(1000);
        published.Provider.Should().Be("spotify");
        published.ObservedAt.Should().NotBe(default);
    }

    [Fact]
    public async Task PauseAsync_publishes_state_reflecting_paused()
    {
        (MusicService sut, RecordingEventBus bus, _) = Build(TrackJson("Song A", isPlaying: false));

        Result ok = await sut.PauseAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        PlaybackStateChangedEvent published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .Single();
        published.IsPlaying.Should().BeFalse();
        published.TrackName.Should().Be("Song A");
    }

    /// <summary>
    /// The actual latency fix: pausing/resuming has a KNOWN outcome (the track doesn't change, only
    /// IsPlaying does), so with a fresh <see cref="INowPlayingCache"/> entry — the ordinary case for an
    /// actively streaming channel, kept warm every 1s by <c>MusicStatePollingService</c> — PauseAsync must
    /// publish off that cached snapshot instead of issuing a second live GET /me/player. That second call
    /// used to sit in series with the pause command itself on every Stream Deck press, roughly doubling
    /// the time before anything downstream (dashboard, overlay, Stream Deck) saw the new state — and it
    /// could race Spotify's own playback-state propagation delay (a GET right after a PUT can still echo
    /// the pre-mutation state).
    /// </summary>
    [Fact]
    public async Task PauseAsync_skips_the_second_provider_round_trip_when_the_cache_is_warm()
    {
        INowPlayingCache cache = new NowPlayingCache();
        (MusicService sut, RecordingEventBus bus, FakeSpotifyHttpHandler handler) = Build(
            TrackJson("Song A", isPlaying: true),
            cache
        );

        // Warms the cache exactly the way the poller does: one real read.
        await sut.GetNowPlayingAsync(ChannelId.ToString());
        handler.NowPlayingReadCount.Should().Be(1);

        Result ok = await sut.PauseAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        handler
            .NowPlayingReadCount.Should()
            .Be(1, "pause must publish from the warm cache, not a second live GET");
        PlaybackStateChangedEvent published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .Last();
        published.IsPlaying.Should().BeFalse();
        published
            .TrackName.Should()
            .Be("Song A", "the track itself is carried over from the cache");
        published.Artist.Should().Be("Artist");
        published.DurationMs.Should().Be(200000);
    }

    /// <summary>The other half: without ANY prior real read (nothing cached yet — e.g. right after the
    /// backend starts, before the poller's first tick), Pause must still fall back to a real fetch rather
    /// than publishing a guess or failing.</summary>
    [Fact]
    public async Task PauseAsync_falls_back_to_a_real_fetch_when_nothing_is_cached_yet()
    {
        (MusicService sut, RecordingEventBus bus, FakeSpotifyHttpHandler handler) = Build(
            TrackJson("Song A", isPlaying: false)
        );

        Result ok = await sut.PauseAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        handler.NowPlayingReadCount.Should().Be(1, "no cache existed, so a real read was required");
        bus.Published.OfType<PlaybackStateChangedEvent>().Single().IsPlaying.Should().BeFalse();
    }

    /// <summary>A stale cache entry (older than the freshness window) must NOT be trusted — Pause should
    /// re-fetch rather than publish state that could be meaningfully out of date.</summary>
    [Fact]
    public async Task PauseAsync_ignores_a_stale_cache_entry_and_refetches()
    {
        INowPlayingCache cache = new NowPlayingCache();
        (MusicService sut, RecordingEventBus bus, FakeSpotifyHttpHandler handler) = Build(
            TrackJson("Song A", isPlaying: true),
            cache
        );
        await sut.GetNowPlayingAsync(ChannelId.ToString());
        handler.NowPlayingReadCount.Should().Be(1);

        // Directly age the entry past the freshness window rather than sleeping the test — proves the
        // staleness check itself, not a timing coincidence.
        cache.Set(
            ChannelId,
            new TrackInfo
            {
                TrackName = "Song A",
                Artist = "Artist",
                Album = "Album",
                TrackUri = "spotify:track:x",
                Provider = "spotify",
                IsPlaying = true,
            },
            DateTimeOffset.UtcNow.AddSeconds(-30)
        );

        Result ok = await sut.PauseAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        handler
            .NowPlayingReadCount.Should()
            .Be(
                2,
                "the cache entry was stale, so pause must have refetched rather than trusting it"
            );
        // The fallback path ignores `assumeIsPlaying` entirely and believes whatever the real fetch
        // reports (the fixture's canned isPlaying: true) — proving a stale cache doesn't just get a
        // slower version of the SAME trust-the-caller shortcut, it genuinely defers to the provider.
        bus.Published.OfType<PlaybackStateChangedEvent>().Last().IsPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task SkipAsync_publishes_the_next_tracks_state()
    {
        (MusicService sut, RecordingEventBus bus, _) = Build(TrackJson("Song B", isPlaying: true));

        Result ok = await sut.SkipAsync(ChannelId.ToString());

        ok.IsSuccess.Should().BeTrue();
        PlaybackStateChangedEvent published = bus
            .Published.OfType<PlaybackStateChangedEvent>()
            .Single();
        published.TrackName.Should().Be("Song B");
        published.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public async Task PlayContextAsync_publishes_state_on_success()
    {
        (MusicService sut, RecordingEventBus bus, _) = Build(
            TrackJson("Playlist Track", isPlaying: true)
        );

        bool ok = await sut.PlayContextAsync(ChannelId.ToString(), "spotify:playlist:xyz");

        ok.Should().BeTrue();
        bus.Published.OfType<PlaybackStateChangedEvent>()
            .Single()
            .TrackName.Should()
            .Be("Playlist Track");
    }

    [Fact]
    public async Task PlayAsync_publishes_nothing_when_no_channel_has_a_connected_provider()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        RecordingEventBus bus = new();
        MusicService sut = new(
            [],
            db,
            bus,
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            new NowPlayingCache()
        );

        Result ok = await sut.PlayAsync(ChannelId.ToString());

        ok.IsFailure.Should().BeTrue();
        bus.Published.Should().BeEmpty();
    }

    private static (MusicService Sut, RecordingEventBus Bus, FakeSpotifyHttpHandler Handler) Build(
        string? currentTrackJson
    ) => Build(currentTrackJson, new NowPlayingCache());

    private static (MusicService Sut, RecordingEventBus Bus, FakeSpotifyHttpHandler Handler) Build(
        string? currentTrackJson,
        INowPlayingCache nowPlayingCache
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
        db.SaveChanges();

        FakeIntegrationTokenVault vault = new(db);
        vault.SeedConnectedSpotify(ChannelId);

        FakeSpotifyHttpHandler handler = new() { CurrentTrackJson = currentTrackJson };
        SpotifyMusicProvider spotify = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            NullSystemCredentialsProvider.Instance,
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(NullSystemCredentialsProvider.Instance)
        );

        RecordingEventBus bus = new();
        MusicService sut = new(
            [spotify],
            db,
            bus,
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            PermissiveMusicConfigService.Instance,
            Substitute.For<ICurrencyAccountService>(),
            nowPlayingCache
        );
        return (sut, bus, handler);
    }

    private static string TrackJson(string name, bool isPlaying) =>
        """
            {"item":{"name":"__NAME__","uri":"spotify:track:x","duration_ms":200000,"artists":[{"name":"Artist"}],"album":{"name":"Album","images":[]}},"is_playing":__PLAYING__,"progress_ms":1000}
            """.Replace("__NAME__", name).Replace("__PLAYING__", isPlaying ? "true" : "false");

    /// <summary>Round-trips plaintext unchanged — the mutation-publish tests exercise MusicService/SpotifyMusicProvider,
    /// not the envelope-encryption stack, which has its own dedicated tests elsewhere.</summary>
    private sealed class PassthroughTokenProtector : ITokenProtector
    {
        public Task<string> ProtectAsync(
            string plaintext,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(plaintext);

        public Task<string?> TryUnprotectAsync(
            string? sealedEnvelope,
            TokenProtectionContext context,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(sealedEnvelope);
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Stubs the Spotify Web API surface SpotifyMusicProvider calls: every player-mutation endpoint
    /// (play/pause/next/context) returns Spotify's real 204, and "currently playing" returns the configured
    /// canned track (or 204/no-content when null, Spotify's real "nothing playing" response).</summary>
    private sealed class FakeSpotifyHttpHandler : HttpMessageHandler
    {
        public string? CurrentTrackJson { get; set; }

        /// <summary>Counts real GET /me/player reads — the second Spotify round trip the assumed-outcome
        /// fast path exists to eliminate from Pause/Play's critical path when a fresh cache entry exists.</summary>
        public int NowPlayingReadCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            // The now-playing read is GET /me/player (full playback state). The transport writes go to
            // /me/player/play|pause|next, so a GET ending exactly in "/me/player" matches only the read.
            bool isNowPlayingRead =
                request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.EndsWith(
                    "/me/player",
                    StringComparison.Ordinal
                );

            if (isNowPlayingRead)
            {
                NowPlayingReadCount++;
                HttpResponseMessage response = CurrentTrackJson is null
                    ? new(HttpStatusCode.NoContent)
                    : new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            CurrentTrackJson,
                            Encoding.UTF8,
                            "application/json"
                        ),
                    };
                return Task.FromResult(response);
            }

            // play / pause / next / queue / play-context — Spotify's real success response is 204.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }
}
