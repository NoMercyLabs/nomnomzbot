# Integration OAuth (Spotify · YouTube · Kick · Patreon · Shopify · Treatstream) — Interface Specification

**Status:** Implementable.
**Subsystem:** The per-provider OAuth **connect** flow for Spotify, YouTube, Kick (`kick` + the dedicated bot account `kick_bot`), Patreon, Shopify and Treatstream — the generic shape every non-Twitch, non-Discord provider follows. Owns `IntegrationOAuthController` + `IIntegrationOAuthService` + the provider descriptors/scope sets. Closes gap **P2**. **Token storage (`IIntegrationTokenVault`), the `IntegrationConnections` table, and the `Integration*Event`s are owned by `identity-auth.md`** — this spec owns the OAuth dance and hands the resulting tokens to that vault. The provider **management surface** (playback, playlists, search) is consumed/owned by `music-sr.md`; this spec only gets the user **connected** with the right scopes.

## Grounding & locked decisions (binding)

- **Connect-only, vault-elsewhere.** This spec performs `authorize → callback → token-exchange` and then calls identity-auth's `IIntegrationTokenVault.UpsertConnectionAsync` + `StoreTokensAsync` (crypto-vaulted via `ISubjectKeyService`/`IFieldCipher`). It never stores tokens itself, never defines `IntegrationConnections` (E.1), and never re-emits `IntegrationConnectedEvent`/`IntegrationNeedsReauthEvent` (identity-auth §2 owns them).
- **Generalize the Discord pattern, don't fork it.** `discord.md` owns Discord's bespoke flow (guild/bot specifics). This spec is the **generic OAuth2 authorization-code connect** for ordinary user-resource providers (Spotify, YouTube, Kick, Patreon, Shopify, Treatstream), parameterized by an `OAuthProviderDescriptor`. Adding a provider = add a descriptor + scopes, no new controller.
- **Progressive scopes** (CLAUDE rule): request only the scopes a feature needs, when enabled. A connect carries a **scope-set key** (e.g. `spotify.playback`, `spotify.library`, `youtube.manage`); re-connecting with a wider set is an incremental re-auth (mirrors identity-auth's Twitch progressive-scope model). Per the [[external-api-full-management-coverage]] rule the descriptors enumerate the **full** manageable scope surface; features gate which subset is requested.
- **PKCE + state.** Authorization-code **with PKCE** (`S256`) wherever the provider supports it (`UsesPkce`: true for Spotify, YouTube, Kick; false for the confidential-client providers Patreon, Shopify, Treatstream); `state` is a random single-use 10-minute nonce whose cache entry (`OAuthStateEntry`) binds `(broadcasterId, provider, scopeSetKey, actingUserId, returnUrl, codeVerifier, redirectUri, shopDomain)` — verified and consumed on callback, fail-closed on mismatch/expiry (CSRF + mix-up defense). The redirect URI is built from the request's public origin — `Request.ResolvePublicOrigin(IConfiguration)` (`PublicOriginExtensions`): a non-loopback `App:BaseUrl` wins, else the forwarded `X-Forwarded-Host`/`-Proto` origin, else the loopback default — persisted in the state so the callback's token exchange reuses the exact same value (OAuth needs a byte-for-byte match; Spotify rejects loopback callbacks). One callback path per provider.
- **YouTube has two auth modes.** **Search/metadata** (the music-SR read path) uses an **app-level API key** (`YouTube:ApiKey`, no per-user OAuth) — configured, not connected. **Managing the user's own YouTube** (playlists/ratings, per the coverage rule) uses **per-user OAuth** (`youtube` scope). Spotify is **always per-user OAuth** (playback + library require the user's token; playback control additionally requires Spotify **Premium** — surfaced as a capability, not an error at connect).
- **Self-host BYOK.** Self-host operators may supply their **own** provider `ClientId`/`ClientSecret` (`IsByok` on the connection, already in identity-auth's `UpsertConnectionDto`); SaaS uses the platform app credentials. The descriptor carries only the `IsByok` flag (from `{Provider}:Byok` config); the credentials themselves are resolved per request by `IChannelCredentialsResolver` (channel BYOC → vaulted system credentials → config), keyed by `descriptor.CredentialsProvider ?? provider` (so `kick_bot` reuses the `kick` app).
- Conventions: `NomNomzBot.*`, .NET 10, `Result<T>`, `Guid` keys, async-all-the-way, `StatusResponseDto<T>`, `[ApiVersion("1.0")]`, Newtonsoft app-JSON.

---

## 1. Entities

**None new.** Connections + tokens live in `IntegrationConnections` (E.1, identity-auth) + the crypto vault (Q.1). The OAuth `state` nonce is a short-TTL cache entry (`ICacheService`, key `oauth:state:{nonce}`), not a table. Provider descriptors are code/config, not data.

**Read/write dependencies (owned elsewhere):** `IntegrationConnections` (E.1 — identity-auth, written via its service), `CryptoKey`/`EventSubjectKeys` (Q.1 — token vault), `IntegrationConnectedEvent`/`IntegrationDisconnectedEvent`/`IntegrationNeedsReauthEvent`/`IntegrationTokenRefreshedEvent` (identity-auth §2 — emitted by its store), `AppSetting` (P.13 — `Spotify:*`, `YouTube:*`, `Kick:*`, `Patreon:*`, `Shopify:*`, `Treatstream:*` client config). A connect is not tier-gated.

---

## 2. Domain events

**None new.** A successful connect raises identity-auth's `IntegrationConnectedEvent` (from `StoreTokensAsync`); disconnect raises `IntegrationDisconnectedEvent`; a refresh failure raises `IntegrationNeedsReauthEvent`. This subsystem emits nothing of its own — it is a flow over identity-auth's connection lifecycle.

---

## 3. Service interfaces

`NomNomzBot.Application.Integrations.Services` (DTOs in `NomNomzBot.Application.Integrations.Dtos`); impls in `NomNomzBot.Infrastructure/Integrations/`. Async, `Result`/`Result<T>`.

### 3.1 `IIntegrationOAuthService` — the generic connect flow

```csharp
namespace NomNomzBot.Application.Integrations.Services;

public interface IIntegrationOAuthService
{
    // Builds the provider authorize URL (PKCE challenge + single-use state bound to broadcaster/provider/scopeSet/returnUrl).
    // Stashes the verifier + redirect_uri + state entry in ICacheService (single-use, 10-minute TTL). Returns the URL the client opens.
    // publicOrigin = the public scheme://host the request arrived on (Request.ResolvePublicOrigin) - the redirect_uri is built from it.
    // shopDomain = the shop name for a shop-scoped provider (Shopify; required there, sanitized, ignored elsewhere).
    // Fails if the provider is unknown (UNKNOWN_PROVIDER), the scope-set key is invalid, the shop is missing (SHOP_REQUIRED),
    // or the app credentials are not configured (PROVIDER_NOT_CONFIGURED).
    Task<Result<OAuthStartDto>> StartConnectAsync(
        Guid broadcasterId, string provider, string scopeSetKey, string? returnUrl, Guid actingUserId,
        string publicOrigin, string? shopDomain = null,
        CancellationToken cancellationToken = default);

    // Handles the provider callback: validates+consumes state, exchanges code (+PKCE verifier) for tokens at the provider
    // token endpoint using the redirect_uri persisted at connect-start, fetches the provider account identity, then persists
    // via identity-auth's IIntegrationTokenVault (UpsertConnectionAsync + StoreTokensAsync - vaulted). Reconciles granted vs
    // requested scopes (records the actual grant; a narrower grant is surfaced, not silently accepted as full).
    // Returns the connection + a redirect target (returnUrl, else publicOrigin, else App:BaseUrl).
    // Fail-closed on state/PKCE/exchange failure (no partial connection persisted).
    Task<Result<OAuthCallbackResultDto>> HandleCallbackAsync(
        string provider, OAuthCallbackParams callbackParams, string? publicOrigin = null,
        CancellationToken cancellationToken = default);

    // Severs the connection: revokes the provider token where the provider supports revocation, then calls the vault's
    // RevokeConnectionAsync (soft-delete). Idempotent.
    Task<Result> DisconnectAsync(
        Guid broadcasterId, string provider, Guid actingUserId,
        CancellationToken cancellationToken = default);

    // Read model for the integrations screen: one row per registry provider plus Discord (reported from its own guild
    // connection table) - connected?, account name, granted scope-sets, observed capabilities
    // (e.g. spotify.premium once detected), needs-reauth flag, login-only flag (Kick signed in but not connected). No secrets.
    Task<Result<IReadOnlyList<IntegrationStatusDto>>> GetStatusAsync(
        Guid broadcasterId, CancellationToken cancellationToken = default);
}
```

### 3.2 `IOAuthProviderRegistry` — descriptors (the only place provider specifics live)

```csharp
namespace NomNomzBot.Application.Integrations.Services;

public interface IOAuthProviderRegistry
{
    // The descriptor for a provider (UNKNOWN_PROVIDER when not registered). Credentials are NOT on the descriptor -
    // they are resolved per request by IChannelCredentialsResolver.
    Result<OAuthProviderDescriptor> Resolve(string provider, Guid broadcasterId);
    IReadOnlyList<string> KnownProviders { get; }   // "spotify", "youtube", "kick", "kick_bot", "patreon", "shopify", "treatstream"
}

// Code/config record - NOT a DB row. Scope sets enumerate the FULL manageable surface; features request subsets.
public sealed record OAuthProviderDescriptor(
    string Provider,
    string AuthorizeEndpoint,                                // Shopify: {shop} template, substituted from the sanitized shop name
    string TokenEndpoint,
    string? RevokeEndpoint,                                  // null if the provider has no token revocation
    string? AccountIdentityEndpoint,                         // "me"-style endpoint to read account id/name post-exchange; null = identity-less connect (Treatstream)
    bool UsesPkce,                                           // true for Spotify, YouTube, Kick; false for the confidential-client providers
    IReadOnlyDictionary<string, IReadOnlyList<string>> ScopeSets,  // scopeSetKey -> provider scope strings
    bool IsByok,                                             // deployment BYOK flag ({Provider}:Byok)
    bool RequiresShopDomain = false,                         // Shopify
    string? IdentityTokenHeader = null,                      // custom identity-call token header (Shopify: X-Shopify-Access-Token)
    string? CredentialsProvider = null);                     // reuse another provider's app credentials (kick_bot -> kick)
```

**Seeded scope sets (the descriptors ship with these):**

| Provider | Scope-set key | Provider scopes | Feature |
|---|---|---|---|
| spotify | `spotify.playback` | `user-read-playback-state` `user-modify-playback-state` `user-read-currently-playing` | now-playing + playback control (music-sr) |
| spotify | `spotify.library` | `playlist-read-private` `playlist-modify-public` `playlist-modify-private` `user-library-read` `user-library-modify` | playlist/library management (coverage rule) |
| spotify | `spotify.streaming` | `streaming` `user-read-email` `user-read-private` | OBS playback widget's embedded audio (Web Playback SDK) - progressive, only when enabled |
| youtube | `youtube.manage` | `https://www.googleapis.com/auth/youtube` `https://www.googleapis.com/auth/youtube.force-ssl` | playlist/rating management + Live Chat moderation writes (reply/ban/delete need `force-ssl`) |
| youtube | `youtube.readonly` | `https://www.googleapis.com/auth/youtube.readonly` | read-only manage surface |
| kick | `kick.chat` | `user:read` `chat:write` `moderation:ban` `moderation:chat_message:manage` `events:subscribe` | Kick chat platform (send + moderation + webhook event subscription) |
| kick_bot | `kick_bot.chat` | same five as `kick.chat` | a channel's dedicated Kick bot account (own authorizing account, same Kick app via `CredentialsProvider="kick"`) |
| patreon | `patreon.supporters` | `identity` `campaigns` `campaigns.members` `w:campaigns.webhook` | supporter-events ingest core (webhooks registered by the bot) |
| patreon | `patreon.members_pii` | `campaigns.members[email]` `campaigns.members.address` | member PII - its own opt-in set, never bundled |
| patreon | `patreon.posts` | `campaigns.posts` | campaign posts |
| patreon | `patreon.lives` | `campaigns.lives` `w:campaigns.lives` | campaign lives |
| shopify | `shopify.orders` | `read_orders` | supporter-events merch ingest (orders + bot-registered webhooks) |
| treatstream | `treatstream.treats` | `userinfo` | TreatStream's whole API surface; the realtime treats socket token is exchanged from the access token |

> Provider specifics: **Spotify** has no token revocation; **YouTube** revokes at `oauth2.googleapis.com/revoke`. **Patreon** and **Shopify** are confidential clients with no PKCE. **Shopify** endpoints are shop-scoped (`{shop}.myshopify.com`) - the connect request carries `shopDomain`, which is sanitized to a bare shop name (never a URL) and remembered in the connection's `SettingsJson`. **Treatstream** has no identity endpoint, so its connection is stored identity-less.

> YouTube **search** (music-SR queue source) needs **no** scope-set — it rides the app-level `YouTube:ApiKey` (config), so a channel can queue YouTube tracks **without** any per-user YouTube connect. Per-user `youtube.*` connect is only for managing the user's own YouTube.

---

## 4. DTOs / contracts

`NomNomzBot.Application/Integrations/Dtos/`.

```csharp
namespace NomNomzBot.Application.Integrations.Dtos;

public sealed record ConnectIntegrationRequest(string ScopeSetKey, string? ReturnUrl, string? ShopDomain = null);   // POST body

public sealed record OAuthStartDto(string AuthorizeUrl, string State);   // client opens AuthorizeUrl

public sealed record OAuthCallbackParams(string? Code, string? State, string? Error, string? ErrorDescription);

public sealed record OAuthCallbackResultDto(
    string Provider, string ProviderAccountName, IReadOnlyList<string> GrantedScopeSets,
    string RedirectTarget);                                  // where to send the browser/app after connect

public sealed record IntegrationStatusDto(
    string Provider, bool Connected, string? AccountName,
    IReadOnlyList<string> GrantedScopeSets,
    IReadOnlyDictionary<string, bool> Capabilities,         // e.g. {"spotify.premium": true, "playback": true}
    bool NeedsReauth,
    bool LoginOnly = false);                                 // signed in with the provider (Kick) but not connected as a platform
```

---

## 5. Controller endpoints

`IntegrationOAuthController` (`NomNomzBot.Api/Controllers/V1/`), `[ApiVersion("1.0")]`, class route `api/v{version:apiVersion}` with per-action routes, responses `StatusResponseDto<T>`. The connect/disconnect mutations gate on the streamer/Editor principal via `[RequireAction]`; the callback is a provider redirect (cannot carry the JWT - secured by the single-use `state`). The connect passes `Request.ResolvePublicOrigin(config)` to the service, so the `redirect_uri` follows the origin the dashboard was served from.

| Route | Verb | Request | Response | Auth |
|---|---|---|---|---|
| `/channels/{channelId}/integrations/status` | GET | — | `StatusResponseDto<IReadOnlyList<IntegrationStatusDto>>` | `[Authorize]` · management / Moderator · `integration:read` |
| `/channels/{channelId}/integrations/{provider}/connect` | POST | `ConnectIntegrationRequest { scopeSetKey, returnUrl?, shopDomain? }` | `StatusResponseDto<OAuthStartDto>` (a failure carries its `ErrorCode` in the body, e.g. `PROVIDER_NOT_CONFIGURED`, so the dashboard opens the BYOC onboarding dialog) | `[Authorize]` · management / Editor · `integration:write` |
| `/integrations/{provider}/callback` | GET | `?code&state` (or `?error&error_description`) | `302` to `{publicOrigin}/oauth-relay?return={RedirectTarget}` on success, or `{publicOrigin}/oauth-relay?provider={provider}&error={ErrorCode}` on failure (the relay page `postMessage`s the opener and closes the popup; with no opener it navigates the target) | **Anonymous** — secured by the single-use `state` (no JWT) |
| `/channels/{channelId}/integrations/{provider}/disconnect` | POST | — | `StatusResponseDto<object>` | `[Authorize]` · management / Editor · `integration:write` |

> Management action keys **`integration:read`** (Moderator) and **`integration:write`** (Editor), both `Low`, seeded in `ActionDefinitionSeeder` and listed in `roles-permissions.md §7.1`. Redirect URIs registered with each provider: `{public origin}/api/v1/integrations/{provider}/callback` for every provider in `KnownProviders` (built at connect time from the resolved public origin, not configured; register the deployment's public origin with the provider).

---

## 6. Pipeline actions

**None here.** Provider *actions* (e.g. `play_music`, queue control) are owned by `music-sr.md`; this spec only establishes the connection they require.

---

## 7. DI registration

Registered from `AddInfrastructure` (`DependencyInjection.cs`); there is no separate `AddIntegrationOAuth`.

| Interface | Impl | Lifetime | Notes |
|---|---|---|---|
| `IIntegrationOAuthService` | `IntegrationOAuthService` | Scoped (I<X>Service convention scan) | Uses the named `IHttpClientFactory` client `integration-oauth` for token exchange/revocation/identity reads; persists via identity-auth's `IIntegrationTokenVault`; state entries in `ICacheService`; mirrors Spotify tokens via `IMusicProviderTokenMirror` (a no-op for every other provider). |
| `IOAuthProviderRegistry` | `OAuthProviderRegistry` | Singleton | Holds the seven descriptors; sets `IsByok` from `{Provider}:Byok`. App credentials come from `IChannelCredentialsResolver` (Singleton). |

No deployment-profile adapter pair — the flow is identical across profiles; only credential resolution (BYOK vs platform) differs, handled by `IChannelCredentialsResolver`.

---

## 8. Dependencies

| Dependency | Party | Use |
|---|---|---|
| `System.Net.Http` (`IHttpClientFactory`) | 1st | Provider token exchange / revocation / account-identity reads. |
| `System.Security.Cryptography` (`SHA256`, RNG) | 1st (in-box) | PKCE `S256` challenge + random `state` nonce. |
| identity-auth `IIntegrationTokenVault` + `ISubjectKeyService`/`IFieldCipher` | 1st (this project) | Connection upsert + crypto-vaulted token storage. |
| `Newtonsoft.Json` | 3rd | Provider response parsing (app-JSON convention). |

**Not used:** any 3rd-party OAuth-client library — authorization-code + PKCE is a few in-box calls; no Duende/IdentityModel client needed for the relying-party token exchange.

---

## 9. Decisions (resolved)

- **This spec is connect-only**; tokens/connections/events are owned by identity-auth, the manage surface by music-sr. It closes the "Spotify/YouTube OAuth flow unspecced" gap without duplicating either.
- **Generic descriptor-driven flow** — one controller + service + registry; a new provider is a descriptor, not new code.
- **PKCE (where the provider supports it) + single-use cached `state`**, redirect URIs built at runtime from `ResolvePublicOrigin`, progressive scope-sets enumerating the full manageable surface per the coverage rule.
- **YouTube search rides an app-level API key** (no per-user connect); per-user `youtube.*` OAuth is only for managing the user's own YouTube. **Spotify is always per-user OAuth**, playback additionally needs Premium (surfaced as a capability).
- **Self-host BYOK** credentials supported via the existing `IsByok` connection flag; SaaS uses platform app credentials.
