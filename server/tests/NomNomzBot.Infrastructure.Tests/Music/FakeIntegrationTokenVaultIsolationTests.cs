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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;

namespace NomNomzBot.Infrastructure.Tests.Music;

/// <summary>
/// S036b — pins the fix for a cross-test-class race: <see cref="FakeIntegrationTokenVault"/> used to
/// back its token storage with a single process-global <c>static readonly Dictionary</c>, so under
/// parallel xUnit execution two unrelated test classes — each with their own <see cref="MusicTestDbContext"/>
/// — could observe and corrupt each other's seeded tokens (a different random test failing every run,
/// all green individually). The fix scopes storage per DB INSTANCE via a <c>ConditionalWeakTable</c>,
/// which must keep two guarantees at once: separate dbs never see each other's tokens (this file), and
/// separate <see cref="FakeIntegrationTokenVault"/> instances built over the SAME db still share state
/// (already covered by <see cref="SongRequestQueueCrossScopeTests"/>-style scoping and S003's own tests).
/// </summary>
public sealed class FakeIntegrationTokenVaultIsolationTests
{
    private static readonly Guid Broadcaster = Guid.Parse("0192a000-0000-7000-8000-0000000f2001");

    [Fact]
    public async Task Two_vaults_over_different_dbs_do_not_observe_each_others_connections()
    {
        MusicTestDbContext dbOne = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        MusicTestDbContext dbTwo = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

        FakeIntegrationTokenVault vaultOne = new(dbOne);
        FakeIntegrationTokenVault vaultTwo = new(dbTwo);

        Guid connectionOne = vaultOne.SeedConnectedSpotify(
            Broadcaster,
            accessToken: "vault-one-token"
        );
        Guid connectionTwo = vaultTwo.SeedConnectedSpotify(
            Broadcaster,
            accessToken: "vault-two-token"
        );

        // Each vault resolves its OWN connection id to its OWN token.
        Result<DecryptedTokenDto> ownLookupOne = await vaultOne.GetAccessTokenAsync(connectionOne);
        Result<DecryptedTokenDto> ownLookupTwo = await vaultTwo.GetAccessTokenAsync(connectionTwo);
        ownLookupOne.IsSuccess.Should().BeTrue();
        ownLookupOne.Value.Value.Should().Be("vault-one-token");
        ownLookupTwo.IsSuccess.Should().BeTrue();
        ownLookupTwo.Value.Value.Should().Be("vault-two-token");

        // Neither vault can see the OTHER db's connection id at all — proving there is no shared,
        // process-global backing store left for a parallel test class to corrupt.
        Result<DecryptedTokenDto> crossLookupOneForTwo = await vaultOne.GetAccessTokenAsync(
            connectionTwo
        );
        Result<DecryptedTokenDto> crossLookupTwoForOne = await vaultTwo.GetAccessTokenAsync(
            connectionOne
        );
        crossLookupOneForTwo.IsSuccess.Should().BeFalse();
        crossLookupOneForTwo.ErrorCode.Should().Be("NOT_FOUND");
        crossLookupTwoForOne.IsSuccess.Should().BeFalse();
        crossLookupTwoForOne.ErrorCode.Should().Be("NOT_FOUND");
    }

    [Fact]
    public async Task Two_vaults_over_the_SAME_db_still_share_state_like_real_DI_scopes()
    {
        MusicTestDbContext db = new(
            new DbContextOptionsBuilder<MusicTestDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

        // Scope 1 seeds the connection (mirrors a real OAuth connect handled by one scoped instance).
        FakeIntegrationTokenVault scopeOne = new(db);
        Guid connectionId = scopeOne.SeedConnectedSpotify(Broadcaster, accessToken: "shared-token");

        // Scope 2 is a BRAND-NEW FakeIntegrationTokenVault over the same db (exactly how the real
        // container hands out a fresh scoped IIntegrationTokenVault per request) — it must still resolve
        // the token scope 1 seeded.
        FakeIntegrationTokenVault scopeTwo = new(db);
        Result<DecryptedTokenDto> lookup = await scopeTwo.GetAccessTokenAsync(connectionId);

        lookup.IsSuccess.Should().BeTrue();
        lookup.Value.Value.Should().Be("shared-token");
    }
}
