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
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.Services;
using NomNomzBot.Domain.Integrations.Entities;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Marketplace;
using NomNomzBot.Infrastructure.Tests.Identity;

namespace NomNomzBot.Infrastructure.Tests.Marketplace;

/// <summary>
/// Proves the publisher-token custody (marketplace.md §4: "the bot stores it vaulted") over the REAL vault
/// + crypto stack: set stores an AES-sealed envelope (the persisted ciphertext is NOT the raw token) and
/// flips <c>hasToken</c>; the value is never echoed by any read surface — only the internal outbound seam
/// decrypts it back; clear revokes and survives repeats (idempotent).
/// </summary>
public sealed class MarketplacePublisherTokenServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000e001");
    private static readonly Guid Actor = Guid.Parse("0192a000-0000-7000-8000-00000000e0aa");
    private const string RawToken = "mp_publisher_raw_token_SECRET";

    private static (MarketplacePublisherTokenService Service, AuthDbContext Db) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext(Guid.NewGuid().ToString());
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
        return (new MarketplacePublisherTokenService(vault), db);
    }

    [Fact]
    public async Task Set_vaults_the_token_sealed_and_flips_hasToken_without_echoing()
    {
        (MarketplacePublisherTokenService service, AuthDbContext db) = Build();

        Result set = await service.SetTokenAsync(Channel, RawToken, Actor);
        set.IsSuccess.Should().BeTrue(set.ErrorMessage);

        Result<MarketplacePublisherStatusDto> status = await service.GetStatusAsync(Channel);
        status.IsSuccess.Should().BeTrue();
        status.Value.HasToken.Should().BeTrue();

        // Protected at rest: the persisted ciphertext is a sealed envelope, never the raw token.
        IntegrationToken stored = (await db.IntegrationTokens.IgnoreQueryFilters().ToListAsync())
            .Should()
            .ContainSingle()
            .Subject;
        stored.CipherText.Should().NotBeNullOrEmpty();
        stored.CipherText.Should().NotBe(RawToken);
        stored.CipherText.Should().NotContain(RawToken);

        // The write-only contract: the ONLY readable REST-facing shape carries presence, not the value.
        status.Value.Should().BeEquivalentTo(new MarketplacePublisherStatusDto(true));

        // The internal outbound seam round-trips the exact plaintext for the marketplace call.
        (await service.GetPublisherTokenAsync(Channel))
            .Should()
            .Be(RawToken);
    }

    [Fact]
    public async Task Replacing_the_token_reseals_and_serves_the_new_value()
    {
        (MarketplacePublisherTokenService service, _) = Build();
        (await service.SetTokenAsync(Channel, RawToken, Actor)).IsSuccess.Should().BeTrue();

        Result replaced = await service.SetTokenAsync(Channel, "mp_publisher_ROTATED", Actor);

        replaced.IsSuccess.Should().BeTrue(replaced.ErrorMessage);
        (await service.GetPublisherTokenAsync(Channel)).Should().Be("mp_publisher_ROTATED");
    }

    [Fact]
    public async Task Clear_revokes_the_token_and_is_idempotent()
    {
        (MarketplacePublisherTokenService service, _) = Build();
        (await service.SetTokenAsync(Channel, RawToken, Actor)).IsSuccess.Should().BeTrue();

        Result cleared = await service.ClearTokenAsync(Channel);
        cleared.IsSuccess.Should().BeTrue(cleared.ErrorMessage);

        (await service.GetStatusAsync(Channel)).Value.HasToken.Should().BeFalse();
        (await service.GetPublisherTokenAsync(Channel)).Should().BeNull();

        // Clearing again (or with nothing stored) still succeeds.
        (await service.ClearTokenAsync(Channel))
            .IsSuccess.Should()
            .BeTrue();
    }

    [Fact]
    public async Task An_empty_token_is_refused()
    {
        (MarketplacePublisherTokenService service, _) = Build();

        Result result = await service.SetTokenAsync(Channel, "   ", Actor);

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("VALIDATION_FAILED");
        (await service.GetStatusAsync(Channel)).Value.HasToken.Should().BeFalse();
    }

    /// <summary>A passthrough scope-grant so the vault's reconcile call is a no-op for these custody tests.</summary>
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
