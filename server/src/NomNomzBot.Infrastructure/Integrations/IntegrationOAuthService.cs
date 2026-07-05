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
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Integrations.Dtos;
using NomNomzBot.Application.Integrations.Services;
using NomNomzBot.Domain.Identity.Enums;

namespace NomNomzBot.Infrastructure.Integrations;

/// <summary>
/// The generic, descriptor-driven OAuth connect flow for non-Twitch providers (integrations-oauth §3.1):
/// authorize → callback → token-exchange with PKCE (S256) + a signed single-use state nonce, then hands the
/// tokens to identity-auth's <see cref="IIntegrationTokenVault"/> (crypto-vaulted). It stores no tokens
/// itself and is generic over <see cref="OAuthProviderDescriptor"/> — a new provider is a descriptor.
/// </summary>
public sealed class IntegrationOAuthService : IIntegrationOAuthService
{
    private const string StateCachePrefix = "oauth:state:";
    private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

    private readonly IOAuthProviderRegistry _registry;
    private readonly IIntegrationTokenVault _vault;
    private readonly IDiscordGuildService _discord;
    private readonly IIntegrationCapabilityStore _capabilities;
    private readonly ISystemCredentialsProvider _credentials;
    private readonly ICacheService _cache;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly string _baseUrl;
    private readonly ILogger<IntegrationOAuthService> _logger;

    public IntegrationOAuthService(
        IOAuthProviderRegistry registry,
        IIntegrationTokenVault vault,
        IDiscordGuildService discord,
        IIntegrationCapabilityStore capabilities,
        ISystemCredentialsProvider credentials,
        ICacheService cache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<IntegrationOAuthService> logger
    )
    {
        _registry = registry;
        _vault = vault;
        _discord = discord;
        _capabilities = capabilities;
        _credentials = credentials;
        _cache = cache;
        _http = httpClientFactory.CreateClient("integration-oauth");
        _timeProvider = timeProvider;
        _baseUrl = configuration["App:BaseUrl"] ?? "http://localhost:5080";
        _logger = logger;
    }

    public async Task<Result<OAuthStartDto>> StartConnectAsync(
        Guid broadcasterId,
        string provider,
        string scopeSetKey,
        string? returnUrl,
        Guid actingUserId,
        string publicOrigin,
        CancellationToken cancellationToken = default
    )
    {
        Result<OAuthProviderDescriptor> descriptorResult = _registry.Resolve(
            provider,
            broadcasterId
        );
        if (descriptorResult.IsFailure)
            return descriptorResult.WithValue<OAuthStartDto>(null!);
        OAuthProviderDescriptor descriptor = descriptorResult.Value;

        if (!descriptor.ScopeSets.TryGetValue(scopeSetKey, out IReadOnlyList<string>? scopes))
            return Result.Failure<OAuthStartDto>(
                $"Unknown scope set '{scopeSetKey}' for provider '{provider}'.",
                "UNKNOWN_SCOPE_SET"
            );

        SystemAppCredentials? app = await _credentials.GetAsync(provider, cancellationToken);
        if (app is null)
            return Result.Failure<OAuthStartDto>(
                $"{provider} app credentials are not configured.",
                "PROVIDER_NOT_CONFIGURED"
            );

        string state = Base64UrlBytes(RandomNumberGenerator.GetBytes(32));
        string codeVerifier = Base64UrlBytes(RandomNumberGenerator.GetBytes(32));
        string codeChallenge = Base64UrlBytes(
            SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier))
        );

        // Build the redirect_uri from the request's public origin (the tunnel/domain the dashboard was served
        // from) and persist it in the state: the callback's token exchange reuses this exact value so the two
        // requests match byte-for-byte, no matter what host the provider's redirect arrives on.
        string redirectUri = RedirectUriFor(publicOrigin, provider);

        OAuthStateEntry entry = new(
            broadcasterId,
            provider,
            scopeSetKey,
            actingUserId,
            returnUrl,
            codeVerifier,
            redirectUri
        );
        await _cache.SetAsync(StateCachePrefix + state, entry, StateTtl, cancellationToken);

        string authorizeUrl =
            descriptor.AuthorizeEndpoint
            + $"?client_id={Uri.EscapeDataString(app.ClientId)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + "&response_type=code"
            + $"&scope={Uri.EscapeDataString(string.Join(' ', scopes))}"
            + $"&state={Uri.EscapeDataString(state)}"
            + (
                descriptor.UsesPkce
                    ? $"&code_challenge={Uri.EscapeDataString(codeChallenge)}&code_challenge_method=S256"
                    : string.Empty
            )
            // Google requires these to return a refresh token; Spotify ignores them.
            + "&access_type=offline&prompt=consent";

        return Result.Success(new OAuthStartDto(authorizeUrl, state));
    }

    public async Task<Result<OAuthCallbackResultDto>> HandleCallbackAsync(
        string provider,
        OAuthCallbackParams callbackParams,
        CancellationToken cancellationToken = default
    )
    {
        if (!string.IsNullOrEmpty(callbackParams.Error))
            return Result.Failure<OAuthCallbackResultDto>(
                callbackParams.ErrorDescription ?? callbackParams.Error,
                "PROVIDER_ERROR"
            );

        if (string.IsNullOrEmpty(callbackParams.Code) || string.IsNullOrEmpty(callbackParams.State))
            return Result.Failure<OAuthCallbackResultDto>(
                "Missing code or state.",
                "INVALID_CALLBACK"
            );

        string cacheKey = StateCachePrefix + callbackParams.State;
        OAuthStateEntry? entry = await _cache.GetAsync<OAuthStateEntry>(
            cacheKey,
            cancellationToken
        );
        if (
            entry is null
            || !string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase)
        )
            return Result.Failure<OAuthCallbackResultDto>(
                "State is invalid or expired.",
                "INVALID_STATE"
            );

        // Single-use: consume the state immediately so a replay fails closed.
        await _cache.RemoveAsync(cacheKey, cancellationToken);

        Result<OAuthProviderDescriptor> descriptorResult = _registry.Resolve(
            provider,
            entry.BroadcasterId
        );
        if (descriptorResult.IsFailure)
            return descriptorResult.WithValue<OAuthCallbackResultDto>(null!);
        OAuthProviderDescriptor descriptor = descriptorResult.Value;

        SystemAppCredentials? app = await _credentials.GetAsync(provider, cancellationToken);
        if (app is null)
            return Result.Failure<OAuthCallbackResultDto>(
                $"{provider} app credentials are not configured.",
                "PROVIDER_NOT_CONFIGURED"
            );

        TokenExchangeResult? tokens = await ExchangeCodeAsync(
            descriptor,
            app,
            callbackParams.Code,
            entry.CodeVerifier,
            entry.RedirectUri,
            cancellationToken
        );
        if (tokens is null)
            return Result.Failure<OAuthCallbackResultDto>(
                "Token exchange failed.",
                "TOKEN_EXCHANGE_FAILED"
            );

        (string? accountId, string? accountName) = await FetchAccountIdentityAsync(
            descriptor,
            tokens.AccessToken,
            cancellationToken
        );

        IReadOnlyList<string> grantedScopes = SplitScopes(tokens.Scope);

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new UpsertConnectionDto(
                entry.BroadcasterId,
                provider,
                accountId,
                accountName,
                grantedScopes,
                app.ClientId,
                descriptor.IsByok,
                entry.ActingUserId,
                SettingsJson: null
            ),
            cancellationToken
        );
        if (connection.IsFailure)
            return connection.WithValue<OAuthCallbackResultDto>(null!);

        Result store = await _vault.StoreTokensAsync(
            connection.Value.Id,
            new StoreTokensDto(
                tokens.AccessToken,
                tokens.RefreshToken,
                AppToken: null,
                tokens.ExpiresAt
            ),
            grantedScopes,
            cancellationToken
        );
        if (store.IsFailure)
            return store.WithValue<OAuthCallbackResultDto>(null!);

        IReadOnlyList<string> grantedScopeSets = GrantedScopeSets(descriptor, grantedScopes);
        return Result.Success(
            new OAuthCallbackResultDto(
                provider,
                accountName ?? accountId ?? provider,
                grantedScopeSets,
                entry.ReturnUrl ?? _baseUrl
            )
        );
    }

    public async Task<Result> DisconnectAsync(
        Guid broadcasterId,
        string provider,
        Guid actingUserId,
        CancellationToken cancellationToken = default
    )
    {
        Result<IReadOnlyList<IntegrationConnectionDto>> connections =
            await _vault.ListConnectionsAsync(broadcasterId, cancellationToken);
        if (connections.IsFailure)
            return connections;

        IntegrationConnectionDto? connection = connections.Value.FirstOrDefault(c =>
            string.Equals(c.Provider, provider, StringComparison.OrdinalIgnoreCase)
        );
        if (connection is null)
            return Result.Success(); // idempotent

        return await _vault.RevokeConnectionAsync(
            connection.Id,
            "user_disconnect",
            cancellationToken
        );
    }

    public async Task<Result<IReadOnlyList<IntegrationStatusDto>>> GetStatusAsync(
        Guid broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Result<IReadOnlyList<IntegrationConnectionDto>> connections =
            await _vault.ListConnectionsAsync(broadcasterId, cancellationToken);
        if (connections.IsFailure)
            return connections.WithValue<IReadOnlyList<IntegrationStatusDto>>(null!);

        List<IntegrationStatusDto> statuses = [];
        foreach (string provider in _registry.KnownProviders)
        {
            IntegrationConnectionDto? c = connections.Value.FirstOrDefault(x =>
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase)
            );
            Result<OAuthProviderDescriptor> descriptor = _registry.Resolve(provider, broadcasterId);

            IReadOnlyList<string> grantedSets =
                c is not null && descriptor.IsSuccess
                    ? GrantedScopeSets(descriptor.Value, c.Scopes)
                    : [];

            statuses.Add(
                new IntegrationStatusDto(
                    provider,
                    Connected: c is not null && c.Status == AuthEnums.IntegrationStatus.Connected,
                    AccountName: c?.ProviderAccountName,
                    GrantedScopeSets: grantedSets,
                    // Runtime-observed capabilities (e.g. spotify.premium flipped by the music
                    // provider's player-403 detection) — absent until observed, never guessed.
                    Capabilities: _capabilities.GetObserved(broadcasterId, provider),
                    NeedsReauth: c?.Status == AuthEnums.IntegrationStatus.NeedsReauth
                )
            );
        }

        // Discord lives outside the descriptor registry (its connect carries a guild authorization, not an
        // ordinary user-resource grant — discord.md §0), so it is reported here from its own connection table,
        // consistently with IntegrationsController.ListIntegrations: connected iff any non-deleted
        // DiscordGuildConnection exists for the tenant. This keeps /integrations/status the one status surface.
        Result<IReadOnlyList<DiscordGuildConnectionDto>> discordConnections =
            await _discord.GetConnectionsAsync(broadcasterId, cancellationToken);
        if (discordConnections.IsFailure)
            return discordConnections.WithValue<IReadOnlyList<IntegrationStatusDto>>(null!);

        DiscordGuildConnectionDto? discord = discordConnections.Value.FirstOrDefault();
        statuses.Add(
            new IntegrationStatusDto(
                AuthEnums.IntegrationProvider.Discord,
                Connected: discord is not null,
                AccountName: discord?.GuildName,
                GrantedScopeSets: [],
                Capabilities: _capabilities.GetObserved(
                    broadcasterId,
                    AuthEnums.IntegrationProvider.Discord
                ),
                NeedsReauth: false
            )
        );

        return Result.Success<IReadOnlyList<IntegrationStatusDto>>(statuses);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static string RedirectUriFor(string publicOrigin, string provider) =>
        $"{publicOrigin.TrimEnd('/')}/api/v1/integrations/{provider}/callback";

    private async Task<TokenExchangeResult?> ExchangeCodeAsync(
        OAuthProviderDescriptor descriptor,
        SystemAppCredentials app,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, string> form = new()
        {
            ["client_id"] = app.ClientId,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["client_secret"] = app.ClientSecret,
        };

        using FormUrlEncodedContent content = new(form);
        HttpResponseMessage response = await _http.PostAsync(
            descriptor.TokenEndpoint,
            content,
            cancellationToken
        );
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "OAuth token exchange for {Provider} failed: {Status}",
                descriptor.Provider,
                response.StatusCode
            );
            return null;
        }

        ProviderTokenResponse? json =
            await response.Content.ReadFromJsonAsync<ProviderTokenResponse>(
                cancellationToken: cancellationToken
            );
        if (json is null || string.IsNullOrEmpty(json.AccessToken))
            return null;

        DateTime? expiresAt =
            json.ExpiresIn > 0
                ? _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(json.ExpiresIn)
                : null;
        return new TokenExchangeResult(json.AccessToken, json.RefreshToken, expiresAt, json.Scope);
    }

    private async Task<(string? Id, string? Name)> FetchAccountIdentityAsync(
        OAuthProviderDescriptor descriptor,
        string accessToken,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                descriptor.AccountIdentityEndpoint
            );
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (null, null);

            ProviderIdentity? identity = await response.Content.ReadFromJsonAsync<ProviderIdentity>(
                cancellationToken: cancellationToken
            );
            return (identity?.Id ?? identity?.Sub, identity?.DisplayName ?? identity?.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Failed to read {Provider} account identity after connect",
                descriptor.Provider
            );
            return (null, null);
        }
    }

    /// <summary>The scope-set keys whose every scope is present in the granted set (a narrower grant is surfaced).</summary>
    private static IReadOnlyList<string> GrantedScopeSets(
        OAuthProviderDescriptor descriptor,
        IReadOnlyList<string> grantedScopes
    )
    {
        HashSet<string> granted = new(grantedScopes, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. descriptor
                .ScopeSets.Where(kv => kv.Value.All(granted.Contains))
                .Select(kv => kv.Key),
        ];
    }

    private static IReadOnlyList<string> SplitScopes(string? scope) =>
        string.IsNullOrWhiteSpace(scope)
            ? []
            : scope.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);

    private static string Base64UrlBytes(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record OAuthStateEntry(
        Guid BroadcasterId,
        string Provider,
        string ScopeSetKey,
        Guid ActingUserId,
        string? ReturnUrl,
        string CodeVerifier,
        string RedirectUri
    );

    private sealed record TokenExchangeResult(
        string AccessToken,
        string? RefreshToken,
        DateTime? ExpiresAt,
        string? Scope
    );

    private sealed class ProviderTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refresh_token")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("scope")]
        public string? Scope { get; set; }
    }

    private sealed class ProviderIdentity
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("sub")]
        public string? Sub { get; set; }

        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
