# NomNomzBot — Security Architecture

**Applies to:** Both hosted (multi-tenant SaaS) and self-hosted deployment modes
**Codebase:** `server/` — the .NET 10 / ASP.NET Core backend (the only deployed component)

> Scope note: this review covers the backend API. The previous frontend was removed and a
> replacement is undecided, so any finding below that mentions "the frontend" is forward-looking
> guidance for whatever client is built next — the backend-side fixes still apply today.

---

## Priority Legend

| Priority | Meaning |
|----------|---------|
| 🔴 CRITICAL | Can be exploited right now with trivial effort. Block the hosted launch. |
| 🟠 HIGH | Serious risk that must be fixed before the hosted version handles real user data. |
| 🟡 MEDIUM | Should be fixed but does not block the first hosted deployment. |
| 🟢 LOW | Best-practice improvement, low exploitability. |

---

## Table of Contents

1. [Token Storage & Encryption](#1-token-storage--encryption)
2. [JWT Security](#2-jwt-security)
3. [Setup Wizard Protection](#3-setup-wizard-protection)
4. [Multi-Tenant Data Isolation](#4-multi-tenant-data-isolation)
5. [OAuth Flow Security](#5-oauth-flow-security)
6. [Rate Limiting](#6-rate-limiting)
7. [Input Sanitization](#7-input-sanitization)
8. [Twitch EventSub Verification](#8-twitch-eventsub-verification)
9. [CORS & Network Security](#9-cors--network-security)
10. [Docker & Infrastructure Security](#10-docker--infrastructure-security)
11. [Logging & Secret Handling](#11-logging--secret-handling)
12. [Admin Access & Privilege Assignment](#12-admin-access--privilege-assignment)
13. [HTTPS Enforcement](#13-https-enforcement)
14. [GDPR / Data Subject Rights](#14-gdpr--data-subject-rights)
15. [Update & Patch Delivery (Self-hosted)](#15-update--patch-delivery-self-hosted)

---

## 1. Token Storage & Encryption

### Current State

All Twitch, Spotify, and Discord OAuth tokens are stored encrypted in the `Services` table via `EncryptionService`:

```csharp
// Format: Base64( IV[16 bytes] + CipherText )
// Algorithm: AES-256-CBC
// Key derivation: SHA-256( Base64Decode( Encryption:Key ) )
```

The encryption key is a single platform-wide secret (`Encryption:Key` / `ENCRYPTION_KEY`). Every tenant's tokens are encrypted with the same key.

The `Configuration` table has a `SecureValue` column used for system-level secrets (Twitch app client_secret, Spotify secret, etc.). These are stored **without encryption** — `UpsertSystemConfig(..., secure: true)` sets `cfg.SecureValue = value` (raw string, no call to `IEncryptionService`).

### Gaps & Vulnerabilities

**🟢 RESOLVED — `SecureValue` is sealed at rest**
`SystemController.UpsertSystemConfig` now seals the `SecureValue` column through `ITokenProtector` (per-subject DEK envelope, AAD-bound to `provider`+`field`) before persisting, and `GetSystemConfig` unseals on read. A raw DB read yields only sealed bytes — the Twitch/Spotify/Discord client secrets are no longer plaintext.

**🟢 RESOLVED — Per-subject key derivation (no single shared key)**
The legacy single-`ENCRYPTION_KEY` AES-CBC `IEncryptionService` was replaced by `ITokenProtector` envelope encryption: each secret is sealed under a **per-subject DEK** (minted via `ISubjectKeyService`), wrapped by the root KEK. A leaked KEK still requires the per-subject DEK to open any ciphertext, and the AAD binds each ciphertext to its subject+field so it is non-transplantable.

**🟢 RESOLVED — Authenticated encryption (AES-GCM envelope)**
The CBC-without-MAC primitive is gone; `ITokenProtector` uses an authenticated sealed envelope (key id + nonce + ciphertext + tag). `TryUnprotectAsync` returns null on any authentication failure — there is no padding-oracle surface.

**🟢 RESOLVED (by design) — Key rotation is non-destructive**
Because data is sealed under per-subject DEKs and only the DEKs are wrapped by the KEK, rotating the KEK re-wraps the DEKs without re-encrypting (or invalidating) any token. The original "changing `ENCRYPTION_KEY` logs everyone out" failure mode no longer applies.

### Recommendations

**Fix 1 — Encrypt `SecureValue` at rest (CRITICAL, fix before launch)**
Inject `IEncryptionService` into `SystemController` (or move credential persistence to a dedicated service) and call `_encryption.Encrypt(value)` before writing to `SecureValue`. Add a corresponding decrypt call in `GetSystemConfig`. This is a one-line change per write path but must be done before any real credentials are stored.

```csharp
// In UpsertSystemConfig:
if (secure)
    cfg.SecureValue = _encryption.Encrypt(value); // was: cfg.SecureValue = value;
else
    cfg.Value = value;

// In GetSystemConfig, decrypt before return:
string? raw = cfg?.SecureValue ?? cfg?.Value;
return (cfg?.SecureValue != null) ? _encryption.TryDecrypt(raw) : raw;
```

**Fix 2 — Migrate to AES-256-GCM (HIGH)**
Replace `EncryptionService` with GCM mode. The format becomes `Base64( Nonce[12 bytes] + CipherText + Tag[16 bytes] )`. This is a drop-in replacement that adds integrity checking. Existing CBC-encrypted rows must be re-encrypted on first read (lazy migration) or via a one-time migration script.

**Fix 3 — Per-tenant key derivation (HIGH, hosted mode)**
For hosted mode, derive per-user encryption keys using HKDF: `HKDF(masterKey, salt: userId, info: "token-encryption")`. The master key remains a platform secret, but compromising it requires an additional per-user derivation step and limits a stolen key to a single user's tokens. This requires a migration for existing rows.

**Fix 4 — Key rotation tooling (MEDIUM)**
Add a `dotnet run --project NomNomzBot.Api -- rotate-key --old-key <> --new-key <>` maintenance command (or a protected admin endpoint) that iterates every encrypted value, decrypts with the old key, and re-encrypts with the new key within a transaction.

---

## 2. JWT Security

### Current State

> **Rebuilt to a session model.** The auth layer no longer issues stateless refresh JWTs. It uses a
> DB-backed session store (`AuthSession` + `RefreshToken`): the **access token** is a short-lived signed
> JWT; the **refresh token** is an opaque random string stored only as a SHA-256 hash, rotated single-use
> with reuse detection, and revocable per-session or per-user. The points below describe the access JWT.

- Algorithm: HMAC-SHA256 (`SecurityAlgorithms.HmacSha256`) — correct
- Access token expiry: 60 minutes (configurable via `Jwt:ExpiryMinutes`)
- Token validation: issuer ✅, audience ✅, lifetime ✅, signature ✅, clock skew 1 min ✅
- JTI claim included in every token ✅
- Default secret in `appsettings.json`: `dev-secret-key-at-least-32-characters-long!!` — **boot-blocked in production** (§2 startup guard)
- Default secret in `docker-compose.yml`: same string, passed via `JWT_SECRET` env var fallback

### Gaps & Vulnerabilities

**🟢 RESOLVED — Production refuses to boot with default secrets**
`StartupSecretGuard.Validate` (called in `Program.cs` before authentication is wired) throws in any non-Development environment when `Jwt:Secret` is a known bundled default or shorter than 32 chars, or when `Encryption:Key` is still the committed development key. A misconfigured production deploy now fails fast instead of silently running with a forgeable signing key. The guard is pure and unit-tested (`StartupSecretGuardTests`).

**🟢 RESOLVED — Refresh tokens are server-side, revocable, and single-use**
The auth layer was rebuilt onto a DB-backed session store (`AuthSession` + `RefreshToken`). Refresh tokens are opaque random strings stored only as a SHA-256 hash; `RotateAsync` consumes one and issues a successor linked via `PreviousTokenHash`, and **presenting an already-consumed token is detected as reuse**. `RevokeSessionAsync` / `RevokeAllForUserAsync` terminate a session (or all of a user's) immediately — no JWT-secret rotation needed.

**🟢 RESOLVED — Access and refresh tokens no longer share a key**
The refresh token is not a JWT and is signed with no key — it is an opaque, hash-stored server-side credential. Only the short-lived access token is a signed JWT. Compromising the JWT signing key cannot forge refresh tokens.

**🟢 RESOLVED — Accurate expiry returned to the client**
`BuildAuthResult` returns `session.AccessExpiresAt` (the session's real access-token expiry), not a hardcoded hour, for both `HandleTwitchCallbackAsync` and `RefreshTokenAsync`.

### Recommendations

**Fix 1 — Startup guard for default secrets (CRITICAL)**
Add a startup validation that fails hard if `JWT_SECRET` or `ENCRYPTION_KEY` still equal the known defaults in `Production` environment:

```csharp
if (!builder.Environment.IsDevelopment())
{
    string secret = builder.Configuration["Jwt:Secret"] ?? "";
    if (secret == "dev-secret-key-at-least-32-characters-long!!" || secret.Length < 32)
        throw new InvalidOperationException(
            "Jwt:Secret must be set to a strong random value in production. " +
            "Generate one with: openssl rand -base64 32");

    string encKey = builder.Configuration["Encryption:Key"] ?? "";
    if (encKey == "ZGV2LWVuY3J5cHRpb24ta2V5LWZvci1sb2NhbC1kZXY=" || encKey.Length < 20)
        throw new InvalidOperationException(
            "Encryption:Key must be set to a strong random value in production.");
}
```

**Fix 2 — JWT refresh token revocation via Redis (HIGH)**
Store the JTI of each issued refresh token in Redis with the remaining TTL as the expiry. On `RefreshTokenAsync`, verify the JTI exists in Redis before accepting the token. On logout and on token refresh (rotation), delete the old JTI and write the new one. This bounds damage from stolen refresh tokens to the window between theft and next use.

**Fix 3 — Separate signing keys for access vs. refresh tokens (HIGH)**
Add `Jwt:RefreshSecret` config key. Use it only in `GenerateRefreshToken` and `ValidateToken` when the `refresh` role claim is present. This creates a key boundary between session establishment and API access.

**Fix 4 — Return accurate expiry (MEDIUM)**
Derive the expiry in `AuthResultDto` from the token itself rather than hardcoding:
```csharp
DateTime.UtcNow.Add(_jwt.AccessTokenExpiration) // expose property from IJwtTokenService
```

---

## 3. Setup Wizard Protection

### Current State

`SystemController` is decorated with `[AllowAnonymous]` at the class level. The credential-saving endpoints (`PUT /api/v1/system/setup/credentials/twitch`, `/spotify`, `/discord`) have no authentication, no setup-completion guard, and no rate limiting. The system is considered "ready" when Twitch credentials + platform bot are configured — determined dynamically on every status request.

### Gaps & Vulnerabilities

**🟢 RESOLVED — Credential endpoints lock once setup completes**
`SaveTwitchCredentials` / `SaveSpotifyCredentials` / `SaveDiscordCredentials` now call `IsSetupCompleteAsync` and return `403 Forbid` for non-admins once setup is complete. "Complete" means either the explicit `POST /system/setup/complete` flag was set, or the system is already ready (Twitch app + platform bot both configured). The anonymous window is now only the genuine first run; after that, repointing the platform's Twitch app requires a platform-admin JWT.

**🟢 RESOLVED — Setup-completion lock**
`system.setup_complete` (set by `POST /system/setup/complete`, or implied once the system is ready) flips the credential endpoints from anonymous to admin-only. Once set it is sticky — a later bot disconnect does not re-open them.

**🟢 RESOLVED — Status detail no longer leaks the deployment model**
The `/system/status` detail only emits the `.env` hint in `Development`; in any other environment the missing-Twitch detail is the generic `"Not configured"`. All three credential endpoints and `setup/complete` are `[EnableRateLimiting("auth")]`.

### Recommendations

**Fix 1 — Gate credential endpoints behind first-run flag (CRITICAL)**
Add a `SetupCompleted` boolean to the `Configuration` table (key: `system.setup_complete`). The setup endpoints check this flag:

```csharp
// PUT /system/setup/credentials/twitch
bool setupComplete = await IsSetupCompleteAsync(ct);
if (setupComplete && !User.IsInRole("admin"))
    return Forbid();
```

At the end of the setup wizard flow, set `system.setup_complete = true`. Once set, these endpoints require `[Authorize(Roles = "admin")]`. The status endpoint and bot-oauth-url endpoint may remain anonymous.

**Fix 2 — Rate limit all setup endpoints (HIGH)**
Apply `[EnableRateLimiting("auth")]` to `SaveTwitchCredentials`, `SaveSpotifyCredentials`, and `SaveDiscordCredentials`. Currently they have no rate limiting at all.

**Fix 3 — Scrub detail strings from status in production (MEDIUM)**
Return generic status strings in production, reserving the `.env` hint only for `IsDevelopment()`:

```csharp
string detail = hasTwitch ? "configured"
    : builder.Environment.IsDevelopment() ? "Set TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET in .env"
    : "missing";
```

---

## 4. Multi-Tenant Data Isolation

### Current State

Tenant context is resolved in `TenantResolutionMiddleware` from three sources (in priority order):
1. Route parameter: `/channels/{channelId}/...`
2. `X-Channel-Id` request header
3. `?channelId=` query string

The authenticated user's ID comes from the JWT `sub` claim. Controllers use `IApplicationDbContext` with EF Core global query filters that enforce `BroadcasterId == tenantId` at the ORM level. **`TenantResolutionMiddleware` validates the requested tenant before setting it** — via `IChannelAccessService.CanResolveTenantAsync`, which grants access only for the caller's own channel, an active moderator grant, an active management membership (roles-permissions Gate 1, §3.1), or a platform principal; everything else fails closed. Per-action authorization is then enforced by the **roles-permissions Gate 2** (`[RequireAction("<key>")]` → `IActionAuthorizationService`), comparing the caller's resolved level to the action's floor-clamped required level.

### Gaps & Vulnerabilities

**🟢 RESOLVED — Tenant access is validated by the middleware**
Previously the middleware set the tenant context from the request without verifying access. It now calls `IChannelAccessService.CanResolveTenantAsync` (own channel / moderator grant / management membership / platform principal) before setting the tenant, so passing another channel's `channelId` is rejected at the boundary. Service-layer queries remain tenant-filtered as defence in depth.

**🟡 MEDIUM — No integration test for cross-tenant access**
There are no tests that assert User A cannot access User B's resources. The EF global query filters provide protection only if they are configured correctly and not bypassed with `IgnoreQueryFilters()`.

**🟡 MEDIUM — Bot service record is global (`BroadcasterId == null`)**
The platform bot `Service` record has `BroadcasterId = null`. A bug in any query that fetches services without filtering on `BroadcasterId` could accidentally return the platform bot's token when a user requests their own bot status.

### Recommendations

**Fix 1 — Cross-tenant access test suite (MEDIUM)**
Add integration tests that:
- Create two users (UserA, UserB) with authenticated JWT tokens
- Assert UserA cannot read/write UserB's commands, timers, rewards, chat history
- Assert querying with UserA's JWT and UserB's channel ID is rejected

**Fix 2 — Centralize tenant ownership assertion — ✅ DONE**
This is now `IChannelAccessService.CanResolveTenantAsync(userId, channelId)`, called by `TenantResolutionMiddleware` before the tenant is set (own channel / moderator grant / management membership / platform principal; fails closed). It has DB-level behaviour tests (`ChannelAccessServiceTests`); the broader HTTP cross-tenant suite of Fix 1 is still outstanding.

---

## 5. OAuth Flow Security

### Current State

- Twitch OAuth redirect URIs are static strings from configuration (`Twitch:RedirectUri`, etc.)
- The `state` parameter is accepted and forwarded in `GetTwitchOAuthUrl` but **not validated on callback**
- Mobile clients can override `callback.RedirectUri` — this value is used directly in `ExchangeCodeAsync`
- Channel-bot state encodes `{ channel_id, redirect_uri }` as Base64 JSON (no signature)

### Gaps & Vulnerabilities

**🟠 HIGH — OAuth state not validated on callback (CSRF on OAuth flow)**
The `state` parameter is included in the authorization URL but `HandleTwitchCallbackAsync` does not verify that the returned `state` matches what was originally sent. An attacker can construct a malicious link, trick an admin into clicking it, and the resulting Twitch code exchange will link the attacker's Twitch account to the target session (OAuth CSRF / login CSRF).

**🟠 HIGH — Mobile `redirect_uri` override accepted without validation**
`HandleTwitchCallbackAsync` uses `callback.RedirectUri ?? _options.RedirectUri`. A client can supply any redirect URI. Twitch will reject mismatches, but the application should also validate the override against an allow-list of registered URIs to prevent manipulation.

**🟠 HIGH — Channel-bot `state` payload unsigned**
The channel-bot OAuth state (`{ channel_id, redirect_uri }`) is Base64-encoded JSON. Anyone can decode and modify it, then re-encode it. If an attacker can intercept or forge the state, they can associate a bot connection with an arbitrary channel.

**🟡 MEDIUM — No PKCE for mobile OAuth flows**
Mobile OAuth flows should use PKCE (RFC 7636) to prevent authorization code interception attacks. The current flow is standard authorization code without PKCE.

### Recommendations

**Fix 1 — Validate `state` on callback (HIGH)**
Before redirecting to Twitch, generate a cryptographically random nonce, store it in a short-lived cache (Redis, 10-minute TTL) keyed to the client's session, and include it as `state`. On callback, verify the returned `state` matches the stored nonce:

```csharp
// Generate
string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
await _cache.SetStringAsync($"oauth:state:{nonce}", userId, new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) });

// Validate on callback
string? storedUserId = await _cache.GetStringAsync($"oauth:state:{callback.State}");
if (storedUserId is null)
    return Result.Failure<AuthResultDto>("Invalid state parameter.", "INVALID_STATE");
await _cache.RemoveAsync($"oauth:state:{callback.State}");
```

**Fix 2 — Sign the channel-bot state payload (HIGH)**
Use HMAC-SHA256 over the JSON payload with a server-side secret before encoding. Verify the signature on callback before processing:

```csharp
string json = JsonSerializer.Serialize(new { channel_id = channelId, redirect_uri = state });
byte[] mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(_secret), Encoding.UTF8.GetBytes(json));
string signed = Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
    + "." + Convert.ToBase64String(mac);
```

**Fix 3 — Allow-list redirect URI overrides (HIGH)**
Validate `callback.RedirectUri` against a configured allow-list before use. Reject any URI not on the list.

---

## 6. Rate Limiting

### Current State

Two policies are configured in `Program.cs`:

| Policy | Limit | Scope |
|--------|-------|-------|
| `api` | 120 req/min | Per authenticated user ID, or per IP if anonymous |
| `auth` | 10 req/min | Per IP, for auth/bot endpoints |

Applied via `[EnableRateLimiting("auth")]` on `AuthController`, `ChannelBotController`, and `GetBotOAuthUrl`. `BaseController` appears to apply the `api` policy globally.

### Gaps & Vulnerabilities

**🟢 RESOLVED — Rate limiter keys on the real client IP**
`app.UseForwardedHeaders()` runs first in the pipeline, with `ForwardedHeadersOptions` trusting `X-Forwarded-For`/`-Proto` from the upstream proxy (loopback-only restriction cleared for the documented single-proxy model). `RemoteIpAddress` is now the real client, so the per-IP buckets partition correctly instead of lumping every user behind the proxy IP.

**🟢 RESOLVED — Setup credential endpoints rate-limited**
All three `PUT /system/setup/credentials/*` endpoints and `POST /system/setup/complete` carry `[EnableRateLimiting("auth")]` (see §3).

**🟢 RESOLVED — Sliding-window limiter**
Both the `api` (120/min) and `auth` (10/min) policies use `GetSlidingWindowLimiter` with 6 segments, so a window boundary cannot be exploited to burst 2× the limit.

### Recommendations

**Fix 1 — Configure forwarded headers for real client IP (HIGH)**
Add to the middleware pipeline before rate limiting:
```csharp
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    KnownProxies = { /* load balancer IPs */ },
});
```
In hosted mode with Cloudflare, use `CF-Connecting-IP` as the trusted source.

**Fix 2 — Rate limit setup endpoints (MEDIUM)**
Add `[EnableRateLimiting("auth")]` to all three credential endpoints in `SystemController`.

**Fix 3 — Migrate to sliding window for API policy (LOW)**
Replace `GetFixedWindowLimiter` with `GetSlidingWindowLimiter` for the general API policy to smooth out burst patterns.

---

## 7. Input Sanitization

### Current State

- Command names are normalized to lowercase (`ToLowerInvariant`) before storage
- Command response text and bot messages are stored as-is (no sanitization)
- No HTML encoding or XSS sanitization in backend API responses
- EF Core parameterized queries prevent SQL injection throughout
- Template variable expansion (`{{user.name}}`, etc.) happens in the pipeline engine at runtime

### Gaps & Vulnerabilities

**🟢 RESOLVED — Stored content is rendered safely**
The backend correctly returns raw values inside JSON (`System.Text.Json` escapes string values into valid JSON — re-encoding server-side would double-encode and corrupt JSON consumers). The XSS sink is the renderer, and the backend-served public pages handle it: `web/sr` and `web/overlay` route **every** user-controlled value through `escapeHtml` (which escapes `& < > " '`) before any `innerHTML`, and use `textContent` elsewhere. The Compose dashboard renders text safely by default.

**🟢 RESOLVED — Template substitution cannot be injected**
`TemplateEngine.Render` is a **single-pass** regex substitution (`{{(.+?)}}` → dictionary lookup) — it never re-expands a resolved value, so a user-supplied `{{args.N}}` whose value contains `{{…}}` is emitted literally, not re-evaluated. There is no scripting/eval path.

**🟢 RESOLVED — Length-bounded user input**
User-supplied string fields carry `[MaxLength]` (e.g. `Command.Name` 100, `Command.Response` 2000, `Command.Description` 500; `EventResponse` and `Timer` text fields likewise), so a single payload cannot bloat the database.

### Recommendations

**Fix 1 — HTML-encode string values before returning in API responses (MEDIUM)**
Add a JSON converter or middleware that applies `HttpUtility.HtmlEncode` to string fields tagged with a `[HtmlEncode]` attribute, or configure the frontend to always use safe text rendering (`innerText`, React's default JSX text rendering).

**Fix 2 — Audit pipeline template engine for injection (MEDIUM)**
Review `PipelineEngine` and all `ICommandAction` implementations. Ensure `{{args.N}}` substitution does not allow escaping to access other template variables or cause path traversal in file-writing actions.

**Fix 3 — MaxLength attributes on all user-supplied fields (LOW)**
Audit the Domain entities for string fields without `[MaxLength]` and add appropriate limits (e.g., 500 chars for command response, 32 for command name).

---

## 8. Twitch EventSub Verification

### Current State

NomNomzBot uses **WebSocket EventSub** (`wss://eventsub.wss.twitch.tv/ws`), not the webhook/HTTP delivery method. The TLS connection is established by the server, and subscriptions are created with a Bearer token that Twitch validates. There are no inbound webhook HTTP endpoints.

### Assessment

**No vulnerability here for the current architecture.** WebSocket EventSub does not require HMAC signature verification (that requirement applies only to webhook delivery). The TLS connection itself and the Bearer token authenticate the session.

The only scenario where this changes: if a future feature adds a public HTTPS callback endpoint for Twitch webhooks (e.g., for legacy callback-based webhooks or third-party services). In that case, Twitch signature verification via `X-Twitch-Eventsub-Message-Signature` (HMAC-SHA256 over `message-id + timestamp + body`) must be implemented before the endpoint is deployed.

---

## 9. CORS & Network Security

### Current State

CORS is configured with explicit origins from `Cors:Origins` (`appsettings.json`):
```json
["http://localhost:3000", "http://localhost:5173", "http://localhost:8081",
 "http://localhost:19006", "https://bot-dev.nomercy.tv", "https://bot-dev-api.nomercy.tv",
 "https://bot.nomercy.tv", "https://bot-api.nomercy.tv", "https://nomnomz.bot"]
```

`AllowCredentials()` is set, and `AllowAnyHeader()` + `AllowAnyMethod()` are used. The list still carries localhost dev origins from the removed frontend; prune it to the actual client origins in production once a client exists.

`AllowedHosts: "*"` in `appsettings.json`.

Scalar API reference (`/scalar`) and OpenAPI spec (`/openapi/v1.json`) are always served, including in production.

### Gaps & Vulnerabilities

**🟢 RESOLVED — Host-header filtering derived from `App:BaseUrl`**
When `AllowedHosts` is still the permissive `"*"`, `Program.cs` derives it from `App:BaseUrl`'s host (plus `localhost`/`127.0.0.1` for container health checks), so filtering is on with the correct host for any deployment from the one domain the operator already configures. An explicit `AllowedHosts` still wins. Redirect URIs are independently built from `App:BaseUrl`, not the `Host` header.

**🟢 RESOLVED — Scalar/OpenAPI gated out of production**
`MapOpenApi()` + `MapScalarApiReference()` now register only in `Development`, or in production when an operator explicitly opts in with `Api:ExposeDocs=true`. A default production deploy no longer publishes the full schema for reconnaissance.

**🟢 RESOLVED — Security headers on every response**
An early-pipeline middleware sets `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: no-referrer`, and `Permissions-Policy: geolocation=(), microphone=(), camera=()` on every response (static pages included). CSP is deferred to the page layer until a client content-security model is finalized.

**🟡 MEDIUM — JWT passed in query string for SignalR**
The `OnMessageReceived` event extracts `?access_token=` from the URL for SignalR connections. Tokens in query strings are:
- Logged by reverse proxies in access logs
- Stored in browser history
- Included in the `Referer` header on navigations

This is the [recommended SignalR pattern](https://learn.microsoft.com/en-us/aspnet/core/signalr/authn-and-authz) but carries the above risks.

### Recommendations

**Fix 1 — Set `AllowedHosts` to production domain (HIGH)**
```json
"AllowedHosts": "nomnomzbot.tv;*.nomnomzbot.tv"
```
In `appsettings.Production.json` or via environment variable.

**Fix 2 — Gate Scalar/OpenAPI behind environment or admin auth (HIGH)**
```csharp
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(...);
}
```
For self-hosted deployments where operators need it, gate behind a `[Authorize(Roles = "admin")]` middleware or an opt-in config flag.

**Fix 3 — Add security headers middleware (MEDIUM)**
```csharp
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Permissions-Policy"] = "geolocation=(), microphone=()";
    await next();
});
```
Add CSP after the frontend content security model is finalized.

**Fix 4 — Short-lived SignalR tokens (MEDIUM)**
Issue a dedicated short-lived (5 min) SignalR token separate from the main API JWT, so that URL-logged tokens expire quickly. The frontend requests this token immediately before establishing the SignalR connection.

---

## 10. Docker & Infrastructure Security

### Current State

From `docker-compose.yml` and `Dockerfile`:
- **API container**: No `USER` directive; runs as `root`
- **PostgreSQL**: Port `5432` mapped to host (`0.0.0.0:5432:5432`)
- **Redis**: Port `6379` mapped to host; no authentication configured
- **Adminer**: Port `8082` mapped to host; direct database GUI access with no extra auth layer
- **API ports** `5080` and `5081` mapped to host; no bind address restriction
- Default PostgreSQL password: `nomnomzbot_dev` (fallback when `POSTGRES_PASSWORD` not set)
- No Docker network isolation beyond a single `nomnomzbot` bridge network

### Gaps & Vulnerabilities

**🟢 RESOLVED — Adminer removed from the production compose**
`docker-compose.yml` no longer ships Adminer. The DB GUI now lives only in `docker-compose.dev.yml`, enabled explicitly with `docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d adminer`, and even there it binds to `127.0.0.1`. A production deploy has no database GUI surface.

**🟢 RESOLVED — Redis requires a password**
The Redis service starts with `--requirepass "${REDIS_PASSWORD}"` and the connection string carries it. Unauthenticated cache access (rate-limit counters, session state, refresh-token JTIs) is closed.

**🟢 RESOLVED — PostgreSQL/Redis ports bound to loopback**
Both database ports publish to `127.0.0.1` only (`docker compose config` confirms `host_ip: 127.0.0.1`), so they are reachable over the compose network and from the host, never from other interfaces.

**🟢 RESOLVED — API container runs as an unprivileged user**
The Dockerfile's final stage creates `appuser`, `chown`s `/app` to it (so rolling logs stay writable), and sets `USER appuser`. An RCE in the app or a dependency no longer lands as container root.

**🟢 RESOLVED — TLS-termination model documented**
The README "Production deployment (TLS)" section now states that the API speaks plain HTTP only inside the container and **must** sit behind a TLS-terminating reverse proxy (the bundled Cloudflare Tunnel, or Caddy/nginx with a worked example), with the DB ports loopback-bound so only the proxy reaches the API. See §13.

### Recommendations

**Fix 1 — Remove Adminer from production compose file (CRITICAL)**
Create a separate `docker-compose.dev.yml` that adds Adminer. Keep it out of the production compose entirely. If needed in production for maintenance, use `docker exec -it postgres psql` directly with proper access controls.

**Fix 2 — Add Redis authentication (HIGH)**
```yaml
redis:
  image: redis:7-alpine
  command: redis-server --requirepass "${REDIS_PASSWORD}"
```
Add `REDIS_PASSWORD` to `.env.example`. Update `ConnectionStrings:Redis` to include the password.

**Fix 3 — Bind database ports to localhost only (HIGH)**
```yaml
postgres:
  ports:
    - "127.0.0.1:${POSTGRES_PORT:-5432}:5432"
redis:
  ports:
    - "127.0.0.1:${REDIS_PORT:-6379}:6379"
```

**Fix 4 — Run API container as non-root (HIGH)**
In `Dockerfile`, after the build stage:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN adduser --disabled-password --gecos "" appuser
WORKDIR /app
COPY --from=build /app/publish .
USER appuser
ENTRYPOINT ["dotnet", "NomNomzBot.Api.dll"]
```

**Fix 5 — Add HTTPS documentation and enforce redirect (MEDIUM)**
Document that production deployments require a reverse proxy terminating TLS. Add a `HTTPS_REQUIRED=true` note in `.env.example`. `app.UseHttpsRedirection()` is already in `Program.cs` but only works if HTTPS is configured; document that Caddy or nginx with Let's Encrypt is the expected deployment target.

---

## 11. Logging & Secret Handling

### Current State

- `RequestLoggingMiddleware` logs method, path, status code, and timing — no request body or parameters
- `GlobalExceptionMiddleware` returns generic error messages in production, full messages in development
- Log files written to `logs/nomnomzbot-.log` with daily rolling
- No evidence of tokens or secrets appearing in log output
- Serilog configured to write to both console and file

### Gaps & Vulnerabilities

**🟢 RESOLVED — Bounded log retention**
The Serilog file sink now sets `retainedFileCountLimit: 30`, so rolling logs self-prune to ~30 days instead of growing without bound. (The file name was also corrected from the legacy `nomercybot-` prefix.)

**🟢 RESOLVED — PII exposure bounded by retention**
Request paths still carry Twitch user IDs (route params), but the 30-day cap bounds how long that personal data lives in logs, and it is covered by the platform privacy policy. No OAuth tokens or secrets are logged.

**🟢 RESOLVED — Structured, DB-backed audit logs for privileged actions**
Privileged operations write to dedicated DB tables, not just the general log: `IamAuditLog` (platform IAM / role changes, via `PlatformIamService`, indexed by `OccurredAt`) and `DeletionAuditLog` (GDPR erasures, via `GdprService`). These are queryable, structured, and outlive the rolling text logs — stronger than the originally-suggested separate log file.

### Recommendations

**Fix 1 — Add log retention configuration (MEDIUM)**
```csharp
.WriteTo.File("logs/nomnomzbot-.log",
    rollingInterval: RollingInterval.Day,
    retainedFileCountLimit: 30)  // keep 30 days
```

**Fix 2 — Define log data retention policy (MEDIUM)**
Document in operational runbook: logs are kept for 30 days, contain Twitch user IDs but no OAuth tokens or secrets, and are covered by the platform's privacy policy.

**Fix 3 — Structured admin audit log (LOW)**
Add a dedicated `AdminAuditLogger` that writes admin actions to a separate log sink (separate file or database table) with actor, action, target, and timestamp.

---

## 12. Admin Access & Privilege Assignment

### Current State

Platform-admin status is the `User.IsPlatformPrincipal` flag (it replaced the old `IsAdmin` boolean), sourced into the JWT and read by platform-plane authorization; finer platform IAM (principals, role assignments, with an `IamAuditLog`) is managed by `PlatformIamService`. Admin routes are gated with `[Authorize(Roles = "admin")]`.

### Gaps & Vulnerabilities

**🟢 RESOLVED — First admin bootstrapped from config, no raw SQL**
A self-hoster sets `App:InitialAdminTwitchId` (env `INITIAL_ADMIN_TWITCH_ID`, documented in `.env.example`) to their Twitch **user id**. On that account's next login, `AdminBootstrap.ShouldPromote` (pure, unit-tested: opt-in, exact-match, idempotent) promotes it to platform principal. No `UPDATE … SET IsPlatformPrincipal` by hand.

**🟢 RESOLVED — Admin/IAM management exists at the service layer**
`PlatformIamService` exposes resolve / create-principal / assign-role / revoke-assignment, each writing an `IamAuditLog` row, so admins can be listed and demoted programmatically (and an erased/compromised admin is revocable). The operator-facing surface for these operations is the KMP dashboard (the frontend phase), not a bespoke server-rendered UI.

### Recommendations

**Fix 1 — Bootstrap admin from environment variable (HIGH)**
In `DataSeeder.SeedAsync`, after seeding reference data:
```csharp
string? bootstrapAdminId = _config["App:InitialAdminTwitchId"];
if (!string.IsNullOrWhiteSpace(bootstrapAdminId))
{
    User? admin = await _db.Users.FirstOrDefaultAsync(u => u.Id == bootstrapAdminId, ct);
    if (admin is not null && !admin.IsAdmin)
    {
        admin.IsAdmin = true;
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Bootstrapped admin: {UserId}", bootstrapAdminId);
    }
}
```
Document `APP__INITIAL_ADMIN_TWITCH_ID` in `.env.example`.

**Fix 2 — Admin management API (MEDIUM)**
Add `GET /api/v1/admin/users` and `PUT /api/v1/admin/users/{id}/role` endpoints gated by `[Authorize(Roles = "admin")]`.

---

## 13. HTTPS Enforcement

### Current State

`app.UseHttpsRedirection()` is in the middleware pipeline but `ASPNETCORE_URLS=http://+:5000` in docker-compose means HTTPS is not bound. HTTPS termination is expected at a reverse proxy layer but this is not documented or enforced.

### Gaps & Vulnerabilities

**🟢 RESOLVED — Reverse-proxy / TLS requirement documented**
The README "Production deployment (TLS)" section states plainly that running the API port directly on the internet without TLS is unsupported, and gives two supported paths: the bundled `cloudflared` tunnel (public HTTPS, no inbound ports) or a Caddy/nginx reverse proxy with a worked Caddyfile that auto-provisions Let's Encrypt certs. `App:BaseUrl` is the single value an operator points at their HTTPS URL.

### Recommendations

**Fix 1 — Document and enforce reverse proxy requirement (HIGH)**
Add to README and setup documentation:
> Production deployments MUST place NomNomzBot behind a TLS-terminating reverse proxy. We recommend Caddy (`reverse_proxy localhost:5080`) which handles certificate provisioning automatically. Running the API directly on port 80/443 without TLS is not supported.

**Fix 2 — Include optional Caddy service in docker-compose (MEDIUM)**
Add a commented-out `caddy` service to `docker-compose.yml` as the recommended path:
```yaml
# Uncomment to enable automatic HTTPS via Caddy
# caddy:
#   image: caddy:2-alpine
#   ports: ["80:80", "443:443"]
#   volumes: ["./Caddyfile:/etc/caddy/Caddyfile"]
```

---

## 14. GDPR / Data Subject Rights

### Current State

`IGdprService` (exposed on `UsersController`) implements both data-subject rights, each guarded so a user can only act on their own data.

### Gaps & Vulnerabilities

**🟢 RESOLVED — User data export (GDPR Art. 20)**
`GET /api/v1/users/{userId}/data-export` (self-only) returns `GdprService.ExportUserDataAsync` — a JSON document of the user's profile, chat messages, moderation history, and vaulted OAuth connections.

**🟢 RESOLVED — User data erasure (GDPR Art. 17)**
`DELETE` erasure hard-deletes chat messages and records and **anonymizes** the user profile (`UsernameNormalized = "deleted_{id}"`) rather than relying on the soft-delete flag, so personal data is actually removed.

**🟢 RESOLVED — Token revocation on erasure**
Erasure first calls `_vault.RevokeConnectionAsync(connectionId, "gdpr_erasure")` for every vaulted OAuth connection, so the provider tokens are revoked before the record is anonymized.

### Recommendations

**Fix 1 — Data export endpoint (MEDIUM)**
Add `GET /api/v1/account/export` that returns a JSON archive of the authenticated user's commands, timers, rewards, integrations (without raw tokens), and account metadata.

**Fix 2 — Account deletion endpoint (MEDIUM)**
Add `DELETE /api/v1/account` that:
1. Revokes Twitch OAuth tokens via Helix
2. Nulls or deletes all personal data columns (name, image, etc.)
3. Hard-deletes or permanently anonymizes the user record
4. Logs the deletion event to the audit log

---

## 15. Update & Patch Delivery (Self-hosted)

### Current State

No in-app update mechanism exists. Self-hosters must monitor the GitHub repository for new releases and manually pull and rebuild.

### Gaps & Vulnerabilities

**🟡 MEDIUM — No security advisory channel**
If a critical vulnerability is discovered, there is no mechanism to notify self-hosted deployments. A self-hoster running an old version may be exposed indefinitely without knowing it.

### Recommendations

**Fix 1 — GitHub Releases with security advisories (MEDIUM)**
Use GitHub's Security Advisories feature for the repo. Tag security releases with `[SECURITY]` in release notes. Document in the README that operators should watch releases.

**Fix 2 — Version endpoint (LOW)**
Add `GET /health/version` that returns the running build version. Allows an operator to verify what version is deployed.

---

## Critical Fixes Before Hosted Launch

These must be resolved before the hosted version handles any real user data. Listed in order of exploit severity.

### 1. Lock setup credential endpoints (🟢 RESOLVED)

**File:** `SystemController.cs`
**Issue:** Any unauthenticated HTTP request could overwrite the platform Twitch `client_id`/`client_secret`.
**Resolution:** `IsSetupCompleteAsync` guard — once setup completes (explicit flag or system ready), the credential endpoints `403` non-admins; all are now rate-limited. See §3.

### 2. Encrypt `SecureValue` in the Configuration table (🔴 CRITICAL)

**File:** `SystemController.UpsertSystemConfig`
**Issue:** Twitch/Spotify/Discord client secrets stored in plaintext.
**Fix:** Inject `IEncryptionService`, encrypt on write, decrypt on read. Two lines of code. See §1 Fix 1.

### 3. Startup guard for default secrets (🟢 RESOLVED)

**File:** `Program.cs` → `StartupSecretGuard.Validate`
**Issue:** If `JWT_SECRET` or `ENCRYPTION_KEY` env vars are not set, the app ran with publicly known default values.
**Resolution:** `StartupSecretGuard.Validate` throws in any non-Development environment on a default/short `Jwt:Secret` or the bundled `Encryption:Key`. Unit-tested.

### 4. Remove Adminer from production docker-compose (🟢 RESOLVED)

**File:** `docker-compose.yml` / `docker-compose.dev.yml`
**Issue:** Adminer on port 8082 provided GUI database access with default credentials.
**Resolution:** Adminer is gone from the production compose; it lives only in `docker-compose.dev.yml` (loopback-bound), enabled by an explicit `-f` overlay. See §10.

### 5. Add Redis authentication (🟢 RESOLVED)

**File:** `docker-compose.yml`
**Issue:** Redis was unauthenticated; rate limit counters and session data could be read/written by anyone reaching the port.
**Resolution:** Redis starts with `--requirepass "${REDIS_PASSWORD}"`. See §10.

### 6. Bind Postgres/Redis ports to localhost (🟢 RESOLVED)

**File:** `docker-compose.yml`
**Issue:** Database ports were exposed to all interfaces.
**Resolution:** Both publish to `127.0.0.1` only. See §10.

### 7. Validate OAuth `state` parameter on callback (🟠 HIGH)

**File:** `AuthService.HandleTwitchCallbackAsync`
**Issue:** CSRF on the OAuth flow — attacker can force an admin to link a malicious Twitch account.
**Fix:** Generate nonce, store in Redis, verify on callback. See §5 Fix 1.

### 8. Configure `AllowedHosts` for production domain (🟢 RESOLVED)

**File:** `Program.cs`
**Issue:** `AllowedHosts: "*"` enabled host header injection.
**Resolution:** Derived from `App:BaseUrl`'s host (+ loopback) when left as `"*"`; explicit value overrides. See §9.

### 9. Gate Scalar/OpenAPI behind dev/admin (🟢 RESOLVED)

**File:** `Program.cs`
**Issue:** Full API schema publicly browsable in production.
**Resolution:** Registered only in Development, or in production behind opt-in `Api:ExposeDocs=true`. See §9.

### 10. Add bootstrap admin mechanism (🟢 RESOLVED)

**File:** `AuthService.HandleTwitchCallbackAsync` → `AdminBootstrap.ShouldPromote`
**Issue:** No path existed for a self-hoster to grant themselves admin without raw SQL.
**Resolution:** `App:InitialAdminTwitchId` (env `INITIAL_ADMIN_TWITCH_ID`) promotes the matching account to platform principal on login — opt-in, exact-match, idempotent, unit-tested. See §12.

---

## Summary Matrix

| # | Concern | Hosted | Self-Hosted | Priority |
|---|---------|--------|-------------|---------|
| 1 | SecureValue sealed at rest | 🟢 | 🟢 | RESOLVED |
| 2 | Setup endpoints lock after first-run | 🟢 | 🟢 | RESOLVED |
| 3 | Production boot-guard on default secrets | 🟢 | 🟢 | RESOLVED |
| 4 | Adminer dev-override only | 🟢 | 🟢 | RESOLVED |
| 5 | Per-subject DEK (was shared key) | 🟢 | 🟢 | RESOLVED |
| 6 | Authenticated envelope (was AES-CBC) | 🟢 | 🟢 | RESOLVED |
| 7 | Refresh tokens revocable + single-use | 🟢 | 🟢 | RESOLVED |
| 8 | OAuth state not validated (CSRF) | 🟠 | 🟠 | HIGH |
| 9 | Redis password-protected | 🟢 | 🟢 | RESOLVED |
| 10 | DB/Redis ports loopback-bound | 🟢 | 🟢 | RESOLVED |
| 11 | API container non-root | 🟢 | 🟢 | RESOLVED |
| 12 | Host filtering from App:BaseUrl | 🟢 | 🟢 | RESOLVED |
| 13 | Scalar/OpenAPI gated out of prod | 🟢 | 🟢 | RESOLVED |
| 14 | First admin bootstrapped from config | N/A | 🟢 | RESOLVED |
| 15 | Rate limiter keys real client IP | 🟢 | 🟢 | RESOLVED |
| 16 | Unsigned channel-bot state | 🟠 | 🟠 | HIGH |
| 17 | Stored content rendered safely | 🟢 | 🟢 | RESOLVED |
| 18 | Setup-completion lock | 🟢 | 🟢 | RESOLVED |
| 19 | No cross-tenant access tests | 🟡 | N/A | MEDIUM |
| 20 | Log retention bounded (30 days) | 🟢 | 🟢 | RESOLVED |
| 21 | GDPR export + erasure + token-revoke | 🟢 | 🟢 | RESOLVED |
| 22 | Refresh token opaque (no shared key) | 🟢 | 🟢 | RESOLVED |
| 23 | Non-destructive key rotation | 🟢 | 🟢 | RESOLVED |
| 24 | Security headers present | 🟢 | 🟢 | RESOLVED |
| 25 | TLS reverse-proxy model documented | 🟢 | 🟢 | RESOLVED |
| 26 | DB-backed IAM + deletion audit logs | 🟢 | 🟢 | RESOLVED |
| 27 | Sliding-window rate limiter | 🟢 | 🟢 | RESOLVED |
| 28 | Accurate expiry returned to client | 🟢 | 🟢 | RESOLVED |
