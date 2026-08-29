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
/// <see cref="IKickAccessTokenProvider"/> over the vaulted Kick connection. Prefers a dedicated,
/// tenant-scoped bot account (<c>Provider=kick_bot</c>, <c>BroadcasterId=broadcasterId</c>) when one is
/// registered — <see cref="KickAccess.IsBotAccount"/> is then <c>true</c> and the caller sends
/// <c>type:"bot"</c> under the bot's own token. Otherwise falls back to the streamer's own account: the
/// tenant channel's <c>ExternalChannelId</c> IS the streamer's numeric Kick account id (the platform
/// channel is provisioned from the same identity the login vaulted), so that connection is found by
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
    private readonly Identity.IConnectionRefreshGate _refreshGate;

    public KickAccessTokenProvider(
        IApplicationDbContext db,
        IIntegrationTokenVault vault,
        ISystemCredentialsProvider credentials,
        TimeProvider clock,
        IHttpClientFactory httpClientFactory,
        ILogger<KickAccessTokenProvider> logger,
        Identity.IConnectionRefreshGate refreshGate
    )
    {
        _db = db;
        _vault = vault;
        _credentials = credentials;
        _clock = clock;
        _http = httpClientFactory.CreateClient("kick");
        _logger = logger;
        _refreshGate = refreshGate;
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

        // Resolution order for the Kick chat identity (mirrors TwitchTokenResolver.GetBotTokenAsync):
        //   1. A registered dedicated bot account — the tenant-scoped `kick_bot` connection.
        //   2. Self-host fallback: until a bot account is registered, the bot speaks as the streamer's
        //      OWN account. Two possible custody rows for that same Kick account, both keyed by the
        //      numeric account id: the streamer-plane integration connect (tenant-scoped, carries the
        //      chat/moderation/events scopes) and the identity-plane login connection (BroadcasterId
        //      null, user:read only) — prefer the scoped one, it is the grant the chat surface needs.
        var botConnectionRow = await _db
            .IntegrationConnections.Where(c =>
                c.Provider == AuthEnums.IntegrationProvider.KickBot
                && c.BroadcasterId == broadcasterId
                && c.Status != "revoked"
            )
            .Select(c => new { c.Id })
            .FirstOrDefaultAsync(cancellationToken);

        bool isBotAccount = botConnectionRow is not null;
        Guid connectionId;
        if (botConnectionRow is not null)
        {
            connectionId = botConnectionRow.Id;
        }
        else
        {
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
            connectionId = connectionRow.Id;
        }

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
            return new(access.Value.Value, kickUserId, isBotAccount);

        if (!await ShouldAttemptRefreshAsync(connectionId, cancellationToken))
        {
            _logger.LogDebug(
                "Skipping Kick refresh for connection {ConnectionId}: needs_reauth or backing off",
                connectionId
            );
            return null;
        }

        string? refreshed = await RefreshAsync(connectionId, cancellationToken);
        return refreshed is null ? null : new KickAccess(refreshed, kickUserId, isBotAccount);
    }

    /// <summary>
    /// Retry-storm guard for the routine "is my token still good" refresh check: a connection already
    /// flagged <c>needs_reauth</c> cannot be fixed by retrying — only a fresh OAuth grant (a deliberate
    /// re-connect through <see cref="IIntegrationTokenVault.StoreTokensAsync"/>) clears it — so hammering
    /// Kick's token endpoint on every routine call just re-confirms the same dead refresh token forever (the
    /// same shape that ran the Spotify path's ConsecutiveFailureCount to 4653, fixed in 07f60a1c). Skip the
    /// refresh entirely once needs_reauth, and back off exponentially between attempts before that
    /// threshold so a flaky-but-alive connection isn't hammered at full call cadence either. A deliberate
    /// re-auth does not go through this routine path at all — it calls <c>StoreTokensAsync</c> directly.
    /// </summary>
    private static readonly TimeSpan[] RefreshBackoffSchedule =
    [
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(10),
    ];

    private async Task<bool> ShouldAttemptRefreshAsync(Guid connectionId, CancellationToken ct)
    {
        IntegrationConnectionStatusSnapshot? connection = await _db
            .IntegrationConnections.AsNoTracking()
            .Where(c => c.Id == connectionId)
            .Select(c => new IntegrationConnectionStatusSnapshot(
                c.Status,
                c.ConsecutiveFailureCount,
                c.LastErrorAt
            ))
            .FirstOrDefaultAsync(ct);
        if (connection is null)
            return false;

        // Dead beyond retry — needs a human to re-auth; retrying cannot fix it and only burns the rate limit.
        if (connection.Status == AuthEnums.IntegrationStatus.NeedsReauth)
            return false;

        if (connection.ConsecutiveFailureCount <= 0)
            return true;

        TimeSpan backoff = RefreshBackoffSchedule[
            Math.Min(connection.ConsecutiveFailureCount - 1, RefreshBackoffSchedule.Length - 1)
        ];
        DateTime now = _clock.GetUtcNow().UtcDateTime;
        return connection.LastErrorAt is null || now >= connection.LastErrorAt.Value + backoff;
    }

    private sealed record IntegrationConnectionStatusSnapshot(
        string Status,
        int ConsecutiveFailureCount,
        DateTime? LastErrorAt
    );

    private async Task<string?> RefreshAsync(Guid connectionId, CancellationToken ct)
    {
        // S036 — serialize refreshes of the SAME connection. Kick is OAuth 2.1 and rotates the refresh token
        // on every grant, so a second concurrent caller posting the token the first caller already spent
        // would fail (or worse, a race could vault the loser's stale pair over the winner's). A different
        // connection refreshing concurrently uses a different key and is unaffected.
        using IDisposable gate = await _refreshGate.AcquireAsync($"kick:{connectionId}", ct);

        // Re-check under the gate: another caller may have already refreshed this connection while we waited.
        Result<DecryptedTokenDto> current = await _vault.GetAccessTokenAsync(connectionId, ct);
        if (
            current is { IsSuccess: true, Value: { IsExpired: false, ExpiresAt: { } expiresAt } }
            && expiresAt > _clock.GetUtcNow().UtcDateTime.Add(RefreshMargin)
        )
            return current.Value.Value;

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
                new(
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
