// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Marketplace;

/// <summary>
/// <see cref="IMarketplacePublisherTokenService"/> over the crypto-shred-ready
/// <see cref="IIntegrationTokenVault"/> (provider <c>marketplace</c>, account sentinel
/// <c>publisher</c>) — the ONE custody seam for per-channel provider secrets, so the publisher token is
/// AES-256-GCM sealed at rest with no new table and no hand-rolled crypto. The marketplace account is not
/// an OAuth flow (the user pastes a token minted on the marketplace site), so there is no refresh token and
/// no scope set — just the single vaulted access credential.
/// </summary>
public sealed class MarketplacePublisherTokenService : IMarketplacePublisherTokenService
{
    /// <summary>The one publisher identity per channel — the vault upsert key's account component.</summary>
    private const string PublisherAccountSentinel = "publisher";

    private readonly IIntegrationTokenVault _vault;

    public MarketplacePublisherTokenService(IIntegrationTokenVault vault)
    {
        _vault = vault;
    }

    public async Task<Result> SetTokenAsync(
        Guid broadcasterId,
        string token,
        Guid? actorUserId = null,
        CancellationToken ct = default
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure("The publisher token must not be empty.", "VALIDATION_FAILED");

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                broadcasterId,
                AuthEnums.IntegrationProvider.Marketplace,
                PublisherAccountSentinel,
                ProviderAccountName: null,
                Scopes: [],
                ClientId: null,
                IsByok: false,
                ConnectedByUserId: actorUserId,
                SettingsJson: null
            ),
            ct
        );
        if (connection.IsFailure)
            return Result.Failure(
                connection.ErrorMessage ?? "The publisher token could not be stored.",
                connection.ErrorCode ?? "TOKEN_STORE_FAILED"
            );

        return await _vault.StoreTokensAsync(
            connection.Value.Id,
            new StoreTokensDto(token, RefreshToken: null, AppToken: null, AccessExpiresAt: null),
            grantedScopes: null,
            ct
        );
    }

    public async Task<Result> ClearTokenAsync(Guid broadcasterId, CancellationToken ct = default)
    {
        IntegrationConnectionDto? connection = await FindConnectionAsync(broadcasterId, ct);
        if (connection is null)
            return Result.Success(); // idempotent — clearing an absent token succeeds
        return await _vault.RevokeConnectionAsync(
            connection.Id,
            "Marketplace publisher token cleared.",
            ct
        );
    }

    public async Task<Result<MarketplacePublisherStatusDto>> GetStatusAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        IntegrationConnectionDto? connection = await FindConnectionAsync(broadcasterId, ct);
        bool hasToken = connection?.Status == AuthEnums.IntegrationStatus.Connected;
        return Result.Success(new MarketplacePublisherStatusDto(hasToken));
    }

    public async Task<string?> GetPublisherTokenAsync(
        Guid broadcasterId,
        CancellationToken ct = default
    )
    {
        IntegrationConnectionDto? connection = await FindConnectionAsync(broadcasterId, ct);
        if (connection is null || connection.Status != AuthEnums.IntegrationStatus.Connected)
            return null;

        Result<DecryptedTokenDto> token = await _vault.GetAccessTokenAsync(connection.Id, ct);
        return token.IsSuccess ? token.Value.Value : null;
    }

    private async Task<IntegrationConnectionDto?> FindConnectionAsync(
        Guid broadcasterId,
        CancellationToken ct
    )
    {
        Result<IReadOnlyList<IntegrationConnectionDto>> connections =
            await _vault.ListConnectionsAsync(broadcasterId, ct);
        if (connections.IsFailure)
            return null;
        return connections.Value.FirstOrDefault(c =>
            c.Provider == AuthEnums.IntegrationProvider.Marketplace
        );
    }
}
