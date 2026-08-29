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
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Discord;
using NomNomzBot.Application.Contracts.Kick;
using NomNomzBot.Application.Contracts.Music;
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
    private readonly IKickApiClient _kick;
    private readonly IIntegrationCapabilityStore _capabilities;
    private readonly IChannelCredentialsResolver _channelCredentials;
    private readonly IMusicProviderTokenMirror _musicTokenMirror;
    private readonly ICacheService _cache;
    private readonly HttpClient _http;
    private readonly TimeProvider _timeProvider;
    private readonly string _baseUrl;
    private readonly ILogger<IntegrationOAuthService> _logger;

    public IntegrationOAuthService(
        IOAuthProviderRegistry registry,
        IIntegrationTokenVault vault,
        IDiscordGuildService discord,
        IKickApiClient kick,
        IIntegrationCapabilityStore capabilities,
        IChannelCredentialsResolver channelCredentials,
        IMusicProviderTokenMirror musicTokenMirror,
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
        _kick = kick;
        _capabilities = capabilities;
        _channelCredentials = channelCredentials;
        _musicTokenMirror = musicTokenMirror;
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
        string? shopDomain = null,
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

        Result<IReadOnlyList<string>> scopesResult = ResolveScopes(descriptor, scopeSetKey);
        if (scopesResult.IsFailure)
            return scopesResult.WithValue<OAuthStartDto>(null!);
        IReadOnlyList<string> scopes = scopesResult.Value;

        // A shop-scoped provider (Shopify) parameterizes its endpoints with the shop name — required,
        // sanitized (never a full URL: the substitution must not become an SSRF vector).
        string? shop = null;
        if (descriptor.RequiresShopDomain)
        {
            shop = SanitizeShopName(shopDomain);
            if (shop is null)
                return Result.Failure<OAuthStartDto>(
                    $"'{provider}' needs the shop name (e.g. my-store or my-store.myshopify.com).",
                    "SHOP_REQUIRED"
                );
        }

        Result<SystemAppCredentials> appResult = await _channelCredentials.ResolveAsync(
            broadcasterId,
            provider,
            cancellationToken
        );
        if (appResult.IsFailure)
            return appResult.WithValue<OAuthStartDto>(null!);
        SystemAppCredentials app = appResult.Value;

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
            redirectUri,
            shop
        );
        await _cache.SetAsync(StateCachePrefix + state, entry, StateTtl, cancellationToken);

        string authorizeUrl =
            ResolveEndpoint(descriptor.AuthorizeEndpoint, shop)
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
        string? publicOrigin = null,
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

        Result<SystemAppCredentials> callbackAppResult = await _channelCredentials.ResolveAsync(
            entry.BroadcasterId,
            provider,
            cancellationToken
        );
        if (callbackAppResult.IsFailure)
            return callbackAppResult.WithValue<OAuthCallbackResultDto>(null!);
        SystemAppCredentials app = callbackAppResult.Value;

        TokenExchangeResult? tokens = await ExchangeCodeAsync(
            descriptor,
            app,
            callbackParams.Code,
            entry.CodeVerifier,
            entry.RedirectUri,
            entry.ShopDomain,
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
            entry.ShopDomain,
            cancellationToken
        );

        IReadOnlyList<string> grantedScopes = SplitScopes(tokens.Scope);

        Result<IntegrationConnectionDto> connection = await _vault.UpsertConnectionAsync(
            new(
                entry.BroadcasterId,
                provider,
                accountId,
                accountName,
                grantedScopes,
                app.ClientId,
                descriptor.IsByok,
                entry.ActingUserId,
                // A shop-scoped connection remembers its shop — later provider API calls (webhook
                // provisioning, order reads) need the domain, not just the numeric shop id.
                SettingsJson: entry.ShopDomain is null
                    ? null
                    : JsonSerializer.Serialize(new { shopDomain = entry.ShopDomain })
            ),
            cancellationToken
        );
        if (connection.IsFailure)
            return connection.WithValue<OAuthCallbackResultDto>(null!);

        Result store = await _vault.StoreTokensAsync(
            connection.Value.Id,
            new(tokens.AccessToken, tokens.RefreshToken, AppToken: null, tokens.ExpiresAt),
            grantedScopes,
            cancellationToken
        );
        if (store.IsFailure)
            return store.WithValue<OAuthCallbackResultDto>(null!);

        // NOTE (token-store bridge): the vault above is the CANONICAL store for every provider. Spotify
        // alone still needs a mirror — SpotifyMusicProvider and IntegrationsController.ListIntegrations's
        // Spotify auth-status read still consult the legacy `Service` token store, which nothing else
        // writes for it — so without this mirror a connected Spotify reads back as disconnected and the
        // dashboard loops on "reconnect". YouTube was migrated off this bridge in S036c-b: its readers
        // (YouTubeAccessTokenProvider, YouTubeMusicProvider, the live-chat poller, IntegrationStatusService)
        // all resolve the vault directly now, so mirroring a YouTube grant here would be a second, dead
        // copy of the token. MirrorAsync is a no-op for every non-mirrored provider (including YouTube).
        await _musicTokenMirror.MirrorAsync(
            entry.BroadcasterId,
            provider,
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
            app.ClientId,
            app.ClientSecret,
            grantedScopes,
            cancellationToken
        );

        IReadOnlyList<string> grantedScopeSets = GrantedScopeSets(descriptor, grantedScopes);
        return Result.Success(
            new OAuthCallbackResultDto(
                provider,
                accountName ?? accountId ?? provider,
                grantedScopeSets,
                entry.ReturnUrl ?? publicOrigin ?? _baseUrl
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

        if (string.Equals(provider, "kick", StringComparison.OrdinalIgnoreCase))
            await UnsubscribeKickWebhooksAsync(connection.Id, cancellationToken);

        return await _vault.RevokeConnectionAsync(
            connection.Id,
            "user_disconnect",
            cancellationToken
        );
    }

    /// <summary>
    /// Best-effort: tells Kick to stop delivering webhooks for this connection before the token is
    /// revoked. Without this, Kick keeps the subscriptions registered and <c>KickWebhookIngest</c>
    /// keeps resolving/processing deliveries for a streamer who just disconnected. Never blocks the
    /// disconnect itself — a failure here is logged and the revoke proceeds regardless.
    /// </summary>
    private async Task UnsubscribeKickWebhooksAsync(
        Guid connectionId,
        CancellationToken cancellationToken
    )
    {
        Result<DecryptedTokenDto> token = await _vault.GetAccessTokenAsync(
            connectionId,
            cancellationToken
        );
        if (token.IsFailure)
            return;

        Result<IReadOnlyList<KickEventSubscription>> subscriptions =
            await _kick.ListEventSubscriptionsAsync(token.Value.Value, cancellationToken);
        if (subscriptions.IsFailure)
        {
            _logger.LogWarning(
                "Kick disconnect: could not list webhook subscriptions for connection {ConnectionId}: {Error}",
                connectionId,
                subscriptions.ErrorMessage
            );
            return;
        }

        if (subscriptions.Value.Count == 0)
            return;

        Result unsubscribed = await _kick.UnsubscribeAsync(
            token.Value.Value,
            [.. subscriptions.Value.Select(s => s.Id)],
            cancellationToken
        );
        if (unsubscribed.IsFailure)
            _logger.LogWarning(
                "Kick disconnect: could not remove webhook subscriptions for connection {ConnectionId}: {Error}",
                connectionId,
                unsubscribed.ErrorMessage
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
                new(
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
            new(
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

    // A key may be a single declared set or a '+'-joined union of several, so a reconnect can request an
    // already-granted set together with a newly-needed one in one consent screen.
    private static Result<IReadOnlyList<string>> ResolveScopes(
        OAuthProviderDescriptor descriptor,
        string scopeSetKey
    )
    {
        string[] keys = scopeSetKey.Split('+', StringSplitOptions.RemoveEmptyEntries);
        if (keys.Length == 0)
            return Result.Failure<IReadOnlyList<string>>(
                $"Unknown scope set '{scopeSetKey}' for provider '{descriptor.Provider}'.",
                "UNKNOWN_SCOPE_SET"
            );

        HashSet<string> union = new(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (!descriptor.ScopeSets.TryGetValue(key, out IReadOnlyList<string>? scopes))
                return Result.Failure<IReadOnlyList<string>>(
                    $"Unknown scope set '{key}' for provider '{descriptor.Provider}'.",
                    "UNKNOWN_SCOPE_SET"
                );
            union.UnionWith(scopes);
        }

        return Result.Success<IReadOnlyList<string>>([.. union]);
    }

    private static string RedirectUriFor(string publicOrigin, string provider) =>
        $"{publicOrigin.TrimEnd('/')}/api/v1/integrations/{provider}/callback";

    /// <summary>Substitutes a shop-scoped endpoint template's <c>{shop}</c> with the sanitized shop name.</summary>
    private static string ResolveEndpoint(string endpointTemplate, string? shop) =>
        shop is null ? endpointTemplate : endpointTemplate.Replace("{shop}", shop);

    /// <summary>
    /// The sanitized shop NAME (never a URL — the endpoint substitution must not become an SSRF vector):
    /// lowercased, an optional pasted <c>.myshopify.com</c> suffix stripped, then strictly
    /// <c>[a-z0-9][a-z0-9-]*</c>. Null when absent or invalid.
    /// </summary>
    private static string? SanitizeShopName(string? shopDomain)
    {
        if (string.IsNullOrWhiteSpace(shopDomain))
            return null;
        string shop = shopDomain.Trim().ToLowerInvariant();
        const string suffix = ".myshopify.com";
        if (shop.EndsWith(suffix, StringComparison.Ordinal))
            shop = shop[..^suffix.Length];
        if (shop.Length is 0 or > 100)
            return null;
        if (!char.IsAsciiLetterOrDigit(shop[0]))
            return null;
        foreach (char c in shop)
            if (!char.IsAsciiLetterOrDigit(c) && c != '-')
                return null;
        return shop;
    }

    private async Task<TokenExchangeResult?> ExchangeCodeAsync(
        OAuthProviderDescriptor descriptor,
        SystemAppCredentials app,
        string code,
        string codeVerifier,
        string redirectUri,
        string? shop,
        CancellationToken cancellationToken
    )
    {
        Dictionary<string, string> form = new()
        {
            ["client_id"] = app.ClientId,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["client_secret"] = app.ClientSecret,
        };
        // The verifier only rides a PKCE flow — a confidential-client provider (Patreon) that never saw a
        // code_challenge at authorize must not get a stray code_verifier at exchange.
        if (descriptor.UsesPkce)
            form["code_verifier"] = codeVerifier;

        using FormUrlEncodedContent content = new(form);
        HttpResponseMessage response = await _http.PostAsync(
            ResolveEndpoint(descriptor.TokenEndpoint, shop),
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
        return new(json.AccessToken, json.RefreshToken, expiresAt, json.Scope);
    }

    private async Task<(string? Id, string? Name)> FetchAccountIdentityAsync(
        OAuthProviderDescriptor descriptor,
        string accessToken,
        string? shop,
        CancellationToken cancellationToken
    )
    {
        // A provider with no identity endpoint (TreatStream) simply connects identity-less.
        if (descriptor.AccountIdentityEndpoint is null)
            return (null, null);

        try
        {
            using HttpRequestMessage request = new(
                HttpMethod.Get,
                ResolveEndpoint(descriptor.AccountIdentityEndpoint, shop)
            );
            // Most providers take the token as a Bearer; a provider with its own header (Shopify's
            // X-Shopify-Access-Token) names it on the descriptor.
            if (descriptor.IdentityTokenHeader is { } headerName)
                request.Headers.Add(headerName, accessToken);
            else
                request.Headers.Add("Authorization", $"Bearer {accessToken}");
            HttpResponseMessage response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return (null, null);

            // Providers disagree on the identity envelope: Spotify/Google return a flat object
            // (id/sub + display_name/name); Kick wraps the caller in a data ARRAY with a numeric
            // user_id; Patreon speaks JSON:API — a data OBJECT whose display fields live under
            // "attributes"; Shopify wraps the store in a "shop" object. Probe the shapes generically
            // instead of branching per provider.
            using JsonDocument doc = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(cancellationToken)
            );
            JsonElement root = doc.RootElement;
            JsonElement subject = root;
            if (
                root.ValueKind == JsonValueKind.Object
                && (
                    root.TryGetProperty("data", out JsonElement data)
                    || root.TryGetProperty("shop", out data)
                )
            )
            {
                if (data.ValueKind == JsonValueKind.Array && data.GetArrayLength() > 0)
                    subject = data[0];
                else if (data.ValueKind == JsonValueKind.Object)
                    subject = data;
            }

            string? accountName = ReadIdentityValue(
                subject,
                "display_name",
                "name",
                "username",
                "full_name"
            );
            // JSON:API nests display fields under "attributes" beside the top-level id.
            if (
                accountName is null
                && subject.ValueKind == JsonValueKind.Object
                && subject.TryGetProperty("attributes", out JsonElement attributes)
            )
                accountName = ReadIdentityValue(
                    attributes,
                    "display_name",
                    "name",
                    "username",
                    "full_name"
                );

            return (ReadIdentityValue(subject, "id", "sub", "user_id"), accountName);
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

    /// <summary>The first present property among <paramref name="names"/>, stringified — numbers (Kick's
    /// numeric user_id) become their invariant string form so the connection key stays a string.</summary>
    private static string? ReadIdentityValue(JsonElement subject, params string[] names)
    {
        if (subject.ValueKind != JsonValueKind.Object)
            return null;

        foreach (string name in names)
        {
            if (!subject.TryGetProperty(name, out JsonElement value))
                continue;
            string? read = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                _ => null,
            };
            if (!string.IsNullOrEmpty(read))
                return read;
        }

        return null;
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
        string RedirectUri,
        string? ShopDomain = null
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
}
