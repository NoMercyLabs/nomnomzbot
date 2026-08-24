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
///
/// <para>
/// <b>S036b — deliberate second custody path (not migrated to <c>IIntegrationTokenVault</c>).</b>
/// Twitch/Kick/Spotify vault their OAuth tokens as <c>IntegrationConnection</c> rows; YouTube alone
/// still lives on the legacy flat <c>Service</c> table (predates the vault). Folding it into
/// <c>IIntegrationTokenVault</c> is the right end state, but it is a DATA migration, not a code
/// change: every reader/writer of the YouTube <c>Service</c> row (this provider,
/// <c>YouTubeMusicProvider</c>, the OAuth callback that creates the row, the integrations-status
/// surface, and any admin/support tooling that queries <c>Services</c> directly) would need to move
/// in lockstep with a one-time backfill of existing rows into <c>IntegrationConnection</c> — a
/// decision beyond this slice's scope. Until that migration lands, this provider closes the SAME
/// concurrent-refresh race Twitch/Kick/Spotify close (see the <see cref="_refreshGate"/> use below),
/// just keyed to the <c>Service</c> row's identity instead of a vault connection id.
/// </para>
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
    private readonly NomNomzBot.Infrastructure.Identity.IConnectionRefreshGate _refreshGate;

    public YouTubeAccessTokenProvider(
        IApplicationDbContext db,
        ITokenProtector tokenProtector,
        TimeProvider timeProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<YouTubeAccessTokenProvider> logger,
        NomNomzBot.Infrastructure.Identity.IConnectionRefreshGate refreshGate
    )
    {
        _db = db;
        _tokenProtector = tokenProtector;
        _timeProvider = timeProvider;
        _http = httpClientFactory.CreateClient(ProviderName);
        _logger = logger;
        _refreshGate = refreshGate;
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
                new(service.BroadcasterId?.ToString() ?? "_platform", ProviderName, "access"),
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

        // S036 — serialize refreshes of the SAME YouTube service row. Google does not invalidate the prior
        // refresh token on a refresh grant, but two concurrent callers still each burn a quota-limited
        // request and could interleave the SaveChangesAsync writes; gate them to exactly one HTTP call.
        using IDisposable gate = await _refreshGate.AcquireAsync(
            $"youtube:{service.Id}",
            cancellationToken
        );

        // Re-check under the gate against the CURRENT row: another caller may already have refreshed it
        // while we waited. AsNoTracking + a detached read on purpose — it must NOT replace the tracked
        // `service` instance below, which the HTTP-refresh path mutates and saves in place.
        Service? current = await _db
            .Services.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == service.Id, cancellationToken);
        if (
            current is not null
            && current.TokenExpiry.HasValue
            && current.TokenExpiry.Value > _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(5)
            && current.AccessToken is not null
        )
        {
            return await _tokenProtector.TryUnprotectAsync(
                current.AccessToken,
                new(current.BroadcasterId?.ToString() ?? "_platform", ProviderName, "access"),
                cancellationToken
            );
        }

        string subjectId = service.BroadcasterId?.ToString() ?? "_platform";

        string? refreshToken = await _tokenProtector.TryUnprotectAsync(
            service.RefreshToken,
            new(subjectId, ProviderName, "refresh"),
            cancellationToken
        );
        if (refreshToken is null)
            return null;

        string? clientId = service.ClientId is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.ClientId,
                new(subjectId, ProviderName, "client_id"),
                cancellationToken
            )
            : null;
        string? clientSecret = service.ClientSecret is not null
            ? await _tokenProtector.TryUnprotectAsync(
                service.ClientSecret,
                new(subjectId, ProviderName, "client_secret"),
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
                new(subjectId, ProviderName, "access"),
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
