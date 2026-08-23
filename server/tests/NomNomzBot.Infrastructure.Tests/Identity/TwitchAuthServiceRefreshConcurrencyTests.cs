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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Configuration;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// S036 — two callers racing a refresh for the SAME connection must not both post the vaulted refresh
/// token to Twitch: Twitch invalidates the prior refresh token on use, so the loser's re-vault would
/// destroy the winner's fresh pair and force the channel to re-auth. Proven over the REAL
/// <see cref="IntegrationTokenVault"/> + REAL envelope crypto (so the vault's AsNoTracking re-check is
/// exercised for real, not stubbed away) with two INDEPENDENT <see cref="AuthDbContext"/> instances sharing
/// one SQLite backing store — the same shape as two concurrent HTTP requests each getting their own scoped
/// DbContext in production — and one shared <see cref="ConnectionRefreshGate"/>, exactly as the DI
/// singleton registration wires it.
/// </summary>
public sealed class TwitchAuthServiceRefreshConcurrencyTests
{
    private static readonly Guid BroadcasterA = Guid.Parse("0199c000-0000-7000-8000-0000000000a1");
    private static readonly Guid BroadcasterB = Guid.Parse("0199c000-0000-7000-8000-0000000000b1");

    [Fact]
    public async Task TwoConcurrentRefreshesOfTheSameConnection_HitTwitchExactlyOnce_AndBothCallersGetTheSameToken()
    {
        string dbName = Guid.NewGuid().ToString();
        ConnectionRefreshGate gate = new();
        GatedTokenHandler wire = new() { HoldFirstCall = true };

        (TwitchAuthService serviceA, _) = await BuildAsync(dbName, gate, wire, BroadcasterA);
        (TwitchAuthService serviceB, _) = await BuildAsync(dbName, gate, wire, BroadcasterA);

        // Force a REAL race, not two calls that happen to run one after the other: t1 is made to block
        // INSIDE the HTTP call (holding the gate), so t2 is guaranteed to arrive while t1's refresh is
        // still in flight and block on the SAME gate key — exactly the two-401s-close-together scenario
        // S036 exists for. Only once both are provably overlapping do we release t1's HTTP call.
        Task<TokenResult?> t1 = serviceA.RefreshTokenAsync(
            BroadcasterA,
            AuthEnums.IntegrationProvider.Twitch
        );
        await WaitUntilAsync(() => wire.CallCount >= 1); // t1 is now blocked inside the HTTP call
        Task<TokenResult?> t2 = serviceB.RefreshTokenAsync(
            BroadcasterA,
            AuthEnums.IntegrationProvider.Twitch
        );
        await Task.Delay(50); // give t2 time to reach ResolveConnectionAsync + block on the gate
        wire.Release();

        TokenResult?[] results = await Task.WhenAll(t1, t2);

        wire.CallCount.Should()
            .Be(
                1,
                "the second caller must reuse the winner's re-vaulted token, not post a second refresh"
            );
        results[0].Should().NotBeNull();
        results[1].Should().NotBeNull();
        results[0]!.AccessToken.Should().Be("issued-access-1");
        results[1]!.AccessToken.Should().Be("issued-access-1");
    }

    [Fact]
    public async Task ConcurrentRefreshesOfDifferentConnections_AreNotBlockedByEachOther()
    {
        string dbName = Guid.NewGuid().ToString();
        ConnectionRefreshGate gate = new();
        GatedTokenHandler wire = new();
        wire.HoldFirstCall = true; // connection A's HTTP call blocks until released

        (TwitchAuthService serviceA, _) = await BuildAsync(dbName, gate, wire, BroadcasterA);
        (TwitchAuthService serviceB, _) = await BuildAsync(dbName, gate, wire, BroadcasterB);

        Task<TokenResult?> blockedA = serviceA.RefreshTokenAsync(
            BroadcasterA,
            AuthEnums.IntegrationProvider.Twitch
        );

        // Connection B must complete WHILE A is still blocked — a different connection's refresh must never
        // wait on A's gate key.
        Task<TokenResult?> completedB = serviceB.RefreshTokenAsync(
            BroadcasterB,
            AuthEnums.IntegrationProvider.Twitch
        );
        TokenResult? resultB = await completedB.WaitAsync(TimeSpan.FromSeconds(5));
        resultB
            .Should()
            .NotBeNull("connection B must not be blocked by connection A's in-flight refresh");

        wire.Release();
        TokenResult? resultA = await blockedA;
        resultA.Should().NotBeNull();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, CancellationToken.None);
    }

    private static async Task<(TwitchAuthService Service, AuthDbContext Db)> BuildAsync(
        string dbName,
        ConnectionRefreshGate gate,
        GatedTokenHandler wire,
        Guid broadcasterId
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext(dbName);
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );

        // Seed the Twitch app credentials once per db (idempotent — a second seed for the same key throws
        // a unique-index violation, so only seed if not already present).
        if (!db.Configurations.Any(c => c.Key == "twitch.client_id"))
        {
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = null,
                    Key = "twitch.client_id",
                    Value = "app-id",
                }
            );
            db.Configurations.Add(
                new()
                {
                    BroadcasterId = null,
                    Key = "twitch.client_secret",
                    SecureValue = await protector.ProtectAsync(
                        "app-secret",
                        SystemCredentialsProvider.ContextFor("twitch.client_secret")
                    ),
                }
            );
            await db.SaveChangesAsync();
        }

        RecordingEventBus bus = new();
        IScopeGrantService scopeGrant = new PassthroughScopeGrant();
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            scopeGrant,
            bus,
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );

        // Seed a connected connection with a vaulted refresh token, if one doesn't already exist for this
        // broadcaster (both harnesses for the SAME broadcaster share the seeded row via the shared db name).
        IntegrationConnection? existing = db.IntegrationConnections.FirstOrDefault(c =>
            c.BroadcasterId == broadcasterId && c.Provider == AuthEnums.IntegrationProvider.Twitch
        );
        if (existing is null)
        {
            Result<IntegrationConnectionDto> upsert = await vault.UpsertConnectionAsync(
                new UpsertConnectionDto(
                    BroadcasterId: broadcasterId,
                    Provider: AuthEnums.IntegrationProvider.Twitch,
                    ProviderAccountId: $"twitch-{broadcasterId}",
                    ProviderAccountName: "streamer",
                    Scopes: ["user:read:chat"],
                    ClientId: null,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            );
            await vault.StoreTokensAsync(
                upsert.Value.Id,
                new StoreTokensDto(
                    "old-access",
                    "old-refresh",
                    null,
                    DateTime.UtcNow.AddMinutes(-1)
                ),
                grantedScopes: null
            );
        }

        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            new ConfigurationBuilder().Build()
        );

        TwitchAuthService service = new(
            db,
            vault,
            credentials,
            new SingleClientFactory(wire),
            NullLogger<TwitchAuthService>.Instance,
            TimeProvider.System,
            gate
        );
        return (service, db);
    }

    // ── doubles ──────────────────────────────────────────────────────────────

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

    /// <summary>
    /// Counts every POST and returns a fresh (always-succeeding) token pair each time. When
    /// <see cref="HoldFirstCall"/> is set, the FIRST call blocks until <see cref="Release"/> is called — used
    /// to prove a different connection's refresh is never blocked behind it.
    /// </summary>
    private sealed class GatedTokenHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _callCount;

        public bool HoldFirstCall { get; set; }
        public int CallCount => _callCount;

        public void Release() => _gate.TrySetResult();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            int callNumber = Interlocked.Increment(ref _callCount);
            if (HoldFirstCall && callNumber == 1)
                await _gate.Task.WaitAsync(cancellationToken);

            return new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"access_token":"issued-access-{{callNumber}}","refresh_token":"issued-refresh-{{callNumber}}","expires_in":3600,"scope":["user:read:chat"],"token_type":"bearer"}""",
                    Encoding.UTF8,
                    "application/json"
                ),
            };
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
