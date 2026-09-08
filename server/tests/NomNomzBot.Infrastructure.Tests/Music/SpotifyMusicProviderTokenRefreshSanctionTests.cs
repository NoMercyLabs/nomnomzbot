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
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Music.Interfaces;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Platform.Security;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// The live incident: a Spotify now-playing widget went dark because its OWN background poll — no chat
/// command, no dashboard click, no <see cref="IOutboundSanctionAccessor"/> scope open anywhere on the call
/// stack — hit an expiring vaulted token and needed to refresh it. <see cref="OutboundSanctionHandler"/> (the
/// real one, not a fake) sits on the "spotify" <see cref="HttpClient"/> and refuses every POST with no open
/// sanction, so the refresh's POST to accounts.spotify.com/api/token was rejected, the read that triggered it
/// failed, and the widget stayed silent for as long as the vaulted token stayed expired — every subsequent
/// poll hit the same wall. The broadcaster already consented to this by connecting Spotify in the first
/// place; keeping that connection's own token alive is not a write to anything they didn't already agree to,
/// unlike an actual playback command (skip/pause/queue), which still needs — and still gets — a real caller-
/// supplied sanction.
/// </summary>
public sealed class SpotifyMusicProviderTokenRefreshSanctionTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000f3");
    private const string ExternalId = "spotify-widget-poll-1";

    [Fact]
    public async Task A_now_playing_poll_refreshes_an_expiring_token_through_the_real_sanction_gate_with_no_ambient_sanction_open()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        RecordingSpotifyHandler wire = new();

        (SpotifyMusicProvider provider, IntegrationTokenVault vault, Guid connectionId) =
            await BuildAsync(dbName, wire, clock);

        // Already expiring, exactly like a token a periodic poll finds stale — nobody has opened an
        // IOutboundSanctionAccessor scope anywhere above this call, matching a background widget poll.
        await vault.StoreTokensAsync(
            connectionId,
            new(
                "expiring-access",
                "live-refresh",
                null,
                clock.GetUtcNow().UtcDateTime.AddMinutes(-10)
            )
        );

        TrackInfo? track = await provider.GetCurrentTrackAsync(Broadcaster);

        wire.RefreshCallCount.Should()
            .Be(1, "the expiring token must be refreshed once behind the read");
        track.Should().NotBeNull("the read must succeed once the refresh goes through");
        track.TrackName.Should().Be("Currently Playing");
    }

    [Fact]
    public async Task A_healthy_token_needs_no_refresh_and_no_sanction_at_all()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        RecordingSpotifyHandler wire = new();

        (SpotifyMusicProvider provider, IntegrationTokenVault vault, Guid connectionId) =
            await BuildAsync(dbName, wire, clock);

        await vault.StoreTokensAsync(
            connectionId,
            new("fresh-access", "fresh-refresh", null, clock.GetUtcNow().UtcDateTime.AddHours(1))
        );

        TrackInfo? track = await provider.GetCurrentTrackAsync(Broadcaster);

        wire.RefreshCallCount.Should()
            .Be(0, "the token is not expiring — no refresh POST is needed");
        track.Should().NotBeNull();
    }

    [Fact]
    public async Task A_genuine_playback_command_still_requires_a_real_caller_supplied_sanction_and_never_reaches_spotify_without_one()
    {
        // The token-refresh exemption must stay exactly that — an exemption for the bot's OWN necessary
        // upkeep, never a blanket pass for the whole call. A user-triggered write (play/pause/skip/queue)
        // is a command being PROPAGATED to a third-party vendor, not upkeep the bot does on its own, so it
        // must still fail closed without a real caller-supplied sanction (a chat command's pipeline scope,
        // or a dashboard action) exactly as before this fix.
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        RecordingSpotifyHandler wire = new();

        (SpotifyMusicProvider provider, IntegrationTokenVault vault, Guid connectionId) =
            await BuildAsync(dbName, wire, clock);

        // Healthy token — isolates the write-gate from the refresh-gate this fix touches.
        await vault.StoreTokensAsync(
            connectionId,
            new("fresh-access", "fresh-refresh", null, clock.GetUtcNow().UtcDateTime.AddHours(1))
        );

        await provider.PlayAsync(Broadcaster);

        wire.RefreshCallCount.Should().Be(0, "the token was already healthy");
        wire.PlayerCommandCallCount.Should()
            .Be(0, "an unsanctioned playback write must never reach Spotify");
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
                ExternalChannelId = "5551234",
                Name = "widget-poll-streamer",
                NameNormalized = "widget-poll-streamer",
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
                ProviderAccountName: "widget-poll-streamer",
                Scopes: ["user-read-playback-state"],
                ClientId: null,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );

        // The REAL gate, not a fake — this is the exact chain "spotify" HttpClients get in production
        // (DependencyInjection.cs adds OutboundSanctionHandler to the named client), so a passing test here
        // is a passing test against the mechanism that actually blocked the live widget.
        OutboundSanctionHandler gate = new(
            new OutboundSanctionAccessor(),
            NullLogger<OutboundSanctionHandler>.Instance
        )
        {
            InnerHandler = wire,
        };
        HttpClient client = new(gate, disposeHandler: false);

        SpotifyMusicProvider provider = new(
            db,
            vault,
            new InMemoryIntegrationCapabilityStore(),
            new LastActiveSpotifyDeviceTracker(),
            new SingleClientFactory(client),
            clock,
            NullLogger<SpotifyMusicProvider>.Instance,
            new FixedSpotifyCredentialsProvider(),
            new ConnectionRefreshGate(),
            new NullChannelCredentialsResolver(new FixedSpotifyCredentialsProvider()),
            new OutboundSanctionAccessor()
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

    /// <summary>Answers the token endpoint once per call and the player GET with a minimal "now playing"
    /// payload — enough for <see cref="SpotifyMusicProvider.GetCurrentTrackAsync"/> to map a real track.</summary>
    private sealed class RecordingSpotifyHandler : HttpMessageHandler
    {
        private int _refreshCallCount;
        private int _playerCommandCallCount;

        public int RefreshCallCount => _refreshCallCount;

        /// <summary>Every write that is NOT the token endpoint (e.g. PUT /me/player/play) — a genuine
        /// playback command, distinct from the read-only GET /me/player now-playing poll.</summary>
        public int PlayerCommandCallCount => _playerCommandCallCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request.RequestUri!.AbsoluteUri.Contains("accounts.spotify.com"))
            {
                Interlocked.Increment(ref _refreshCallCount);
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

            if (
                request.Method == HttpMethod.Get
                && request.RequestUri!.AbsolutePath.EndsWith("/me/player", StringComparison.Ordinal)
            )
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {
                                "is_playing": true,
                                "progress_ms": 1000,
                                "item": {
                                    "id": "track1",
                                    "name": "Currently Playing",
                                    "duration_ms": 200000,
                                    "explicit": false,
                                    "is_playable": true,
                                    "artists": [{ "name": "Test Artist" }],
                                    "album": { "images": [{ "url": "https://example.com/art.jpg" }] },
                                    "external_urls": { "spotify": "https://open.spotify.com/track/track1" }
                                }
                            }
                            """,
                            Encoding.UTF8,
                            "application/json"
                        ),
                    }
                );
            }

            Interlocked.Increment(ref _playerCommandCallCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
