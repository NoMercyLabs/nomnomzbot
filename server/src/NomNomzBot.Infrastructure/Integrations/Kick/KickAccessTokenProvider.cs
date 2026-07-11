// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Integrations.Kick;

/// <summary>
/// <see cref="IKickAccessTokenProvider"/> over the vaulted Kick connection: the tenant channel's
/// <c>ExternalChannelId</c> IS the streamer's numeric Kick account id (the platform channel is
/// provisioned from the same identity the login vaulted), so the connection is found by
/// <c>(Provider=kick, ProviderAccountId=externalId)</c>. An expiring token refreshes against
/// id.kick.com with the shared app credentials — Kick is OAuth 2.1 and ROTATES the refresh token on
/// every grant, so the NEW pair is re-vaulted (losing it would strand the connection); a failed refresh
/// is marked on the connection so the reauth surface fires.
/// </summary>
public sealed class KickAccessTokenProvider : IKickAccessTokenProvider
{
    private const string TokenEndpoint = "https://id.kick.com/oauth/token";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly TimeProvider _clock;
    private readonly HttpClient _http;
    private readonly ILogger<KickAccessTokenProvider> _logger;

    public KickAccessTokenProvider(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        ISystemCredentialsProvider credentials,
        TimeProvider clock,
        IHttpClientFactory httpClientFactory,
        ILogger<KickAccessTokenProvider> logger
    )
    {
        _db = db;
        _vault = vault;
        _credentials = credentials;
        _clock = clock;
        _http = httpClientFactory.CreateClient("kick");
        _logger = logger;
    }

    public async Task<KickAccess?> GetAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        string? externalId = await _db
            .Channels.Where(c => c.Id == broadcasterId && c.Provider == AuthEnums.Platform.Kick)
            .Select(c => c.ExternalChannelId)
            .FirstOrDefaultAsync(cancellationToken);
        if (
            externalId is null
            || !long.TryParse(
                externalId,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out long kickUserId
            )
        )
        {
            _logger.LogDebug(
                "No Kick channel identity for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }

        // Two possible custody rows for the same Kick account, both keyed by the numeric account id:
        // the streamer-plane integration connect (tenant-scoped, carries the chat/moderation/events
        // scopes) and the identity-plane login connection (BroadcasterId null, user:read only). Prefer
        // the scoped one — it is the grant the chat surface actually needs.
        var connectionRow = await _db
            .IntegrationConnections.Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.Kick
                && c.ProviderAccountId == externalId
                && c.Status != "revoked"
            )
            .OrderByDescending(c => c.BroadcasterId != null)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(cancellationToken);
        if (connectionRow is null)
        {
            _logger.LogDebug(
                "No Kick connection vaulted for account {KickUserId} (broadcaster {BroadcasterId})",
                kickUserId,
                broadcasterId
            );
            return null;
        }
        Guid connectionId = connectionRow.Id;

        Result<DecryptedTokenDto> access = await _vault.GetAccessTokenAsync(
            connectionId,
            cancellationToken
        );
        if (access.IsFailure)
            return null;

        bool expiring =
            access.Value.IsExpired
            || (
                access.Value.ExpiresAt is { } expiresAt
                && expiresAt <= _clock.GetUtcNow().UtcDateTime.Add(RefreshMargin)
            );
        if (!expiring)
            return new KickAccess(access.Value.Value, kickUserId);

        string? refreshed = await RefreshAsync(connectionId, cancellationToken);
        return refreshed is null ? null : new KickAccess(refreshed, kickUserId);
    }

    private async Task<string?> RefreshAsync(Guid connectionId, CancellationToken ct)
    {
        Result<DecryptedTokenDto> refresh = await _vault.GetRefreshTokenAsync(connectionId, ct);
        if (refresh.IsFailure)
            return null;

        SystemAppCredentials? app = await _credentials.GetAsync(AuthEnums.LoginProvider.Kick, ct);
        if (app is null)
        {
            _logger.LogWarning("Kick credentials are not configured — cannot refresh");
            return null;
        }

        using FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh.Value.Value,
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
            }
        );

        try
        {
            HttpResponseMessage response = await _http.PostAsync(TokenEndpoint, form, ct);
            if (!response.IsSuccessStatusCode)
            {
                await _vault.MarkRefreshFailureAsync(
                    connectionId,
                    $"Kick refresh failed ({(int)response.StatusCode})",
                    ct
                );
                return null;
            }

            KickTokenResponse? token = await response.Content.ReadFromJsonAsync<KickTokenResponse>(
                cancellationToken: ct
            );
            if (token is null || string.IsNullOrEmpty(token.AccessToken))
            {
                await _vault.MarkRefreshFailureAsync(
                    connectionId,
                    "Kick refresh returned an unexpected body",
                    ct
                );
                return null;
            }

            // OAuth 2.1 rotation: the OLD refresh token is now dead — vault the NEW pair atomically.
            await _vault.StoreTokensAsync(
                connectionId,
                new StoreTokensDto(
                    token.AccessToken,
                    token.RefreshToken,
                    AppToken: null,
                    AccessExpiresAt: _clock.GetUtcNow().UtcDateTime.AddSeconds(token.ExpiresIn)
                ),
                grantedScopes: null,
                ct
            );

            _logger.LogInformation(
                "Refreshed Kick token for connection {ConnectionId}",
                connectionId
            );
            return token.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Kick token refresh threw for connection {ConnectionId}",
                connectionId
            );
            return null;
        }
    }

    private sealed class KickTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
