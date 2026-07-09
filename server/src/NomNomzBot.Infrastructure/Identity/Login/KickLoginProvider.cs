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
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Identity.Login;

/// <summary>
/// The Kick auth-code + PKCE login provider (platform-identity §10.3): builds Kick's authorize URL with an
/// S256 challenge, exchanges the returned code (proving possession via the verifier) for tokens, then proves
/// the Kick identity from <c>GET /public/v1/users</c> after vaulting the issued tokens through
/// <see cref="IIntegrationTokenVault"/>. Dispatched by the generic <c>auth/kick/authorize</c> +
/// <c>auth/kick/callback</c> routes like every non-device provider.
/// </summary>
public sealed class KickLoginProvider : IAuthCodeLoginProvider
{
    private const string AuthorizeEndpoint = "https://id.kick.com/oauth/authorize";
    private const string TokenEndpoint = "https://id.kick.com/oauth/token";
    private const string UserInfoEndpoint = "https://api.kick.com/public/v1/users";

    private static readonly string[] LoginScopes = ["user:read"];

    private readonly HttpClient _http;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly IIntegrationTokenVault _vault;
    private readonly TimeProvider _clock;

    public KickLoginProvider(
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

    public string Key => AuthEnums.LoginProvider.Kick;

    public async Task<Result<Uri>> BuildAuthorizeUrlAsync(
        string state,
        string redirectUri,
        string codeChallenge,
        CancellationToken cancellationToken = default
    )
    {
        string? clientId = await _credentials.GetClientIdAsync(
            AuthEnums.LoginProvider.Kick,
            cancellationToken
        );
        if (clientId is null)
            return Result.Failure<Uri>("Kick login is not configured.", "SERVICE_UNAVAILABLE");

        Dictionary<string, string> parameters = new()
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
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
            AuthEnums.LoginProvider.Kick,
            cancellationToken
        );
        if (app is null)
            return Result.Failure<ExternalIdentityProof>(
                "Kick login is not configured.",
                "SERVICE_UNAVAILABLE"
            );

        using FormUrlEncodedContent content = new(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = app.ClientId,
                ["client_secret"] = app.ClientSecret,
                ["redirect_uri"] = redirectUri,
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
            }
        );
        HttpResponseMessage response = await _http.PostAsync(
            TokenEndpoint,
            content,
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
            return Result.Failure<ExternalIdentityProof>(
                "Kick token exchange failed.",
                "SERVICE_UNAVAILABLE"
            );

        TokenResponse? token = await response.Content.ReadFromJsonAsync<TokenResponse>(
            cancellationToken
        );
        if (token is null || string.IsNullOrEmpty(token.AccessToken))
            return Result.Failure<ExternalIdentityProof>(
                "Kick returned an unexpected token response.",
                "SERVICE_UNAVAILABLE"
            );

        KickUser? identity = await FetchUserAsync(token.AccessToken, cancellationToken);
        if (identity is null || identity.UserId is null)
            return Result.Failure<ExternalIdentityProof>(
                "Failed to read the Kick account identity.",
                "SERVICE_UNAVAILABLE"
            );

        string providerUserId = identity.UserId.Value.ToString(CultureInfo.InvariantCulture);
        string username = identity.Name ?? providerUserId;

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                BroadcasterId: null,
                Provider: AuthEnums.LoginProvider.Kick,
                ProviderAccountId: providerUserId,
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
                AuthEnums.LoginProvider.Kick,
                providerUserId,
                username,
                identity.Name,
                identity.ProfilePicture,
                connection.Value.Id
            )
        );
    }

    private async Task<KickUser?> FetchUserAsync(
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        using HttpRequestMessage request = new(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Add("Authorization", $"Bearer {accessToken}");
        HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;

        UsersResponse? body = await response.Content.ReadFromJsonAsync<UsersResponse>(
            cancellationToken
        );
        return body?.Data?.FirstOrDefault();
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

    private sealed class UsersResponse
    {
        [JsonPropertyName("data")]
        public List<KickUser>? Data { get; set; }
    }

    private sealed class KickUser
    {
        [JsonPropertyName("user_id")]
        public long? UserId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("profile_picture")]
        public string? ProfilePicture { get; set; }
    }
}
