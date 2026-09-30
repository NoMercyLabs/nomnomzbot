# Identity-Auth — Interface Specification

**Status:** Implementable. Code from this directly. Grounded against the LOCKED schema
(`2026-06-16-database-schema.md`), onboarding / GDPR / deployment-profile / stack / decisions docs, and
the existing `server/src` code (extends, never duplicates or renames).

**Subsystem scope:** Users, Channels (tenant root — the streamer's ONE channel spanning platforms; each platform's presence is a `PlatformConnection` under it, `platform-identity.md` §0/§9.4), BotAccounts, OAuth token vault (per-tenant/per-subject
DEK envelope crypto, crypto-shred), AuthSessions + RefreshTokens, JWT issuance, and the
login / callback / refresh / logout HTTP surface. IPC dev-mode keys.

## Conventions (binding, applied throughout)

- Namespace `NomNomzBot.*`. File-scoped namespaces, `Nullable` enabled, async all the way
  (never `.Result`/`.Wait()`). `Result<T>` over exceptions/null. Repository + `IUnitOfWork`, no raw
  `DbContext` in controllers. DI via typed interfaces, no MediatR, no Roslyn.
- Surrogate PKs are `Guid`; new entities use `Guid.CreateVersion7()` (app-side, never DB-default). Some legacy `Guid.NewGuid()` calls remain (e.g. `Service.Id`, `Channel.OverlayToken`, a few Infrastructure id generators).
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
> `IFieldCipher`); the legacy `IEncryptionService` no longer exists (see §3.5).

---

## 1. Entities (owned by this subsystem)

Defined authoritatively in `2026-06-16-database-schema.md`; referenced here, not redefined. Each owns its
`DbSet` and an EF `IEntityTypeConfiguration<T>` in Infrastructure.

| Entity | Schema | Base | Key fields (type) | Notes |
|---|---|---|---|---|
| `User` | A.1 | `SoftDeletableEntity` | `Id Guid` PK; `TwitchUserId string(50)` uniq idx; `Platform string(20)`; `Username/UsernameNormalized string(255)`; `DisplayName/NickName string(255)?`; `EmailCipher string(512)?`; `SubjectKeyId Guid?` FK→CryptoKey; `PronounId int?` / `AltPronounId int?` FK→Pronouns; `IsPlatformPrincipal/IsBot/IsAnonymized/Enabled bool`; `LastSeenAt DateTime?` | **Rebuild:** `Id` `string`→`Guid`; raw Twitch id → `TwitchUserId`. `IsAdmin` bool → `IsPlatformPrincipal`. Email moves to `EmailCipher` (shred). |
| `UserIdentity` | A.6 | `SoftDeletableEntity` | `Id Guid` PK (UUIDv7); `UserId Guid` FK→User; `Provider string(20)`; `ProviderUserId string(100)`; `ProviderUsername string(255)`; `ProviderDisplayName string(255)?`; `ProviderAvatarUrl string(2048)?`; `IsPrimary bool`; `ConnectionId Guid?` FK→IntegrationConnection; `LinkedAt DateTime`; `LastLoginAt DateTime?` | One proven external account linked to a `User` (see §3.7). GLOBAL. Uniq `(Provider, ProviderUserId)` and `(UserId, Provider)`; exactly one primary per user. Tokens never live here — `ConnectionId` points at the user-level login connection in the vault. |
| `Channel` | A.2 | `SoftDeletableEntity` | `Id Guid` PK (= tenant id); `OwnerUserId Guid` uniq FK→User; `TwitchChannelId string(50)` uniq idx; `Name/NameNormalized string(25)`; `Status string(20)`; `SuspendedAt DateTime?`; `SuspendedReason string(500)?`; `DeploymentMode string(20)`; `BillingTierKey string(20)`; `OverlayToken string(36)` uniq; `IsOnboarded/IsLive/Enabled bool` | **Rebuild:** `Id` `string`→`Guid`; `OwnerUserId` FK replaces the `[ForeignKey(nameof(Id))]` shared-PK hack. `OwnerUserId` stays **unique**: one Channel per owner; platforms attach as `PlatformConnection` rows (A.7, `platform-identity.md` §1), so `TwitchChannelId` is a nullable projection of the `twitch` connection. |
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
`Platform` (`AuthEnums.Platform`, streaming platforms) = `twitch|kick|youtube`. `LoginProvider` (`AuthEnums.LoginProvider`, ranges over `UserIdentity.Provider`) = the `Platform` set plus login-only `twitter`. `Channel.Status` = `active|suspended|churned|platform_banned`.
`Channel.DeploymentMode` = `saas|self_host_lite|self_host_full`. `AuthSession.ClientType` =
`web|desktop|mobile|ipc_dev`. `RefreshToken.RevokedReason` = `logout|rotation|reuse_detected|erasure|admin`.
`IntegrationConnection.Provider` (`AuthEnums.IntegrationProvider`) = `twitch|spotify|discord|youtube|kick|kick_bot|patreon|shopify|treatstream|azure_tts|elevenlabs|marketplace` (a channel's own Twitch bot connection is stored as `twitch_bot`).
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

### 3.1 `IAuthService` — `Application/Identity/Services/IAuthService.cs`

As built, `IAuthService` is **Twitch-specific**: the Twitch user login (OAuth + device code), the platform-shared bot, the per-channel custom bot, and refresh/logout. Ids are `Guid`; every method returns `Result<T>`.

The **provider-generic login (D2) is not on `IAuthService`.** `AuthController` orchestrates it through the login seams: `ILoginProviderRegistry` (descriptors + `EnabledAsync`; unknown provider → 404 `UNKNOWN_PROVIDER`, disabled → 403 `PROVIDER_DISABLED`), `ILoginIdentityProvider` (device grant: `StartDeviceAsync` / `PollDeviceAsync` → `ExternalIdentityProof`), `IAuthCodeLoginProvider` (auth-code + PKCE: `BuildAuthorizeUrlAsync` / `ExchangeCodeAsync` → `ExternalIdentityProof`), and `IExternalLoginService.LoginAsync(proof, context)`, which resolves or creates the `User` + primary `UserIdentity` through `IUserIdentityService`, links the vaulted login connection, and opens a **tenant-less** `AuthSession` (a brand-new non-Twitch user has no channel yet; they land on the channel picker). Twitch keeps its own richer path (`IAuthService` below), which also onboards the owner's `Channel` + `twitch` `PlatformConnection`. `provider=twitch` on the generic routes delegates to the `IAuthService` methods.

```csharp
namespace NomNomzBot.Application.Identity.Services;

public interface IAuthService
{
    // ── User OAuth (redirect flow) ────────────────────────────────────────────
    Task<Result<string>> GetTwitchOAuthUrl(string? state = null, string? baseUrl = null, Guid? broadcasterHint = null, CancellationToken cancellationToken = default);
    // Behavior: builds the Twitch authorize URL with progressive streamer scopes; `broadcasterHint` widens the scope set for a returning operator. No state change.

    Task<Result<AuthResultDto>> HandleTwitchCallbackAsync(OAuthCallbackDto callback, AuthContextDto context, CancellationToken cancellationToken = default);
    // Behavior: exchanges code→Twitch tokens; upserts User via IUserIdentityService.ResolveUserAsync("twitch", providerUserId, getOrCreate: true)
    // + Channel (tenant root, IsOnboarded on first login) + its `twitch` PlatformConnection; vaults the user's Twitch tokens via IIntegrationTokenVault;
    // opens an AuthSession; issues JWT + rotating RefreshToken. Emits UserRegisteredEvent (first time), ChannelOnboardedEvent (first onboarding), UserLoggedInEvent.

    // ── Device Code Flow login (no client secret; NomNomzBot's shipped public client id) ──
    Task<Result<DeviceCodeStartDto>> StartTwitchDeviceLoginAsync(CancellationToken cancellationToken = default);
    Task<Result<DeviceCodeStartDto>> StartTwitchDeviceLoginForScopesAsync(IReadOnlyList<string> scopes, CancellationToken cancellationToken = default);
    Task<Result<DeviceLoginPollDto>> PollTwitchDeviceLoginAsync(string deviceCode, AuthContextDto context, CancellationToken cancellationToken = default);
    // Behavior: the operator approves a short user code at twitch.tv/activate; the client polls. Status is pending | slow_down | authorized | expired | denied | error
    // (DeviceLoginStatus). On authorized the poll opens a session exactly like the callback path.

    // ── Refresh / logout ──────────────────────────────────────────────────────
    Task<Result<AuthResultDto>> RefreshTokenAsync(string refreshToken, AuthContextDto context, CancellationToken cancellationToken = default);
    // Behavior: validates+consumes the presented refresh token (single-use rotation); on reuse of a consumed/revoked token,
    // revokes the whole session lineage and emits RefreshTokenReuseDetectedEvent. On success issues a new JWT + RefreshToken.

    Task<Result> LogoutAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken = default);
    // Behavior: revokes the AuthSession + all its refresh tokens (RevokedReason=logout) and the session's `sid` (§3.7). Emits UserLoggedOutEvent. Vault untouched.

    Task<Result<int>> LogoutAllAsync(Guid userId, CancellationToken cancellationToken = default);
    // Behavior: revokes every active session of the user; returns the count.

    // ── Platform bot (NomNomzBot) — platform plane, BroadcasterId=null ────────
    Task<Result<string>> GetTwitchBotOAuthUrl(string? state = null, string? baseUrl = null, CancellationToken cancellationToken = default);
    Task<Result<BotStatusDto>> HandleTwitchBotCallbackAsync(OAuthCallbackDto callback, CancellationToken cancellationToken = default);
    // Behavior: exchanges code; upserts the shared BotAccount (IdentityType=shared); vaults its tokens under a platform IntegrationConnection (BroadcasterId=null).
    // Emits BotAccountAuthorizedEvent (BroadcasterId=Guid.Empty/platform sentinel).
    Task<Result<DeviceCodeStartDto>> StartBotDeviceLoginAsync(CancellationToken cancellationToken = default);
    Task<Result<DeviceBotPollDto>> PollBotDeviceLoginAsync(string deviceCode, CancellationToken cancellationToken = default);
    Task<Result<BotStatusDto>> GetBotStatusAsync(CancellationToken cancellationToken = default);
    Task<Result> DisconnectBotAsync(CancellationToken cancellationToken = default);
    // Behavior: marks the shared BotAccount inactive; revokes vaulted bot tokens. Emits BotAccountDisconnectedEvent.

    // ── Custom (white-label) bot — per-channel, BroadcasterId=channelId. The shared platform BotAccount is the default for every
    //    channel; a custom bot identity is gated on IBillingTierService.AllowsCustomBotName (tier N.1) and fails NOT_ENTITLED otherwise. ──
    Task<Result<string>> GetTwitchChannelBotOAuthUrl(Guid broadcasterId, string? state = null, string? baseUrl = null, CancellationToken cancellationToken = default);
    Task<Result<BotStatusDto>> HandleTwitchChannelBotCallbackAsync(Guid broadcasterId, OAuthCallbackDto callback, CancellationToken cancellationToken = default);
    Task<Result<DeviceCodeStartDto>> StartChannelBotDeviceLoginAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task<Result<DeviceBotPollDto>> PollChannelBotDeviceLoginAsync(Guid broadcasterId, string deviceCode, CancellationToken cancellationToken = default);
    // Behavior: exchanges code / polls; upserts a custom BotAccount + a ChannelBotAuthorization (BroadcasterId, BotAccountId); vaults tokens.
    // The entitlement is checked BEFORE Twitch is polled, so no token is ever issued to a channel whose plan lacks its own bot. Emits BotAccountAuthorizedEvent.
    Task<Result<BotStatusDto>> GetChannelBotStatusAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    Task<Result> DisconnectChannelBotAsync(Guid broadcasterId, CancellationToken cancellationToken = default);
    // Behavior: deactivates the ChannelBotAuthorization; revokes that bot's vaulted tokens. Emits BotAccountDisconnectedEvent.
}
```

### 3.2 `IJwtTokenService` — `Application/Abstractions/Auth/IJwtTokenService.cs`

Widens `userId` to `Guid`; adds the resolved tenant + session to the issued claims, plus the login provider and the act-as claims (§3.7). Signing is config-selected (`Jwt:Algorithm`: HS256 default, RS256/ES256 asymmetric; decisions doc #4) behind the unchanged interface. Claim names: `tenant`, `sid`, `act` (constants on `JwtTokenService`).

```csharp
namespace NomNomzBot.Application.Abstractions.Auth;

public interface IJwtTokenService
{
    string GenerateAccessToken(
        Guid userId, string username, Guid? broadcasterId, Guid sessionId,
        IEnumerable<string>? roles = null, string? idp = null,
        string? actorUserId = null, DateTime? maxExpiresAt = null);
    // Behavior: mints a short-lived access JWT (sub=userId, tenant=broadcasterId, sid=sessionId, idp=login provider, act=actorUserId when acting as someone).
    // Lifetime = Jwt:ExpiryMinutes (default 60), clamped to `maxExpiresAt` when given (an act-as token never outlives its support grant). No persistence. Pure.

    string GenerateRefreshTokenValue();
    // Behavior: returns a cryptographically-random opaque refresh token string (RNG). The CALLER hashes+persists it; the JWT layer never stores it.

    ClaimsPrincipal? ValidateAccessToken(string token);
    // Behavior: validates signature+lifetime+issuer/audience with the pinned algorithm; returns the principal or null. No state change.

    TokenValidationParameters GetValidationParameters();
    // Behavior: the same parameters JwtBearer uses; consumers that must inspect an expired token clone it and switch lifetime validation off.
}
```

> `GenerateRefreshToken(userId, username)` (legacy) is **removed** — refresh tokens are opaque random values hashed into `RefreshTokens`, not self-describing JWTs. `GenerateToken` → `GenerateAccessToken`. The implementation still uses `JwtSecurityTokenHandler` (`System.IdentityModel.Tokens.Jwt`); that handler stays until **S-JWT-HANDLER** moves it to `JsonWebTokenHandler`.

### 3.3 `ISessionService` — `Application/Identity/Services/ISessionService.cs`

Owns the `AuthSessions` + `RefreshTokens` lifecycle (extracted from `IAuthService` for single
responsibility; `IAuthService` calls it).

```csharp
namespace NomNomzBot.Application.Identity.Services;

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

    Task<Result<AuthSessionDto>> PeekSessionAsync(string rawRefreshToken, CancellationToken ct = default);
    // Behavior: read-only — resolves the session a LIVE raw refresh token belongs to WITHOUT consuming or rotating it (no LastSeenAt write, never counts as reuse, never revokes).
    // Used where a browser navigation carries only the HttpOnly refresh cookie, e.g. the streamer authorize redirect widening its scope set for a returning operator,
    // and logout-while-acting resolving the operator's own session. Fails for a consumed, revoked, expired or unknown token.
}
```

Lifetimes as built: access token 1 hour (`Jwt:ExpiryMinutes`), refresh token 30 days, session 30 days.

### 3.4 `IIntegrationTokenVault` — `Application/Identity/Services/IIntegrationTokenVault.cs`

The crypto-shred-ready OAuth token vault (Domain E + Q). **Replaces** all direct
`Service.AccessToken`/`RefreshToken` access. Sits over the canonical crypto primitives owned by
`gdpr-crypto.md` — `IFieldCipher` (AES-256-GCM AEAD) + `ISubjectKeyService` (DEK lifecycle), composed by `ITokenProtector`; see §3.5.

```csharp
namespace NomNomzBot.Application.Identity.Services;

public interface IIntegrationTokenVault
{
    Task<Result<IntegrationConnectionDto>> UpsertConnectionAsync(UpsertConnectionDto request, CancellationToken ct = default);
    // Behavior: upserts an IntegrationConnection (by BroadcasterId+Provider+ProviderAccountId); does NOT store secrets. Emits IntegrationConnectedEvent on first connect.

    Task<Result> StoreTokensAsync(Guid connectionId, StoreTokensDto tokens, IReadOnlyList<string>? grantedScopes = null, CancellationToken ct = default);
    // Behavior: AES-256-GCM-encrypts access/refresh/app tokens (AAD = tenantId‖provider‖tokenType‖keyVersion) via ITokenProtector (IFieldCipher under the connection's tenant/subject DEK,
    // created via ISubjectKeyService if absent — see gdpr-crypto.md §3.4); when `grantedScopes` is given, calls IScopeGrantService.ReconcileGrantedScopesAsync (§3.4a); upserts IntegrationTokens rows; sets Status=connected, resets ConsecutiveFailureCount. Emits IntegrationTokenRefreshedEvent.

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
namespace NomNomzBot.Application.Identity.Services;

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

**Wiring (binding):** `IIntegrationTokenVault.StoreTokensAsync` calls `ReconcileGrantedScopesAsync` with the token's `scope` set (the `grantedScopes` argument) on every store/refresh, so `Scopes` is never stale. `ScopesDroppedEvent(Guid BroadcasterId, string Provider, IReadOnlyList<string> DroppedScopes, IReadOnlyList<string> DisabledFeatures)` joins the §2 domain-event catalogue (handlers: disable the affected feature toggles + surface a "reconnect to restore X" prompt in the dashboard). Twitch exposes **no per-scope revoke** — a full app disconnect invalidates the token and flows through the refresh-failure → `needs_reauth` path; a partial scope loss arises only from re-authorizing with a narrower set or a Twitch scope deprecation, both caught by reconciliation.

### 3.5 DEK lifecycle + field AEAD — **consumed, not redefined** (owner: `gdpr-crypto.md`)

> **Single owner: `gdpr-crypto.md` §3.** The per-subject/tenant DEK lifecycle (`CryptoKey`) and the AEAD
> data-plane primitive are authored there; this subsystem **consumes** them and does not redefine a parallel
> interface. The canonical types as built:
> - **`ISubjectKeyService`** (`NomNomzBot.Application.Services`) — per-subject/tenant DEK create/get/rotate/destroy
>   lifecycle over `CryptoKey` + `KeyUsageBinding`. Members: `GetOrCreateSubjectKeyAsync`,
>   `GetOrCreateTenantKeyAsync`, `GetOrCreatePlatformKeyAsync`, `ProtectAsync`/`UnprotectAsync`, `RotateKeyAsync`,
>   `ResolveSubjectKeysAsync`, **`DestroyKeyAsync(Guid cryptoKeyId, Guid erasureRequestId, …)`** (the O(1) crypto-shred).
> - **`IFieldCipher`**, **`IKeyVault`** (`local_aes` / `kms_envelope` KEK custody), **`ISubjectKeyStore`** (the DEK
>   registry, persisted in `CryptoKey`) and **`ITokenProtector`** (the token-sealing facade over the three) — all in
>   `NomNomzBot.Application.Common.Interfaces.Crypto`. `IFieldCipher` is AES-256-GCM AEAD with AAD =
>   `tenantId‖provider‖tokenType‖keyVersion`; it is the single AEAD primitive.
>
> `IIntegrationTokenVault` (§3.4) seals through `ITokenProtector`; there is **one** AEAD primitive across the
> platform. The legacy AES-CBC `IEncryptionService` (no MAC, no AAD) has been deleted, and no `IKdf` interface exists.

### 3.6 `ICurrentTenantService` / `ICurrentUserService` — `Application/Abstractions/Auth/`

```csharp
public interface ICurrentTenantService
{
    Guid? BroadcasterId { get; }
    bool HasTenant { get; }
    void SetTenant(Guid broadcasterId);
    void Clear();   // drops tenant context so a background service can be reused across tenants
}

public interface ICurrentUserService
{
    string? UserId { get; }            // the JWT `sub`, as a string (callers parse to Guid)
    string? Username { get; }
    bool IsAuthenticated { get; }
    bool IsPlatformPrincipal { get; }  // JWT `admin` role claim, sourced from User.IsPlatformPrincipal at login
    ImpersonationContext? Impersonation { get; }   // non-null only on an act-as token (§3.7)
}

public sealed record ImpersonationContext(Guid OperatorUserId, Guid SubjectUserId, Guid SessionId);
```

Tenant id widened `string?`→`Guid?`; `IsPlatformPrincipal` replaces the old `IsAdmin` read; `Impersonation` surfaces the true operator + session ambiently so every journalled write during an act-as request attributes both actors.

### 3.7 Act-as tokens, `sid` revocation, linked identities, refresh custody

**Act-as tokens (impersonation = full identity swap).** An operator with an open support-access grant (an `IamRoleAssignment`) acts as a channel member. `ImpersonationTokenMinter` calls `IJwtTokenService.GenerateAccessToken` with the **target's** identity and roles (`user`, plus `admin` only if the target is a platform principal), `tenant` = the target's own oldest channel (null for a moderator or viewer), `sid` = the **grant id**, `act` = the operator's user id (non-authoritative; never a role), and `maxExpiresAt` = the grant's expiry. It is access-only — no refresh token — and never outlives the grant.
- *Validation.* JwtBearer `OnTokenValidated` rejects an act-as token unless its grant is still open (`ImpersonationSessionCheck` → `IImpersonationSessionService.IsActiveAsync` = grant not revoked, not expired, and `sid` not revoked). Ending the grant any way — operator Exit, ending the support session, an IAM revoke, the expiry job — stops the token at once.
- *Transport.* Web: an httpOnly `nnz_act_as` cookie (`SameSite=Strict`, `Path=/api/v1/auth`, `Secure` when the public origin is HTTPS). Native: `RefreshTokenRequest.ActAsToken` in the body.
- *Refresh.* `POST auth/refresh` with an act-as token calls `IImpersonationSessionService.RefreshAsync`, which re-mints the same subject under the same grant only while the grant is open, the token names the grant's operator, and the subject is still a member of the scope channel; the response carries `impersonation { sessionId, expiresAt }` and no refresh token. Otherwise it fails `IMPERSONATION_ENDED`, the cookie is cleared, and the operator's own refresh runs.
- *Exit.* `POST auth/impersonation/exit` ends the support session (audited, `sid` revocation, owner notice), clears `nnz_act_as`, and hands back the **operator's own** session (web via the refresh cookie, native via a body refresh token). Logout / logout-all while acting affect the operator only — never the impersonated user's sessions. Only Exit shows the admin; the swap is total (`ICurrentUserService.Impersonation` is the one place both actors are visible).

**`sid` revocation (PRODUCT-ALIGNMENT D10).** Access tokens stay 60 minutes; an in-flight token must still stop authenticating the moment its session ends. `ISessionRevocationService` (`RevokeAsync(sid)` / `IsRevokedAsync(sid)`, singleton) records revoked `sid`s in the durable `ICacheService` (Redis where configured, in-process otherwise) behind a short per-process cache. JwtBearer `OnTokenValidated` (`SessionRevocationCheck`) rejects a token whose `sid` is revoked; a missing or unparsable `sid` is not treated as revoked here (it fails elsewhere). Logout, logout-all and support-session end revoke the `sid`.

**Linked identities.** A user has 1..n `UserIdentity` rows (§1), at most one per provider, exactly one primary; any of them can log the user in. `IUserIdentityService` = `ListAsync` / `ResolveUserAsync` / `LinkAsync` / `UnlinkAsync` / `SetPrimaryAsync` / `MergeIdentitiesAsync`. The self-scoped routes (§5) key on the JWT `sub` — no tenant, no Gate-2. Auth-code + PKCE providers (kick, twitter) return the provider's authorize URL from `identities/{provider}/link`, and the shared `{provider}/callback` finishes the link because the server-side state carries the caller's id; device-grant providers (youtube) return the device start and the client polls `identities/{provider}/link/poll` (authenticated; it attaches the proven identity and never mints a session). Twitch is the primary sign-in and is not linkable through these routes. Errors: `IDENTITY_NOT_FOUND`→404; `PRIMARY_IDENTITY`, `LAST_IDENTITY`, `IDENTITY_ALREADY_LINKED`, `PROVIDER_ALREADY_LINKED`→409.

**Refresh custody.** Custody is decided by what the request itself carries, never by a caller-controlled `?client=` flag on refresh (S098c). Web: the refresh token lives only in the `nnz_refresh_token` HttpOnly cookie (`SameSite=Lax`, `Path=/api/v1/auth`, 30 days, `Secure` when the public origin is HTTPS) and is stripped from the JSON body; the device-poll and callback routes set it (`?client=web`). A valueless companion `nnz_session` cookie (`Path=/`, presence only) exists purely so the root route can tell a returning visitor from a new one — real authentication still runs on the refresh token. A cookie-borne refresh additionally requires an allowed `Origin` (`SameSite=Lax` alone still lets a top-level cross-site navigation attach the cookie). Native clients keep the token in the response body / their own vault (no browser XSS surface) and send it in `RefreshTokenRequest.RefreshToken`. Logout deletes both cookies with the same `Path` they were set with. The refresh token is **never** in `localStorage`. Rotation is single-use; reuse of a consumed token revokes the whole lineage (§3.3).

---

## 4. DTOs / contracts

`AuthDtos.cs` / `DeviceLoginDtos.cs` (`NomNomzBot.Application.Identity.Dtos`) — extend existing:

```csharp
public sealed record AuthResultDto(string AccessToken, string RefreshToken, DateTime ExpiresAt, UserDto User);          // existing — User.Id now Guid-as-string
public sealed record OAuthCallbackDto { public required string Code { get; init; } public string? State { get; init; } public string? RedirectUri { get; init; } } // existing
public sealed record RefreshTokenRequest(string? RefreshToken, string? ActAsToken = null);                              // existing — RefreshToken null when the web cookie carries it; ActAsToken = native act-as session (§3.7)

public sealed record AuthContextDto(string ClientType, string? IpAddress, string? UserAgent);                          // new — request fingerprint for the session
public sealed record SessionTokensDto(string AccessToken, string RawRefreshToken, DateTime AccessExpiresAt, DateTime RefreshExpiresAt, Guid SessionId);
public sealed record AuthSessionDto(Guid Id, Guid UserId, Guid? BroadcasterId, string ClientType, DateTime LastSeenAt, DateTime ExpiresAt, bool IsRevoked);
public sealed record BotStatusDto(bool Connected, string? Login, string? DisplayName, string? ProfileImageUrl);        // existing (already in IAuthService.cs)
```

`IntegrationDtos.cs` (`NomNomzBot.Application.Identity.Dtos`) — new:

```csharp
public sealed record UpsertConnectionDto(Guid? BroadcasterId, string Provider, string? ProviderAccountId, string? ProviderAccountName, IReadOnlyList<string> Scopes, string? ClientId, bool IsByok, Guid? ConnectedByUserId, string? SettingsJson);
public sealed record StoreTokensDto(string AccessToken, string? RefreshToken, string? AppToken, DateTime? AccessExpiresAt);
public sealed record DecryptedTokenDto(string Value, string TokenType, DateTime? ExpiresAt, bool IsExpired);
public sealed record IntegrationConnectionDto(Guid Id, Guid? BroadcasterId, string Provider, string? ProviderAccountId, string? ProviderAccountName, string Status, IReadOnlyList<string> Scopes, bool IsByok, DateTime? ConnectedAt, DateTime? LastRefreshedAt, int ConsecutiveFailureCount);
```

`ITwitchAuthService` (`Application/Abstractions/Auth/ITwitchAuthService.cs`) — keep as the low-level Twitch HTTP exchange; widen
`broadcasterId` to `Guid?` (null = the platform/shared-bot row) and route token storage through `IIntegrationTokenVault` instead of
`Service.AccessToken`:

```csharp
public interface ITwitchAuthService
{
    Task<TokenResult?> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);
    Task<TokenResult?> RefreshTokenAsync(Guid? broadcasterId, string serviceName, CancellationToken ct = default);
    Task RefreshExpiringTokensAsync(CancellationToken ct = default);
    Task RevokeTokenAsync(Guid? broadcasterId, string serviceName, CancellationToken ct = default);
}
public record TokenResult(string AccessToken, string RefreshToken, DateTime ExpiresAt, string[] Scopes); // existing
```

---

## 5. Controller endpoints

`AuthController` (`api/v1/auth`, `[ApiVersion("1.0")]`, `: BaseController`) — extends the existing
controller. Auth plane = **platform JWT**; the per-action floor is in the gate column.

**Role gate.** Two distinct authorization planes; a row uses exactly one.

- **Management plane (Gate-2 — `ActionDefinitions`).** Gate-1 = `[Authorize]` + tenant resolution (pure entry — any authenticated caller, channel must exist; entry ≠ permission, floors are Gate-2's). Gate-2 = `IActionAuthorizationService.AuthorizeActionAsync(userId,
  broadcasterId, actionKey)` enforces the per-route floor named in the gate column before the service call
  (403 FORBIDDEN when below). The `actionKey`s are seeded global **`ActionDefinition`s** (schema B.3); a
  broadcaster may raise a floor via `ChannelActionOverride` but not below the seeded `FloorLevel`.
- **Platform plane (Plane-C — `IamPermissions`).** Plane-C rows gate on
  `IPlatformIamService.AuthorizePlatformAsync(principalId, permissionKey, …)`, where `permissionKey` is a
  seeded global **`IamPermission`** (schema C.1) — a different table and key namespace from `ActionDefinitions`.
  The ASP.NET `[Authorize(Policy="<key>")]` policy-name IS that `IamPermission.Key` verbatim. No
  `ChannelActionOverride`/`FloorLevel` applies (platform scope, not tenant).

`[AllowAnonymous]` rows are the OAuth handshake (neither gate). Rate limits: `auth` on every start/exchange/refresh/self-scoped route, `device-poll` on every device poll.

| Route | Verb | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `auth/me` | GET | — | `StatusResponseDto<CurrentUserDto>` | — (any authenticated user, own session) |
| `auth/identities` | GET | — | `StatusResponseDto<IReadOnlyList<UserIdentityDto>>` | — (self-scoped: JWT `sub`, primary first; §3.7) |
| `auth/identities/{provider}/link` | POST | — | `StatusResponseDto<…>` (authorize URL for kick/twitter, device start for youtube) | — (self-scoped; Twitch not linkable here) |
| `auth/identities/{provider}/link/poll` | POST | `DevicePollRequest` | `StatusResponseDto<…>` (loop status / linked identity) | — (self-scoped, authenticated, never mints a session; `device-poll`) |
| `auth/identities/{identityId:guid}` | DELETE | — | `StatusResponseDto<object>` | — (self-scoped unlink; 404 `IDENTITY_NOT_FOUND`, 409 `PRIMARY_IDENTITY`/`LAST_IDENTITY`) |
| `auth/identities/{identityId:guid}/primary` | POST | — | `StatusResponseDto<UserIdentityDto>` | — (self-scoped set-primary) |
| `auth/providers` | GET | — | `StatusResponseDto<IReadOnlyList<LoginProviderDto>>` | `[AllowAnonymous]` (login screen; `platform-identity.md` §5) |
| `auth/twitch` | GET | `?redirect_uri&client&return_to&return_route` | 302 → Twitch | — (OAuth handshake; `client=web` returns tokens in the URL fragment + HttpOnly cookie; a live web session widens the scope set) |
| `auth/twitch/callback` | GET | `?code&state` | 302 (served-web) / JSON tokens+user | — (OAuth handshake; single-use state nonce) |
| `auth/twitch/callback` | POST | `OAuthCallbackDto` | `StatusResponseDto<object>` (tokens+user) | — (OAuth handshake, SPA/mobile code exchange) |
| `auth/twitch/device` | POST | — | `StatusResponseDto<DeviceCodeStartDto>` | — (OAuth handshake; the `provider=twitch` case of the generic route, same handler) |
| `auth/twitch/device/poll` | POST | `DevicePollRequest`, `?client` | `StatusResponseDto<DeviceLoginPollDto>` | — (OAuth handshake; `client=web` sets the refresh cookie and blanks the body token) |
| `auth/{provider}/device` | POST | — | `StatusResponseDto<DeviceCodeStartDto>` | — (OAuth handshake; `provider` ∈ `ILoginProviderRegistry.EnabledAsync`, else 404 `UNKNOWN_PROVIDER` / 403 `PROVIDER_DISABLED`; no impl yet → 501) |
| `auth/{provider}/device/poll` | POST | `DevicePollRequest`, `?client` | `StatusResponseDto<DeviceLoginPollDto>` | — (OAuth handshake; non-Twitch → `ILoginIdentityProvider.PollDeviceAsync` → `IExternalLoginService.LoginAsync`, tenant-less session) |
| `auth/{provider}/authorize` | GET | `?redirect_uri&client&return_to` | 302 → provider | — (auth-code + PKCE providers kick/twitter; verifier + provider stashed server-side under a single-use state) |
| `auth/{provider}/callback` | GET | `?code&state` | 302 / JSON tokens+user | — (finishes an auth-code login, or a link when the state carries the caller's id) |
| `auth/refresh` | POST | `RefreshTokenRequest?` | `StatusResponseDto<object>` (rotated tokens+user; act-as → `impersonation`, no refresh token) | — (OAuth handshake). **Web reads the HttpOnly `nnz_refresh_token` cookie** (and `nnz_act_as` while acting), requires an allowed `Origin`, and blanks the body token; native sends `RefreshToken` / `ActAsToken` in the body (§3.7) |
| `auth/impersonation/exit` | POST | `RefreshTokenRequest?` | `StatusResponseDto<object>` (operator's own session) | — (any authenticated caller holding an act-as token; else `INVALID_STATE`) |
| `auth/logout` | POST | — (session from JWT) | `StatusResponseDto<object>` | — (any authenticated user, own session; revokes the `sid`, clears cookies) |
| `auth/logout/all` | POST | — | `StatusResponseDto<object>` | — (any authenticated user, revokes all own sessions) |
| `auth/twitch/bot` | GET | `?redirect_uri` | `StatusResponseDto<OAuthStartDto>` (authorize URL + state) | platform · `iam:manage` (platform-shared bot, `BroadcasterId=null`) |
| `auth/twitch/bot/status` | GET | — | `StatusResponseDto<BotStatusDto>` | platform · `iam:manage` (platform-shared bot) |
| `auth/twitch/bot` | DELETE | — | `StatusResponseDto<object>` | platform · `iam:manage` (platform-shared bot) |
| `auth/twitch/bot/device` | POST | — | `StatusResponseDto<DeviceCodeStartDto>` | `[AllowAnonymous]` only during the first-run setup window; admin (`403`) once setup is complete |
| `auth/twitch/bot/device/poll` | POST | `DevicePollRequest` | `StatusResponseDto<DeviceBotPollDto>` | same setup-window gate as the start route |
| `auth/twitch/channels/{channelId:guid}/bot/device` | POST | — | `StatusResponseDto<DeviceCodeStartDto>` | management · `integration:write` on the channel; `403 NOT_ENTITLED` when the plan lacks `AllowsCustomBotName` |
| `auth/twitch/channels/{channelId:guid}/bot/device/poll` | POST | `DevicePollRequest` | `StatusResponseDto<DeviceBotPollDto>` | management · `integration:write`; a non-entitled channel gets a terminal `error` status (HTTP 200), never a token |

The platform-bot and channel-bot **redirect** callbacks have no route of their own: they finish through the unified, nonce-validated `auth/twitch/callback`, whose state records the flow (`bot` / `channel_bot`).

`ChannelBotController` (`api/v1/channels`) — custom (white-label) bot, **tenant
plane** (Plane B management ladder). A custom per-channel bot identity is gated on the
**`IBillingTierService.AllowsCustomBotName`** entitlement (tier N.1; cross-reference `monetization.md`) — the
resolved tier flag, never a tier-key string compare. The shared platform bot is the **Base default** every
channel gets. The connect route therefore fails **`403 NOT_ENTITLED`** when
`AllowsCustomBotName` is false for the channel's resolved tier (self-host always allows it). Same Gate-1/Gate-2 mechanism as above; `{channelId}` is a route string parsed to `Guid` (`400` when malformed):

| Route | Verb | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `{channelId}/scopes` | GET | — | `StatusResponseDto<ScopesResponseDto>` (granted vs required bot chat scopes; falls back to the streamer's own `twitch` connection on self-host) | management / Broadcaster · `channelbot:read` |
| `{channelId}/bot/connect` | GET | — | `StatusResponseDto<OAuthStartDto>` (Twitch authorize URL, `force_verify`, + single-use state nonce `channel_bot`) | management / Broadcaster · `channelbot:connect`; `403 NOT_ENTITLED` |
| `{channelId}/bot/status` | GET | — | `StatusResponseDto<BotStatusDto>` | management / Broadcaster · `channelbot:read` |
| `{channelId}/bot` | DELETE | — | `StatusResponseDto<object>` | management / Broadcaster · `channelbot:disconnect` |

The OAuth callback completes through `auth/twitch/callback` (no `.../bot/callback` route); the device-code alternative is `auth/twitch/channels/{channelId:guid}/bot/device[/poll]` above.

> All ids in routes/bodies are now `Guid` (`{channelId:guid}` on `AuthController`; `{channelId}` string parsed to `Guid` on `ChannelBotController`). Tenant is resolved from the
> JWT `tenant` claim by `TenantResolutionMiddleware`, never from request input (closes the live cross-tenant
> IDOR — stack doc §Sandbox blocker). The route `broadcasterId` MUST equal the resolved tenant or 403.

---

## 6. Pipeline actions

**None.** Identity-auth owns no pipeline actions, conditions, or template variables.

---

## 7. DI registration

Registered in `DependencyInjection.cs` (Infrastructure); single-implementation `I<X>Service` types are picked up by the convention scan as Scoped.
Lifetimes match the existing convention (request-scoped services, singleton crypto primitive).

| Interface | Implementation | Lifetime | Profile adapter |
|---|---|---|---|
| `IAuthService` | `AuthService` | Scoped | — |
| `ISessionService` | `SessionService` | Scoped | — |
| `ISessionRevocationService` | `SessionRevocationService` | Singleton | durable side = `ICacheService` (Redis / in-process memory) |
| `IImpersonationSessionService` | `ImpersonationSessionService` | Scoped | — |
| `IJwtTokenService` | `JwtTokenService` | Singleton | **profile:** signing key HS256 (single-user self-host) / RS256/ES256 (`Jwt:Algorithm`; JWKS on the federation/SSO path, decisions #4) — selected by config behind the unchanged interface |
| `ICurrentUserService` | `CurrentUserService` (HttpContext) | Scoped | — |
| `ICurrentTenantService` | `CurrentTenantService` | Scoped | — |
| `IIntegrationTokenVault` | `IntegrationTokenVault` | Scoped | consumes `ISubjectKeyService` + `IFieldCipher` (below) |
| `ISubjectKeyService` / `ITokenProtector` / `ISubjectKeyStore` / `IFieldCipher` / `IKeyVault` | **registered by `gdpr-crypto.md` §7** | Scoped (DEK service, token protector, DEK store) / Singleton (`IFieldCipher`, `IKeyVault`) | **Not registered here.** DEK lifecycle + AEAD + KEK custody (`local_aes` / `kms_envelope`, profile-selected) are owned by `gdpr-crypto.md`; this subsystem only consumes them. |
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

Versions are **not** stated here — `server/Directory.Packages.props` is the single source of truth (central package management).

- **`Microsoft.AspNetCore.Authentication.JwtBearer`** — inbound JWT resource-server validation (`OnTokenValidated` runs the `sid` and act-as checks, §3.7).
- **`System.IdentityModel.Tokens.Jwt`** + **`Microsoft.IdentityModel.Tokens`** — JWT create/validate via `JwtSecurityTokenHandler`. The handler stays until **S-JWT-HANDLER** moves it to `JsonWebTokenHandler`.
- **`System.Security.Cryptography`**, **`System.Security.Cryptography.ProtectedData`** (DPAPI KEK-at-rest, Windows) — the AEAD/DEK/KEK primitives are owned and
  registered by `gdpr-crypto.md` (`IFieldCipher`/`IKeyVault`/`ISubjectKeyService`/`ITokenProtector`); this subsystem
  consumes them and does not pull these packages itself. (No Azure Key Vault package is referenced today; the `kms_envelope` KEK adapter is the SaaS profile variant.)
- **`Microsoft.AspNetCore.DataProtection`** — cookie/token protection (DB-backed key ring, rotate on deploy for Linux/SaaS — decisions #9).
- **EF Core 10** (`Microsoft.EntityFrameworkCore`) + **`Npgsql.EntityFrameworkCore.PostgreSQL`**
  (SaaS) / **`Microsoft.EntityFrameworkCore.Sqlite`** (lite) — named query filters for soft-delete +
  tenant; hand-rolled `ValueConverter`+`ValueComparer` for `[VC:JSON]`/`[VC:enum]`.
- **Newtonsoft.Json** — app JSON serialization of `[VC:JSON]` columns and DTO payloads (per task convention).
- **`Microsoft.Extensions.Http.Resilience`** — retry/breaker on the Twitch token-exchange `HttpClient`
  (via `ITwitchAuthService`).
- **OpenIddict** (OIDC issuer) and **`Microsoft.AspNetCore.Authentication.OpenIdConnect`** (OIDC client) belong to the federation/multi-user-SSO path (decisions #3) and are **not referenced in `Directory.Packages.props` today**; they are absent from the basic single-user self-host path, which runs JWT resource-server validation with no issuer.

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
