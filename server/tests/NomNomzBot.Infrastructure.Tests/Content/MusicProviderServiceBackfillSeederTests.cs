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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Content.Music;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Music;
using NomNomzBot.Infrastructure.Tests.Identity;
using Xunit;

namespace NomNomzBot.Infrastructure.Tests.Content;

/// <summary>
/// Behavioural proof for <see cref="MusicProviderServiceBackfillSeeder"/>: a Spotify account that connected
/// before the connect-time token mirror existed (a vaulted grant, no <c>Service</c> row) gains a usable
/// <c>Service</c> row on boot — sealed under the exact context the music provider unseals — and a re-run neither
/// duplicates nor corrupts it. Drives the REAL vault + REAL mirror over the REAL crypto stack, so the assertion
/// is that the provider's <c>GetTokenAsync</c> would now find AND decrypt the token, not that a call merely ran.
/// </summary>
public sealed class MusicProviderServiceBackfillSeederTests
{
    private static readonly Guid Tenant = Guid.Parse("0192a000-0000-7000-8000-0000000000f1");
    private static readonly DateTime Expiry = new(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);

    private const string Provider = "spotify";
    private const string AccessPlaintext = "spotify-access-PLAINTEXT";
    private const string RefreshPlaintext = "spotify-refresh-PLAINTEXT";
    private const string ClientId = "spotify-client-id";
    private const string ClientSecret = "spotify-client-secret";

    private sealed record Harness(
        AuthDbContext Db,
        ITokenProtector Protector,
        IIntegrationTokenVault Vault,
        MusicProviderServiceBackfillSeeder Seeder
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
        MusicProviderTokenMirror mirror = new(
            db,
            protector,
            NullLogger<MusicProviderTokenMirror>.Instance
        );
        MusicProviderServiceBackfillSeeder seeder = new(
            db,
            vault,
            new FixedCredentials(),
            mirror,
            NullLogger<MusicProviderServiceBackfillSeeder>.Instance
        );
        return new Harness(db, protector, vault, seeder);
    }

    /// <summary>Seeds the pre-mirror state: a connected Spotify connection whose tokens are vaulted but which has no Service row.</summary>
    private static async Task<Guid> SeedVaultedConnectionAsync(Harness h)
    {
        Guid connectionId = (
            await h.Vault.UpsertConnectionAsync(
                new UpsertConnectionDto(
                    Tenant,
                    Provider,
                    ProviderAccountId: "spotify-user-1",
                    ProviderAccountName: "streamer",
                    Scopes: ["user-read-playback-state"],
                    ClientId: ClientId,
                    IsByok: false,
                    ConnectedByUserId: null,
                    SettingsJson: null
                )
            )
        )
            .Value
            .Id;

        Result store = await h.Vault.StoreTokensAsync(
            connectionId,
            new StoreTokensDto(AccessPlaintext, RefreshPlaintext, AppToken: null, Expiry)
        );
        store.IsSuccess.Should().BeTrue();
        return connectionId;
    }

    [Fact]
    public async Task Backfills_a_usable_Service_row_the_music_provider_can_decrypt()
    {
        Harness h = Build(Guid.NewGuid().ToString());
        await SeedVaultedConnectionAsync(h);

        // Precondition: the pre-mirror gap — vaulted tokens exist, but no Service row does.
        (await h.Db.Services.AnyAsync())
            .Should()
            .BeFalse();

        await h.Seeder.SeedAsync();

        Service service = await h.Db.Services.SingleAsync(s => s.Name == Provider);
        service.BroadcasterId.Should().Be(Tenant);
        service.Enabled.Should().BeTrue();
        service.TokenExpiry.Should().Be(Expiry);
        service.AccessToken.Should().NotBeNullOrEmpty();
        service.RefreshToken.Should().NotBeNullOrEmpty();
        service.ClientId.Should().NotBeNullOrEmpty();
        service.ClientSecret.Should().NotBeNullOrEmpty();

        // The load-bearing proof: the sealed columns unseal under the SAME context the music provider uses
        // ((broadcaster, provider, field)), so GetTokenAsync would recover the real tokens — the row is usable,
        // not merely present.
        (await Unseal(h, service.AccessToken!, "access"))
            .Should()
            .Be(AccessPlaintext);
        (await Unseal(h, service.RefreshToken!, "refresh")).Should().Be(RefreshPlaintext);
        (await Unseal(h, service.ClientId!, "client_id")).Should().Be(ClientId);
        (await Unseal(h, service.ClientSecret!, "client_secret")).Should().Be(ClientSecret);
    }

    [Fact]
    public async Task A_second_run_is_idempotent_and_leaves_one_intact_row()
    {
        string database = Guid.NewGuid().ToString();
        Harness h = Build(database);
        await SeedVaultedConnectionAsync(h);

        await h.Seeder.SeedAsync();
        await h.Seeder.SeedAsync();

        List<Service> services = await h.Db.Services.Where(s => s.Name == Provider).ToListAsync();
        services
            .Should()
            .ContainSingle("the mirror upserts by (BroadcasterId, provider) — no duplicate row");
        (await Unseal(h, services[0].AccessToken!, "access"))
            .Should()
            .Be(AccessPlaintext, "the re-run leaves the token intact, not corrupted");
    }

    [Fact]
    public async Task A_warm_row_is_left_untouched_and_nothing_is_added()
    {
        Harness h = Build(Guid.NewGuid().ToString());
        await SeedVaultedConnectionAsync(h);

        // First boot writes the row; a second boot must anti-join past it (a no-op).
        await h.Seeder.SeedAsync();
        string firstRowId = (await h.Db.Services.SingleAsync(s => s.Name == Provider)).Id;

        await h.Seeder.SeedAsync();

        Service service = await h.Db.Services.SingleAsync(s => s.Name == Provider);
        service.Id.Should().Be(firstRowId, "the existing usable row is reused, never replaced");
    }

    private static Task<string?> Unseal(Harness h, string cipherText, string field) =>
        h.Protector.TryUnprotectAsync(
            cipherText,
            new TokenProtectionContext(Tenant.ToString(), Provider, field)
        );

    /// <summary>A fixed app-credentials resolver — the backfill needs the client id/secret the mirror seals for refresh.</summary>
    private sealed class FixedCredentials : ISystemCredentialsProvider
    {
        public Task<SystemAppCredentials?> GetAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) =>
            Task.FromResult<SystemAppCredentials?>(
                new SystemAppCredentials(ClientId, ClientSecret)
            );

        public Task<string?> GetClientIdAsync(
            string provider,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(ClientId);

        public Task<string?> GetValueAsync(
            string provider,
            string field,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<string?>(null);
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
