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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Platform.Auth;

/// <summary>
/// Mints and caches the Twitch app access token (<c>client_credentials</c>) — the badge-bearing token behind the
/// chat send (<see cref="ITwitchAppTokenProvider"/>). A singleton: the token is process-wide and long-lived, so
/// it is minted once under a lock and shared by every send, then re-minted on expiry or after a 401 invalidation.
/// The client id + secret resolve DB-first through <see cref="ISystemCredentialsProvider"/> (created per mint in
/// its own scope, so this singleton never captures a scoped <c>DbContext</c>); a credential-less deployment fails
/// cleanly and the caller falls back to the user-token send.
/// </summary>
public sealed class TwitchAppTokenProvider : ITwitchAppTokenProvider
{
    private const string TokenEndpoint = "https://id.twitch.tv/oauth2/token";
    private const string TwitchProvider = AuthEnums.IntegrationProvider.Twitch;

    // Re-mint this far ahead of the stated expiry so an in-flight send never rides a token about to lapse.
    private static readonly TimeSpan ExpirySkew = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<TwitchAppTokenProvider> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    // A single reference holds both fields, so the fast (lock-free) read is a torn-free atomic reference read.
    private volatile CachedToken? _cache;

    public TwitchAppTokenProvider(
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        TimeProvider timeProvider,
        ILogger<TwitchAppTokenProvider> logger
    )
    {
        _scopeFactory = scopeFactory;
        _http = httpClientFactory.CreateClient("twitch-auth");
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<Result<string>> GetAppTokenAsync(CancellationToken ct = default)
    {
        // Fast path — a still-valid cached token needs no lock.
        CachedToken? snapshot = _cache;
        if (snapshot is not null && _timeProvider.GetUtcNow() < snapshot.ExpiresAt)
            return Result.Success(snapshot.Token);

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check under the lock: a racing caller may have just minted one.
            snapshot = _cache;
            if (snapshot is not null && _timeProvider.GetUtcNow() < snapshot.ExpiresAt)
                return Result.Success(snapshot.Token);

            return await MintAsync(ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate() => _cache = null;

    private async Task<Result<string>> MintAsync(CancellationToken ct)
    {
        SystemAppCredentials? app;
        await using (AsyncServiceScope scope = _scopeFactory.CreateAsyncScope())
        {
            ISystemCredentialsProvider credentials =
                scope.ServiceProvider.GetRequiredService<ISystemCredentialsProvider>();
            app = await credentials.GetAsync(TwitchProvider, ct);
        }

        if (app is null)
        {
            _logger.LogWarning(
                "App token mint skipped: Twitch app credentials (client id + secret) are not configured."
            );
            return Result.Failure<string>(
                "Twitch app credentials are not configured.",
                TwitchErrorCodes.NoToken
            );
        }

        FormUrlEncodedContent form = new(
            new Dictionary<string, string>
            {
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
                ["grant_type"] = "client_credentials",
            }
        );

        HttpResponseMessage resp;
        try
        {
            resp = await _http.PostAsync(TokenEndpoint, form, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "App token mint request failed.");
            return Result.Failure<string>(
                "Twitch token request failed.",
                TwitchErrorCodes.Transport
            );
        }

        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("App token mint failed: {Status}", resp.StatusCode);
            return Result.Failure<string>(
                $"Twitch rejected the app-token request ({(int)resp.StatusCode}).",
                TwitchErrorCodes.Unauthorized
            );
        }

        AppTokenResponse? json = await resp.Content.ReadFromJsonAsync<AppTokenResponse>(
            cancellationToken: ct
        );
        if (json is null || string.IsNullOrEmpty(json.AccessToken))
            return Result.Failure<string>(
                "Malformed Twitch token response.",
                TwitchErrorCodes.Transport
            );

        DateTimeOffset expiresAt =
            _timeProvider.GetUtcNow().AddSeconds(json.ExpiresIn) - ExpirySkew;
        _cache = new CachedToken(json.AccessToken, expiresAt);
        _logger.LogInformation(
            "Minted Twitch app access token; valid ~{Hours}h.",
            Math.Round(json.ExpiresIn / 3600.0, 1)
        );
        return Result.Success(json.AccessToken);
    }

    private sealed record CachedToken(string Token, DateTimeOffset ExpiresAt);

    private sealed class AppTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = null!;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; set; }
    }
}
