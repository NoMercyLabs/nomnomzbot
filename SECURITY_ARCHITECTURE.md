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

- Algorithm: HMAC-SHA256 (`SecurityAlgorithms.HmacSha256`) — correct
- Access token expiry: 60 minutes (configurable via `Jwt:ExpiryMinutes`)
- Refresh token expiry: 7 days (configurable via `Jwt:RefreshExpiryDays`)
- Both access and refresh tokens are signed with the **same** key (`_key`)
- Token validation: issuer ✅, audience ✅, lifetime ✅, signature ✅, clock skew 1 min ✅
- JTI claim included in every token ✅
- Refresh tokens carry a `refresh` role claim so they cannot be used as access tokens ✅
- Default secret in `appsettings.json`: `dev-secret-key-at-least-32-characters-long!!`
- Default secret in `docker-compose.yml`: same string, passed via `JWT_SECRET` env var fallback

### Gaps & Vulnerabilities

**🔴 CRITICAL — Default JWT secret is public and committed to source**
If an operator deploys without setting `JWT_SECRET`, the application runs with a publicly known signing key. Anyone who reads this document or the source code can forge valid JWTs for any user ID. This affects both hosted (if the env var is missing) and self-hosted (if the operator doesn't change it) modes.

**🟠 HIGH — No refresh token revocation**
Refresh tokens are stateless JWTs with a 7-day lifetime. There is no token blacklist, no Redis-backed revocation store, and no `jti` tracking. A stolen refresh token grants the attacker a continuous 7-day re-authentication window with no way to terminate the session short of changing the JWT secret (which invalidates all active sessions platform-wide).

**🟠 HIGH — Access and refresh tokens share the same signing key**
If the signing key is ever compromised, an attacker can forge both access tokens and refresh tokens. A separate key for refresh tokens allows rotating the access key without invalidating refresh tokens (or vice versa).

**🟡 MEDIUM — `AuthResultDto` expires hardcoded to `DateTime.UtcNow.AddHours(1)`**
The expiry returned to the client in `HandleTwitchCallbackAsync` and `RefreshTokenAsync` is hardcoded to 1 hour, but the actual token is created with the configurable `Jwt:ExpiryMinutes`. If that setting is changed, the client receives stale expiry information and may not refresh in time (or refresh too eagerly).

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

**🔴 CRITICAL — Credential endpoints permanently unauthenticated (hosted mode)**
In hosted mode, any unauthenticated HTTP request to `PUT /api/v1/system/setup/credentials/twitch` can overwrite the platform's Twitch `client_id` and `client_secret`. An attacker who does this would:
1. Replace the platform's Twitch app with their own
2. All new OAuth logins would exchange codes against the attacker's app
3. The attacker receives every new user's Twitch access tokens
4. Existing sessions continue working (tokens already stored), but new logins are compromised

In self-hosted mode this is a one-time first-run concern, but in hosted mode it is a permanent standing vulnerability with no mitigation at the application layer.

**🟠 HIGH — No setup-completion lock**
Even after the system is fully configured, anyone can overwrite credentials at any time. There is no `IsSetupComplete` flag that transitions the setup endpoints from anonymous to admin-only.

**🟠 HIGH — `/system/status` leaks infrastructure hints**
The public status endpoint returns `"Set TWITCH_CLIENT_ID and TWITCH_CLIENT_SECRET in .env"` as a detail string. This confirms the deployment model to attackers.

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

**🟠 HIGH — Rate limiter uses `RemoteIpAddress` which is the reverse proxy behind load balancers**
In production behind nginx, Caddy, Cloudflare, or any reverse proxy, `context.Connection.RemoteIpAddress` is the proxy's IP, not the client's. All users share the same rate limit bucket, which means:
- The auth limiter (10 req/min) trips immediately under normal load
- The per-IP anonymous limiter lumps all users together

**🟡 MEDIUM — Setup credential endpoints have no rate limiting**
`PUT /system/setup/credentials/twitch|spotify|discord` has no `[EnableRateLimiting]` attribute. While they should be locked behind auth after fix (see §3), they should also be rate-limited.

**🟡 MEDIUM — Fixed-window limiter is burst-exploitable**
A fixed-window limiter allows 120 requests immediately at window reset (e.g., 120 requests at 00:00:00 and 120 more at 00:01:00). A sliding window or token bucket provides smoother protection.

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

**🟡 MEDIUM — XSS via stored command responses (hosted mode)**
A user could store a command response like `<script>alert(1)</script>` or `javascript:...`. If the frontend renders this via `innerHTML` rather than `innerText`, it executes in other users' browsers (stored XSS). The backend API returns this unencoded. Defense must be in the frontend, but the backend should also encode on output.

**🟡 MEDIUM — Template injection via `{{...}}` variables**
If the pipeline template engine resolves user-supplied strings (e.g., command arguments `{{args.1}}`) inside operator-defined templates, a crafted argument could escape the template context depending on the engine's design. This requires auditing the pipeline engine's template substitution logic.

**🟢 LOW — No maximum length enforcement on command names/responses**
Without database-level length constraints or model validation, a large payload can bloat the database. EF Core model MaxLength attributes should be verified on all user-supplied string fields.

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

**🟠 HIGH — `AllowedHosts: "*"` enables host header injection**
ASP.NET Core's `AllowedHosts` middleware filters requests by the `Host` header. Setting it to `*` disables this check. An attacker can forge a `Host` header to change how the application constructs absolute URLs (used in OAuth redirect URIs, email links). Set this to the actual production domain.

**🟠 HIGH — Scalar API docs exposed in production**
`app.MapOpenApi()` and `app.MapScalarApiReference()` are always registered. In production, the full API schema — including all request/response shapes, parameter names, and error codes — is publicly browsable. This isn't a direct vulnerability, but it provides significant reconnaissance value.

**🟡 MEDIUM — No security headers middleware**
The application does not set:
- `Content-Security-Policy`
- `X-Content-Type-Options: nosniff`
- `X-Frame-Options: DENY`
- `Referrer-Policy: no-referrer`
- `Permissions-Policy`

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

**🔴 CRITICAL — Adminer exposed with no additional authentication (hosted mode)**
Adminer at `http://<host>:8082` provides full GUI database access requiring only the Postgres credentials (which are also defaulted). In hosted mode, if port 8082 is publicly reachable, any attacker with the default credentials (or who can brute-force them) has full database access — all user tokens, all configuration, everything.

**🟠 HIGH — Redis has no authentication**
Redis on port `6379` is unauthenticated. Anyone who can reach the port can read/write all cache keys, which includes rate limit counters, session state, and (after Fix 2 in §2) refresh token JTIs. Poisoning rate limit keys defeats the auth rate limiter.

**🟠 HIGH — PostgreSQL port exposed to host**
Port `5432` being mapped to `0.0.0.0` means it's accessible from outside the Docker network. Combined with the default credentials, this is a direct database exposure.

**🟠 HIGH — API container runs as root**
If a remote code execution vulnerability is found in the application or any dependency, the attacker has root access inside the container, which simplifies container escape.

**🟡 MEDIUM — No HTTPS in docker-compose**
The docker-compose sets `ASPNETCORE_URLS=http://+:5000` only. HTTPS is expected to be terminated at a reverse proxy, but this is not documented and not enforced. If a self-hoster deploys without a reverse proxy, tokens travel in cleartext.

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

**🟡 MEDIUM — Rolling log files accumulate indefinitely**
Serilog's `RollingInterval.Day` creates one file per day with no retention limit. On a busy hosted instance, log files can consume significant disk space and may contain sensitive paths, user IDs, and IP addresses.

**🟡 MEDIUM — Log files may contain PII**
Request paths logged include route parameters (e.g., `/api/v1/channels/123456789/commands`), which contain Twitch user IDs. Depending on privacy law (GDPR, CCPA), this constitutes personal data processing that must be disclosed and have a defined retention policy.

**🟢 LOW — No structured audit log for admin actions**
Admin operations (ban, credential changes, user promotion) are logged at `Information` level in the general log but not to a separate tamper-evident audit log.

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

`User.IsAdmin` is a boolean column. It defaults to `false` on user creation. No API endpoint exists to set it — the only path is a direct database update: `UPDATE "Users" SET "IsAdmin" = true WHERE "Id" = '<twitch_id>'`.

The `IsAdmin` flag is read during JWT generation and manifests as the `admin` role claim. Admin routes are gated with `[Authorize(Roles = "admin")]`.

### Gaps & Vulnerabilities

**🟠 HIGH — No bootstrap mechanism for the first admin (self-hosted mode)**
A self-hoster must manually run a raw SQL command to grant themselves admin access. This is undiscovered, fragile, and likely to be done wrong (e.g., disabling row security, using wrong ID format). There should be a `INITIAL_ADMIN_TWITCH_ID` environment variable that is promoted to admin on first startup.

**🟡 MEDIUM — No admin management UI**
Admins cannot revoke other admins or view who has admin access. In hosted mode, this is a gap in the incident response playbook (how do you demote a compromised admin account?).

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

**🟠 HIGH — No documented reverse proxy requirement**
Self-hosters who follow the README and run `docker-compose up` get an HTTP-only API exposed on port 5080. All OAuth tokens, JWTs, and user data travel in plaintext. Twitch requires HTTPS for OAuth redirect URIs, so the Cloudflare tunnel workaround is documented, but nothing prevents a self-hoster from binding the API port directly to the internet without TLS.

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

There are no data export or deletion endpoints. No privacy policy routing is implemented. The system stores Twitch user IDs, display names, profile images, OAuth tokens, and all user-configured data (commands, timers, rewards, integrations) indefinitely with soft-delete only (`IsDeleted` flag).

### Gaps & Vulnerabilities

**🟡 MEDIUM — No user data export (GDPR Art. 20 — right to portability)**
Users have no way to request a machine-readable copy of their data.

**🟡 MEDIUM — No user data deletion (GDPR Art. 17 — right to erasure)**
Soft-delete does not satisfy erasure requirements. Personal data (name, tokens, profile image URL) must be nulled or deleted, not just flagged.

**🟡 MEDIUM — Token revocation on account deletion**
When a user leaves, their Twitch OAuth tokens stored in the database should be revoked with Twitch before being deleted.

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

### 1. Lock setup credential endpoints (🔴 CRITICAL)

**File:** `SystemController.cs`
**Issue:** Any unauthenticated HTTP request can overwrite the platform Twitch `client_id`/`client_secret`.
**Fix:** Add `IsSetupComplete` guard — once setup completes, require admin JWT on credential endpoints. See §3.

### 2. Encrypt `SecureValue` in the Configuration table (🔴 CRITICAL)

**File:** `SystemController.UpsertSystemConfig`
**Issue:** Twitch/Spotify/Discord client secrets stored in plaintext.
**Fix:** Inject `IEncryptionService`, encrypt on write, decrypt on read. Two lines of code. See §1 Fix 1.

### 3. Startup guard for default secrets (🔴 CRITICAL)

**File:** `Program.cs`
**Issue:** If `JWT_SECRET` or `ENCRYPTION_KEY` env vars are not set, the app runs with publicly known default values.
**Fix:** Add startup validation that throws in Production if defaults are detected. See §2 Fix 1.

### 4. Remove Adminer from production docker-compose (🔴 CRITICAL)

**File:** `docker-compose.yml`
**Issue:** Adminer on port 8082 provides GUI database access with default credentials.
**Fix:** Move Adminer to a dev-only compose override. See §10 Fix 1.

### 5. Add Redis authentication (🟠 HIGH)

**File:** `docker-compose.yml`
**Issue:** Redis is unauthenticated; rate limit counters and session data can be read/written by anyone reaching the port.
**Fix:** Add `--requirepass` and `REDIS_PASSWORD` env var. See §10 Fix 2.

### 6. Bind Postgres/Redis ports to localhost (🟠 HIGH)

**File:** `docker-compose.yml`
**Issue:** Database ports exposed to all interfaces.
**Fix:** Bind to `127.0.0.1`. See §10 Fix 3.

### 7. Validate OAuth `state` parameter on callback (🟠 HIGH)

**File:** `AuthService.HandleTwitchCallbackAsync`
**Issue:** CSRF on the OAuth flow — attacker can force an admin to link a malicious Twitch account.
**Fix:** Generate nonce, store in Redis, verify on callback. See §5 Fix 1.

### 8. Configure `AllowedHosts` for production domain (🟠 HIGH)

**File:** `appsettings.Production.json` (to be created)
**Issue:** `AllowedHosts: "*"` enables host header injection affecting OAuth redirect URI construction.
**Fix:** Set to actual production hostname. See §9 Fix 1.

### 9. Gate Scalar/OpenAPI behind dev/admin (🟠 HIGH)

**File:** `Program.cs`
**Issue:** Full API schema publicly browsable in production.
**Fix:** Wrap in `if (app.Environment.IsDevelopment())`. See §9 Fix 2.

### 10. Add bootstrap admin mechanism (🟠 HIGH)

**File:** `DataSeeder.cs`
**Issue:** No path exists for a self-hoster to grant themselves admin without raw SQL.
**Fix:** `APP__INITIAL_ADMIN_TWITCH_ID` env var processed at seeder startup. See §12 Fix 1.

---

## Summary Matrix

| # | Concern | Hosted | Self-Hosted | Priority |
|---|---------|--------|-------------|---------|
| 1 | SecureValue sealed at rest | 🟢 | 🟢 | RESOLVED |
| 2 | Setup endpoints unauthenticated | 🔴 | 🟠 | CRITICAL |
| 3 | Default JWT/encryption secrets | 🔴 | 🔴 | CRITICAL |
| 4 | Adminer exposed in production | 🔴 | 🟠 | CRITICAL |
| 5 | Per-subject DEK (was shared key) | 🟢 | 🟢 | RESOLVED |
| 6 | Authenticated envelope (was AES-CBC) | 🟢 | 🟢 | RESOLVED |
| 7 | JWT refresh token no revocation | 🟠 | 🟠 | HIGH |
| 8 | OAuth state not validated (CSRF) | 🟠 | 🟠 | HIGH |
| 9 | Redis unauthenticated | 🟠 | 🟠 | HIGH |
| 10 | DB/Redis ports exposed | 🟠 | 🟠 | HIGH |
| 11 | API container runs as root | 🟠 | 🟠 | HIGH |
| 12 | AllowedHosts = "*" | 🟠 | 🟠 | HIGH |
| 13 | Scalar/OpenAPI in production | 🟠 | 🟡 | HIGH |
| 14 | No bootstrap admin path | N/A | 🟠 | HIGH |
| 15 | Rate limiter uses proxy IP | 🟠 | 🟡 | HIGH |
| 16 | Unsigned channel-bot state | 🟠 | 🟠 | HIGH |
| 17 | XSS in stored commands | 🟡 | 🟡 | MEDIUM |
| 18 | No setup-completion lock | 🟡 | 🟡 | MEDIUM |
| 19 | No cross-tenant access tests | 🟡 | N/A | MEDIUM |
| 20 | Log file retention unbounded | 🟡 | 🟡 | MEDIUM |
| 21 | No GDPR export/delete | 🟡 | 🟡 | MEDIUM |
| 22 | JWT/refresh share signing key | 🟡 | 🟡 | MEDIUM |
| 23 | Non-destructive key rotation | 🟢 | 🟢 | RESOLVED |
| 24 | Security headers missing | 🟡 | 🟡 | MEDIUM |
| 25 | HTTPS not enforced/documented | 🟡 | 🟠 | MEDIUM |
| 26 | No audit log | 🟢 | 🟢 | LOW |
| 27 | Fixed-window rate limiter burst | 🟢 | 🟢 | LOW |
| 28 | JWT expiry hardcoded in response | 🟢 | 🟢 | LOW |
