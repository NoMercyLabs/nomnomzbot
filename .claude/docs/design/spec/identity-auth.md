# Identity-Auth — Interface Specification

**Status:** Implementable. Code from this directly. Grounded against the LOCKED schema
(`2026-06-16-database-schema.md`), onboarding / GDPR / deployment-profile / stack / decisions docs, and
the existing `server/src` code (extends, never duplicates or renames).

**Subsystem scope:** Users, Channels (tenant root), BotAccounts, OAuth token vault (per-tenant/per-subject
DEK envelope crypto, crypto-shred), AuthSessions + RefreshTokens, JWT issuance, and the
login / callback / refresh / logout HTTP surface. IPC dev-mode keys.

## Conventions (binding, applied throughout)

- Namespace `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way
  (never `.Result`/`.Wait()`). `Result<T>` over exceptions/null. Repository + `IUnitOfWork`, no raw
  `DbContext` in controllers. DI via typed interfaces, no MediatR, no Roslyn.
- Surrogate PKs are `Guid` via `Guid.CreateVersion7()` (app-side, never DB-default, never `Guid.NewGuid()`).
  Twitch ids are indexed attribute columns (`string(50)`), never keys/FKs.
- **Tenant key `BroadcasterId` is `Guid`.** This widens the existing `ITenantScoped.BroadcasterId`
  (`string` → `Guid`) and `DomainEvent.BroadcasterId` / `IDomainEvent.BroadcasterId` / `ICurrentTenantService`
  (`string?` → `Guid?`) — the one-time rebuild change locked by schema §1.1 (owner decision #1). All
  signatures below assume the widened types.
- Soft-delete via `IsDeleted`/`DeletedAt` + EF10 named global query filter. Append-only tables carry
  `CreatedAt` only.
- App JSON: **Newtonsoft.Json** (per task convention). Inbound API responses: `StatusResponseDto<T>` /
  `PaginatedResponse<T>`. Controllers: `[ApiVersion("1.0")]` `[Route("api/v{version:apiVersion}/...")]`.

> **Migration note (extends existing code).** This subsystem replaces three live shapes:
> the flat-token `Service` entity → `IntegrationConnections` + `IntegrationTokens` (Domain E vault);
> the `string`-keyed `User`/`Channel` with raw-Twitch-id PKs → `Guid` surrogate PKs + `TwitchUserId`/
> `TwitchChannelId` attribute columns; the `int`-keyed `ChannelBotAuthorization` → `Guid`-keyed
> `ChannelBotAuthorizations` (E.4) + `BotAccounts` (E.3). The existing `IAuthService` /
> `ITwitchAuthService` / `IJwtTokenService` interfaces are **extended in place**
> (same files, same names) per the signatures here — not forked. **Token-at-rest crypto** (DEK lifecycle +
> field AEAD) is **not** redefined here: it is consumed from `gdpr-crypto.md` (`ISubjectKeyService` +
> `IFieldCipher`), which supersedes the legacy `IEncryptionService` (see §3.5).

---

## 1. Entities (owned by this subsystem)

Defined authoritatively in `2026-06-16-database-schema.md`; referenced here, not redefined. Each owns its
`DbSet` and an EF `IEntityTypeConfiguration<T>` in Infrastructure.

| Entity | Schema | Base | Key fields (type) | Notes |
|---|---|---|---|---|
| `User` | A.1 | `SoftDeletableEntity` | `Id Guid` PK; `TwitchUserId string(50)` uniq idx; `Platform string(20)`; `Username/UsernameNormalized string(255)`; `DisplayName/NickName string(255)?`; `EmailCipher string(512)?`; `SubjectKeyId Guid?` FK→CryptoKey; `PronounId Guid?` FK→Pronouns; `IsPlatformPrincipal/IsBot/IsAnonymized/Enabled bool`; `LastSeenAt DateTime?` | **Rebuild:** `Id` `string`→`Guid`; raw Twitch id → `TwitchUserId`. `IsAdmin` bool → `IsPlatformPrincipal`. Email moves to `EmailCipher` (shred). |
| `Channel` | A.2 | `SoftDeletableEntity` | `Id Guid` PK (= tenant id); `OwnerUserId Guid` uniq FK→User; `TwitchChannelId string(50)` uniq idx; `Name/NameNormalized string(25)`; `Status string(20)`; `SuspendedAt DateTime?`; `SuspendedReason string(500)?`; `DeploymentMode string(20)`; `BillingTierKey string(20)`; `OverlayToken string(36)` uniq; `IsOnboarded/IsLive/Enabled bool` | **Rebuild:** `Id` `string`→`Guid`; `OwnerUserId` FK replaces the `[ForeignKey(nameof(Id))]` shared-PK hack. |
| `AuthSession` | A.3 | `BaseEntity` | `Id Guid` PK; `UserId Guid` FK→User; `BroadcasterId Guid?` FK→Channel; `ClientType string(20)`; `IpAddressCipher string(255)?`; `UserAgent string(512)?`; `LastSeenAt/ExpiresAt DateTime`; `RevokedAt DateTime?` | New. Live login per device; parent of refresh tokens. Tenant-scoped (`ITenantScoped`). |
| `RefreshToken` | A.4 | `BaseEntity` | `Id Guid` PK; `SessionId Guid` FK→AuthSession; `UserId Guid` FK→User; `TokenHash string(64)` uniq; `PreviousTokenHash string(64)?`; `IssuedAt/ExpiresAt DateTime`; `ConsumedAt/RevokedAt DateTime?`; `RevokedReason string(30)?` | New. Hashed, single-use, rotating. Idx `(UserId, RevokedAt)`. |
| `IpcDevModeKey` | A.5 | `SoftDeletableEntity` | `Id Guid` PK; `KeyHash string(64)` uniq; `Label string(100)?`; `IsEnabled bool`; `CreatedByUserId Guid?` FK→User; `ExpiresAt DateTime?` | New. Opt-in local-IPC gate (off by default, never remote). GLOBAL. |
| `IntegrationConnection` | E.1 | `SoftDeletableEntity` | `Id Guid` PK; `BroadcasterId Guid?` FK→Channel (null=platform/global); `Provider string(20)`; `ProviderAccountId string(255)?`; `ProviderAccountName string(255)?`; `Status string(20)`; `Scopes text` [VC:JSON `List<string>`]; `ClientId string(512)?`; `IsByok bool`; `Settings text?` [VC:JSON]; `ConnectedByUserId Guid?`; `ConnectedAt/LastRefreshedAt/LastErrorAt DateTime?`; `ConsecutiveFailureCount int` | **Replaces** `Service`. Uniq `(BroadcasterId, Provider, ProviderAccountId)`. |
| `IntegrationToken` | E.2 | `SoftDeletableEntity` | `Id Guid` PK; `ConnectionId Guid` FK→IntegrationConnection; `BroadcasterId Guid?` FK→Channel (denorm RLS); `TokenType string(10)` (`access`/`refresh`/`app`); `CipherText text`; `Nonce string(64)?`; `EncryptionKeyId Guid` FK→CryptoKey; `ExpiresAt/RotatedAt DateTime?` | **Replaces** `Service.AccessToken/RefreshToken`. Uniq `(ConnectionId, TokenType)`. Ciphertext = `[PII-shred]`. |
| `BotAccount` | E.3 | `SoftDeletableEntity` | `Id Guid` PK; `IdentityType string(10)` (`shared`/`custom`); `Platform string(20)`; `BotUserId string(50)` uniq; `BotUsername string(255)`; `ConnectionId Guid?` FK→IntegrationConnection; `IsActive bool` | New. GLOBAL. Idx `(Platform, IdentityType)`. One shared + optional per-channel custom. |
| `ChannelBotAuthorization` | E.4 | `SoftDeletableEntity` | `Id Guid` PK (was `int`); `BroadcasterId Guid` FK→Channel; `BotAccountId Guid` FK→BotAccount; `AuthorizedAt DateTime`; `AuthorizedByUserId Guid?`; `BotJoinedAt DateTime?`; `IsActive bool` | **Replaces** the `int`-keyed entity. Uniq `(BroadcasterId, BotAccountId)`. |
| `CryptoKey` | Q.1 | `BaseEntity` | `Id Guid` PK; `KeyScope string(20)` (`tenant`/`subject`/`platform`); `BroadcasterId Guid?` FK→Channel; `SubjectIdHash string(64)?`; `WrappedKeyMaterial text?`; `KekReference string(255)?`; `Provider string(20)` (`kms_envelope`/`local_aes`); `Algorithm string(30)`; `Status string(20)` (`active`/`rotating`/`destroyed`); `DestroyedAt DateTime?`; `ErasureRequestId Guid?`; `RotatedFromKeyId Guid?` FK→CryptoKey | **Co-owned with crypto/GDPR subsystem.** This subsystem owns the *vault read/write/shred* surface; the GDPR subsystem owns erasure orchestration. Mixed scope (platform rows have no `BroadcasterId`). |

`Pronoun` (R.1) and `UserPreferences` (R.2) are referenced (FK target / per-user prefs) but owned by the
lookups/preferences subsystem — not redefined here.

**Enum value sets (stored as `string`, [VC:enum]):**
`Platform` = `twitch|kick|youtube`. `Channel.Status` = `active|suspended|churned|platform_banned`.
`Channel.DeploymentMode` = `saas|self_host_lite|self_host_full`. `AuthSession.ClientType` =
`web|desktop|mobile|ipc_dev`. `RefreshToken.RevokedReason` = `logout|rotation|reuse_detected|erasure|admin`.
`IntegrationConnection.Provider` = `twitch|spotify|discord|youtube|azure_tts|elevenlabs`.
`IntegrationConnection.Status` = `connected|expired|revoked|needs_reauth|pending`.
`IntegrationToken.TokenType` = `access|refresh|app`. `BotAccount.IdentityType` = `shared|custom`.
`CryptoKey.KeyScope` = `tenant|subject|platform`. `CryptoKey.Provider` = `kms_envelope|local_aes`.

---

## 2. Domain events

Namespace `NomNomzBot.Domain.Events`. Every event is a `sealed record` inheriting the canonical
`DomainEventBase` (`platform-conventions.md` §2.0): `Guid EventId` (UUIDv7), `DateTimeOffset OccurredAt`,
`Guid BroadcasterId`. Events **inherit** `EventId` / `OccurredAt` / `BroadcasterId` from the base and **must
NOT redeclare them** (the inherited `init` properties are set by the publisher); each record adds only its own
payload fields. Published via `IEventBus`. **Tenant-scoped events** set the inherited `BroadcasterId` to the
owning channel; **platform-scoped events** leave it at `Guid.Empty` — the canonical platform-level sentinel per
`DomainEventBase`, **not `null`**. The platform-bot events below (`BotAccountAuthorizedEvent` for the shared
bot, etc.) carry `Guid.Empty`.

```csharp
public sealed record UserRegisteredEvent(Guid UserId, string TwitchUserId, string Username, string Platform) : DomainEventBase;
public sealed record UserLoggedInEvent(Guid UserId, Guid SessionId, string ClientType) : DomainEventBase;
public sealed record UserLoggedOutEvent(Guid UserId, Guid SessionId, string Reason) : DomainEventBase;

public sealed record ChannelOnboardedEvent(Guid OwnerUserId, string TwitchChannelId, string Name) : DomainEventBase;
public sealed record ChannelSuspendedEvent(string Status, string? Reason, Guid? ActorUserId) : DomainEventBase;
public sealed record ChannelReinstatedEvent(Guid? ActorUserId) : DomainEventBase;

public sealed record IntegrationConnectedEvent(Guid ConnectionId, string Provider, string ProviderAccountId) : DomainEventBase;
public sealed record IntegrationDisconnectedEvent(Guid ConnectionId, string Provider, string Reason) : DomainEventBase;
public sealed record IntegrationTokenRefreshedEvent(Guid ConnectionId, string Provider, DateTime ExpiresAt) : DomainEventBase;
public sealed record IntegrationNeedsReauthEvent(Guid ConnectionId, string Provider, int ConsecutiveFailureCount) : DomainEventBase;

public sealed record BotAccountAuthorizedEvent(Guid BotAccountId, string IdentityType, string BotUsername) : DomainEventBase;
public sealed record BotAccountDisconnectedEvent(Guid BotAccountId, string Reason) : DomainEventBase;

public sealed record RefreshTokenReuseDetectedEvent(Guid UserId, Guid SessionId, string TokenHash) : DomainEventBase;
public sealed record CryptoKeyShreddedEvent(Guid CryptoKeyId, string KeyScope, Guid? ErasureRequestId) : DomainEventBase;
```

> `IntegrationConnectedEvent` / `IntegrationDisconnectedEvent` already exist (live, `string`-keyed). Widen
> their fields to the shapes above; do not create parallel records.

---

## 3. Service interfaces

All in `NomNomzBot.Application`. One responsibility per interface. Constructor-injected, called directly.

### 3.1 `IAuthService` — extend `Services/IAuthService.cs`

Keep the existing OAuth-URL + callback + bot methods; widen ids to `Guid`. Add session-aware login/logout.

```csharp
namespace NomNomzBot.Application.Services;

public interface IAuthService
{
    // ── User OAuth (existing — widened) ──────────────────────────────────────
    Task<string> GetTwitchOAuthUrl(string? state = null, string? baseUrl = null, CancellationToken ct = default);
    // Behavior: builds the Twitch authorize URL with progressive streamer scopes. No state change.

    Task<Result<AuthResultDto>> HandleTwitchCallbackAsync(OAuthCallbackDto callback, AuthContextDto context, CancellationToken ct = default);
    // Behavior: exchanges code→Twitch tokens; upserts User (by TwitchUserId) + Channel (tenant root, IsOnboarded on first login);
    // vaults the user's Twitch tokens via IIntegrationTokenVault; opens an AuthSession; issues JWT + rotating RefreshToken.
    // Emits UserRegisteredEvent (first time), ChannelOnboardedEvent (first onboarding), UserLoggedInEvent.

    Task<Result<AuthResultDto>> RefreshTokenAsync(string refreshToken, AuthContextDto context, CancellationToken ct = default);
    // Behavior: validates+consumes the presented refresh token (single-use rotation); on reuse of a consumed/revoked token,
    // revokes the whole session lineage and emits RefreshTokenReuseDetectedEvent. On success issues a new JWT + RefreshToken.

    Task<Result> LogoutAsync(Guid userId, Guid sessionId, CancellationToken ct = default);
    // Behavior: revokes the AuthSession + all its refresh tokens (RevokedReason=logout). Emits UserLoggedOutEvent. Vault untouched.

    // ── Platform bot (NomNomzBot) — IsPlatformPrincipal gate, BroadcasterId=null ──
    Task<string> GetTwitchBotOAuthUrl(string? state = null, string? baseUrl = null, CancellationToken ct = default);
    Task<Result<BotStatusDto>> HandleTwitchBotCallbackAsync(OAuthCallbackDto callback, CancellationToken ct = default);
    // Behavior: exchanges code; upserts the shared BotAccount (IdentityType=shared); vaults its tokens under a platform IntegrationConnection (BroadcasterId=null).
    // Emits BotAccountAuthorizedEvent (BroadcasterId=Guid.Empty/platform sentinel).
    Task<Result<BotStatusDto>> GetBotStatusAsync(CancellationToken ct = default);
    Task<Result> DisconnectBotAsync(CancellationToken ct = default);
    // Behavior: marks the shared BotAccount inactive; revokes vaulted bot tokens. Emits BotAccountDisconnectedEvent.

    // ── Custom (white-label) bot — per-channel, custom-bot-name entitlement (monetization.md), BroadcasterId=channelId.
    //    The shared platform BotAccount (IdentityType=shared) is the Base default for every channel; a custom
    //    per-channel bot identity is gated on IBillingTierService.AllowsCustomBotName (tier N.1), not a tier-key compare. ──
    Task<string> GetTwitchChannelBotOAuthUrl(Guid broadcasterId, string? state = null, string? baseUrl = null, CancellationToken ct = default);
    Task<Result<BotStatusDto>> HandleTwitchChannelBotCallbackAsync(Guid broadcasterId, OAuthCallbackDto callback, CancellationToken ct = default);
    // Behavior: exchanges code; upserts a custom BotAccount + a ChannelBotAuthorization (BroadcasterId, BotAccountId); vaults tokens.
    // Emits BotAccountAuthorizedEvent.
    Task<Result<BotStatusDto>> GetChannelBotStatusAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result> DisconnectChannelBotAsync(Guid broadcasterId, CancellationToken ct = default);
    // Behavior: deactivates the ChannelBotAuthorization; revokes that bot's vaulted tokens. Emits BotAccountDisconnectedEvent.
}
```

### 3.2 `IJwtTokenService` — extend `Common/Interfaces/IJwtTokenService.cs`

Widen `userId` to `Guid`; add the resolved tenant + session to the issued claims. Asymmetric-signing-ready
(RS256/ES256 per decisions doc #4 — the impl chooses the key; signature unchanged).

```csharp
namespace NomNomzBot.Application.Common.Interfaces;

public interface IJwtTokenService
{
    string GenerateAccessToken(Guid userId, string username, Guid? broadcasterId, Guid sessionId, IEnumerable<string>? roles = null);
    // Behavior: mints a short-lived access JWT (sub=userId, tenant=broadcasterId, sid=sessionId). No persistence. Pure.

    string GenerateRefreshTokenValue();
    // Behavior: returns a cryptographically-random opaque refresh token string (RNG). The CALLER hashes+persists it; the JWT layer never stores it.

    ClaimsPrincipal? ValidateAccessToken(string token);
    // Behavior: validates signature+lifetime+issuer/audience; returns the principal or null. No state change.
}
```

> `GenerateRefreshToken(userId, username)` (current) is **removed** — refresh tokens are now opaque random
> values hashed into `RefreshTokens`, not self-describing JWTs. `GenerateToken` → `GenerateAccessToken`.

### 3.3 `ISessionService` — new, `Common/Interfaces/ISessionService.cs`

Owns the `AuthSessions` + `RefreshTokens` lifecycle (extracted from `IAuthService` for single
responsibility; `IAuthService` calls it).

```csharp
namespace NomNomzBot.Application.Common.Interfaces;

public interface ISessionService
{
    Task<Result<SessionTokensDto>> CreateSessionAsync(Guid userId, Guid? broadcasterId, AuthContextDto context, CancellationToken ct = default);
    // Behavior: inserts an AuthSession + an initial hashed RefreshToken; returns the access JWT + raw refresh token (raw value returned once, only the hash persisted).

    Task<Result<SessionTokensDto>> RotateAsync(string rawRefreshToken, AuthContextDto context, CancellationToken ct = default);
    // Behavior: looks up by TokenHash; if already ConsumedAt/RevokedAt → reuse: revoke the lineage, emit RefreshTokenReuseDetectedEvent, return failure.
    // Else marks it ConsumedAt, inserts a successor (PreviousTokenHash set), returns fresh tokens. Single-use chain.

    Task<Result> RevokeSessionAsync(Guid sessionId, string reason, CancellationToken ct = default);
    // Behavior: sets AuthSession.RevokedAt and revokes all its non-revoked RefreshTokens with the reason.

    Task<Result<int>> RevokeAllForUserAsync(Guid userId, string reason, CancellationToken ct = default);
    // Behavior: revokes every active session + refresh token for the user (used by logout-all and erasure). Returns count revoked. Uses the (UserId, RevokedAt) index.

    Task<Result<AuthSessionDto>> ValidateSessionAsync(Guid sessionId, CancellationToken ct = default);
    // Behavior: returns the session if not revoked/expired; updates LastSeenAt. Failure if revoked/expired/missing.
}
```

### 3.4 `IIntegrationTokenVault` — new, `Contracts/Identity/IIntegrationTokenVault.cs`

The crypto-shred-ready OAuth token vault (Domain E + Q). **Replaces** all direct
`Service.AccessToken`/`RefreshToken` access. Sits over the canonical crypto primitives owned by
`gdpr-crypto.md` — `IFieldCipher` (AES-256-GCM AEAD) + `ISubjectKeyService` (DEK lifecycle); see §3.5.

```csharp
namespace NomNomzBot.Application.Contracts.Identity;

public interface IIntegrationTokenVault
{
    Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(UpsertConnectionDto request, CancellationToken ct = default);
    // Behavior: upserts an IntegrationConnection (by BroadcasterId+Provider+ProviderAccountId); does NOT store secrets. Emits IntegrationConnectedEvent on first connect.

    Task<Result> StoreTokensAsync(Guid connectionId, StoreTokensDto tokens, CancellationToken ct = default);
    // Behavior: AES-256-GCM-encrypts access/refresh/app tokens (AAD = tenantId‖provider‖tokenType‖keyVersion) via IFieldCipher under the connection's tenant/subject DEK
    // (created via ISubjectKeyService if absent — see gdpr-crypto.md §3.4); upserts IntegrationTokens rows; sets Status=connected, resets ConsecutiveFailureCount. Emits IntegrationTokenRefreshedEvent.

    Task<Result<DecryptedTokenDto>> GetAccessTokenAsync(Guid connectionId, CancellationToken ct = default);
    // Behavior: decrypts the access token; if expired and a refresh token exists, returns the stored (still-encrypted-at-rest) value without auto-refresh (refresh is the provider service's job). Failure if the DEK is destroyed (crypto-shredded).

    Task<Result<DecryptedTokenDto>> GetRefreshTokenAsync(Guid connectionId, CancellationToken ct = default);
    // Behavior: decrypts the refresh token for a provider-side refresh call. Same shred-failure semantics.

    Task<Result> MarkRefreshFailureAsync(Guid connectionId, string error, CancellationToken ct = default);
    // Behavior: increments ConsecutiveFailureCount, stamps LastErrorAt; at threshold sets Status=needs_reauth and emits IntegrationNeedsReauthEvent (drives backoff/re-auth UI).

    Task<Result> RevokeConnectionAsync(Guid connectionId, string reason, CancellationToken ct = default);
    // Behavior: soft-deletes IntegrationTokens, sets Status=revoked; best-effort provider-side token revoke. Emits IntegrationDisconnectedEvent. Does NOT destroy the DEK (other rows may share it).

    Task<Result<IReadOnlyList<IntegrationConnectionDto>>> ListConnectionsAsync(Guid? broadcasterId, CancellationToken ct = default);
    // Behavior: lists connections for a tenant (or platform/global when null). Read-only; never returns ciphertext.
}
```

### 3.4a `IScopeGrantService` — progressive scopes: grant-aware enable + drop detection

Progressive scopes are **grant-aware**: enabling a feature triggers **no OAuth** when the scopes it needs are already on the connection, and a **dropped** scope degrades only the features that needed it — never a blind re-auth. `IntegrationConnection.Scopes` is the stored grant set; this service keeps it truthful and gates feature enablement on it.

```csharp
namespace NomNomzBot.Application.Services;

public interface IScopeGrantService
{
    // The Twitch scopes a feature requires (static FeatureScopeMap registry). Pure lookup.
    IReadOnlyList<string> RequiredScopesFor(string featureKey);

    // Decides whether enabling `featureKey` needs user interaction.
    //  • RequiredScopesFor(feature) ⊆ connection.Scopes  → AlreadyGranted=true, no URL: caller enables now, ZERO OAuth.
    //  • otherwise → AlreadyGranted=false + an authorize URL requesting (connection.Scopes ∪ required), so the user
    //    consents once to just the delta. Twitch skips the consent screen when all requested scopes are already
    //    authorized (force_verify defaults to false), so the common path is silent even here.
    Task<Result<ScopeGrantState>> EnsureFeatureScopesAsync(Guid broadcasterId, string featureKey, string? baseUrl = null, CancellationToken ct = default);

    // Reconciles stored Scopes to the AUTHORITATIVE granted set from a token response or
    // GET id.twitch.tv/oauth2/validate. Called on EVERY token store/refresh (and an optional periodic validate
    // sweep to catch out-of-band changes between refreshes).
    //  dropped = previousScopes \ actualScopes; if non-empty → emits ScopesDroppedEvent and disables every feature
    //  whose RequiredScopesFor is no longer satisfied (a removed scope silently degrades, never silently keeps
    //  calling a now-forbidden endpoint). Returns the dropped scopes. Distinct from MarkRefreshFailureAsync →
    //  needs_reauth (token unusable); this is a still-valid token that simply lost a scope.
    Task<Result<IReadOnlyList<string>>> ReconcileGrantedScopesAsync(Guid connectionId, IReadOnlyList<string> actualScopes, CancellationToken ct = default);
}

public sealed record ScopeGrantState(bool AlreadyGranted, string? IncrementalAuthorizeUrl, IReadOnlyList<string> MissingScopes);
```

**Wiring (binding):** `IIntegrationTokenVault.StoreTokensAsync` calls `ReconcileGrantedScopesAsync` with the token's `scope` set on every store/refresh, so `Scopes` is never stale. `ScopesDroppedEvent(Guid BroadcasterId, string Provider, IReadOnlyList<string> DroppedScopes, IReadOnlyList<string> DisabledFeatures)` joins the §2 domain-event catalogue (handlers: disable the affected feature toggles + surface a "reconnect to restore X" prompt in the dashboard). Twitch exposes **no per-scope revoke** — a full app disconnect invalidates the token and flows through the refresh-failure → `needs_reauth` path; a partial scope loss arises only from re-authorizing with a narrower set or a Twitch scope deprecation, both caught by reconciliation.

### 3.5 DEK lifecycle + field AEAD — **consumed, not redefined** (owner: `gdpr-crypto.md`)

> **Single owner: `gdpr-crypto.md` §3.** The per-subject/tenant DEK lifecycle (`CryptoKey`) and the AEAD
> data-plane primitive are authored there; this subsystem **consumes** them and does not redefine a parallel
> interface. The canonical types (see `gdpr-crypto.md` §3.2/§3.4):
> - **`ISubjectKeyService`** (`NomNomzBot.Application.Services`) — per-subject/tenant DEK create/get/rotate/destroy
>   lifecycle over `CryptoKey` + `KeyUsageBinding`. Canonical members: `GetOrCreateSubjectKeyAsync(Guid subjectUserId,
>   string subjectIdHash, …)`, `GetOrCreateTenantKeyAsync(Guid broadcasterId, …)`, `GetOrCreatePlatformKeyAsync`,
>   `ProtectAsync`/`UnprotectAsync`, `RotateKeyAsync`, **`DestroyKeyAsync(Guid cryptoKeyId, Guid erasureRequestId, …)`**
>   (the O(1) crypto-shred — replaces the earlier `ICryptoKeyService.ShredAsync` draft), `ResolveSubjectKeysAsync`.
> - **`IFieldCipher`** (`NomNomzBot.Application.Common.Interfaces.Crypto`) — AES-256-GCM AEAD with AAD =
>   `tenantId‖provider‖tokenType‖keyVersion`; the single AEAD primitive (replaces the legacy
>   `IEncryptionService` AES-CBC defect). `IKeyVault` (`local_aes` / `kms_envelope` KEK custody) and `IKdf`
>   are its siblings, also owned by `gdpr-crypto.md`.
>
> The `IIntegrationTokenVault` (§3.4) builds on `ISubjectKeyService.ProtectAsync`/`UnprotectAsync` (DEK lifecycle)
> + `IFieldCipher` (AEAD); there is **one** AEAD primitive across the platform. The legacy `IEncryptionService`
> is retained only as a transitional read-shim during token re-encryption migration, then retired (per
> `gdpr-crypto.md` §9).

### 3.6 `ICurrentTenantService` / `ICurrentUserService` — widen (existing)

```csharp
public interface ICurrentTenantService { Guid? BroadcasterId { get; } void SetTenant(Guid broadcasterId); }
public interface ICurrentUserService   { Guid? UserId { get; } string? Username { get; } bool IsAuthenticated { get; } bool IsPlatformPrincipal { get; } }
```
Behavior unchanged; ids widened `string?`→`Guid?`; `IsPlatformPrincipal` added (replaces the old `IsAdmin`
read, sourced from the JWT/`User.IsPlatformPrincipal`).

---

## 4. DTOs / contracts

`AuthDtos.cs` (`NomNomzBot.Application.DTOs.Auth`) — extend existing:

```csharp
public sealed record AuthResultDto(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);          // existing — User.Id now Guid-as-string
public sealed record OAuthCallbackDto { public required string Code { get; init; } public string? State { get; init; } public string? RedirectUri { get; init; } } // existing
public sealed record RefreshTokenRequest(string RefreshToken);                                                          // existing

public sealed record AuthContextDto(string ClientType, string? IpAddress, string? UserAgent);                          // new — request fingerprint for the session
public sealed record SessionTokensDto(string AccessToken, string RawRefreshToken, DateTime AccessExpiresAt, DateTime RefreshExpiresAt, Guid SessionId);
public sealed record AuthSessionDto(Guid Id, Guid UserId, Guid? BroadcasterId, string ClientType, DateTime LastSeenAt, DateTime ExpiresAt, bool IsRevoked);
public sealed record BotStatusDto(bool Connected, string? Login, string? DisplayName, string? ProfileImageUrl);        // existing (already in IAuthService.cs)
```

`IntegrationDtos.cs` (`NomNomzBot.Application.Contracts.Identity`) — new:

```csharp
public sealed record UpsertConnectionDto(Guid? BroadcasterId, string Provider, string? ProviderAccountId, string? ProviderAccountName, IReadOnlyList<string> Scopes, string? ClientId, bool IsByok, Guid? ConnectedByUserId, string? SettingsJson);
public sealed record StoreTokensDto(string AccessToken, string? RefreshToken, string? AppToken, DateTime? AccessExpiresAt);
public sealed record DecryptedTokenDto(string Value, string TokenType, DateTime? ExpiresAt, bool IsExpired);
public sealed record IntegrationConnectionDto(Guid Id, Guid? BroadcasterId, string Provider, string? ProviderAccountId, string? ProviderAccountName, string Status, IReadOnlyList<string> Scopes, bool IsByok, DateTime? ConnectedAt, DateTime? LastRefreshedAt, int ConsecutiveFailureCount);
```

`ITwitchAuthService` (`Contracts/Twitch`) — keep as the low-level Twitch HTTP exchange; widen
`broadcasterId` to `Guid` and route token storage through `IIntegrationTokenVault` instead of
`Service.AccessToken`:

```csharp
public interface ITwitchAuthService
{
    Task<TokenResult?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);
    Task<TokenResult?> RefreshTokenAsync(Guid broadcasterId, string provider, CancellationToken ct = default);
    Task RefreshExpiringTokensAsync(CancellationToken ct = default);
    Task RevokeTokenAsync(Guid broadcasterId, string provider, CancellationToken ct = default);
}
public record TokenResult(string AccessToken, string RefreshToken, DateTime ExpiresAt, string[] Scopes); // existing
```

---

## 5. Controller endpoints

`AuthController` (`api/v1/auth`, `[ApiVersion("1.0")]`, `: BaseController`) — extends the existing
controller. Auth plane = **platform JWT**; the per-action floor is in the gate column.

**Role gate.** Two distinct authorization planes; a row uses exactly one.

- **Management plane (Gate-2 — `ActionDefinitions`).** Gate 1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate 2's). Gate 2 = `IActionAuthorizationService.AuthorizeActionAsync(userId,
  broadcasterId, actionKey)` enforces the per-route floor named in the gate column before the service call
  (403 FORBIDDEN when below). The `actionKey`s are seeded global **`ActionDefinition`s** (schema B.3); a
  broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`.
- **Platform plane (Plane-C — `IamPermissions`).** Plane-C rows gate on
  `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, …)`, where `permissionKey` is a
  seeded global **`IamPermission`** (schema C.1) — a different table and key namespace from `ActionDefinitions`.
  The ASP.NET `[Authorize(Policy="<key>")]` policy-name IS that `IamPermission.Key` verbatim. No
  `ChannelActionOverride`/`FloorLevel` applies (platform scope, not tenant).

`[AllowAnonymous]` rows are the OAuth handshake (neither gate).

| Route | Verb | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `auth/me` | GET | — | `StatusResponseDto<CurrentUserDto>` | — (any authenticated user, own session) |
| `auth/twitch` | GET | `?redirect_uri` | 302 → Twitch | — (OAuth handshake, rate-limited `auth`) |
| `auth/twitch/callback` | GET | `?code&state` | 302 deep-link / `StatusResponseDto<object>` (tokens+user) | — (OAuth handshake) |
| `auth/twitch/callback` | POST | `OAuthCallbackDto` | `StatusResponseDto<object>` (tokens+user) | — (OAuth handshake, SPA/mobile code exchange) |
| `auth/refresh` | POST | `RefreshTokenRequest` | `StatusResponseDto<object>` (rotated tokens+user) | — (OAuth handshake, refresh token in body) |
| `auth/logout` | POST | — (session from JWT) | `StatusResponseDto<object>` | — (any authenticated user, own session) |
| `auth/logout/all` | POST | — | `StatusResponseDto<object>` | — (any authenticated user, revokes all own sessions) |
| `auth/twitch/bot` | GET | `?redirect_uri` | 302 → Twitch | platform · `iam:manage` (platform-shared bot, `BroadcasterId=null`) |
| `auth/twitch/bot/callback` | GET | `?code&state` | HTML success / 302 | — (OAuth handshake; principal proven via state) |
| `auth/twitch/bot/status` | GET | — | `StatusResponseDto<BotStatusDto>` | platform · `iam:manage` (platform-shared bot) |
| `auth/twitch/bot` | DELETE | — | `StatusResponseDto<object>` | platform · `iam:manage` (platform-shared bot) |

`ChannelBotController` (`api/v1/channels/{broadcasterId:guid}/bot`) — custom (white-label) bot, **tenant
plane** (Plane B management ladder). A custom per-channel bot identity is gated on the
**`IBillingTierService.AllowsCustomBotName`** entitlement (tier N.1; cross-reference `monetization.md`) — the
resolved tier flag, never a tier-key string compare. The shared platform bot is the **Base default** every
channel gets. The connect route therefore returns `403 FORBIDDEN` (entitlement) when
`AllowsCustomBotName` is false for the channel's resolved tier. Same Gate-1/Gate-2 mechanism as above:

| Route | Verb | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `.../bot/twitch` | GET | `?redirect_uri` | 302 → Twitch | management / Broadcaster · `channelbot:connect` (route `broadcasterId` == resolved tenant) |
| `.../bot/callback` | GET | `?code&state` | 302 dashboard | — (OAuth handshake) |
| `.../bot/status` | GET | — | `StatusResponseDto<BotStatusDto>` | management / Broadcaster · `channelbot:read` |
| `.../bot` | DELETE | — | `StatusResponseDto<object>` | management / Broadcaster · `channelbot:disconnect` |

> All ids in routes/bodies are now `Guid`; `[FromRoute] Guid broadcasterId`. Tenant is resolved from the
> JWT `tenant` claim by `TenantResolutionMiddleware`, never from request input (closes the live cross-tenant
> IDOR — stack doc §Sandbox blocker). The route `broadcasterId` MUST equal the resolved tenant or 403.

---

## 6. Pipeline actions

**None.** Identity-auth owns no pipeline actions, conditions, or template variables.

---

## 7. DI registration

Registered in `InfrastructureServiceExtensions` (Infrastructure) and `ServiceCollectionExtensions` (Api).
Lifetimes match the existing convention (request-scoped services, singleton crypto primitive).

| Interface | Implementation | Lifetime | Profile adapter |
|---|---|---|---|
| `IAuthService` | `AuthService` | Scoped | — |
| `ISessionService` | `SessionService` | Scoped | — |
| `IJwtTokenService` | `JwtTokenService` | Singleton | **profile:** signing key HS256 (single-user self-host) / RS256/ES256 + JWKS (federation/SSO path, decisions #4) — selected by config behind the unchanged interface |
| `ICurrentUserService` | `CurrentUserService` (HttpContext) | Scoped | — |
| `ICurrentTenantService` | `CurrentTenantService` | Scoped | — |
| `IIntegrationTokenVault` | `IntegrationTokenVault` | Scoped | consumes `ISubjectKeyService` + `IFieldCipher` (below) |
| `ISubjectKeyService` / `IFieldCipher` / `IKeyVault` / `IKdf` | **registered by `gdpr-crypto.md` §7** | Scoped (DEK service) / Singleton (primitives) | **Not registered here.** DEK lifecycle + AEAD + KEK custody (`local_aes` / `kms_envelope`, profile-selected) are owned by `gdpr-crypto.md`; this subsystem only consumes them. |
| `ITwitchAuthService` | `TwitchAuthService` | Scoped | — |
| `IUnitOfWork` | `UnitOfWork` (over `ApplicationDbContext`) | Scoped | **profile:** DB provider Npgsql (Postgres/SaaS) / Sqlite (lite) selected in the DbContext registration |
| `IEventBus` | `EventBus` **or** `RedisEventBus` | Singleton | **profile:** in-memory (lite) / Redis (SaaS) |

EF: each owned entity gets an `IEntityTypeConfiguration<T>` (keys, indexes, uniques, `[VC:JSON]`/`[VC:enum]`
converters, soft-delete + tenant named query filters). `User`, `Channel`, `IntegrationConnection`,
`BotAccount`, `ChannelBotAuthorization`, `IntegrationToken`, `IpcDevModeKey`, `CryptoKey` get
`UsernameNormalized`/`NameNormalized`/`*Hash` unique indexes per schema. Tenant-scoped entities implement
`ITenantScoped` (`AuthSession`, `IntegrationConnection`, `IntegrationToken`, `ChannelBotAuthorization`,
tenant-scope `CryptoKey`); `IpcDevModeKey`, `BotAccount`, platform `IntegrationConnection`, platform
`CryptoKey` are GLOBAL (no filter).

---

## 8. Dependencies (from the stack doc)

- **Microsoft.IdentityModel.JsonWebTokens (+ .Tokens) 8.19.1** — JWT create/validate via
  `JsonWebTokenHandler` (replaces legacy `System.IdentityModel.Tokens.Jwt`).
- **Microsoft.AspNetCore.Authentication.JwtBearer 10.0.9** — inbound JWT resource-server validation.
- **Microsoft.AspNetCore.Authentication.OpenIdConnect 10.0.x** — OIDC *client* only; it is a dependency of
  the federation/SSO path and is absent from the single-user self-host path (decisions #3).
- **System.Security.Cryptography**, **System.Security.Cryptography.ProtectedData 10.0.9** (DPAPI KEK-at-rest),
  **Azure.Security.KeyVault.Keys 4.10.0** (SaaS KEK custody) — the AEAD/DEK/KEK primitives are owned and
  registered by `gdpr-crypto.md` (`IFieldCipher`/`IKdf`/`IKeyVault`/`ISubjectKeyService`); this subsystem
  consumes them and does not pull these packages itself.
- **Microsoft.AspNetCore.DataProtection ≥ 10.0.7** — cookie/token protection (CVE-2026-40372; DB-backed key
  ring, rotate on deploy for Linux/SaaS — decisions #9).
- **EF Core 10 (Microsoft.EntityFrameworkCore 10.0.9)** + **Npgsql.EntityFrameworkCore.PostgreSQL 10.0.2**
  (SaaS) / **Microsoft.EntityFrameworkCore.Sqlite 10.0.9** (lite) — named query filters for soft-delete +
  tenant; hand-rolled `ValueConverter`+`ValueComparer` for `[VC:JSON]`/`[VC:enum]`.
- **Newtonsoft.Json** — app JSON serialization of `[VC:JSON]` columns and DTO payloads (per task convention).
- **OpenIddict 7.5.0** — the OIDC issuer for the federation/multi-user-SSO path (decisions #3); it is a
  dependency of that path and is absent from the basic single-user self-host path, which runs JWT
  resource-server validation with no issuer.
- **Microsoft.Extensions.Http.Resilience 10.7.0** — retry/breaker on the Twitch token-exchange `HttpClient`
  (via `ITwitchAuthService`).

---

## 9. Decisions (resolved)

- **JWT signing algorithm.** This subsystem ships HS256 signing on the basic single-user self-host path and
  uses RS256/ES256 on the federation/SSO path (decisions #4). The `IJwtTokenService` signature is
  signing-algorithm-agnostic, so the asymmetric path is selected by impl/config behind the same interface —
  no signature change. Asymmetric signing (RS256/ES256 + published JWKS) is a build dependency of the
  federation/SSO subsystem (`federation-oidc.md`) and is in place before federation runs; that ordering is a
  dependency, owned by the task board, not a property of this surface.
- **Crypto-shred scope.** This subsystem's vault uses O(1) DEK-destroy crypto-shred for `[PII-shred]`
  ciphertext, which fully covers its surface (the OAuth token vault — §3.4). Row-level scrub of `[PII-scrub]`
  snapshots and multi-subject-event keying are owned by the GDPR subsystem (`gdpr-crypto.md` §3.5/§3.7), not
  this one (decisions #10); this subsystem depends on that subsystem's `ISubjectKeyService.DestroyKeyAsync`
  for the shred call.
- **Custom bot identity is an entitlement, gated by `AllowsCustomBotName` (binding).** Every channel runs on
  the **shared platform `BotAccount`** (`IdentityType=shared`) by default (the Base tier identity). A **custom
  per-channel (white-label) bot** is gated on **`IBillingTierService.AllowsCustomBotName`** (the resolved tier
  flag, `BillingTier` N.1; cross-referenced to `monetization.md` for the tier matrix) — **not** a hard-coded
  "Pro+" tier-key string comparison. `ChannelBotController` connect (§5) is the gated surface; when
  `AllowsCustomBotName` is false it returns `403 FORBIDDEN`.
