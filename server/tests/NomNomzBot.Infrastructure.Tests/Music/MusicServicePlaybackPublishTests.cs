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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Domain.Music.Events;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

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
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        RecordingEventBus bus = new();
        MusicService sut = new([], db, bus, NullLogger<MusicService>.Instance);

        Result ok = await sut.PlayAsync(ChannelId.ToString());

        ok.IsFailure.Should().BeTrue();
        bus.Published.Should().BeEmpty();
    }

    private static (MusicService Sut, RecordingEventBus Bus, FakeSpotifyHttpHandler Handler) Build(
        string? currentTrackJson
    )
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.Services.Add(
            new Service
            {
                Id = Guid.NewGuid().ToString(),
                Name = "spotify",
                BroadcasterId = ChannelId,
                Enabled = true,
                AccessToken = "test-access-token",
            }
        );
        db.SaveChanges();

        FakeSpotifyHttpHandler handler = new() { CurrentTrackJson = currentTrackJson };
        SpotifyMusicProvider spotify = new(
            db,
            new PassthroughTokenProtector(),
            new InMemoryIntegrationCapabilityStore(),
            new SingleClientFactory(handler),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance
        );

        RecordingEventBus bus = new();
        MusicService sut = new([spotify], db, bus, NullLogger<MusicService>.Instance);
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

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            bool isCurrentlyPlayingRead = request.RequestUri!.AbsolutePath.EndsWith(
                "/currently-playing",
                StringComparison.Ordinal
            );

            if (isCurrentlyPlayingRead)
            {
                HttpResponseMessage response = CurrentTrackJson is null
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
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
