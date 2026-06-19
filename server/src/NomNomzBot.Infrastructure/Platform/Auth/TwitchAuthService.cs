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
using Microsoft.Extensions.Options;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Domain.Platform.Entities;
using NomNomzBot.Infrastructure.Platform;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Manages Twitch OAuth tokens: exchange, refresh, and revoke.
/// Tokens are stored encrypted in the Service entity.
/// Service.Name conventions: "twitch" = broadcaster account, "twitch_bot" = shared bot account.
/// </summary>
public sealed class TwitchAuthService : ITwitchAuthService
{
    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly HttpClient _http;
    private readonly TwitchOptions _options;
    private readonly ILogger<TwitchAuthService> _logger;
    private readonly TimeProvider _timeProvider;

    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string RevokeEndpoint = "https://id.twitch.tv/oauth2/revoke";

    public TwitchAuthService(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        IHttpClientFactory httpClientFactory,
        IOptions<TwitchOptions> options,
        ILogger<TwitchAuthService> logger,
        TimeProvider timeProvider
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _http = httpClientFactory.CreateClient("twitch-auth");
        _options = options.Value;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Exchange an authorization code for access + refresh tokens.
    /// Does NOT persist to DB — caller is responsible for saving the returned result.
    /// </summary>
    public async Task<TokenResult?> ExchangeCodeAsync(
        string code,
        string redirectUri,
        CancellationToken ct = default
    )
    {
        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
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
    /// Refresh the token for a specific broadcaster / service combination.
    /// Persists updated tokens back to the Service entity.
    /// </summary>
    public async Task<TokenResult?> RefreshTokenAsync(
        string broadcasterId,
        string serviceName,
        CancellationToken ct = default
    )
    {
        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Name == serviceName,
            ct
        );

        if (service?.RefreshToken is null)
        {
            _logger.LogDebug(
                "No refresh token found for {BroadcasterId}/{Service}",
                broadcasterId,
                serviceName
            );
            return null;
        }

        string? refreshToken = await _tokenProtector.TryUnprotectAsync(
            service.RefreshToken,
            new TokenProtectionContext(broadcasterId, serviceName, "refresh"),
            ct
        );
        if (refreshToken is null)
        {
            _logger.LogWarning(
                "Could not decrypt refresh token for {BroadcasterId}/{Service}",
                broadcasterId,
                serviceName
            );
            return null;
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["refresh_token"] = refreshToken,
                ["grant_type"] = "refresh_token",
            }
        );

        HttpResponseMessage resp = await _http.PostAsync(TokenEndpoint, form, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Token refresh failed for {BroadcasterId}/{Service}: {Status}",
                broadcasterId,
                serviceName,
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

        service.AccessToken = await _tokenProtector.ProtectAsync(
            result.AccessToken,
            new TokenProtectionContext(broadcasterId, serviceName, "access"),
            ct
        );
        service.RefreshToken = await _tokenProtector.ProtectAsync(
            result.RefreshToken,
            new TokenProtectionContext(broadcasterId, serviceName, "refresh"),
            ct
        );
        service.TokenExpiry = result.ExpiresAt;
        service.Scopes = result.Scopes;
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Refreshed token for {BroadcasterId}/{Service}, expires {ExpiresAt:u}",
            broadcasterId,
            serviceName,
            result.ExpiresAt
        );

        return result;
    }

    /// <summary>
    /// Proactively refresh all tokens expiring within the next 30 minutes.
    /// Called by the background TokenRefreshService every 30 minutes.
    /// </summary>
    public async Task RefreshExpiringTokensAsync(CancellationToken ct = default)
    {
        DateTime threshold = _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(30);

        var expiring = await _db
            .Services.Where(s =>
                s.Enabled
                && s.RefreshToken != null
                && s.TokenExpiry != null
                && s.TokenExpiry < threshold
                && s.BroadcasterId != null
            )
            .Select(s => new { s.BroadcasterId, s.Name })
            .ToListAsync(ct);

        _logger.LogDebug("Refreshing {Count} expiring token(s)", expiring.Count);

        foreach (var entry in expiring)
        {
            try
            {
                await RefreshTokenAsync(entry.BroadcasterId!, entry.Name, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to refresh token for {BroadcasterId}/{Service}",
                    entry.BroadcasterId,
                    entry.Name
                );
            }
        }
    }

    /// <summary>
    /// Revoke the token for a broadcaster / service and clear the stored values.
    /// </summary>
    public async Task RevokeTokenAsync(
        string broadcasterId,
        string serviceName,
        CancellationToken ct = default
    )
    {
        Service? service = await _db.Services.FirstOrDefaultAsync(
            s => s.BroadcasterId == broadcasterId && s.Name == serviceName,
            ct
        );

        if (service is null)
            return;

        if (service.AccessToken is not null)
        {
            string? accessToken = await _tokenProtector.TryUnprotectAsync(
                service.AccessToken,
                new TokenProtectionContext(broadcasterId, serviceName, "access"),
                ct
            );
            if (accessToken is not null)
            {
                FormUrlEncodedContent form = new(
                    new Dictionary<string, string>
                    {
                        ["client_id"] = _options.ClientId,
                        ["token"] = accessToken,
                    }
                );

                try
                {
                    await _http.PostAsync(RevokeEndpoint, form, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Token revocation request failed for {BroadcasterId}/{Service}",
                        broadcasterId,
                        serviceName
                    );
                }
            }
        }

        service.AccessToken = null;
        service.RefreshToken = null;
        service.TokenExpiry = null;
        service.Scopes = [];
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Revoked and cleared token for {BroadcasterId}/{Service}",
            broadcasterId,
            serviceName
        );
    }

    // ─── Internal response model ────────────────────────────────────────────────

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
