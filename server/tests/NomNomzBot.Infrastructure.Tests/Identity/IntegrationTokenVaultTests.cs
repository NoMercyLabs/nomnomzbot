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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Integrations.Events;
using NomNomzBot.Infrastructure.Identity;

namespace NomNomzBot.Infrastructure.Tests.Identity;

/// <summary>
/// Proves the OAuth token vault's load-bearing behavior (identity-auth §3.4) over the REAL crypto stack:
/// tokens are sealed at rest (the persisted ciphertext is NOT the plaintext and is independently
/// non-readable) yet decrypt back to the exact plaintext on use; a connect emits the connection event; the
/// failure threshold flips the connection to needs-reauth and emits the re-auth event.
/// </summary>
public sealed class IntegrationTokenVaultTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000d4");

    private static (IntegrationTokenVault Vault, AuthDbContext Db, RecordingEventBus Bus) Build() =>
        Build(Guid.NewGuid().ToString());

    private static (IntegrationTokenVault Vault, AuthDbContext Db, RecordingEventBus Bus) Build(
        string databaseName
    )
    {
        AuthDbContext db = AuthTestBuilder.NewContext(databaseName);
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        RecordingEventBus bus = new();
        // The vault reconciles scopes through IScopeGrantService; a passthrough recorder isolates the vault.
        IScopeGrantService scopeGrant = new NoopScopeGrant();
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            scopeGrant,
            bus,
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        return (vault, db, bus);
    }

    private static UpsertConnectionDto TwitchConnect() =>
        new(
            Tenant,
            AuthEnums.IntegrationProvider.Twitch,
            "twitch-account-123",
            "streamer",
            ["channel:read:subscriptions"],
            ClientId: "client-abc",
            IsByok: false,
            ConnectedByUserId: null,
            SettingsJson: null
        );

    [Fact]
    public async Task StoreThenGet_RoundTripsThePlaintext()
    {
        (IntegrationTokenVault vault, _, _) = Build();
        Guid connectionId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;

        Result store = await vault.StoreTokensAsync(
            connectionId,
            new(
                "access-token-PLAINTEXT",
                "refresh-token-PLAINTEXT",
                AppToken: null,
                AccessExpiresAt: DateTime.UtcNow.AddHours(1)
            )
        );
        store.IsSuccess.Should().BeTrue();

        Result<DecryptedTokenDto> access = await vault.GetAccessTokenAsync(connectionId);
        Result<DecryptedTokenDto> refresh = await vault.GetRefreshTokenAsync(connectionId);

        access.IsSuccess.Should().BeTrue();
        access.Value.Value.Should().Be("access-token-PLAINTEXT");
        access.Value.TokenType.Should().Be(AuthEnums.TokenType.Access);
        access.Value.IsExpired.Should().BeFalse();

        refresh.IsSuccess.Should().BeTrue();
        refresh.Value.Value.Should().Be("refresh-token-PLAINTEXT");
    }

    [Fact]
    public async Task StoredToken_DecryptsAfterARestart_OverTheSamePersistedStore()
    {
        // The reported bug end-to-end: store a token, then a FRESH vault + FRESH DbContext (a process restart)
        // over the SAME persisted database reads it back. Before the fix the DEK record lived only in process
        // RAM, so the restarted process hit KEY_NOT_FOUND → DECRYPT_FAILED and every Helix read returned 0.
        string database = Guid.NewGuid().ToString();

        Guid connectionId;
        {
            (IntegrationTokenVault vault, _, _) = Build(database);
            connectionId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;
            Result store = await vault.StoreTokensAsync(
                connectionId,
                new(
                    "access-token-survives-restart",
                    "refresh-token-survives-restart",
                    AppToken: null,
                    AccessExpiresAt: DateTime.UtcNow.AddHours(1)
                )
            );
            store.IsSuccess.Should().BeTrue();
        }

        // ── Restart: new vault instance, new DbContext, same backing database. ──
        (IntegrationTokenVault restarted, _, _) = Build(database);

        Result<DecryptedTokenDto> access = await restarted.GetAccessTokenAsync(connectionId);
        Result<DecryptedTokenDto> refresh = await restarted.GetRefreshTokenAsync(connectionId);

        access.IsSuccess.Should().BeTrue("the wrapped DEK persisted, so the token still decrypts");
        access.Value.Value.Should().Be("access-token-survives-restart");
        refresh.IsSuccess.Should().BeTrue();
        refresh.Value.Value.Should().Be("refresh-token-survives-restart");
    }

    [Fact]
    public async Task StoredCipherText_IsNotThePlaintext_AndIsNotReadableWithoutTheVault()
    {
        (IntegrationTokenVault vault, AuthDbContext db, _) = Build();
        Guid connectionId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;

        await vault.StoreTokensAsync(
            connectionId,
            new("super-secret-access", null, null, DateTime.UtcNow.AddHours(1))
        );

        IntegrationToken stored = await db
            .IntegrationTokens.AsNoTracking()
            .SingleAsync(t => t.TokenType == AuthEnums.TokenType.Access);

        // The persisted column is a sealed envelope, not the plaintext, and the plaintext is not a substring.
        stored.CipherText.Should().NotBe("super-secret-access");
        stored.CipherText.Should().NotContain("super-secret-access");
        // The DEK that opens it is recorded for crypto-shred targeting.
        stored.EncryptionKeyId.Should().NotBe(Guid.Empty);
        stored.BroadcasterId.Should().Be(Tenant);
    }

    [Fact]
    public async Task FirstConnect_EmitsIntegrationConnectedEvent()
    {
        (IntegrationTokenVault vault, _, RecordingEventBus bus) = Build();

        Result<IntegrationConnectionDto> connection = await vault.UpsertConnectionAsync(
            TwitchConnect()
        );

        IntegrationConnectedEvent connected = bus
            .Published.OfType<IntegrationConnectedEvent>()
            .Single();
        connected.ConnectionId.Should().Be(connection.Value.Id);
        connected.Provider.Should().Be(AuthEnums.IntegrationProvider.Twitch);
        connected.ProviderAccountId.Should().Be("twitch-account-123");
        connected.BroadcasterId.Should().Be(Tenant);
    }

    [Fact]
    public async Task StoreTokens_SetsConnectedStatus_AndEmitsRefreshedEvent()
    {
        (IntegrationTokenVault vault, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid connectionId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;

        await vault.StoreTokensAsync(
            connectionId,
            new("a", "r", null, DateTime.UtcNow.AddHours(1))
        );

        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        connection.ConnectedAt.Should().NotBeNull();
        bus.Published.OfType<IntegrationTokenRefreshedEvent>().Should().ContainSingle();
    }

    [Fact]
    public async Task StoreTokens_WhenScopeReconcileFails_SurfacesTheFailure_InsteadOfReportingSuccess()
    {
        // Guards S070: StoreTokensAsync used to await ReconcileGrantedScopesAsync and discard the Result,
        // so a reconcile failure (e.g. the connection vanished mid-refresh) was invisible to the caller —
        // StoreTokensAsync always returned Result.Success() regardless. The vault must propagate the real
        // failure instead of swallowing it.
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        RecordingEventBus bus = new();
        FailingScopeGrant failingScopeGrant = new();
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            failingScopeGrant,
            bus,
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        Guid connectionId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;

        Result store = await vault.StoreTokensAsync(
            connectionId,
            new("access-token", "refresh-token", null, DateTime.UtcNow.AddHours(1)),
            grantedScopes: ["channel:read:subscriptions"]
        );

        store.IsSuccess.Should().BeFalse("the scope reconcile failed and must not be swallowed");
        store.ErrorMessage.Should().Be("Reconcile blew up.");
        store.ErrorCode.Should().Be("RECONCILE_BOOM");
        // The token refresh event must not fire on a failed store — a half-applied write is not a success.
        bus.Published.OfType<IntegrationTokenRefreshedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task MarkRefreshFailure_AtThreshold_FlipsToNeedsReauth_AndEmitsEvent()
    {
        (IntegrationTokenVault vault, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid connectionId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;
        await vault.StoreTokensAsync(
            connectionId,
            new("a", "r", null, DateTime.UtcNow.AddHours(1))
        );

        await vault.MarkRefreshFailureAsync(connectionId, "err-1");
        await vault.MarkRefreshFailureAsync(connectionId, "err-2");

        IntegrationConnection afterTwo = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        afterTwo.Status.Should().Be(AuthEnums.IntegrationStatus.Connected, "still under threshold");

        await vault.MarkRefreshFailureAsync(connectionId, "err-3");

        IntegrationConnection afterThree = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        afterThree.Status.Should().Be(AuthEnums.IntegrationStatus.NeedsReauth);
        afterThree.ConsecutiveFailureCount.Should().Be(3);

        IntegrationNeedsReauthEvent reauth = bus
            .Published.OfType<IntegrationNeedsReauthEvent>()
            .Single();
        reauth.ConnectionId.Should().Be(connectionId);
        reauth.ConsecutiveFailureCount.Should().Be(3);
    }

    [Fact]
    public async Task Reconnect_WithAnExistingLiveConnection_UpdatesItInPlace_InsteadOfInsertingASibling()
    {
        // The qtkitte incident: reconnecting Spotify with a NEW (BYOC) client id created a SECOND live row —
        // the dashboard kept reading the stale needs_reauth one. The upsert must match the live connection by
        // (BroadcasterId, Provider) alone, never by the fetched ProviderAccountId (which a reconnect can
        // legitimately resolve differently than the stale row's).
        (IntegrationTokenVault vault, AuthDbContext db, _) = Build();
        Guid firstId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;
        await vault.StoreTokensAsync(
            firstId,
            new("old-access", "old-refresh", null, DateTime.UtcNow.AddHours(1))
        );
        await vault.MarkRefreshFailureAsync(firstId, "e1");
        await vault.MarkRefreshFailureAsync(firstId, "e2");
        await vault.MarkRefreshFailureAsync(firstId, "e3"); // crosses the threshold -> needs_reauth

        // Reconnect: same broadcaster/provider, a DIFFERENT resolved account id/name and a NEW (BYOC) client id.
        Result<IntegrationConnectionDto> reconnect = await vault.UpsertConnectionAsync(
            new(
                Tenant,
                AuthEnums.IntegrationProvider.Twitch,
                "twitch-account-BYOC-999",
                "streamer (byoc)",
                ["channel:read:subscriptions"],
                ClientId: "byoc-client-xyz",
                IsByok: true,
                ConnectedByUserId: null,
                SettingsJson: null
            )
        );
        reconnect
            .Value.Id.Should()
            .Be(firstId, "the same live connection is updated, not sibling-inserted");
        await vault.StoreTokensAsync(
            reconnect.Value.Id,
            new("new-access", "new-refresh", null, DateTime.UtcNow.AddHours(1))
        );

        List<IntegrationConnection> live = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null)
            .ToListAsync();
        live.Should()
            .ContainSingle("exactly one live row must exist for this (broadcaster, provider)");
        IntegrationConnection surviving = live.Single();
        surviving.ClientId.Should().Be("byoc-client-xyz");
        surviving.IsByok.Should().BeTrue();
        surviving.Status.Should().Be(AuthEnums.IntegrationStatus.Connected);
        surviving
            .ConsecutiveFailureCount.Should()
            .Be(0, "a successful reconnect clears the failure count");
    }

    [Fact]
    public async Task Reconnect_AfterADisconnect_CreatesAFreshLiveRow_NotBlockedByTheSoftDeletedOne()
    {
        (IntegrationTokenVault vault, AuthDbContext db, _) = Build();
        Guid firstId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;
        await vault.StoreTokensAsync(firstId, new("a", "r", null, DateTime.UtcNow.AddHours(1)));
        await vault.RevokeConnectionAsync(firstId, "user_disconnect"); // status=revoked; NOT soft-deleted by this call

        // Simulate the row also being soft-deleted (e.g. GDPR erasure / full disconnect cleanup) — the
        // scenario the filtered unique index and the upsert's DeletedAt==null match must tolerate.
        IntegrationConnection revoked = await db.IntegrationConnections.SingleAsync(c =>
            c.Id == firstId
        );
        revoked.DeletedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        Result<IntegrationConnectionDto> reconnect = await vault.UpsertConnectionAsync(
            TwitchConnect()
        );

        reconnect
            .IsSuccess.Should()
            .BeTrue("a soft-deleted prior connection must not block a fresh reconnect");
        reconnect.Value.Id.Should().NotBe(firstId);

        List<IntegrationConnection> live = await db
            .IntegrationConnections.IgnoreQueryFilters()
            .Where(c => c.DeletedAt == null)
            .ToListAsync();
        live.Should().ContainSingle();
        live.Single().Id.Should().Be(reconnect.Value.Id);
    }

    [Fact]
    public async Task RevokeConnection_SoftDeletesTokens_AndEmitsDisconnectedEvent()
    {
        (IntegrationTokenVault vault, AuthDbContext db, RecordingEventBus bus) = Build();
        Guid connectionId = (await vault.UpsertConnectionAsync(TwitchConnect())).Value.Id;
        await vault.StoreTokensAsync(
            connectionId,
            new("a", "r", null, DateTime.UtcNow.AddHours(1))
        );

        await vault.RevokeConnectionAsync(connectionId, "user_disconnect");

        IntegrationConnection connection = await db
            .IntegrationConnections.AsNoTracking()
            .SingleAsync();
        connection.Status.Should().Be(AuthEnums.IntegrationStatus.Revoked);

        List<IntegrationToken> tokens = await db.IntegrationTokens.AsNoTracking().ToListAsync();
        tokens.Should().OnlyContain(t => t.DeletedAt != null);

        IntegrationDisconnectedEvent disconnected = bus
            .Published.OfType<IntegrationDisconnectedEvent>()
            .Single();
        disconnected.Reason.Should().Be("user_disconnect");
    }

    /// <summary>A passthrough scope-grant so the vault's reconcile call is a no-op for these vault-focused tests.</summary>
    private sealed class NoopScopeGrant : IScopeGrantService
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

    /// <summary>A scope-grant stub whose reconcile always fails, to prove StoreTokensAsync (S070) propagates it.</summary>
    private sealed class FailingScopeGrant : IScopeGrantService
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
        ) =>
            Task.FromResult(
                Result.Failure<IReadOnlyList<string>>("Reconcile blew up.", "RECONCILE_BOOM")
            );
    }
}
