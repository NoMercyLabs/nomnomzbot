# Integration OAuth (Spotify · YouTube) — Interface Specification

**Status:** Implementable.
**Subsystem:** The per-provider OAuth **connect** flow for Spotify and YouTube (and the generic shape any future non-Twitch, non-Discord provider follows). Owns `IntegrationOAuthController` + `IIntegrationOAuthService` + the provider descriptors/scope sets. Closes gap **P2**. **Token storage, the `IntegrationConnections` table, and the `Integration*Event`s are owned by `identity-auth.md`** — this spec owns the OAuth dance and hands the resulting tokens to that vault. The provider **management surface** (playback, playlists, search) is consumed/owned by `music-sr.md`; this spec only gets the user **connected** with the right scopes.

## Grounding & locked decisions (binding)

- **Connect-only, vault-elsewhere.** This spec performs `authorize → callback → token-exchange` and then calls identity-auth's `IIntegrationService.UpsertConnectionAsync` + `StoreTokensAsync` (crypto-vaulted via `ISubjectKeyService`/`IFieldCipher`). It never stores tokens itself, never defines `IntegrationConnections` (E.1), and never re-emits `IntegrationConnectedEvent`/`IntegrationNeedsReauthEvent` (identity-auth §2 owns them).
- **Generalize the Discord pattern, don't fork it.** `discord.md` owns Discord's bespoke flow (guild/bot specifics). This spec is the **generic OAuth2 authorization-code connect** for ordinary user-resource providers (Spotify, YouTube), parameterized by an `OAuthProviderDescriptor`. Adding a future provider = add a descriptor + scopes, no new controller.
- **Progressive scopes** (CLAUDE rule): request only the scopes a feature needs, when enabled. A connect carries a **scope-set key** (e.g. `spotify.playback`, `spotify.library`, `youtube.manage`); re-connecting with a wider set is an incremental re-auth (mirrors identity-auth's Twitch progressive-scope model). Per the [[external-api-full-management-coverage]] rule the descriptors enumerate the **full** manageable scope surface; features gate which subset is requested.
- **PKCE + state.** Authorization-code **with PKCE** (`S256`); `state` is a signed, single-use, TTL'd nonce bound to `(broadcasterId, provider, scopeSetKey, returnUrl)` — verified on callback, fail-closed on mismatch/expiry (CSRF + mix-up defense). Redirect URI is computed at runtime from `App:BaseUrl` (same rule as Twitch), one callback path per provider.
- **YouTube has two auth modes.** **Search/metadata** (the music-SR read path) uses an **app-level API key** (`YouTube:ApiKey`, no per-user OAuth) — configured, not connected. **Managing the user's own YouTube** (playlists/ratings, per the coverage rule) uses **per-user OAuth** (`youtube` scope). Spotify is **always per-user OAuth** (playback + library require the user's token; playback control additionally requires Spotify **Premium** — surfaced as a capability, not an error at connect).
- **Self-host BYOK.** Self-host operators may supply their **own** provider `ClientId`/`ClientSecret` (`IsByok` on the connection, already in identity-auth's `UpsertConnectionDto`); SaaS uses the platform app credentials. The descriptor resolves credentials per deployment profile.
- Conventions: `NomNomzBot.*`, .NET 10, `Result<T>`, `Guid` keys, async-all-the-way, `StatusResponseDto<T>`, `[ApiVersion("1.0")]`, Newtonsoft app-JSON.

---

## 1. Entities

**None new.** Connections + tokens live in `IntegrationConnections` (E.1, identity-auth) + the crypto vault (Q.1). The OAuth `state` nonce is a short-TTL cache entry (`ICacheService`, key `oauth:state:{nonce}`), not a table. Provider descriptors are code/config, not data.

**Read/write dependencies (owned elsewhere):** `IntegrationConnections` (E.1 — identity-auth, written via its service), `CryptoKey`/`EventSubjectKeys` (Q.1 — token vault), `IntegrationConnectedEvent`/`IntegrationDisconnectedEvent`/`IntegrationNeedsReauthEvent`/`IntegrationTokenRefreshedEvent` (identity-auth §2 — emitted by its store), `AppSetting` (P.13 — `Spotify:*`, `YouTube:*` client config), `IBillingTierService` (a provider connect may be tier-gated, e.g. Spotify on a paid tier).

---

## 2. Domain events

**None new.** A successful connect raises identity-auth's `IntegrationConnectedEvent` (from `StoreTokensAsync`); disconnect raises `IntegrationDisconnectedEvent`; a refresh failure raises `IntegrationNeedsReauthEvent`. This subsystem emits nothing of its own — it is a flow over identity-auth's connection lifecycle.

---

## 3. Service interfaces

`NomNomzBot.Application.Services.Integrations`; impls in `NomNomzBot.Infrastructure/Services/Integrations/`. Async, `Result`/`Result<T>`.

### 3.1 `IIntegrationOAuthService` — the generic connect flow

```csharp
namespace NomNomzBot.Application.Services.Integrations;

public interface IIntegrationOAuthService
{
    // Builds the provider authorize URL (PKCE challenge + signed state bound to broadcaster/provider/scopeSet/returnUrl).
    // Stashes the verifier + state in ICacheService (single-use, TTL). Returns the URL the client opens.
    // Fails if the provider is unknown, the scope-set key is invalid, or a connect is tier-gated and disallowed.
    Task<Result<OAuthStartDto>> StartConnectAsync(
        Guid broadcasterId, string provider, string scopeSetKey, string? returnUrl, Guid actingUserId,
        CancellationToken cancellationToken = default);

    // Handles the provider callback: validates+consumes state, exchanges code (+PKCE verifier) for tokens at the provider
    // token endpoint, fetches the provider account identity, then persists via identity-auth's IIntegrationService
    // (UpsertConnectionAsync + StoreTokensAsync — vaulted). Reconciles granted vs requested scopes (records the actual
    // grant; a narrower grant is surfaced, not silently accepted as full). Returns the connection + a redirect target.
    // Fail-closed on state/PKCE/exchange failure (no partial connection persisted).
    Task<Result<OAuthCallbackResultDto>> HandleCallbackAsync(
        string provider, OAuthCallbackParams callbackParams,
        CancellationToken cancellationToken = default);

    // Severs the connection: revokes the provider token where the provider supports revocation, then calls identity-auth's
    // DisconnectAsync (soft-delete + crypto-shred the token DEK). Idempotent.
    Task<Result> DisconnectAsync(
        Guid broadcasterId, string provider, Guid actingUserId,
        CancellationToken cancellationToken = default);

    // Read model for the integrations screen: per provider — connected?, account name, granted scope-sets, capabilities
    // (e.g. Spotify premium? youtube manage-enabled?), needs-reauth flag. No secrets.
    Task<Result<IReadOnlyList<IntegrationStatusDto>>> GetStatusAsync(
        Guid broadcasterId, CancellationToken cancellationToken = default);
}
```

### 3.2 `IOAuthProviderRegistry` — descriptors (the only place provider specifics live)

```csharp
namespace NomNomzBot.Application.Services.Integrations;

public interface IOAuthProviderRegistry
{
    // The descriptor for a provider, with deployment-profile-resolved credentials (BYOK self-host / platform SaaS).
    Result<OAuthProviderDescriptor> Resolve(string provider, Guid broadcasterId);
    IReadOnlyList<string> KnownProviders { get; }   // "spotify", "youtube" (+ future)
}

// Code/config record — NOT a DB row. Scope sets enumerate the FULL manageable surface; features request subsets.
public sealed record OAuthProviderDescriptor(
    string Provider,
    string AuthorizeEndpoint,
    string TokenEndpoint,
    string? RevokeEndpoint,                                  // null if the provider has no token revocation
    string AccountIdentityEndpoint,                          // "me"-style endpoint to read account id/name post-exchange
    bool UsesPkce,                                           // true for both Spotify + YouTube
    IReadOnlyDictionary<string, IReadOnlyList<string>> ScopeSets,  // scopeSetKey -> provider scope strings
    OAuthCredentials Credentials);                           // resolved per profile (BYOK vs platform)

public sealed record OAuthCredentials(string ClientId, string? ClientSecret, bool IsByok);
```

**Seeded scope sets (the descriptors ship with these):**

| Provider | Scope-set key | Provider scopes | Feature |
|---|---|---|---|
| spotify | `spotify.playback` | `user-read-playback-state` `user-modify-playback-state` `user-read-currently-playing` | now-playing + playback control (music-sr) |
| spotify | `spotify.library` | `playlist-read-private` `playlist-modify-public` `playlist-modify-private` `user-library-read` `user-library-modify` | playlist/library management (coverage rule) |
| youtube | `youtube.manage` | `https://www.googleapis.com/auth/youtube` | playlist/rating management (coverage rule) |
| youtube | `youtube.readonly` | `https://www.googleapis.com/auth/youtube.readonly` | read-only manage surface |

> YouTube **search** (music-SR queue source) needs **no** scope-set — it rides the app-level `YouTube:ApiKey` (config), so a channel can queue YouTube tracks **without** any per-user YouTube connect. Per-user `youtube.*` connect is only for managing the user's own YouTube.

---

## 4. DTOs / contracts

`NomNomzBot.Application/DTOs/Integrations/`.

```csharp
namespace NomNomzBot.Application.DTOs.Integrations;

public sealed record OAuthStartDto(string AuthorizeUrl, string State);   // client opens AuthorizeUrl

public sealed record OAuthCallbackParams(string? Code, string? State, string? Error, string? ErrorDescription);

public sealed record OAuthCallbackResultDto(
    string Provider, string ProviderAccountName, IReadOnlyList<string> GrantedScopeSets,
    string RedirectTarget);                                  // where to send the browser/app after connect

public sealed record IntegrationStatusDto(
    string Provider, bool Connected, string? AccountName,
    IReadOnlyList<string> GrantedScopeSets,
    IReadOnlyDictionary<string, bool> Capabilities,         // e.g. {"spotify.premium": true, "playback": true}
    bool NeedsReauth);
```

---

## 5. Controller endpoints

`IntegrationOAuthController` (`NomNomzBot.Api/Controllers/V1/`), `[ApiVersion("1.0")]`, `[Route("api/v{version:apiVersion}/integrations")]`, responses `StatusResponseDto<T>`. The connect/disconnect mutations gate on the streamer/Editor principal; the callback is a provider redirect (cannot carry the JWT — secured by the signed single-use `state`).

| Route | Verb | Request | Response | Auth |
|---|---|---|---|---|
| `/integrations/status` | GET | — | `StatusResponseDto<IReadOnlyList<IntegrationStatusDto>>` | `[Authorize]` · `integration:read` (Moderator 10) |
| `/integrations/{provider}/connect` | POST | `{ scopeSetKey, returnUrl? }` | `StatusResponseDto<OAuthStartDto>` | `[Authorize]` · `integration:write` (Editor 30) |
| `/integrations/{provider}/callback` | GET | `?code&state` (or `?error`) | `302` redirect to `RedirectTarget` (or app deep-link) | **Anonymous** — secured by signed single-use `state` (no JWT) |
| `/integrations/{provider}/disconnect` | POST | — | `StatusResponseDto<object>` | `[Authorize]` · `integration:write` (Editor 30) |

> New management action keys **`integration:read`** (Moderator 10) and **`integration:write`** (Editor 30), both `Low`, added to `roles-permissions.md §7.1`. Redirect URIs registered with each provider: `{App:BaseUrl}/api/v1/integrations/spotify/callback`, `.../youtube/callback` (computed at runtime, not configured).

---

## 6. Pipeline actions

**None here.** Provider *actions* (e.g. `play_music`, queue control) are owned by `music-sr.md`; this spec only establishes the connection they require.

---

## 7. DI registration

`AddIntegrationOAuth(this IServiceCollection, IConfiguration)` from `AddInfrastructure`.

| Interface | Impl | Lifetime | Notes |
|---|---|---|---|
| `IIntegrationOAuthService` | `IntegrationOAuthService` | Scoped | Uses `IHttpClientFactory` (+ `Microsoft.Extensions.Http.Resilience`) for token exchange/revocation; persists via identity-auth's `IIntegrationService`; state nonces in `ICacheService`. |
| `IOAuthProviderRegistry` | `OAuthProviderRegistry` | Singleton | Holds the Spotify/YouTube descriptors; resolves credentials per `IDeploymentProfileService` (BYOK vs platform `AppSetting`). |

No deployment-profile adapter pair — the flow is identical across profiles; only credential resolution (BYOK vs platform) differs, handled inside the registry.

---

## 8. Dependencies

| Dependency | Party | Use |
|---|---|---|
| `System.Net.Http` (`IHttpClientFactory`) + `Microsoft.Extensions.Http.Resilience` 10.7.0 | 1st/2nd | Provider token exchange / revocation / account-identity reads. |
| `System.Security.Cryptography` (`SHA256`, RNG) | 1st (in-box) | PKCE `S256` challenge + signed `state` nonce. |
| identity-auth `IIntegrationService` + `ISubjectKeyService`/`IFieldCipher` | 1st (this project) | Connection upsert + crypto-vaulted token storage. |
| `Newtonsoft.Json` | 3rd | Provider response parsing (app-JSON convention). |

**Not used:** any 3rd-party OAuth-client library — authorization-code + PKCE is a few in-box calls; no Duende/IdentityModel client needed for the relying-party token exchange.

---

## 9. Decisions (resolved)

- **This spec is connect-only**; tokens/connections/events are owned by identity-auth, the manage surface by music-sr. It closes the "Spotify/YouTube OAuth flow unspecced" gap without duplicating either.
- **Generic descriptor-driven flow** — one controller + service + registry; a new provider is a descriptor, not new code.
- **PKCE + signed single-use `state`**, runtime-computed redirect URIs, progressive scope-sets enumerating the full manageable surface per the coverage rule.
- **YouTube search rides an app-level API key** (no per-user connect); per-user `youtube.*` OAuth is only for managing the user's own YouTube. **Spotify is always per-user OAuth**, playback additionally needs Premium (surfaced as a capability).
- **Self-host BYOK** credentials supported via the existing `IsByok` connection flag; SaaS uses platform app credentials.
