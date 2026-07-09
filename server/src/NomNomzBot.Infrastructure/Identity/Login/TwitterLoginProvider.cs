// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity.Login;

/// <summary>
/// The Twitter/X auth-code + PKCE login provider (platform-identity §10.3): builds X's authorize URL with an
/// S256 challenge, exchanges the returned code for tokens (a confidential client authenticates the token
/// request with HTTP Basic auth per X's OAuth 2.0 rules), then proves the X identity from <c>GET /2/users/me</c>
/// after vaulting the issued tokens through <see cref="IIntegrationTokenVault"/>. Twitter is a login-only
/// provider — an X identity never owns a <c>Channel</c> — dispatched by the generic <c>auth/twitter/authorize</c>
/// + <c>auth/twitter/callback</c> routes.
/// </summary>
public sealed class TwitterLoginProvider : IAuthCodeLoginProvider
{
    private const string AuthorizeEndpoint = "https://twitter.com/i/oauth2/authorize";
    private const string TokenEndpoint = "https://api.x.com/2/oauth2/token";
    private const string UserInfoEndpoint = "https://api.x.com/2/users/me";

    private static readonly string[] LoginScopes = ["users.read", "tweet.read", "offline.access"];

    private readonly HttpClient _http;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IIntegrationTokenVault _vault;
    private readonly TimeProvider _clock;

    public TwitterLoginProvider(
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

    public string Key => AuthEnums.LoginProvider.Twitter;

    public async Task<Result<Uri>> BuildAuthorizeUrlAsync(
        string state,
        string redirectUri,
        string codeChallenge,
        CancellationToken cancellationToken = default
    )
    {
        string? clientId = await _credentials.GetClientIdAsync(
            AuthEnums.LoginProvider.Twitter,
            cancellationToken
        );
        if (clientId is null)
            return Result.Failure<Uri>("Twitter login is not configured.", "SERVICE_UNAVAILABLE");

        Dictionary<string, string> parameters = new()
        {
            ["response_type"] = "code",
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["scope"] = string.Join(' ', LoginScopes),
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        string query = string.Join(
            '&',
            parameters.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"
            )
        );
        return Result.Success(new Uri($"{AuthorizeEndpoint}?{query}"));
    }

    public async Task<Result<ExternalIdentityProof>> ExchangeCodeAsync(
        string code,
        string redirectUri,
        string codeVerifier,
        CancellationToken cancellationToken = default
    )
    {
        SystemAppCredentials? app = await _credentials.GetAsync(
            AuthEnums.LoginProvider.Twitter,
            cancellationToken
        );
        if (app is null)
            return Result.Failure<ExternalIdentityProof>(
                "Twitter login is not configured.",
                "SERVICE_UNAVAILABLE"
            );

        using HttpRequestMessage tokenRequest = new(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = code,
                    ["redirect_uri"] = redirectUri,
                    ["code_verifier"] = codeVerifier,
                    ["client_id"] = app.ClientId,
                }
            ),
        };
        // A confidential client authenticates the token request with HTTP Basic auth (X OAuth 2.0 §token).
        string basic = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{app.ClientId}:{app.ClientSecret}")
        );
        tokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        HttpResponseMessage response = await _http.SendAsync(tokenRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return Result.Failure<ExternalIdentityProof>(
                "Twitter token exchange failed.",
                "SERVICE_UNAVAILABLE"
            );

        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken
        );
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            return Result.Failure<ExternalIdentityProof>(
                "Twitter returned an unexpected token response.",
                "SERVICE_UNAVAILABLE"
            );

        TwitterUser? identity = await FetchUserAsync(token.AccessToken, cancellationToken);
        if (identity is null || string.IsNullOrEmpty(identity.Id))
            return Result.Failure<ExternalIdentityProof>(
                "Failed to read the Twitter account identity.",
                "SERVICE_UNAVAILABLE"
            );

        string username = identity.Username ?? identity.Id;

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                BroadcasterId: null,
                Provider: AuthEnums.LoginProvider.Twitter,
                ProviderAccountId: identity.Id,
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
                AuthEnums.LoginProvider.Twitter,
                identity.Id,
                username,
                identity.Name,
                AvatarUrl: null,
                connection.Value.Id
            )
        );
    }

    private async Task<TwitterUser?> FetchUserAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        UserMeResponse? body = await response.Content.ReadFromJsonAsync<UserMeResponse>(
            cancellationToken
        );
        return body?.Data;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class UserMeResponse
    {
        [JsonPropertyName("data")]
        public TwitterUser? Data { get; set; }
    }

    private sealed class TwitterUser
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }
    }
}
