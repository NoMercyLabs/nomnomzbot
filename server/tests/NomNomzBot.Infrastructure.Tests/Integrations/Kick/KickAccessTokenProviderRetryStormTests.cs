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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Integrations.Kick;
using NomNomzBot.Infrastructure.Platform.Configuration;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Integrations.Kick;

/// <summary>
/// S044 — mirrors the Spotify retry-storm fix (07f60a1c) for the Kick refresh path:
/// <see cref="KickAccessTokenProvider.GetAsync"/> refreshed on the routine cadence regardless of connection
/// status, the same shape that ran a dead Spotify connection's ConsecutiveFailureCount to 4653 on the
/// deployed box. Proves the guard: (1) a needs_reauth connection makes ZERO upstream HTTP calls on the
/// routine path, (2) repeated failures back off instead of retrying every call, and (3) a deliberate
/// re-auth (StoreTokensAsync, not the routine GetAsync path) still clears needs_reauth and restores refreshes.
/// </summary>
public sealed class KickAccessTokenProviderRetryStormTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0199d000-0000-7000-8000-0000000000d4");
    private const string ExternalId = "554433001";

    [Fact]
    public async Task NeedsReauthConnection_IsSkippedByTheRoutineRefresher_NoUpstreamCallMade()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        CountingKickHandler wire = new();

        (KickAccessTokenProvider provider, IntegrationTokenVault vault, Guid connectionId) =
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

        for (int i = 0; i < 5; i++)
        {
            clock.Advance(TimeSpan.FromSeconds(15));
            await provider.GetAsync(Broadcaster);
        }

        wire.CallCount.Should()
            .Be(0, "needs_reauth cannot be fixed by retrying — only a fresh OAuth grant clears it");
    }

    [Fact]
    public async Task FailingConnection_BelowThreshold_BacksOff_InsteadOfRetryingAtFullCallCadence()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        CountingKickHandler wire = new() { AlwaysFail = true };

        (KickAccessTokenProvider provider, IntegrationTokenVault vault, Guid connectionId) =
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

        // First call: attempts and fails (failure #1 — still under the needs_reauth threshold of 3).
        await provider.GetAsync(Broadcaster);
        wire.CallCount.Should().Be(1);

        // Immediately-following calls must back off, not retry at once.
        clock.Advance(TimeSpan.FromSeconds(5));
        await provider.GetAsync(Broadcaster);
        wire.CallCount.Should().Be(1, "still inside the backoff window for failure #1");

        // Once the backoff window has elapsed, a retry is allowed again.
        clock.Advance(TimeSpan.FromMinutes(1));
        await provider.GetAsync(Broadcaster);
        wire.CallCount.Should().Be(2, "the backoff window elapsed, so the next call may retry");
    }

    [Fact]
    public async Task ExplicitReAuth_ClearsNeedsReauth_AndRoutineRefreshesResume()
    {
        FakeTimeProvider clock = new(DateTimeOffset.UtcNow);
        string dbName = Guid.NewGuid().ToString();
        CountingKickHandler wire = new();

        (KickAccessTokenProvider provider, IntegrationTokenVault vault, Guid connectionId) =
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
        await vault.MarkRefreshFailureAsync(connectionId, "e1");
        await vault.MarkRefreshFailureAsync(connectionId, "e2");
        await vault.MarkRefreshFailureAsync(connectionId, "e3");

        // While needs_reauth, the routine path makes no call.
        await provider.GetAsync(Broadcaster);
        wire.CallCount.Should().Be(0);

        // Deliberate/user-initiated re-auth — a fresh grant, NOT the routine GetAsync path — must still be
        // able to clear the connection regardless of the guard above.
        await vault.StoreTokensAsync(
            connectionId,
            new(
                "reauthed-access",
                "reauthed-refresh",
                null,
                clock.GetUtcNow().UtcDateTime.AddMinutes(-30)
            )
        );

        // The routine path now resumes refreshing normally.
        clock.Advance(TimeSpan.FromMinutes(15));
        KickAccess? result = await provider.GetAsync(Broadcaster);

        wire.CallCount.Should()
            .Be(1, "the fresh grant cleared needs_reauth so routine refresh resumed");
        result.Should().NotBeNull();
    }

    private static async Task<(
        KickAccessTokenProvider Provider,
        IntegrationTokenVault Vault,
        Guid ConnectionId
    )> BuildAsync(string dbName, CountingKickHandler wire, TimeProvider clock)
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
                Provider = AuthEnums.Platform.Kick,
                ExternalChannelId = ExternalId,
                Name = "kick-storm-streamer",
                NameNormalized = "kick-storm-streamer",
                OwnerUserId = Guid.NewGuid(),
            }
        );
        db.Configurations.Add(
            new()
            {
                BroadcasterId = null,
                Key = "kick.client_id",
                Value = "kick-app-id",
            }
        );
        db.Configurations.Add(
            new()
            {
                BroadcasterId = null,
                Key = "kick.client_secret",
                SecureValue = await protector.ProtectAsync(
                    "kick-app-secret",
                    SystemCredentialsProvider.ContextFor("kick.client_secret")
                ),
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
                Provider: AuthEnums.IntegrationProvider.Kick,
                ProviderAccountId: ExternalId,
                ProviderAccountName: "kick-storm-streamer",
                Scopes: ["chat:write"],
                ClientId: null,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );

        ISystemCredentialsProvider credentials = AuthTestBuilder.CredentialsProvider(
            db,
            protector,
            new ConfigurationBuilder().Build()
        );

        KickAccessTokenProvider provider = new(
            db,
            vault,
            credentials,
            clock,
            new SingleClientFactory(wire),
            NullLogger<KickAccessTokenProvider>.Instance,
            new ConnectionRefreshGate()
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

    /// <summary> Counts every POST; optionally always fails so the backoff path can be driven. </summary>
    private sealed class CountingKickHandler : HttpMessageHandler
    {
        private int _callCount;

        public bool AlwaysFail { get; set; }
        public int CallCount => _callCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            int callNumber = Interlocked.Increment(ref _callCount);

            if (AlwaysFail)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $$"""{"access_token":"kick-access-{{callNumber}}","refresh_token":"kick-refresh-{{callNumber}}","expires_in":3600}""",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
