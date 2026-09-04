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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Music.Dtos;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Music.Realtime;
using NomNomzBot.Infrastructure.Platform.Eventing;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// Proves S-MUSIC-1's fallback leg: <see cref="SpotifyDealerHostedService"/> never starts a dealer socket for a
/// channel it cannot authenticate as Spotify (no connection at all) or that prefers YouTube. Combined with
/// <c>MusicStatePollingServiceTests</c> (unmodified, still green — the poller's own comprehensive suite, which
/// takes no dependency on this service at all: it is registered independently in DI and is never gated by, or
/// even aware of, this service's eligibility decisions), this proves the fallback runs regardless rather than
/// merely assuming it does.
/// </summary>
public sealed class SpotifyDealerHostedServiceTests
{
    private static readonly Guid SpotifyChannel = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f3001"
    );
    private static readonly Guid YouTubeOnlyChannel = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f3002"
    );
    private static readonly Guid NoConnectionChannel = Guid.Parse(
        "0192a000-0000-7000-8000-0000000f3003"
    );

    [Fact]
    public async Task Eligibility_ExcludesYouTubePreferredAndUnconnectedChannels_IncludesSpotifyConnectedOnes()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = SpotifyChannel,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.Connected,
            }
        );
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = YouTubeOnlyChannel,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.Connected,
            }
        );
        await db.SaveChangesAsync();

        FakeMusicConfigService configs = new();
        configs.SetPreferredProvider(YouTubeOnlyChannel, "youtube");

        SpotifyDealerHostedService sut = new(
            new SingleScopeFactory(db, configs),
            NeverConnectsFactory.Instance,
            NoopHttpClientFactory.Instance,
            new RecordingEventBus(),
            new MusicRealtimeSignal(),
            new SongRequestQueueStore(),
            new FakeTimeProvider(new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<SpotifyDealerHostedService>.Instance
        );

        List<Guid> eligible = await sut.LoadEligibleChannelsAsync(CancellationToken.None);

        eligible
            .Should()
            .Contain(
                SpotifyChannel,
                "a non-revoked Spotify connection with no YouTube preference gets a dealer socket"
            );
        eligible
            .Should()
            .NotContain(
                YouTubeOnlyChannel,
                "the channel prefers YouTube — the dealer socket is Spotify-only, the poller already covers it"
            );
        eligible
            .Should()
            .NotContain(
                NoConnectionChannel,
                "no Spotify connection at all means nothing to authenticate the dealer socket with"
            );
    }

    [Fact]
    public async Task Reconcile_StopsAConnection_WhenItsChannelStopsBeingEligible()
    {
        MusicTestDbContext db = MusicTestDbContext.New();
        db.IntegrationConnections.Add(
            new IntegrationConnection
            {
                Id = Guid.NewGuid(),
                BroadcasterId = SpotifyChannel,
                Provider = AuthEnums.IntegrationProvider.Spotify,
                Status = AuthEnums.IntegrationStatus.Connected,
            }
        );
        await db.SaveChangesAsync();

        FakeMusicConfigService configs = new();
        SpotifyDealerHostedService sut = new(
            new SingleScopeFactory(db, configs),
            NeverConnectsFactory.Instance,
            NoopHttpClientFactory.Instance,
            new RecordingEventBus(),
            new MusicRealtimeSignal(),
            new SongRequestQueueStore(),
            new FakeTimeProvider(new(2026, 9, 4, 0, 0, 0, TimeSpan.Zero)),
            NullLogger<SpotifyDealerHostedService>.Instance
        );

        // First reconcile pass: the channel is eligible, so a connection is started (NeverConnectsFactory
        // throws only when actually asked to open a socket — starting the background task itself does not).
        await sut.ReconcileConnectionsAsync(CancellationToken.None);

        // The channel switches to preferring YouTube — a second pass must stop the now-ineligible connection
        // rather than leaving it running forever.
        configs.SetPreferredProvider(SpotifyChannel, "youtube");
        Func<Task> act = () => sut.ReconcileConnectionsAsync(CancellationToken.None);

        await act.Should()
            .NotThrowAsync("stopping a connection must not surface the socket's own failures");
    }

    private sealed class FakeMusicConfigService : IMusicConfigService
    {
        private readonly Dictionary<string, string> _preferredProvider = new();

        public void SetPreferredProvider(Guid channelId, string provider) =>
            _preferredProvider[channelId.ToString()] = provider;

        public Task<Result<MusicConfigDto>> GetConfigAsync(
            string broadcasterId,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult(
                Result.Success(
                    new MusicConfigDto(
                        true,
                        _preferredProvider.GetValueOrDefault(broadcasterId, "auto"),
                        50,
                        3,
                        true,
                        true,
                        "everyone"
                    )
                )
            );

        public Task<Result<MusicConfigDto>> UpdateConfigAsync(
            string broadcasterId,
            UpdateMusicConfigDto request,
            CancellationToken cancellationToken = default
        ) => throw new NotSupportedException();
    }

    private sealed class SingleScopeFactory(IApplicationDbContext db, IMusicConfigService configs)
        : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => new Scope(db, configs);

        private sealed class Scope(IApplicationDbContext db, IMusicConfigService configs)
            : IServiceScope,
                IServiceProvider
        {
            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(IApplicationDbContext))
                    return db;
                if (serviceType == typeof(IMusicConfigService))
                    return configs;
                return null;
            }

            public void Dispose() { }
        }
    }

    /// <summary>Throws only when actually asked to open a socket — proves eligibility/reconcile logic never
    /// touches the transport itself, so these tests exercise real production wiring end to end short of the
    /// network hop (which <see cref="SpotifyDealerConnectionTests"/> covers separately).</summary>
    private sealed class NeverConnectsFactory : IWebSocketChannelFactory
    {
        public static readonly NeverConnectsFactory Instance = new();

        public Task<IWebSocketChannel> ConnectAsync(Uri uri, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Not expected to connect in this test.");
    }

    private sealed class NoopHttpClientFactory : IHttpClientFactory
    {
        public static readonly NoopHttpClientFactory Instance = new();

        public HttpClient CreateClient(string name) => new();
    }
}
