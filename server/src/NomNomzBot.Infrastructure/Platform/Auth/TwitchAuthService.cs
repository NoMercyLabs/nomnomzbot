// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Integrations.Entities;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Low-level Twitch OAuth HTTP: exchange, refresh, and revoke. Token storage is owned by the
/// <see cref="IIntegrationTokenVault"/> (identity-auth §3.4) — this service reads/writes through it, never
/// the flat <c>Service</c> table. <see cref="ExchangeCodeAsync"/> is pure HTTP (the caller vaults the
/// result); refresh and revoke read the vaulted refresh token, call Twitch, and re-vault.
/// </summary>
public sealed class TwitchAuthService : ITwitchAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly HttpClient _http;
    private readonly ILogger<TwitchAuthService> _logger;
    private readonly TimeProvider _timeProvider;

    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string RevokeEndpoint = "https://id.twitch.tv/oauth2/revoke";
    private const string TwitchProvider = AuthEnums.IntegrationProvider.Twitch;

    public TwitchAuthService(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        ISystemCredentialsProvider credentials,
        IHttpClientFactory httpClientFactory,
        ILogger<TwitchAuthService> logger,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _vault = vault;
        _credentials = credentials;
        _http = httpClientFactory.CreateClient("twitch-auth");
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Exchange an authorization code for access + refresh tokens. Does NOT persist — the caller vaults the
    /// returned result via the token vault.
    /// </summary>
    public async Task<TokenResult?> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        SystemAppCredentials? app = await _credentials.GetAsync(TwitchProvider, ct);
        if (app is null)
        {
            _logger.LogWarning("Code exchange skipped: Twitch app credentials are not configured.");
            return null;
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
            }
        );

        HttpResponseMessage resp = await _http.PostAsync(TokenEndpoint, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("Code exchange failed: {Status}", resp.StatusCode);
            return null;
        }

        TwitchTokenResponse? json = await resp.Content.ReadFromJsonAsync<TwitchTokenResponse>(
            cancellationToken: ct
        );
        if (json is null)
            return null;

        return new(
            json.AccessToken,
            json.RefreshToken,
            _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn),
            json.Scope ?? []
        );
    }

    /// <summary>
    /// Refresh the vaulted token for a broadcaster / provider, then re-vault. On Twitch failure, records a
    /// vault refresh-failure (drives the needs-reauth path). Returns null when no connection / refresh token
    /// exists or the call fails.
    /// </summary>
    public async Task<TokenResult?> RefreshTokenAsync(
        Guid? broadcasterId,
        string provider,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection = await ResolveConnectionAsync(
            broadcasterId,
            provider,
            ct
        );
        if (connection is null)
            return null;

        Result<DecryptedTokenDto> refresh = await _vault.GetRefreshTokenAsync(connection.Id, ct);
        if (refresh.IsFailure)
        {
            _logger.LogWarning(
                "No usable refresh token for {BroadcasterId}/{Provider}",
                broadcasterId,
                provider
            );
            return null;
        }

        SystemAppCredentials? app = await _credentials.GetAsync(TwitchProvider, ct);
        if (app is null)
        {
            _logger.LogWarning(
                "Token refresh skipped for {BroadcasterId}/{Provider}: Twitch app credentials are not configured.",
                broadcasterId,
                provider
            );
            return null;
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
                ["refresh_token"] = refresh.Value.Value,
                ["grant_type"] = "refresh_token",
            }
        );

        HttpResponseMessage resp = await _http.PostAsync(TokenEndpoint, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            await _vault.MarkRefreshFailureAsync(
                connection.Id,
                $"twitch_refresh_{(int)resp.StatusCode}",
                ct
            );
            _logger.LogWarning(
                "Token refresh failed for {BroadcasterId}/{Provider}: {Status}",
                broadcasterId,
                provider,
                resp.StatusCode
            );
            return null;
        }

        TwitchTokenResponse? json = await resp.Content.ReadFromJsonAsync<TwitchTokenResponse>(
            cancellationToken: ct
        );
        if (json is null)
            return null;

        TokenResult result = new(
            json.AccessToken,
            json.RefreshToken,
            _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn),
            json.Scope ?? []
        );

        await _vault.StoreTokensAsync(
            connection.Id,
            new StoreTokensDto(
                result.AccessToken,
                result.RefreshToken,
                AppToken: null,
                result.ExpiresAt
            ),
            result.Scopes,
            ct
        );

        _logger.LogInformation(
            "Refreshed token for {BroadcasterId}/{Provider}, expires {ExpiresAt:u}",
            broadcasterId,
            provider,
            result.ExpiresAt
        );
        return result;
    }

    /// <summary>Proactively refresh tokens expiring within the next 30 minutes.</summary>
    public async Task RefreshExpiringTokensAsync(CancellationToken ct = default)
    {
        DateTime threshold = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(30);

        var expiring = await _db
            .IntegrationTokens.IgnoreQueryFilters()
            .Where(t =>
                t.DeletedAt == null
                && t.TokenType == AuthEnums.TokenType.Access
                && t.ExpiresAt != null
                && t.ExpiresAt < threshold
                && t.BroadcasterId != null
            )
            .Select(t => new { t.BroadcasterId, t.Connection.Provider })
            .ToListAsync(ct);

        _logger.LogDebug("Refreshing {Count} expiring token(s)", expiring.Count);

        foreach (var entry in expiring)
        {
            try
            {
                await RefreshTokenAsync(entry.BroadcasterId, entry.Provider, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "Failed to refresh token for {BroadcasterId}/{Provider}",
                    entry.BroadcasterId,
                    entry.Provider
                );
            }
        }
    }

    /// <summary>Revoke the access token at Twitch and revoke the vaulted connection.</summary>
    public async Task RevokeTokenAsync(
        Guid? broadcasterId,
        string provider,
        CancellationToken ct = default
    )
    {
        IntegrationConnection? connection = await ResolveConnectionAsync(
            broadcasterId,
            provider,
            ct
        );
        if (connection is null)
            return;

        Result<DecryptedTokenDto> access = await _vault.GetAccessTokenAsync(connection.Id, ct);
        SystemAppCredentials? app = await _credentials.GetAsync(TwitchProvider, ct);
        if (access.IsSuccess && app is not null)
        {
            FormUrlEncodedContent form = new(
                new Dictionary<string, string>
                {
                    ["client_id"] = app.ClientId,
                    ["token"] = access.Value.Value,
                }
            );
            try
            {
                await _http.PostAsync(RevokeEndpoint, form, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Token revocation request failed for {BroadcasterId}/{Provider}",
                    broadcasterId,
                    provider
                );
            }
        }

        await _vault.RevokeConnectionAsync(connection.Id, "token_revoked", ct);
        _logger.LogInformation(
            "Revoked token for {BroadcasterId}/{Provider}",
            broadcasterId,
            provider
        );
    }

    private async Task<IntegrationConnection?> ResolveConnectionAsync(
        Guid? broadcasterId,
        string provider,
        CancellationToken ct
    ) =>
        await _db
            .IntegrationConnections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c =>
                    c.BroadcasterId == broadcasterId
                    && c.Provider == provider
                    && c.DeletedAt == null,
                ct
            );

    private sealed class TwitchTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = null!;

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = null!;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string[]? Scope { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
