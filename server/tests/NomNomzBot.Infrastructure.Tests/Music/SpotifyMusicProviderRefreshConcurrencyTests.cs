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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S036b — <see cref="SpotifyMusicProvider"/>'s vaulted refresh had the identical unguarded-concurrent-refresh
/// shape S036 (23f1e119) closed for Twitch/Kick but left open here: two callers racing a refresh for the same
/// connection could both post the same Spotify refresh token. Proven over the REAL
/// <see cref="IntegrationTokenVault"/> with two INDEPENDENT DbContext instances sharing one SQLite store (the
/// same shape as two concurrent requests each getting their own scoped DbContext) and one shared
/// <see cref="ConnectionRefreshGate"/> — mirrors
/// <c>KickAccessTokenProviderConcurrencyTests.TwoConcurrentGetAsyncCalls_ForTheSameConnection_HitKickExactlyOnce_AndBothGetTheSameToken</c>.
/// </summary>
public sealed class SpotifyMusicProviderRefreshConcurrencyTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000d1");
    private const string ExternalId = "spotify-998877";

    [Fact]
    public async Task TwoConcurrentPlayCalls_ForTheSameConnection_HitSpotifyExactlyOnce_AndBothGetTheSameToken()
    {
        string dbName = Guid.NewGuid().ToString();
        ConnectionRefreshGate gate = new();
        CountingSpotifyHandler wire = new() { HoldFirstCall = true };

        SpotifyMusicProvider providerA = await BuildAsync(dbName, gate, wire);
        SpotifyMusicProvider providerB = await BuildAsync(dbName, gate, wire);

        // Force a REAL race: t1 is made to block INSIDE the HTTP call (holding the gate), so t2 is
        // guaranteed to arrive while t1's refresh is still in flight and block on the SAME gate key.
        Task t1 = providerA.PlayAsync(Broadcaster);
        await WaitUntilAsync(() => wire.CallCount >= 1); // t1 is now blocked inside the HTTP call
        Task t2 = providerB.PlayAsync(Broadcaster);
        await Task.Delay(50); // give t2 time to reach the vault check + block on the gate
        wire.Release();

        await Task.WhenAll(t1, t2);

        wire.RefreshCallCount.Should()
            .Be(1, "the second caller must reuse the winner's re-vaulted token");
        wire.PlayerCallTokens.Should().HaveCount(2);
        wire.PlayerCallTokens[0].Should().Be("spotify-access-1");
        wire.PlayerCallTokens[1].Should().Be("spotify-access-1");

        // The vault holds exactly one updated record for this connection — no stale/duplicate write race.
        AuthDbContext verifyDb = AuthTestBuilder.NewContext(dbName);
        List<IntegrationConnection> connections = await verifyDb
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.Spotify
                && c.ProviderAccountId == ExternalId
            )
            .ToListAsync();
        connections.Should().HaveCount(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, CancellationToken.None);
    }

    private static async Task<SpotifyMusicProvider> BuildAsync(
        string dbName,
        ConnectionRefreshGate gate,
        CountingSpotifyHandler wire
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext(dbName);
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );

        if (!db.Channels.Any(c => c.Id == Broadcaster))
        {
            db.Channels.Add(
                new()
                {
                    Id = Broadcaster,
                    Provider = AuthEnums.Platform.Twitch,
                    ExternalChannelId = "1234567",
                    Name = "spotify-streamer",
                    NameNormalized = "spotify-streamer",
                    OwnerUserId = Guid.NewGuid(),
                }
            );
        }
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            bus,
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );

        if (
            !await db.IntegrationConnections.AnyAsync(c =>
                c.Provider == AuthEnums.IntegrationProvider.Spotify
                && c.ProviderAccountId == ExternalId
            )
        )
        {
            Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
                new UpsertConnectionDto(
                    BroadcasterId: Broadcaster,
                    Provider: AuthEnums.IntegrationProvider.Spotify,
                    ProviderAccountId: ExternalId,
                    ProviderAccountName: "spotify-streamer",
                    Scopes: ["user-modify-playback-state"],
                    ClientId: null,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            );
            // Already expired — the first call must refresh.
            await vault.StoreTokensAsync(
                upsert.Value.Id,
                new StoreTokensDto(
                    "old-access",
                    "old-refresh",
                    null,
                    DateTime.UtcNow.AddMinutes(-10)
                )
            );
        }

        return new SpotifyMusicProvider(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleClientFactory(wire),
            TimeProvider.System,
            NullLogger<SpotifyMusicProvider>.Instance,
            new FixedSpotifyCredentialsProvider(),
            gate
        );
    }

    private sealed class PassthroughScopeGrant : IScopeGrantService
    {
        public IReadOnlyList<string> RequiredScopesFor(string featureKey) => [];

        public Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(
            Guid broadcasterId,
            string featureKey,
            string? baseUrl = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success(new ScopeGrantState(true, null, [])));

        public Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(
            Guid connectionId,
            IReadOnlyList<string> actualScopes,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Result.Success<IReadOnlyList<string>>([]));
    }

    private sealed class FixedSpotifyCredentialsProvider : ISystemCredentialsProvider
    {
        public Task<SystemAppCredentials?> GetAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<SystemAppCredentials?>(
                new SystemAppCredentials("spotify-app-id", "spotify-app-secret")
            );

        public Task<string?> GetClientIdAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>("spotify-app-id");

        public Task<string?> GetValueAsync(
            string provider,
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);
    }

    /// <summary>
    /// Counts every POST to the Spotify token endpoint AND records the bearer token used on every player
    /// command call. When <see cref="HoldFirstCall"/> is set, the FIRST token-refresh POST blocks until
    /// <see cref="Release"/> is called — forces a REAL race instead of two calls that happen to run one
    /// after the other (which an all-synchronous in-memory test can otherwise produce, defeating the point
    /// of a concurrency test).
    /// </summary>
    private sealed class CountingSpotifyHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _refreshCallCount;

        public bool HoldFirstCall { get; set; }
        public int CallCount => _refreshCallCount;
        public int RefreshCallCount => _refreshCallCount;
        public List<string> PlayerCallTokens { get; } = [];

        public void Release() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.AbsoluteUri.Contains("accounts.spotify.com"))
            {
                int callNumber = Interlocked.Increment(ref _refreshCallCount);
                if (HoldFirstCall && callNumber == 1)
                    await _gate.Task.WaitAsync(cancellationToken);

                return new(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"access_token":"spotify-access-{{callNumber}}","refresh_token":"spotify-refresh-{{callNumber}}","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                };
            }

            lock (PlayerCallTokens)
                PlayerCallTokens.Add(request.Headers.Authorization?.Parameter ?? string.Empty);
            return new(HttpStatusCode.NoContent);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
