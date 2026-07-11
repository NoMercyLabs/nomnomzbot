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
using NomNomzBot.Application.Common.Interfaces.Crypto;
using NomNomzBot.Application.Contracts.YouTube;
using NomNomzBot.Domain.Platform.Entities;

namespace NomNomzBot.Infrastructure.Integrations.YouTube;

/// <summary>
/// <see cref="IYouTubeAccessTokenProvider"/> over the vaulted <c>Service</c> row (Name = "youtube") —
/// extracted from <c>YouTubeMusicProvider</c>'s private token logic so the music manage surface and the
/// live-chat poller share ONE custody path. Refreshes against Google's token endpoint with the stored
/// per-channel client credentials when the token expires within 5 minutes; Google does not rotate the
/// refresh token on a refresh grant, so only the access token + expiry are re-protected and saved.
/// </summary>
public sealed class YouTubeAccessTokenProvider : IYouTubeAccessTokenProvider
{
    private const string ProviderName = "youtube";
    private const string GoogleTokenEndpoint = "https://oauth2.googleapis.com/token";

    private readonly IApplicationDbContext _db;
    private readonly ITokenProtector _tokenProtector;
    private readonly TimeProvider _timeProvider;
    private readonly HttpClient _http;
    private readonly ILogger<YouTubeAccessTokenProvider> _logger;

    public YouTubeAccessTokenProvider(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<YouTubeAccessTokenProvider> logger
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _timeProvider = timeProvider;
        _http = httpClientFactory.CreateClient(ProviderName);
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Service? service = await _db.Services.FirstOrDefaultAsync(
            s =>
                s.BroadcasterId == broadcasterId
                && s.Name == ProviderName
                && s.Enabled
                && s.AccessToken != null,
            cancellationToken
        );

        if (service is null)
        {
            _logger.LogDebug(
                "No YouTube service found for broadcaster {BroadcasterId}",
                broadcasterId
            );
            return null;
        }

        // Refresh if expiring within 5 minutes.
        if (
            service.TokenExpiry.HasValue
            && service.TokenExpiry.Value <= _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5)
        )
            return await RefreshTokenAsync(service, cancellationToken);

        return service.AccessToken is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.AccessToken,
                new TokenProtectionContext(
                    service.BroadcasterId?.ToString() ?? "_platform",
                    ProviderName,
                    "access"
                ),
                cancellationToken
            )
            : null;
    }

    private async Task<string?> RefreshTokenAsync(
        Service service,
        CancellationToken cancellationToken
    )
    {
        if (service.RefreshToken is null)
            return null;

        string subjectId = service.BroadcasterId?.ToString() ?? "_platform";

        string? refreshToken = await _tokenProtector.TryUnprotectAsync(
            service.RefreshToken,
            new TokenProtectionContext(subjectId, ProviderName, "refresh"),
            cancellationToken
        );
        if (refreshToken is null)
            return null;

        string? clientId = service.ClientId is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.ClientId,
                new TokenProtectionContext(subjectId, ProviderName, "client_id"),
                cancellationToken
            )
            : null;
        string? clientSecret = service.ClientSecret is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.ClientSecret,
                new TokenProtectionContext(subjectId, ProviderName, "client_secret"),
                cancellationToken
            )
            : null;

        if (clientId is null || clientSecret is null)
        {
            _logger.LogWarning(
                "YouTube credentials not configured for broadcaster {BroadcasterId}",
                service.BroadcasterId
            );
            return null;
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
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
                _logger.LogWarning(
                    "YouTube token refresh failed for {BroadcasterId}: {Status}",
                    service.BroadcasterId,
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

            service.AccessToken = await _tokenProtector.ProtectAsync(
                json.AccessToken,
                new TokenProtectionContext(subjectId, ProviderName, "access"),
                cancellationToken
            );
            service.TokenExpiry = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn);
            // Google does not rotate refresh tokens on a refresh grant — the stored one stays valid.
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Refreshed YouTube token for {BroadcasterId}",
                service.BroadcasterId
            );
            return json.AccessToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Exception refreshing YouTube token for {BroadcasterId}",
                service.BroadcasterId
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
