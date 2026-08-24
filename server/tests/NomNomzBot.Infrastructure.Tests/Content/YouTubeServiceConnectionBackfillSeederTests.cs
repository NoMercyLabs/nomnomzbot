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
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Content.Identity;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Content;

/// <summary>
/// Behavioural proof for <see cref="YouTubeServiceConnectionBackfillSeeder"/> (S036c-a, step 1 of 3): a
/// YouTube account that connected before the OAuth callback's vault dual-write existed (a legacy
/// <c>Service</c> row, no <c>IntegrationConnection</c>/<c>IntegrationToken</c> row) becomes readable through
/// <see cref="IIntegrationTokenVault"/> on boot, decrypting to the EXACT same plaintext the legacy row holds —
/// additive only, the <c>Service</c> row is never mutated or deleted.
/// </summary>
public sealed class YouTubeServiceConnectionBackfillSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("0192b000-0000-7000-8000-0000000000f2");
    private static readonly Guid OtherTenant = Guid.Parse("0192b000-0000-7000-8000-0000000000f3");
    private static readonly DateTime Expiry = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Provider = "youtube";
    private const string AccessPlaintext = "youtube-access-PLAINTEXT";
    private const string RefreshPlaintext = "youtube-refresh-PLAINTEXT";
    private const string ClientIdPlaintext = "youtube-client-id";

    private sealed record Harness(
        AuthDbContext Db,
        ITokenProtector Protector,
        IIntegrationTokenVault Vault,
        YouTubeServiceConnectionBackfillSeeder Seeder
    );

    private static Harness Build(string databaseName)
    {
        AuthDbContext db = AuthTestBuilder.NewContext(databaseName);
        ITokenProtector protector = AuthTestBuilder.RealTokenProtector(
            db,
            out ISubjectKeyService keys
        );
        IntegrationTokenVault vault = new(
            db,
            protector,
            keys,
            new NoopScopeGrant(),
            new RecordingEventBus(),
            TimeProvider.System,
            NullLogger<IntegrationTokenVault>.Instance
        );
        YouTubeServiceConnectionBackfillSeeder seeder = new(
            db,
            vault,
            protector,
            NullLogger<YouTubeServiceConnectionBackfillSeeder>.Instance
        );
        return new(db, protector, vault, seeder);
    }

    /// <summary>Seeds the pre-dual-write legacy state: a Service row sealed exactly as the real write path seals it.</summary>
    private static async Task SeedLegacyServiceAsync(
        Harness h,
        Guid broadcasterId,
        string accessPlaintext = AccessPlaintext,
        string? refreshPlaintext = RefreshPlaintext,
        string? clientIdPlaintext = ClientIdPlaintext
    )
    {
        string subject = broadcasterId.ToString();
        Service service = new()
        {
            Name = Provider,
            Enabled = true,
            BroadcasterId = broadcasterId,
            UserId = "yt-channel-1",
            UserName = "streamer",
            Scopes = ["https://www.googleapis.com/auth/youtube.readonly"],
            AccessToken = await h.Protector.ProtectAsync(
                accessPlaintext,
                new(subject, Provider, "access")
            ),
            RefreshToken = refreshPlaintext is null
                ? null
                : await h.Protector.ProtectAsync(
                    refreshPlaintext,
                    new(subject, Provider, "refresh")
                ),
            ClientId = clientIdPlaintext is null
                ? null
                : await h.Protector.ProtectAsync(
                    clientIdPlaintext,
                    new(subject, Provider, "client_id")
                ),
            TokenExpiry = Expiry,
        };
        h.Db.Services.Add(service);
        await h.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Backfills_a_vault_connection_that_decrypts_to_the_legacy_plaintext()
    {
        Harness h = Build(Guid.NewGuid().ToString());
        await SeedLegacyServiceAsync(h, Tenant);

        // Precondition: the pre-dual-write gap — a legacy Service row exists, but no vault connection does.
        (await h.Db.IntegrationConnections.AnyAsync())
            .Should()
            .BeFalse();

        await h.Seeder.SeedAsync();

        IntegrationConnection connection = await h.Db.IntegrationConnections.SingleAsync(c =>
            c.Provider == Provider && c.BroadcasterId == Tenant
        );

        // The load-bearing proof: read the backfilled connection back through the REAL vault and assert the
        // DECRYPTED value equals the original plaintext — not merely that a row/token is non-null.
        Result<DecryptedTokenDto> access = await h.Vault.GetAccessTokenAsync(connection.Id);
        access.IsSuccess.Should().BeTrue();
        access.Value.Value.Should().Be(AccessPlaintext);
        access.Value.ExpiresAt.Should().Be(Expiry);

        Result<DecryptedTokenDto> refresh = await h.Vault.GetRefreshTokenAsync(connection.Id);
        refresh.IsSuccess.Should().BeTrue();
        refresh.Value.Value.Should().Be(RefreshPlaintext);

        // The legacy Service row must survive UNTOUCHED — this step is additive only, no consumer cutover.
        Service legacy = await h.Db.Services.SingleAsync(s => s.Name == Provider);
        legacy.AccessToken.Should().NotBeNullOrEmpty();
        legacy.RefreshToken.Should().NotBeNullOrEmpty();
        (
            await h.Protector.TryUnprotectAsync(
                legacy.AccessToken,
                new(Tenant.ToString(), Provider, "access")
            )
        )
            .Should()
            .Be(
                AccessPlaintext,
                "the legacy row must be left byte-for-byte readable, never mutated"
            );
    }

    [Fact]
    public async Task A_second_run_is_idempotent_and_leaves_exactly_one_connection()
    {
        string database = Guid.NewGuid().ToString();
        Harness h = Build(database);
        await SeedLegacyServiceAsync(h, Tenant);

        await h.Seeder.SeedAsync();
        await h.Seeder.SeedAsync();

        List<IntegrationConnection> connections = await h
            .Db.IntegrationConnections.Where(c =>
                c.Provider == Provider && c.BroadcasterId == Tenant
            )
            .ToListAsync();
        connections
            .Should()
            .ContainSingle("the backfill upserts by (BroadcasterId, Provider) — no duplicate row");

        Result<DecryptedTokenDto> access = await h.Vault.GetAccessTokenAsync(connections[0].Id);
        access.IsSuccess.Should().BeTrue();
        access
            .Value.Value.Should()
            .Be(AccessPlaintext, "the re-run leaves the token intact, not corrupted");
    }

    [Fact]
    public async Task A_channel_with_no_YouTube_Service_row_gets_no_connection()
    {
        Harness h = Build(Guid.NewGuid().ToString());
        // Seed an unrelated channel with a YouTube row so the seeder has SOMETHING to do, but assert the
        // channel with NO Service row at all gets no IntegrationConnection.
        await SeedLegacyServiceAsync(h, OtherTenant);

        await h.Seeder.SeedAsync();

        (await h.Db.IntegrationConnections.AnyAsync(c => c.BroadcasterId == Tenant))
            .Should()
            .BeFalse();
        (await h.Db.IntegrationConnections.AnyAsync(c => c.Provider == Provider))
            .Should()
            .BeTrue(
                "the sibling channel's row proves the seeder ran, not that it is a no-op overall"
            );
    }

    /// <summary>A passthrough scope-grant so the vault's reconcile call is a no-op while seeding the vaulted state.</summary>
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
}
