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
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The qtkitte incident: a Spotify connection that crossed into <c>needs_reauth</c> kept being retried on
/// every "now playing" poll cycle — <c>ConsecutiveFailureCount</c> reached 4653 before anyone noticed.
/// Proves the storm is stopped at the source: once needs_reauth, the periodic refresh path makes NO upstream
/// call at all (only a fresh OAuth grant — <c>StoreTokensAsync</c> — can clear it), and short of that
/// threshold, repeated failures back off instead of retrying at full poll cadence.
/// </summary>
public sealed class SpotifyMusicProviderRetryStormTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000e2");
    private const string ExternalId = "spotify-storm-1";

    [Fact]
    public async Task NeedsReauthConnection_IsSkippedByTheRoutineRefresher_NoUpstreamCallMade()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        RecordingSpotifyHandler wire = new();

        (SpotifyMusicProvider provider, IntegrationTokenVault vault, Guid connectionId) =
            await BuildAsync(dbName, wire, clock);

        await vault.StoreTokensAsync(
            connectionId,
            new(
                "expired-access",
                "dead-refresh",
                null,
                clock.GetUtcNow().UtcDateTime.AddMinutes(-30)
            )
        );
        // Cross the needs_reauth threshold exactly as the real refresh-failure path would.
        await vault.MarkRefreshFailureAsync(connectionId, "e1");
        await vault.MarkRefreshFailureAsync(connectionId, "e2");
        await vault.MarkRefreshFailureAsync(connectionId, "e3");

        // Simulate several subsequent poll cycles — the exact shape that ran the counter to 4653 in production.
        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(15));
            await provider.PlayAsync(Broadcaster);
        }

        wire.RefreshCallCount.Should()
            .Be(0, "needs_reauth cannot be fixed by retrying — only a fresh OAuth grant clears it");
    }

    [Fact]
    public async Task FailingConnection_BelowThreshold_BacksOff_InsteadOfRetryingAtFullPollCadence()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        RecordingSpotifyHandler wire = new() { AlwaysFail = true };

        (SpotifyMusicProvider provider, IntegrationTokenVault vault, Guid connectionId) =
            await BuildAsync(dbName, wire, clock);

        await vault.StoreTokensAsync(
            connectionId,
            new(
                "expired-access",
                "flaky-refresh",
                null,
                clock.GetUtcNow().UtcDateTime.AddMinutes(-30)
            )
        );

        // First poll: attempts and fails (failure #1 — still under the needs_reauth threshold of 3).
        await provider.PlayAsync(Broadcaster);
        wire.RefreshCallCount.Should().Be(1);

        // Immediately-following poll cycles (the real cadence is seconds) must back off, not retry at once.
        clock.Advance(TimeSpan.FromSeconds(5));
        await provider.PlayAsync(Broadcaster);
        wire.RefreshCallCount.Should().Be(1, "still inside the backoff window for failure #1");

        // Once the backoff window has elapsed, a retry is allowed again.
        clock.Advance(TimeSpan.FromMinutes(1));
        await provider.PlayAsync(Broadcaster);
        wire.RefreshCallCount.Should()
            .Be(2, "the backoff window elapsed, so the next poll may retry");
    }

    private static async Task<(
        SpotifyMusicProvider Provider,
        IntegrationTokenVault Vault,
        Guid ConnectionId
    )> BuildAsync(string dbName, RecordingSpotifyHandler wire, FakeTimeProvider clock)
    {
        AuthDbContext db = AuthTestBuilder.NewContext(dbName);
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );

        db.Channels.Add(
            new()
            {
                Id = Broadcaster,
                Provider = AuthEnums.Platform.Twitch,
                ExternalChannelId = "7654321",
                Name = "storm-streamer",
                NameNormalized = "storm-streamer",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        await db.SaveChangesAsync();

        RecordingEventBus bus = new();
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new PassthroughScopeGrant(),
            bus,
            clock,
            NullLogger<IntegrationTokenVault>.Instance
        );

        Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
            new(
                BroadcasterId: Broadcaster,
                Provider: AuthEnums.IntegrationProvider.Spotify,
                ProviderAccountId: ExternalId,
                ProviderAccountName: "storm-streamer",
                Scopes: ["user-modify-playback-state"],
                ClientId: null,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );

        SpotifyMusicProvider provider = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleClientFactory(wire),
            clock,
            NullLogger<SpotifyMusicProvider>.Instance,
            new FixedSpotifyCredentialsProvider(),
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(new FixedSpotifyCredentialsProvider())
        );

        return (provider, vault, upsert.Value.Id);
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
        ) => Task.FromResult<SystemAppCredentials?>(new("spotify-app-id", "spotify-app-secret"));

        public Task<string?> GetClientIdAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>("spotify-app-id");

        public Task<string?> GetValueAsync(
            string provider,
            string key,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);

        public Task<bool> IsAppDecisionRecordedAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(true);
    }

    /// <summary>Counts every POST to Spotify's token endpoint; fails every one when <see cref="AlwaysFail"/>.</summary>
    private sealed class RecordingSpotifyHandler : HttpMessageHandler
    {
        private int _refreshCallCount;

        public bool AlwaysFail { get; set; }
        public int RefreshCallCount => _refreshCallCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.AbsoluteUri.Contains("accounts.spotify.com"))
            {
                Interlocked.Increment(ref _refreshCallCount);
                if (AlwaysFail)
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """{"access_token":"a","refresh_token":"r","expires_in":3600}""",
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
