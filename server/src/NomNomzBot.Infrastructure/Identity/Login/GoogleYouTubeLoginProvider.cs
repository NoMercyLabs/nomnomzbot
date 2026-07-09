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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity.Login;

/// <summary>
/// The Google/YouTube device-flow login provider (platform-identity §3.2): begins Google's OAuth 2.0 device
/// authorization grant, polls it, and on approval proves the OpenID identity (sub/name/picture) after vaulting
/// the issued tokens through <see cref="IIntegrationTokenVault"/>. Mirrors the descriptor entry
/// (<c>LoginProviderRegistry</c>, feature-flag <c>use_youtube_login</c>) and funnels a successful poll through
/// the generic <see cref="IExternalLoginService"/> like every non-Twitch provider.
/// </summary>
public sealed class GoogleYouTubeLoginProvider : ILoginIdentityProvider
{
    private const string DeviceCodeEndpoint = "https://oauth2.googleapis.com/device/code";
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";
    private const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

    private static readonly string[] LoginScopes = ["openid", "email", "profile"];

    private readonly HttpClient _http;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IIntegrationTokenVault _vault;
    private readonly TimeProvider _clock;

    public GoogleYouTubeLoginProvider(
        IHttpClientFactory httpClientFactory,
        ISystemCredentialsProvider credentials,
        IIntegrationTokenVault vault,
        TimeProvider clock
    )
    {
        _http = httpClientFactory.CreateClient("integration-oauth");
        _credentials = credentials;
        _vault = vault;
        _clock = clock;
    }

    public string Key => AuthEnums.LoginProvider.YouTube;

    public async Task<Result<DeviceCodeStartDto>> StartDeviceAsync(
        CancellationToken cancellationToken = default
    )
    {
        string? clientId = await _credentials.GetClientIdAsync(
            AuthEnums.LoginProvider.YouTube,
            cancellationToken
        );
        if (clientId is null)
            return Result.Failure<DeviceCodeStartDto>(
                "YouTube login is not configured.",
                "SERVICE_UNAVAILABLE"
            );

        using FormUrlEncodedContent content = new(
            new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["scope"] = string.Join(' ', LoginScopes),
            }
        );
        HttpResponseMessage response = await _http.PostAsync(
            DeviceCodeEndpoint,
            content,
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
            return Result.Failure<DeviceCodeStartDto>(
                "YouTube device authorization could not be started.",
                "SERVICE_UNAVAILABLE"
            );

        DeviceCodeResponse? body = await response.Content.ReadFromJsonAsync<DeviceCodeResponse>(
            cancellationToken
        );
        if (body is null || string.IsNullOrEmpty(body.DeviceCode))
            return Result.Failure<DeviceCodeStartDto>(
                "YouTube returned an unexpected device authorization response.",
                "SERVICE_UNAVAILABLE"
            );

        return Result.Success(
            new DeviceCodeStartDto(
                body.DeviceCode,
                body.UserCode,
                body.VerificationUrl,
                body.Interval,
                body.ExpiresIn
            )
        );
    }

    public async Task<Result<ExternalIdentityProof>> PollDeviceAsync(
        string deviceCode,
        CancellationToken cancellationToken = default
    )
    {
        SystemAppCredentials? app = await _credentials.GetAsync(
            AuthEnums.LoginProvider.YouTube,
            cancellationToken
        );
        if (app is null)
            return Result.Failure<ExternalIdentityProof>(
                "YouTube login is not configured.",
                "SERVICE_UNAVAILABLE"
            );

        using FormUrlEncodedContent content = new(
            new Dictionary<string, string>
            {
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
                ["device_code"] = deviceCode,
                ["grant_type"] = DeviceCodeGrantType,
            }
        );
        HttpResponseMessage response = await _http.PostAsync(
            TokenEndpoint,
            content,
            cancellationToken
        );

        // Google carries the continuation/terminal signal in the JSON body (with a 4xx status), so the body is
        // read regardless of status code: either an "error" to map, or the issued "access_token".
        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken
        );
        if (token is null)
            return Result.Failure<ExternalIdentityProof>(
                "YouTube returned an unexpected token response.",
                DeviceLoginStatus.Error
            );

        if (!string.IsNullOrEmpty(token.Error))
            return MapError(token.Error);

        if (string.IsNullOrEmpty(token.AccessToken))
            return Result.Failure<ExternalIdentityProof>(
                "YouTube returned neither an error nor an access token.",
                DeviceLoginStatus.Error
            );

        UserInfoResponse? identity = await FetchUserInfoAsync(token.AccessToken, cancellationToken);
        if (identity is null || string.IsNullOrEmpty(identity.Sub))
            return Result.Failure<ExternalIdentityProof>(
                "Failed to read the YouTube account identity.",
                DeviceLoginStatus.Error
            );

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                BroadcasterId: null,
                Provider: AuthEnums.LoginProvider.YouTube,
                ProviderAccountId: identity.Sub,
                ProviderAccountName: identity.Name,
                Scopes: LoginScopes,
                ClientId: app.ClientId,
                IsByok: false,
                ConnectedByUserId: null,
                SettingsJson: null
            ),
            cancellationToken
        );
        if (connection.IsFailure)
            return connection.WithValue<ExternalIdentityProof>(null!);

        Result store = await _vault.StoreTokensAsync(
            connection.Value.Id,
            new StoreTokensDto(
                token.AccessToken,
                token.RefreshToken,
                AppToken: null,
                AccessExpiresAt: _clock.GetUtcNow().UtcDateTime.AddSeconds(token.ExpiresIn)
            ),
            LoginScopes,
            cancellationToken
        );
        if (store.IsFailure)
            return store.WithValue<ExternalIdentityProof>(null!);

        return Result.Success(
            new ExternalIdentityProof(
                AuthEnums.LoginProvider.YouTube,
                identity.Sub,
                identity.Name ?? identity.Sub,
                identity.Name,
                identity.Picture,
                connection.Value.Id
            )
        );
    }

    private async Task<UserInfoResponse?> FetchUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<UserInfoResponse>(cancellationToken);
    }

    private static Result<ExternalIdentityProof> MapError(string error) =>
        error switch
        {
            "authorization_pending" => Result.Failure<ExternalIdentityProof>(
                "Authorization is still pending.",
                DeviceLoginStatus.Pending
            ),
            "slow_down" => Result.Failure<ExternalIdentityProof>(
                "Polling too fast — slow down.",
                DeviceLoginStatus.SlowDown
            ),
            "expired_token" or "expired" => Result.Failure<ExternalIdentityProof>(
                "The device code has expired.",
                DeviceLoginStatus.Expired
            ),
            "access_denied" => Result.Failure<ExternalIdentityProof>(
                "Authorization was denied.",
                DeviceLoginStatus.Denied
            ),
            _ => Result.Failure<ExternalIdentityProof>(
                $"YouTube login failed: {error}.",
                DeviceLoginStatus.Error
            ),
        };

    private sealed class DeviceCodeResponse
    {
        [JsonPropertyName("device_code")]
        public string DeviceCode { get; set; } = string.Empty;

        [JsonPropertyName("user_code")]
        public string UserCode { get; set; } = string.Empty;

        [JsonPropertyName("verification_url")]
        public string VerificationUrl { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("interval")]
        public int Interval { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("error")]
        public string? Error { get; set; }

        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class UserInfoResponse
    {
        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("picture")]
        public string? Picture { get; set; }
    }
}
