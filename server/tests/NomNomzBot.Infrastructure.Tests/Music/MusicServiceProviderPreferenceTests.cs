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
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves <c>MusicConfig.PreferredProvider</c> and <c>AllowSpotify</c>/<c>AllowYouTube</c> actually steer
/// which connected provider <see cref="MusicService"/> routes to (S067) — both are stubbed
/// <see cref="IMusicProvider"/>s here (not the real Spotify/YouTube HTTP clients, which have their own
/// dedicated tests) because what's under test is <c>MusicService</c>'s OWN selection logic, not either
/// provider's wire format.
/// </summary>
public sealed class MusicServiceProviderPreferenceTests
{
    private static readonly Guid ChannelId = Guid.Parse("0192a000-0000-7000-8000-0000000ae001");

    [Fact]
    public async Task PreferredProvider_youtube_wins_over_the_default_priority_order_when_both_are_connected()
    {
        MusicService sut = Build(
            new MusicConfigDto(
                IsEnabled: true,
                PreferredProvider: "youtube",
                MaxQueueSize: 50,
                MaxRequestsPerUser: 5,
                AllowYouTube: true,
                AllowSpotify: true,
                MinTrustLevel: "everyone"
            ),
            spotifyConnected: true,
            youTubeConnected: true
        );

        IReadOnlyList<MusicTrack> results = await sut.SearchAsync(ChannelId.ToString(), "song");

        results.Should().ContainSingle().Which.Provider.Should().Be("youtube");
    }

    [Fact]
    public async Task PreferredProvider_auto_keeps_the_default_priority_order()
    {
        // Default priority favors a provider that can drive playback — both stubs here report the same
        // capability set, so it falls to the ThenBy(Provider) tie-break: "spotify" < "youtube" ordinally.
        MusicService sut = Build(
            new MusicConfigDto(
                IsEnabled: true,
                PreferredProvider: "auto",
                MaxQueueSize: 50,
                MaxRequestsPerUser: 5,
                AllowYouTube: true,
                AllowSpotify: true,
                MinTrustLevel: "everyone"
            ),
            spotifyConnected: true,
            youTubeConnected: true
        );

        IReadOnlyList<MusicTrack> results = await sut.SearchAsync(ChannelId.ToString(), "song");

        results.Should().ContainSingle().Which.Provider.Should().Be("spotify");
    }

    [Fact]
    public async Task AllowSpotify_false_routes_to_YouTube_even_though_Spotify_is_connected_and_preferred_by_default_order()
    {
        MusicService sut = Build(
            new MusicConfigDto(
                IsEnabled: true,
                PreferredProvider: "auto",
                MaxQueueSize: 50,
                MaxRequestsPerUser: 5,
                AllowYouTube: true,
                AllowSpotify: false,
                MinTrustLevel: "everyone"
            ),
            spotifyConnected: true,
            youTubeConnected: true
        );

        IReadOnlyList<MusicTrack> results = await sut.SearchAsync(ChannelId.ToString(), "song");

        results.Should().ContainSingle().Which.Provider.Should().Be("youtube");
    }

    [Fact]
    public async Task AllowYouTube_false_leaves_no_active_provider_when_Spotify_is_not_connected()
    {
        MusicService sut = Build(
            new MusicConfigDto(
                IsEnabled: true,
                PreferredProvider: "auto",
                MaxQueueSize: 50,
                MaxRequestsPerUser: 5,
                AllowYouTube: false,
                AllowSpotify: true,
                MinTrustLevel: "everyone"
            ),
            spotifyConnected: false,
            youTubeConnected: true
        );

        IReadOnlyList<MusicTrack> results = await sut.SearchAsync(ChannelId.ToString(), "song");

        results.Should().BeEmpty("the only connected provider is disallowed by config");
    }

    private static MusicService Build(
        MusicConfigDto config,
        bool spotifyConnected,
        bool youTubeConnected
    )
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        if (spotifyConnected)
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
        if (youTubeConnected)
            db.Services.Add(
                new()
                {
                    Id = Guid.NewGuid().ToString(),
                    Name = "youtube",
                    BroadcasterId = ChannelId,
                    Enabled = true,
                    AccessToken = "test-access-token",
                }
            );
        db.SaveChanges();

        IMusicConfigService configService = Substitute.For<IMusicConfigService>();
        configService
            .GetConfigAsync(ChannelId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(config));

        return new(
            [new StubProvider("spotify"), new StubProvider("youtube")],
            db,
            new RecordingEventBus(),
            new BlockedTrackService(db),
            new SongRequestQueueStore(),
            new NoOpSongRequestQueuePersistence(),
            NullLogger<MusicService>.Instance,
            new InMemoryIntegrationCapabilityStore(),
            configService,
            Substitute.For<ICurrencyAccountService>()
        );
    }

    /// <summary>A minimal <see cref="IMusicProvider"/> whose only meaningfully-implemented member is
    /// <see cref="SearchAsync"/> — every other member is unreachable from the selection tests here, so it
    /// throws, matching the same "only implement what the test surface calls" convention as
    /// <c>RegisteredSpotifyStub</c> (MusicStatePollingServiceTests).</summary>
    private sealed class StubProvider(string provider) : IMusicProvider
    {
        public string Provider => provider;

        public MusicProviderCapabilities Capabilities =>
            MusicProviderCapabilities.Search | MusicProviderCapabilities.AcceptsSongRequests;

        public Task<(
            IReadOnlyList<TrackInfo> Tracks,
            MusicProviderFailureReason Failure
        )> SearchAsync(
            Guid broadcasterId,
            string query,
            int maxResults = 5,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<(IReadOnlyList<TrackInfo>, MusicProviderFailureReason)>(
                (
                    [
                        new TrackInfo
                        {
                            TrackName = $"Song via {provider}",
                            Artist = "Artist",
                            Album = "Album",
                            TrackUri = $"{provider}:track:1",
                            Provider = provider,
                        },
                    ],
                    MusicProviderFailureReason.None
                )
            );

        public Task PlayAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PauseAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SkipAsync(Guid broadcasterId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task PreviousAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetVolumeAsync(
            Guid broadcasterId,
            int volumePercent,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SeekAsync(
            Guid broadcasterId,
            int positionSeconds,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetShuffleAsync(
            Guid broadcasterId,
            bool enabled,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task SetRepeatAsync(
            Guid broadcasterId,
            MusicRepeatMode mode,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<MusicDeviceInfo>> GetDevicesAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task TransferPlaybackAsync(
            Guid broadcasterId,
            string deviceId,
            bool play,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<TrackInfo?> GetCurrentTrackAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<(TrackInfo? Track, MusicProviderFailureReason Failure)> ResolveTrackAsync(
            Guid broadcasterId,
            string uriOrId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<bool> AddToQueueAsync(
            Guid broadcasterId,
            string trackUri,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();

        public Task<string?> GetEmbeddedPlaybackTokenAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }
}
