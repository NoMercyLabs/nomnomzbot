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
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Integrations.YouTube;

/// <summary>
/// <see cref="IYouTubeAccessTokenProvider"/> over the vaulted YouTube connection (S036c-b): the
/// non-revoked <c>IntegrationConnection</c> for <c>(BroadcasterId, Provider=youtube)</c> is the sole
/// custody path — no reader anywhere reaches into the legacy <c>Service</c> row any more (S036c-a's
/// seeder backfilled every pre-existing row into the vault). An expiring token refreshes against
/// Google's token endpoint using the channel's resolved (BYOC-or-system) app credentials; Google does
/// NOT rotate the refresh token on a refresh grant, so only the access token + expiry are re-vaulted —
/// the stored refresh token is left untouched (<c>StoreTokensDto.RefreshToken: null</c> is a no-op write
/// for that field, per <see cref="IIntegrationTokenVault.StoreTokensAsync"/>).
/// </summary>
public sealed class YouTubeAccessTokenProvider : IYouTubeAccessTokenProvider
{
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(5);

    private readonly IApplicationDbContext _db;
    private readonly IIntegrationTokenVault _vault;
    private readonly IChannelCredentialsResolver _channelCredentials;
    private readonly TimeProvider _timeProvider;
    private readonly HttpClient _http;
    private readonly ILogger<YouTubeAccessTokenProvider> _logger;
    private readonly NomNomzBot.Infrastructure.Identity.IConnectionRefreshGate _refreshGate;

    public YouTubeAccessTokenProvider(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        IChannelCredentialsResolver channelCredentials,
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<YouTubeAccessTokenProvider> logger,
        NomNomzBot.Infrastructure.Identity.IConnectionRefreshGate refreshGate
    )
    {
        _db = db;
        _vault = vault;
        _channelCredentials = channelCredentials;
        _timeProvider = timeProvider;
        _http = httpClientFactory.CreateClient(AuthEnums.IntegrationProvider.YouTube);
        _logger = logger;
        _refreshGate = refreshGate;
    }

    public async Task<string?> GetAccessTokenAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Guid? connectionId = await ResolveConnectionIdAsync(broadcasterId, cancellationToken);
        if (connectionId is null)
        {
            _logger.LogDebug(
                "No YouTube connection vaulted for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }

        Result<DecryptedTokenDto> access = await _vault.GetAccessTokenAsync(
            connectionId.Value,
            cancellationToken
        );
        if (access.IsFailure)
            return null;

        bool expiring =
            access.Value.IsExpired
            || (
                access.Value.ExpiresAt is { } expiresAt
                && expiresAt <= _timeProvider.GetUtcNow().UtcDateTime.Add(RefreshMargin)
            );
        if (!expiring)
            return access.Value.Value;

        return await RefreshTokenAsync(broadcasterId, connectionId.Value, cancellationToken);
    }

    private async Task<Guid?> ResolveConnectionIdAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken
    )
    {
        var connection = await _db
            .IntegrationConnections.Where(c =>
                c.BroadcasterId == broadcasterId
                && c.Provider == AuthEnums.IntegrationProvider.YouTube
                && c.Status != AuthEnums.IntegrationStatus.Revoked
            )
            .OrderByDescending(c => c.ConnectedAt)
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(cancellationToken);
        return connection?.Id;
    }

    private async Task<string?> RefreshTokenAsync(
        Guid broadcasterId,
        Guid connectionId,
        CancellationToken cancellationToken
    )
    {
        // S036 — serialize refreshes of the SAME YouTube connection. Google does not invalidate the prior
        // refresh token on a refresh grant, but two concurrent callers still each burn a quota-limited
        // request and could interleave the vault writes; gate them to exactly one HTTP call.
        using IDisposable gate = await _refreshGate.AcquireAsync(
            $"youtube:{connectionId}",
            cancellationToken
        );

        // Re-check under the gate: another caller may already have refreshed this connection while we waited.
        Result<DecryptedTokenDto> current = await _vault.GetAccessTokenAsync(
            connectionId,
            cancellationToken
        );
        if (
            current is { IsSuccess: true, Value: { IsExpired: false, ExpiresAt: { } expiresAt } }
            && expiresAt > _timeProvider.GetUtcNow().UtcDateTime.Add(RefreshMargin)
        )
            return current.Value.Value;

        Result<DecryptedTokenDto> refresh = await _vault.GetRefreshTokenAsync(
            connectionId,
            cancellationToken
        );
        if (refresh.IsFailure)
            return null;

        Result<SystemAppCredentials> appResult = await _channelCredentials.ResolveAsync(
            broadcasterId,
            AuthEnums.IntegrationProvider.YouTube,
            cancellationToken
        );
        if (appResult.IsFailure)
        {
            _logger.LogWarning(
                "YouTube credentials not configured for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }
        SystemAppCredentials app = appResult.Value;

        FormUrlEncodedContent form = new(
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
            HttpResponseMessage response = await _http.PostAsync(
                GoogleTokenEndpoint,
                form,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                await _vault.MarkRefreshFailureAsync(
                    connectionId,
                    $"YouTube refresh failed ({(int)response.StatusCode})",
                    cancellationToken
                );
                _logger.LogWarning(
                    "YouTube token refresh failed for {BroadcasterId}: {Status}",
                    broadcasterId,
                    response.StatusCode
                );
                return null;
            }

            GoogleTokenResponse? json =
                await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(
                    cancellationToken: cancellationToken
                );
            if (json is null)
                return null;

            // Google does not rotate refresh tokens on a refresh grant — RefreshToken: null leaves the
            // vaulted one untouched; only the access token + expiry are re-sealed.
            await _vault.StoreTokensAsync(
                connectionId,
                new(
                    json.AccessToken,
                    RefreshToken: null,
                    AppToken: null,
                    _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn)
                ),
                grantedScopes: null,
                cancellationToken
            );

            _logger.LogInformation("Refreshed YouTube token for {BroadcasterId}", broadcasterId);
            return json.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Exception refreshing YouTube token for {BroadcasterId}",
                broadcasterId
            );
            return null;
        }
    }

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = null!;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
