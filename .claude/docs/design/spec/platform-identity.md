# Platform Identity — Interface Specification

**Status:** Implementable. Code from this directly.
**Sources (authoritative):** `identity-auth.md` (auth surface this extends), `2026-06-16-database-schema.md` (Domain A), the multi-platform readiness audit (2026-07-04), `roles-permissions.md` (planes/gates), `federation-oidc.md` (SSO — a *different* concern: operator/enterprise SSO into the platform, not streamer platform identity).

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]`; surrogate PK `guid` via `Guid.CreateVersion7()`; soft-delete global filter; Newtonsoft.Json.

**Goal.** Make login and account identity **platform-agnostic** so YouTube and Kick can be enabled later by *registering a provider descriptor and flipping a feature flag* — with **zero rewrites** of existing Twitch code. Today login is Twitch-welded (`/auth/twitch/*` only, `User.TwitchUserId` 1:1, `Channel.TwitchChannelId` keyed). This spec introduces the identity table and the generic seams; Twitch remains the only **shipped** login provider.

---

## 0. Model

- A **User** is the internal person (UUIDv7 `Id`, the only FK target). A user has **1..n linked external identities**, at most one per provider.
- A **UserIdentity** is one proven external account (`(Provider, ProviderUserId)` unique across the system). Any linked identity can log the user in. Exactly one identity is **primary** (display/default; seeds `User.Username`/avatar refresh).
- A **Channel** is one streaming surface on one platform (`Provider` + `ExternalChannelId`). A streamer with Twitch **and** YouTube presences has **two Channel rows** (two tenants) under one `OwnerUserId` — the existing channel switcher already handles multiple channels; there is **no** cross-platform channel-group construct (decided §9.4).
- **Tokens never live on the identity.** The vault (`IntegrationConnection` + `IntegrationToken`, identity-auth §3.4) stays the single token store; `UserIdentity.ConnectionId` points at the login connection (`BroadcasterId = null` = user-level, the existing global shape).
- **Denormalized projections stay.** `User.TwitchUserId` and `Channel.TwitchChannelId` are hot-path projections of the Twitch identity/channel rows — maintained by this subsystem, **nullable** once the identity table lands (a YouTube-only user has none). `User.Platform` = the primary identity's provider.

## 1. Entities

| Entity | Schema | Base | Key fields (type) | Notes |
|---|---|---|---|---|
| `UserIdentity` | A.6 (new) | `SoftDeletableEntity` | `Id Guid` PK; `UserId Guid` FK→User; `Provider string(20)` [VC:enum `Platform` = `twitch\|kick\|youtube`]; `ProviderUserId string(100)`; `ProviderUsername string(255)`; `ProviderDisplayName string(255)?`; `ProviderAvatarUrl string(2048)?`; `IsPrimary bool`; `ConnectionId Guid?` FK→IntegrationConnection; `LinkedAt DateTime`; `LastLoginAt DateTime?` | **Unique** `(Provider, ProviderUserId)`; **unique** `(UserId, Provider)` (one identity per provider per user — re-link replaces). GLOBAL (not tenant-scoped). |
| `User` (extend) | A.1 | — | `TwitchUserId` → **nullable**; `Platform` = primary identity's provider (projection) | Backfill migration §8. No other column changes. |
| `Channel` (extend) | A.2 | — | + `Provider string(20)` [VC:enum `Platform`]; + `ExternalChannelId string(100)`; `TwitchChannelId` → **nullable** projection (filled iff `Provider=twitch`) | **Unique** `(Provider, ExternalChannelId)`. Invariant: `OwnerUserId` holds a `UserIdentity` for `Channel.Provider` (enforced at onboard and at unlink §3.1). |
| `EventJournal` (extend) | O.x | — | `ActorTwitchUserId` → **`ActorExternalUserId string(100)?`** + new `ActorProvider string(20)?` | Same migration; writers also resolve and store internal `ActorUserId` where they already do — raw external id+provider are kept for audit fidelity. |

## 2. Domain events

Sealed records on `DomainEventBase` (platform-conventions §2.0); user-level events carry `BroadcasterId = Guid.Empty` (platform sentinel).

```csharp
public sealed record UserIdentityLinkedEvent(Guid UserId, string Provider, string ProviderUserId, string ProviderUsername) : DomainEventBase;
public sealed record UserIdentityUnlinkedEvent(Guid UserId, string Provider, string ProviderUserId, string Reason) : DomainEventBase;
public sealed record PrimaryIdentityChangedEvent(Guid UserId, string Provider) : DomainEventBase;
public sealed record ViewerRowAbsorbedEvent(Guid AbsorbedUserId, Guid IntoUserId, string Provider, string ProviderUserId) : DomainEventBase;
```

## 3. Service interfaces

### 3.1 `IUserIdentityService` — new, Application `Abstractions/Identity/`

```csharp
public interface IUserIdentityService
{
    // All identities of a user, primary first.
    Task<Result<IReadOnlyList<UserIdentityDto>>> ListAsync(Guid userId, CancellationToken ct = default);

    // Resolve an external (provider, providerUserId) to the internal user — the ONE lookup every
    // ingest path (chat, EventSub, roster/standing sync, journal attribution) routes through.
    // getOrCreate=true applies the viewer-identity rule (a chatter IS a User row): creates the
    // User + UserIdentity pair when unseen. Replaces direct TwitchUserId lookups over time.
    Task<Result<Guid>> ResolveUserAsync(string provider, string providerUserId, bool getOrCreate, CancellationToken ct = default);

    // Bind a proven external identity to the CALLER's account (called by the link flow AFTER the
    // OAuth proof). Conflict rules: identity already on this user -> refresh row (re-link);
    // on a BARE VIEWER row -> absorb it (§3.1a); on a real account -> Result failure IDENTITY_IN_USE.
    Task<Result<UserIdentityDto>> LinkAsync(Guid userId, string provider, ExternalIdentityProof proof, CancellationToken ct = default);

    // Refuses: last remaining identity (LAST_IDENTITY); provider of a non-suspended owned channel
    // (CHANNEL_DEPENDS_ON_IDENTITY — the channel's API actor would vanish). Revokes the login
    // connection's tokens via the vault; audit-logs.
    Task<Result> UnlinkAsync(Guid userId, string provider, CancellationToken ct = default);

    // Moves the primary flag; refreshes User.Username/DisplayName/avatar + User.Platform projection.
    Task<Result> SetPrimaryAsync(Guid userId, string provider, CancellationToken ct = default);
}
```

`ExternalIdentityProof` = `sealed record (string Provider, string ProviderUserId, string Username, string? DisplayName, string? AvatarUrl, Guid? ConnectionId)` — produced only by the OAuth handlers (device poll / code callback), never from client input.

### 3.1a Viewer-row absorption (the only merge that exists)

When `LinkAsync` finds the identity bound to another `User` that is a **bare viewer row** — owns no `Channel`, has no `ChannelMembership`, `IsPlatformPrincipal == false`, `IsBot == false` — the row is **absorbed** in one `IUnitOfWork` transaction: every domain that stores per-viewer state re-keys `AbsorbedUserId → IntoUserId`, the husk row is soft-deleted (`IsAnonymized` untouched), `ViewerRowAbsorbedEvent` is published, and the identity moves. Re-keying is decentralized: each owning domain registers an

```csharp
public interface IViewerMergeParticipant   // auto-discovered like SeedOnOnboarding handlers
{
    // Re-key all rows owned by this domain from absorbed -> into. Idempotent; unique-collision
    // rule: when `into` already has a row for the same (BroadcasterId, ...) key, KEEP `into`'s row
    // and fold counters/balances additively where the domain defines addition (currency balances,
    // watch minutes); otherwise drop the absorbed duplicate.
    Task MergeAsync(Guid absorbedUserId, Guid intoUserId, CancellationToken ct);
}
```

Initial participant set (each in its owning module): community standings, currency accounts + ledger, per-viewer data store, quotes authorship, analytics viewer aggregates, TTS voice prefs, permit grants, song-request history/trust. A **real** account (owns a channel / has memberships / principal / bot) is **never** merged — `IDENTITY_IN_USE`; the user must unlink from that account first (§9.2).

### 3.2 `ILoginProviderRegistry` + descriptors — new

Same descriptor pattern as integration OAuth (a provider is data, not a fork):

```csharp
public sealed record LoginProviderDescriptor(
    string Key,                       // "twitch" | "youtube" | "kick"
    string DisplayName,
    LoginFlows SupportedFlows,        // [Flags] DeviceCode | AuthCodePkce | AuthCode
    string FeatureFlagKey,            // platform feature flag gating the provider ("" = always on)
    IReadOnlyList<string> LoginScopes // minimal identify scopes for a LOGIN (not streamer scopes)
);

public interface ILoginProviderRegistry
{
    IReadOnlyList<LoginProviderDescriptor> All { get; }
    // Enabled = descriptor registered AND its feature flag resolves true for this deployment.
    Task<IReadOnlyList<LoginProviderDescriptor>> EnabledAsync(CancellationToken ct = default);
    Result<LoginProviderDescriptor> Get(string key);
}

public interface ILoginIdentityProvider    // one per descriptor, keyed by Key
{
    string Key { get; }
    // Device-code pair (twitch today; youtube via Google device flow when enabled).
    Task<Result<DeviceCodeStartDto>> StartDeviceAsync(CancellationToken ct = default);
    Task<Result<ExternalIdentityProof>> PollDeviceAsync(string deviceCode, CancellationToken ct = default);
    // Auth-code (+PKCE) path for providers without device flow (kick).
    Task<Result<Uri>> BuildAuthorizeUrlAsync(string state, string redirectUri, CancellationToken ct = default);
    Task<Result<ExternalIdentityProof>> ExchangeCodeAsync(string code, string redirectUri, CancellationToken ct = default);
}
```

Shipped registrations: **twitch** (`DeviceCode | AuthCode`, flag `""` — always on). **youtube** (`DeviceCode | AuthCode`, flag `use_youtube_login`) and **kick** (`AuthCodePkce`, flag `use_kick_login`) are registered with their descriptors but their flags default **off**; their `ILoginIdentityProvider` implementations ship when each platform's chat/API seams do (until then the descriptor exists, the flag is off, and the registry simply never lists them as enabled — no dead buttons, no rewrites later). **Enabling a new login provider = implement `ILoginIdentityProvider`, register the pair in DI, flip the flag.**

### 3.3 `IAuthService` (extend — do not fork)

`AuthenticateWithDeviceAsync`/`callback` internals stop hard-coding Twitch: the OAuth proof resolves through `IUserIdentityService.ResolveUserAsync(provider, providerUserId, getOrCreate: true)` and then the existing session/JWT issuance runs unchanged. JWT gains an `idp` claim (login provider key). Everything else in `identity-auth.md` §3 stands.

## 4. DTOs

`UserIdentityDto` (`provider`, `providerUserId`, `providerUsername`, `providerDisplayName?`, `providerAvatarUrl?`, `isPrimary`, `linkedAt`, `lastLoginAt?`), `LoginProviderDto` (`key`, `displayName`, `flows`, `enabled`), `DeviceCodeStartDto` (existing twitch shape, reused verbatim). Register all in `ApiContractTest`; refresh `server/openapi/v1.json`.

## 5. Controller endpoints

`AuthController` extends in place. **Route generalization:** `auth/{provider}/device` + `auth/{provider}/device/poll` with `provider` validated against `ILoginProviderRegistry.EnabledAsync` (404 `UNKNOWN_PROVIDER` / 403 `PROVIDER_DISABLED`). The existing literal `auth/twitch/device[/poll]` routes ARE the `provider=twitch` case of the pattern — same handlers, zero client break. Identity routes are **platform-JWT self-scoped** (own account; no tenant, no Gate-2 key — same plane as `auth/me`), rate-limited `auth`.

| Route | Verb | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `auth/providers` | GET | — | `StatusResponseDto<IReadOnlyList<LoginProviderDto>>` | `[AllowAnonymous]` (login screen needs it pre-auth) |
| `auth/{provider}/device` | POST | — | `StatusResponseDto<DeviceCodeStartDto>` | — (OAuth handshake; enabled providers only) |
| `auth/{provider}/device/poll` | POST | `DevicePollRequest` | `StatusResponseDto<object>` (tokens+user) | — (OAuth handshake) |
| `auth/identities` | GET | — | `StatusResponseDto<IReadOnlyList<UserIdentityDto>>` | — (any authenticated user, own identities) |
| `auth/identities/{provider}/link` | POST | — | `StatusResponseDto<DeviceCodeStartDto>` | — (own account; starts the provider's link flow) |
| `auth/identities/{provider}/link/poll` | POST | `DevicePollRequest` | `StatusResponseDto<UserIdentityDto>` | — (own account; completes link, §3.1 conflict rules) |
| `auth/identities/{provider}/primary` | PUT | — | `StatusResponseDto<object>` | — (own account) |
| `auth/identities/{provider}` | DELETE | — | `StatusResponseDto<object>` | — (own account; §3.1 refusal rules) |

Auth-code-flow providers reuse the existing callback route with a `link:{userId}` state variant routed by the state registry (same mechanism as the existing `user`/`bot`/`channel_bot` state routing).

## 6. Pipeline actions

**None.**

## 7. DI registration

```csharp
services.AddScoped<IUserIdentityService, UserIdentityService>();          // Infrastructure/Identity
services.AddSingleton<ILoginProviderRegistry, LoginProviderRegistry>();   // descriptors are data
services.AddScoped<ILoginIdentityProvider, TwitchLoginIdentityProvider>();// wraps the existing device-code impl
// IViewerMergeParticipant implementations: auto-discovered by assembly scan (SeedOnOnboarding pattern).
```

## 8. Migration (additive, in-place — never regenerate Initial, never force logout)

One migration pair (SQLite + Postgres; new `DbSet`s break the `IApplicationDbContext` test fakes — update them in the same slice):
1. Create `UserIdentities`; backfill one `twitch` row per existing `User` from (`TwitchUserId`, `Username`, `DisplayName`, `ProfileImageUrl`) with `IsPrimary = true`.
2. `Users.TwitchUserId` → nullable (values kept; projection semantics from now on).
3. `Channels` + `Provider` (backfill `'twitch'`) + `ExternalChannelId` (backfill `= TwitchChannelId`); `TwitchChannelId` → nullable projection; unique `(Provider, ExternalChannelId)`.
4. `EventJournal.ActorTwitchUserId` → rename `ActorExternalUserId` + add `ActorProvider` (backfill `'twitch'` where the old column was non-null).

Sessions, JWTs, and refresh tokens are untouched — nobody is logged out.

## 9. Decisions (resolved)

1. **Identity table over widening `User`.** `(Provider, ProviderUserId)` unique rows; `User.TwitchUserId`/`Channel.TwitchChannelId` remain as maintained nullable projections for the Twitch-hot paths — no big-bang rewrite of Helix call sites.
2. **No full account merge.** Linking an identity owned by a *real* account (owns a channel / memberships / principal / bot) is refused (`IDENTITY_IN_USE`) — re-pointing a tenant owner's FK graph is catastrophic-risk with zero current users to justify it. The supported path: unlink from the other account, then link. The **bare-viewer absorption** (§3.1a) covers the actual common case: you chatted somewhere as a viewer, later log in — your standing/currency/history follows you.
3. **One identity per provider per user.** Re-linking the same provider replaces the row (`(UserId, Provider)` unique). Users wanting two Twitch accounts have two NomNomzBot accounts — matching how the platforms themselves behave.
4. **A channel is single-platform; no simulcast grouping.** Two platform presences = two tenants under one owner; the channel switcher already models this. Cross-platform aggregation is a dashboard/analytics view concern, not an identity primitive.
5. **Login providers are feature-flagged descriptors** (`use_youtube_login`, `use_kick_login`, default off; twitch always on). The login screen reads `GET auth/providers` — no hardcoded buttons, nothing to rewrite on enable.
6. **Tokens stay in the vault.** `UserIdentity` carries no secrets; `ConnectionId` links to the user-level `IntegrationConnection` (`BroadcasterId = null`), which already models per-provider scopes/status/refresh.
7. **Standing/actor attribution goes through `ResolveUserAsync`.** Every ingest path that today does a `TwitchUserId` lookup migrates to the one resolver (get-or-create per the viewer-identity rule); `EventJournal` keeps raw external id + provider for audit fidelity alongside the resolved internal id.
8. **Federation/OIDC is unrelated.** `federation-oidc.md` covers operator/enterprise SSO into the platform plane; this spec covers streamer/viewer platform identities. Neither replaces the other.
