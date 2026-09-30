# Platform Identity — Interface Specification

**Status:** Implementable. Code from this directly.
**Sources (authoritative):** `identity-auth.md` (auth surface this extends), `2026-06-16-database-schema.md` (Domain A), the multi-platform readiness audit (2026-07-04), `roles-permissions.md` (planes/gates), `federation-oidc.md` (SSO — a *different* concern: operator/enterprise SSO into the platform, not streamer platform identity).

**Binding conventions:** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable` enabled; async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR; responses `StatusResponseDto<T>` / `PaginatedResponse<T>`; controllers `[ApiVersion("1.0")]`; surrogate PK `guid` via `Guid.CreateVersion7()`; soft-delete global filter; Newtonsoft.Json.

**Goal.** Login and account identity are **platform-agnostic**: a provider is a registered descriptor, and any platform can be the first login (`PRODUCT-ALIGNMENT.md` D2). The identity table (`UserIdentity`) and the generic seams (`IUserIdentityService`, `ILoginProviderRegistry`, and the two provider seams `ILoginIdentityProvider` for device flow and `IAuthCodeLoginProvider` for auth-code + PKCE) exist; Twitch, Kick, YouTube and X are all shipped login providers (§3.2). `User.TwitchUserId` / `Channel.TwitchChannelId` remain maintained projections for the Helix hot paths.

---

## 0. Model

- A **User** is the internal person (UUIDv7 `Id`, the only FK target). A user has **1..n linked external identities**, at most one per provider.
- A **UserIdentity** is one proven external account (`(Provider, ProviderUserId)` unique across the system). Any linked identity can log the user in. Exactly one identity is **primary** (display/default; seeds `User.Username`/avatar refresh).
- A **Channel** is the streamer's ONE channel = the tenant (`BroadcasterId`), spanning platforms. Each platform presence is a **`PlatformConnection`** row under it (`ChannelId`, `Provider`, `ExternalChannelId`, …). A streamer live on Twitch **and** YouTube has one Channel and two PlatformConnections — one set of commands/timers/settings/economy fanning out to every connected platform (decided §9.4, `PRODUCT-ALIGNMENT.md` D1). Vocabulary: **channel** = the tenant; **platform connection** = one platform's presence under it.
- **Tokens never live on the identity.** The vault (`IntegrationConnection` + `IntegrationToken`, identity-auth §3.4) stays the single token store; `UserIdentity.ConnectionId` points at the login connection (`BroadcasterId = null` = user-level, the existing global shape).
- **Denormalized projections stay.** `User.TwitchUserId` and `Channel.TwitchChannelId` are hot-path projections of the Twitch identity/channel rows — maintained by this subsystem, **nullable** once the identity table lands (a YouTube-only user has none). `User.Platform` = the primary identity's provider.

## 1. Entities

| Entity | Schema | Base | Key fields (type) | Notes |
|---|---|---|---|---|
| `UserIdentity` | A.6 (new) | `SoftDeletableEntity` | `Id Guid` PK; `UserId Guid` FK→User; `Provider string(20)` [VC:enum `Platform` = `twitch\|kick\|youtube\|twitter`]; `ProviderUserId string(100)`; `ProviderUsername string(255)`; `ProviderDisplayName string(255)?`; `ProviderAvatarUrl string(2048)?`; `IsPrimary bool`; `ConnectionId Guid?` FK→IntegrationConnection; `LinkedAt DateTime`; `LastLoginAt DateTime?` | **Unique** `(Provider, ProviderUserId)`; **unique** `(UserId, Provider)` (one identity per provider per user; a second account of the same provider is refused, §3.1). GLOBAL (not tenant-scoped). |
| `User` (extend) | A.1 | — | `TwitchUserId` → **nullable**; `Platform` = primary identity's provider (projection) | Backfill migration §8. No other column changes. |
| `Channel` (extend) | A.2 | — | `TwitchChannelId` → **nullable** projection (filled iff a `twitch` PlatformConnection exists); `OwnerUserId` stays **unique** (one Channel per owner) | The tenant. Platforms attach as `PlatformConnection` rows. **As built** the entity still carries `Provider` and `ExternalChannelId` (backfilled `twitch`); removing them is the target (S019-remaining). **Open owner question:** does `Channel.Provider` stay as a projection of the primary connection, or is it removed? Invariant (TARGET, not enforced yet): `OwnerUserId` holds a `UserIdentity` for every `PlatformConnection.Provider` (enforced at connect and at unlink §3.1). |
| `PlatformConnection` | A.7 (new) | `SoftDeletableEntity` | `Id Guid` PK; `ChannelId Guid` FK→Channel (= `BroadcasterId`, Index); `Provider string(20)` [VC:enum `Platform` = `twitch\|kick\|youtube\|twitter`]; `ExternalChannelId string(100)`; `DisplayName string(255)` (the platform-side channel name); `IsPrimary bool` (display/default target); `IsLive bool`. **TARGET, not built:** `ConnectionId Guid?` FK→IntegrationConnection (the platform's streamer token) and `ConnectedAt DateTime`. | **Unique** `(Provider, ExternalChannelId)` (built); **unique** `(ChannelId, Provider)` (one presence per platform per channel) is TARGET — as built `ChannelId` carries a plain index. Tenant-scoped (`BroadcasterId = ChannelId`). As built the Twitch login provisioner stamps one primary row for a brand-new channel (S019b); attaching a second platform to an existing channel is S019-remaining. The target: every per-platform subsystem (chat ingest, EventSub/webhooks, API actor, moderation fan-out, SR item `Provider`) keys its platform leg on this row; all tenant scoping stays on the one Channel. |
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
    // User + UserIdentity pair when unseen; false + unseen => IDENTITY_NOT_FOUND.
    Task<Result<Guid>> ResolveUserAsync(string provider, string providerUserId, bool getOrCreate, CancellationToken ct = default);

    // AS BUILT: attach a proven external identity to the CALLER's account as a NON-primary identity (called by the
    // link flow AFTER the OAuth proof; the provider comes from proof.Provider). Idempotent when the identity is
    // already this user's. Identity bound to a DIFFERENT user -> IDENTITY_ALREADY_LINKED; this user already holds
    // an identity for that provider -> PROVIDER_ALREADY_LINKED (both 409). Publishes UserIdentityLinkedEvent.
    // TARGET (S024): an identity bound to a BARE VIEWER row is absorbed (§3.1a) instead of refused; a real
    // account keeps returning a conflict (IDENTITY_IN_USE, §9.2).
    Task<Result<UserIdentityDto>> LinkAsync(Guid userId, ExternalIdentityProof proof, CancellationToken ct = default);

    // AS BUILT: unlinks by identity id (not provider key). Refuses the primary identity (PRIMARY_IDENTITY — set
    // another primary first), the user's last identity (LAST_IDENTITY) and an id that is not the caller's
    // (IDENTITY_NOT_FOUND). Soft-deletes the row, frees both unique slots, publishes UserIdentityUnlinkedEvent.
    // TARGET, not built: refuse the provider of a non-suspended owned channel (CHANNEL_DEPENDS_ON_IDENTITY — the
    // channel's API actor would vanish); revoke the login connection's tokens via the vault; audit-log.
    Task<Result> UnlinkAsync(Guid userId, Guid identityId, CancellationToken ct = default);

    // AS BUILT: moves the single IsPrimary marker to identityId and points User.Platform at its provider;
    // returns the new primary. Publishes PrimaryIdentityChangedEvent. TARGET: also refresh User.Username/
    // DisplayName/avatar from the new primary.
    Task<Result<UserIdentityDto>> SetPrimaryAsync(Guid userId, Guid identityId, CancellationToken ct = default);

    // AS BUILT (identity-table half of absorption, §3.1a): re-parents the absorbed user's identities onto the
    // survivor as non-primary, drops any whose provider the survivor already holds, guarantees exactly one primary.
    Task<Result> MergeIdentitiesAsync(Guid survivingUserId, Guid absorbedUserId, CancellationToken ct = default);
}
```

`ExternalIdentityProof` = `sealed record (string Provider, string ProviderUserId, string Username, string? DisplayName, string? AvatarUrl, Guid? ConnectionId)` — produced only by the OAuth handlers (device poll / code callback), never from client input.

### 3.1a Viewer-row absorption (the only merge that exists) — TARGET, not built (S024)

**As built:** `LinkAsync` never absorbs — an identity bound to any other user returns `IDENTITY_ALREADY_LINKED`. `MergeIdentitiesAsync` (§3.1) moves the identity rows only. The `ViewerRowAbsorbedEvent` type is defined but nothing publishes it, and `IViewerMergeParticipant` does not exist yet.

**Target:** when `LinkAsync` finds the identity bound to another `User` that is a **bare viewer row** — owns no `Channel`, has no `ChannelMembership`, `IsPlatformPrincipal == false`, `IsBot == false` — the row is **absorbed** in one `IUnitOfWork` transaction: every domain that stores per-viewer state re-keys `AbsorbedUserId → IntoUserId`, the husk row is soft-deleted (`IsAnonymized` untouched), `ViewerRowAbsorbedEvent` is published, and the identity moves. Re-keying is decentralized: each owning domain registers an

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

Required participant set (each in its owning module; the build is S024): community standings, viewer restrictions (`ChannelViewerRestriction`, moderation.md J.12), currency accounts + ledger, per-viewer data store, quotes authorship, analytics viewer aggregates, TTS voice prefs, pronoun choice, permit grants, song-request history/trust, giveaway entries. A **real** account (owns a channel / has memberships / principal / bot) is **never** merged — `IDENTITY_IN_USE`; the user must unlink from that account first (§9.2).

### 3.2 `ILoginProviderRegistry` + descriptors — new

Same descriptor pattern as integration OAuth (a provider is data, not a fork):

```csharp
public sealed record LoginProviderDescriptor(
    string Key,                       // "twitch" | "youtube" | "kick" | "twitter"
    string DisplayName,               // "Twitch" | "YouTube" | "Kick" | "X"
    LoginFlows SupportedFlows,        // [Flags] DeviceCode | AuthCodePkce | AuthCode
    string FeatureFlagKey,            // operator kill-switch per deployment ("" = always on; EVERY shipped provider ships "", PRODUCT-ALIGNMENT D2)
    IReadOnlyList<string> LoginScopes // minimal identify scopes for a LOGIN (not streamer scopes)
);

public interface ILoginProviderRegistry
{
    IReadOnlyList<LoginProviderDescriptor> All { get; }
    // Enabled = descriptor registered AND (FeatureFlagKey == "" OR that flag resolves true) AND the provider's OAuth
    // client id is configured. The client-id check is a SEPARATE hide rule, not a shipping flag: a provider with no
    // client id would answer 503 on its authorize/device call, so it is reported enabled=false and the login screen
    // shows no dead button. Twitch's client id is a required boot credential, so it is never hidden.
    Task<IReadOnlyList<LoginProviderDescriptor>> EnabledAsync(CancellationToken ct = default);
    Result<LoginProviderDescriptor> Get(string key);
}

public interface ILoginIdentityProvider    // device-flow providers, one per descriptor, keyed by Key
{
    string Key { get; }
    // Device-code pair (twitch via the established AuthService flow; youtube via the Google device flow).
    Task<Result<DeviceCodeStartDto>> StartDeviceAsync(CancellationToken ct = default);
    Task<Result<ExternalIdentityProof>> PollDeviceAsync(string deviceCode, CancellationToken ct = default);
}

public interface IAuthCodeLoginProvider    // auth-code + PKCE providers (kick, twitter), one per descriptor
{
    string Key { get; }
    Task<Result<Uri>> BuildAuthorizeUrlAsync(string state, string redirectUri, string codeChallenge, CancellationToken ct = default);
    Task<Result<ExternalIdentityProof>> ExchangeCodeAsync(string code, string redirectUri, string codeVerifier, CancellationToken ct = default);
}
```

Shipped registrations (all always on, flag `""` — `PRODUCT-ALIGNMENT.md` D2): **twitch** (`DeviceCode | AuthCode`), **youtube** (`DeviceCode | AuthCode`), **kick** (`AuthCodePkce`), **twitter** (`AuthCodePkce`, display "X"). As built the implementations are `GoogleYouTubeLoginProvider` (`ILoginIdentityProvider`), `KickLoginProvider` and `TwitterLoginProvider` (`IAuthCodeLoginProvider`); Twitch device login runs through `IAuthService.StartTwitchDeviceLoginAsync`, not a provider class. The login screen renders the providers `GET auth/providers` reports with `enabled = true`. `FeatureFlagKey` remains on the descriptor as an operator kill-switch per deployment, never a shipping gate. **Delta to close:** `LoginProviderRegistry` still registers youtube, kick and twitter with `use_youtube_login` / `use_kick_login` / `use_twitter_login`; D2 says they ship `""`, and the client-id presence check alone decides whether their button shows. **Adding a login provider = implement `ILoginIdentityProvider` (device) or `IAuthCodeLoginProvider` (auth-code + PKCE), register it and its descriptor in DI.**

### 3.3 `IAuthService` (extend — do not fork)

`AuthenticateWithDeviceAsync`/`callback` internals stop hard-coding Twitch: the OAuth proof resolves through `IUserIdentityService.ResolveUserAsync(provider, providerUserId, getOrCreate: true)` and then the existing session/JWT issuance runs unchanged. JWT gains an `idp` claim (login provider key). Everything else in `identity-auth.md` §3 stands.

## 4. DTOs

`UserIdentityDto` (`provider`, `providerUserId`, `providerUsername`, `providerDisplayName?`, `providerAvatarUrl?`, `isPrimary`, `linkedAt`, `lastLoginAt?`), `LoginProviderDto` (`key`, `displayName`, `flows`, `enabled`), `DeviceCodeStartDto` (existing twitch shape, reused verbatim). Register all in `ApiContractTest`; refresh `server/openapi/v1.json`.

## 5. Controller endpoints

`AuthController` extends in place. **Route generalization:** `auth/{provider}/device` + `auth/{provider}/device/poll` with `provider` validated against `ILoginProviderRegistry.EnabledAsync` (404 `UNKNOWN_PROVIDER` / 403 `PROVIDER_DISABLED`). The existing literal `auth/twitch/device[/poll]` routes ARE the `provider=twitch` case of the pattern — same handlers, zero client break. Identity routes are **platform-JWT self-scoped** (own account; no tenant, no Gate-2 key — same plane as `auth/me`), rate-limited `auth`.

| Route | Verb | Request | Response | Plane / floor · Gate-2 action key |
|---|---|---|---|---|
| `auth/providers` | GET | — | `StatusResponseDto<IReadOnlyList<LoginProviderDto>>` (every registered descriptor, each with its `enabled` flag) | `[AllowAnonymous]` (login screen needs it pre-auth) |
| `auth/{provider}/device` | POST | — | `StatusResponseDto<DeviceCodeStartDto>` | — (OAuth handshake; enabled providers only) |
| `auth/{provider}/device/poll` | POST | `DevicePollRequest` | `StatusResponseDto<object>` (tokens+user) | — (OAuth handshake) |
| `auth/identities` | GET | — | `StatusResponseDto<IReadOnlyList<UserIdentityDto>>` | — (any authenticated user, own identities) |
| `auth/identities/{provider}/link` | POST | — | `StatusResponseDto<OAuthStartDto>` (kick / twitter: authorize URL + state) or `StatusResponseDto<DeviceCodeStartDto>` (youtube) | — (own account; starts the provider's link flow; `twitch` is refused — it is the primary sign-in) |
| `auth/identities/{provider}/link/poll` | POST | `DevicePollRequest` | `StatusResponseDto<object>` (`{ status, identity? }`; `identity` is the `UserIdentityDto` once authorized) | — (own account; device providers only; completes link, §3.1 conflict rules) |
| `auth/identities/{identityId:guid}/primary` | POST | — | `StatusResponseDto<UserIdentityDto>` | — (own account; keyed by identity id) |
| `auth/identities/{identityId:guid}` | DELETE | — | `StatusResponseDto<object>` | — (own account; §3.1 refusal rules; keyed by identity id) |
| `auth/{provider}/authorize` | GET | — | redirect to the provider | `[AllowAnonymous]` (auth-code + PKCE login start: kick, twitter) |
| `auth/{provider}/callback` | GET | — | redirect back to the client | `[AllowAnonymous]` (auth-code + PKCE callback; login, or link when the state carries a link user) |

Auth-code-flow providers (kick, twitter) finish a link on the shared `auth/{provider}/callback`: the server-side OAuth state is issued with kind `link` and the caller's `LinkUserId`, routed by the state registry (same mechanism as the existing `user`/`bot`/`channel_bot` state routing). Identity-write failures map to HTTP: `IDENTITY_NOT_FOUND` 404; `PRIMARY_IDENTITY`, `LAST_IDENTITY`, `IDENTITY_ALREADY_LINKED`, `PROVIDER_ALREADY_LINKED` 409.

## 6. Pipeline actions

**None.**

## 7. DI registration

```csharp
services.AddScoped<IUserIdentityService, UserIdentityService>();          // Infrastructure/Identity
services.AddSingleton<ILoginProviderRegistry, LoginProviderRegistry>();   // descriptors are data
services.AddScoped<ILoginIdentityProvider, GoogleYouTubeLoginProvider>(); // device flow; Twitch device login stays in IAuthService
services.AddScoped<IAuthCodeLoginProvider, KickLoginProvider>();           // auth-code + PKCE
services.AddScoped<IAuthCodeLoginProvider, TwitterLoginProvider>();        // auth-code + PKCE
// IViewerMergeParticipant implementations (TARGET, S024): auto-discovered by assembly scan (SeedOnOnboarding pattern).
```

## 8. Migration (additive, in-place — never regenerate Initial, never force logout)

One migration pair (SQLite + Postgres; new `DbSet`s break the `IApplicationDbContext` test fakes — update them in the same slice):
1. Create `UserIdentities`; backfill one `twitch` row per existing `User` from (`TwitchUserId`, `Username`, `DisplayName`, `ProfileImageUrl`) with `IsPrimary = true`.
2. `Users.TwitchUserId` → nullable (values kept; projection semantics from now on).
3. Create `PlatformConnections`; backfill one `twitch` row per existing `Channel` (`ExternalChannelId = TwitchChannelId`, `ExternalChannelName = Name`, `IsPrimary = true`, `ConnectionId` = the channel's streamer `IntegrationConnection`); `Channels.TwitchChannelId` → nullable projection; unique `(Provider, ExternalChannelId)` + `(ChannelId, Provider)`.
4. `EventJournal.ActorTwitchUserId` → rename `ActorExternalUserId` + add `ActorProvider` (backfill `'twitch'` where the old column was non-null).

As built, `PlatformConnections` carries only the unique `(Provider, ExternalChannelId)`, uses `DisplayName` for the channel name, and has no `ConnectionId` or `ConnectedAt` column (§1).

Sessions, JWTs, and refresh tokens are untouched — nobody is logged out.

## 9. Decisions (resolved)

1. **Identity table over widening `User`.** `(Provider, ProviderUserId)` unique rows; `User.TwitchUserId`/`Channel.TwitchChannelId` remain as maintained nullable projections for the Twitch-hot paths — no big-bang rewrite of Helix call sites.
2. **No full account merge.** Linking an identity owned by a *real* account (owns a channel / memberships / principal / bot) is refused (target code `IDENTITY_IN_USE`; as built every identity owned by another user returns `IDENTITY_ALREADY_LINKED`, because absorption is not built, S024) — re-pointing a tenant owner's FK graph is catastrophic-risk with zero current users to justify it. The supported path: unlink from the other account, then link. The **bare-viewer absorption** (§3.1a, TARGET, S024) covers the actual common case: you chatted somewhere as a viewer, later log in — your standing/currency/history follows you.
3. **One identity per provider per user.** `(UserId, Provider)` is unique: linking the same identity again is idempotent, and linking a different account of a provider the user already holds is refused (`PROVIDER_ALREADY_LINKED`) — unlink first. Users wanting two Twitch accounts have two NomNomzBot accounts — matching how the platforms themselves behave.
4. **One channel, many platforms (`PRODUCT-ALIGNMENT.md` D1).** `Channel` is the streamer's one channel = the tenant (`BroadcasterId`); each platform presence is a `PlatformConnection(ChannelId, Provider, ExternalChannelId, …)` under it. This replaces the earlier "two Channel rows = two tenants" model: there is no ChannelGroup and no sibling Channel rows per platform. All tenant scoping (`ITenantScoped`, RLS, Gate-1, every config domain) is the one Channel; config domains (commands, timers, event responses, settings, moderation, economy, SR, TTS) are managed once and fan out to every connected platform with per-platform targets; chat, mod queues and alerts fan in to the one channel feed. Per-viewer state (standing, balance, restrictions, bans, TTS voice, pronouns) keys on `Users.Id` — one human — with `UserIdentity` linking that human's platform accounts. The first platform connected creates the Channel (D2); further platforms attach as PlatformConnections (onboarding-setup.md §3 "connect additional platforms").
5. **Login providers are shipped descriptors** (twitch, youtube, kick, twitter — all on, `FeatureFlagKey ""`, D2; the key is an operator kill-switch only). A provider whose OAuth client id is not configured is hidden by a separate presence check (§3.2), never by a shipping flag. The login screen reads `GET auth/providers` — no hardcoded buttons.
6. **Tokens stay in the vault.** `UserIdentity` carries no secrets; `ConnectionId` links to the user-level `IntegrationConnection` (`BroadcasterId = null`), which already models per-provider scopes/status/refresh.
7. **Standing/actor attribution goes through `ResolveUserAsync`.** Every ingest path that today does a `TwitchUserId` lookup migrates to the one resolver (get-or-create per the viewer-identity rule); `EventJournal` keeps raw external id + provider for audit fidelity alongside the resolved internal id.
8. **Federation/OIDC is unrelated.** `federation-oidc.md` covers operator/enterprise SSO into the platform plane; this spec covers streamer/viewer platform identities. Neither replaces the other.

---

## 10. Provider OAuth surfaces + X (`twitter`) — platform + login (verified against live docs 2026-07-09)

The login-provider implementations (`ILoginIdentityProvider`, §3.2) plug these verified surfaces into the
registry seam. Endpoints/flows verified against each provider's live docs (verify-current-docs rule).

| Provider | Flow | Authorize / device | Token / poll | Identity (userinfo) | Login scopes |
|---|---|---|---|---|---|
| **twitch** | DeviceCode + AuthCode | `id.twitch.tv/oauth2/device` | `id.twitch.tv/oauth2/token` | Helix `GET /helix/users` | `user:read:email` |
| **youtube** (Google) | DeviceCode | `oauth2.googleapis.com/device/code` | `oauth2.googleapis.com/token` (poll; `authorization_pending`/`slow_down`) | `openidconnect.googleapis.com/v1/userinfo` | `openid email profile` |
| **kick** | AuthCode + PKCE (S256) | `id.kick.com/oauth/authorize` | `id.kick.com/oauth/token` | Kick `GET /public/v1/users` (docs.kick.com) | `user:read` |
| **twitter / x** | AuthCode + PKCE (S256) | `twitter.com/i/oauth2/authorize` | `api.x.com/2/oauth2/token` | `api.x.com/2/users/me` | `users.read tweet.read offline.access` |

### Decisions (resolved 2026-07-09)

1. **X is a sibling streaming platform AND a login provider (`PRODUCT-ALIGNMENT.md` D3).** Enum key
   `twitter`, display name "X". X Live + its chat are a `PlatformConnection(Provider=twitter)` like
   Twitch/Kick/YouTube (TARGET, S031 — as built X is login-only and never owns a `Channel`), so one `Platform` key space (`twitch|kick|youtube|twitter`) serves
   `UserIdentity.Provider`, `LoginProviderDescriptor.Key` and `PlatformConnection.Provider` alike; an X
   identity can log in, create the channel as first login (D2), or attach to an existing channel. Posting
   tweets/clips is the **announcement targets** slice (S032): an outbound
   announcement-target capability per platform connection, not part of the login/identity seam.
2. **Google device flow needs a TV/limited-input client type;** the YouTube *login* uses only
   `openid email profile` (the broader `youtube.*` scopes belong to the music/channel integration, not login).
3. **Kick + Twitter are auth-code+PKCE only** (no device grant) — they reuse the existing state-registry
   callback (login / `link:{userId}` state variants), the same mechanism as the Twitch redirect flow.

### The provider seams (§3.2)

`Key`; `ILoginIdentityProvider.StartDeviceAsync`/`PollDeviceAsync` (device providers: twitch, youtube); `IAuthCodeLoginProvider.BuildAuthorizeUrlAsync`/
`ExchangeCodeAsync` (auth-code+PKCE providers: kick, twitter). Each returns an `ExternalIdentityProof`
`(Provider, ProviderUserId, Username, DisplayName?, AvatarUrl?, ConnectionId?)`; the generic login flow then
routes through `IUserIdentityService.ResolveUserAsync(getOrCreate)` → session/JWT. Creating or attaching a
`PlatformConnection` for a non-Twitch platform rides that platform's chat/API seam (`IChatPlatform`/`IPlatformApi`)
— S019-remaining (attach a further platform) and S031 (X Live).
