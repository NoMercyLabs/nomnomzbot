# Interface Specification — Stream Deck Integration (+ generic device pairing)

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** Elgato Stream Deck SDK (a plugin connects to a backend and binds keys to actions + key feedback — the client artifact). Corpus: `automation-api.md` (THE surface this rides — `invoke`/`events`/`read` scopes, `AutomationApiToken` P.17, `IAutomationApiTokenService`, the WS `op`/`event` protocol); `platform-conventions.md` (`ICacheService` for ephemeral codes, `IDeploymentProfileService` for the backend URL); `scaling-qos.md` (`IRateLimiter` — brute-force guard on codes); `roles-permissions.md` (Gate-2). Locked schema `2026-06-16-database-schema.md` (Domain P — reuses P.17, no new table).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json.

> **Why.** A first-party Stream Deck integration is "a good first-party solution" precisely because the hard part already exists: the **Automation API** lets any external tool run pipelines, subscribe to events, and read state. So this is **not** a new control plane — it's (1) a thin **Elgato Stream Deck plugin** that consumes the Automation API (keypress → `invoke`; event → key title/value/state), and (2) the one backend convenience that makes it frictionless: **device pairing** so the user never copy-pastes a token or URL. The pairing flow is **generic** — Stream Deck is the first consumer; Touch Portal, Companion, and a mobile remote use the same flow.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **The plugin is a client of the Automation API — no new control plane.** Keypress → `POST /automation/v1/invoke` (scope `invoke`); the action picker is populated from `GET /automation/v1/pipelines` (scope `read`); live key feedback (title/value/state) is driven client-side from the WS event stream (scope `events`) against the public event catalog. Server-driven "Decks" layouts (Streamer.bot-style) are **out of scope** — key mapping + feedback live in the plugin. |
| D2 | **Generic device pairing — no token copy-paste.** The dashboard "Connect a device" mints a **short-lived single-use pairing code** (8 chars, ~5-min TTL, in `ICacheService`); the device exchanges it at `POST /automation/v1/pair` for `{ backendUrl, token, scopes }`. The redeemed token is a freshly-minted `AutomationApiToken` (P.17) scoped `invoke`+`events`+`read`, **named after the device**. Codes are single-use and rate-limited (brute-force guard). |
| D3 | **Paired devices are normal tokens.** A paired device appears in the `automation:tokens` list and is **revoked** like any token (revoking unpairs the device). No separate device registry, no new table — pairing is a distribution convenience over P.17. |
| D4 | **Key feedback rides the event stream.** The plugin subscribes to chosen public events (`GET /api/v1/automation/events/catalog`) and updates keys itself; the backend only streams events (no Stream-Deck-specific push). |
| D5 | **The plugin is a client artifact** (Elgato SDK / TypeScript, built under `tools/streamdeck/`), distributed via the Elgato Marketplace + our releases. This spec defines the **integration contract + pairing** (the backend surface), not the plugin's internal code. |
| D6 | **Schema: none.** Pairing codes are ephemeral (`ICacheService`); the credential is `AutomationApiToken` (P.17). Adds two endpoints: mint-code (management) + redeem (data plane, code-gated). |
| D7 | **Automatic local-loopback handoff — no typed code on the golden path.** The plugin runs a local HTTP listener (`127.0.0.1:{ephemeralPort}`, CORS-restricted to the resolved dashboard origin) from the moment it starts. Its onboarding state (first run / not-yet-paired) registers the listener's port via `localStorage`/a well-known discovery file the dashboard's "Connect a device" button probes (`fetch('http://127.0.0.1:{port}/nomnomz/pair-handoff')` across a small fixed candidate-port range — the same loopback-discovery pattern used by Docker Desktop / Spotify Desktop / 1Password browser↔app handoff). On click: the dashboard mints the code exactly as D2 describes, then **posts the code + the dashboard's own resolved origin** straight to the listener instead of only displaying it. The plugin receives `{ code, backendUrl }`, redeems it against `POST /automation/v1/pair` itself, and finishes onboarding with zero user typing. **Manual code entry (D2) is retained as the fallback** — it's the only path when the plugin isn't reachable on loopback (a different machine, a remote/LAN self-host device, Touch Portal on a phone) — so nothing here removes the generic primitive, it only adds an automated delivery transport for the common same-machine case. The loopback listener trusts nothing by identity — the code itself is still the single-use, rate-limited, ~5-min-TTL credential (D2); a page other than the real dashboard POSTing a guessed code gains nothing beyond what guessing the code already risked. |
| D8 | **Tokens live 30 days and self-refresh — no re-pairing on a healthy device.** A token minted via `RedeemCodeAsync` (D2) gets `ExpiresAt = now + 30 days` (previously unset/no-expiry — a headless device token should expire if abandoned, unlike a human-managed dashboard-created token, which keeps choosing its own `ExpiresAt`/no-expiry via the existing management-plane `CreateAsync`). New self-service data-plane endpoint `POST /automation/v1/refresh` (token-authed by the token being refreshed, any scope) rotates the secret **in place** — same `Id`/`Name`/`Scopes`/`AllowedPipelineIds`, new `TokenHash`/`TokenPrefix`, `ExpiresAt` reset to `now + 30 days` — and returns the new one-time secret; the old secret stops authenticating immediately. The plugin checks its stored token's remaining lifetime on every startup and roughly daily while running, refreshing proactively once under a 7-day threshold. A token that expires **without** ever refreshing (device offline for 30+ days) fails auth on next use with a typed `TOKEN_EXPIRED` result — the plugin's error path re-runs the D7 handoff automatically rather than surfacing a dead key to the user. |

---

## 1. Entities

**None.** Reuses `AutomationApiToken` (P.17, `automation-api.md`). Pairing codes live in `ICacheService` (key `pair:{code}` → `{ broadcasterId, deviceLabel, scopes, expiresAt }`, single-use, ~5-min TTL).

---

## 2. Domain events

Reuses `AutomationTokenCreatedEvent` (`automation-api.md` §2) — a redeemed pairing mints a token, which emits the existing audit event (with the device label as the token name). No new events.

---

## 3. Service interface

Namespace `NomNomzBot.Application.AutomationApi` (beside the automation services). `Task<Result<T>>`.

```csharp
public interface IAutomationPairingService
{
    // Dashboard: mint a single-use code (cached, TTL). The device will redeem it for a scoped token.
    Task<Result<PairingCodeDto>> MintCodeAsync(Guid broadcasterId, Guid actorUserId, MintPairingCodeRequest request, CancellationToken ct = default);

    // Device: redeem the code → backend URL + a freshly-minted AutomationApiToken (via IAutomationApiTokenService).
    // Single-use (consumed on success); rate-limited; invalid/expired/used → typed failure.
    // ExpiresAt on the minted token is now+30 days (D8) — a paired device token is not the no-expiry
    // default a human-managed dashboard-created token gets.
    Task<Result<PairingRedemptionDto>> RedeemCodeAsync(string code, DeviceInfo device, CancellationToken ct = default);
}

public sealed record MintPairingCodeRequest(string DeviceLabel, IReadOnlyList<string> Scopes); // scopes ⊆ invoke|events|read
public sealed record PairingCodeDto(string Code, DateTime ExpiresAt);
public sealed record DeviceInfo(string Kind, string? Name);   // Kind: "streamdeck" | "touchportal" | "mobile" | …
public sealed record PairingRedemptionDto(string BackendUrl, string Token, IReadOnlyList<string> Scopes, DateTime TokenExpiresAt);

// ── Self-refresh (D8) — data plane, token-authed by the token being refreshed ──────────────────
public interface IAutomationApiTokenService
{
    // ...existing ListAsync/CreateAsync/RotateAsync/RevokeAsync/GetEventCatalogAsync (automation-api.md §3)...

    // Rotates the CALLING token's secret in place: same Id/Name/Scopes/AllowedPipelineIds, new
    // TokenHash/TokenPrefix, ExpiresAt reset to now+30 days. Old secret invalidated immediately.
    // No Gate-2 check — a valid token may always refresh itself; this is not a management operation.
    Task<Result<IssuedAutomationTokenDto>> RefreshSelfAsync(AutomationPrincipal principal, CancellationToken ct = default);
}
```

`MintCodeAsync` defaults scopes to `invoke`+`events`+`read` (never `chat` for a key-bound device unless explicitly added). `RedeemCodeAsync` calls `IAutomationApiTokenService.CreateAsync` with `ExpiresAt = now + 30 days` (D8) (token name = `"{DeviceInfo.Kind}: {DeviceLabel}"`, falling back to the mint-time label; a same-name re-pair disambiguates with the code's tail instead of failing) and returns the one-time secret + the resolved `BackendUrl` — **as built 2026-07-17: from `HttpRequest.ResolvePublicOrigin(configuration)`** (`PublicOriginExtensions`), the platform's access-origin source (`App:BaseUrl` → forwarded request origin → loopback), because `IDeploymentProfileService.Current` carries no URL; the controller resolves it from the redeeming request and passes it in.

---

## 4. REST surface

`AutomationPairingController`. Two endpoints across the two planes (see `automation-api.md` §4–§5).

| Verb | Path | Auth | Request | Response | Gate / guard |
|---|---|---|---|---|---|
| POST | `/api/v1/automation/pair-codes` | JWT | `MintPairingCodeRequest` | `StatusResponseDto<PairingCodeDto>` | management / Broadcaster · `automation:tokens:write` (minting a pairing code = minting a token) |
| POST | `/automation/v1/pair` | **code only** | `{ string Code, DeviceInfo Device }` | `StatusResponseDto<PairingRedemptionDto>` | unauthenticated-but-code-gated; `IRateLimiter` brute-force guard (per-IP + global code-attempt bucket) |
| POST | `/automation/v1/refresh` | API token (any scope) | `{}` | `StatusResponseDto<IssuedAutomationTokenDto>` | token-authed (`IAutomationTokenAuthenticator`); D8 self-refresh, no Gate-2 |

The plugin-side loopback listener (D7) is **not** a backend route — it's an HTTP server the plugin itself runs locally; the dashboard's browser JS is the client calling it (`fetch('http://127.0.0.1:{port}/nomnomz/pair-handoff', { method: 'POST', body: { code, backendUrl } })`), CORS-restricted to accept only the resolved dashboard origin.

No new Gate-2 keys — pairing reuses `automation:tokens:write` (mint) and the code itself authorizes redemption; refresh needs no Gate-2 (a valid token refreshing itself is not a privilege-escalating action).

---

## 5. DI & testing

`AddAutomationApi()` (`automation-api.md`) also registers `IAutomationPairingService`→`AutomationPairingService` (Scoped). The `/automation/v1/pair` route is on the data-plane route group but **before** the API-token auth handler (it has no token yet — the code is the credential).

**Tests (prove behavior):** `MintCodeAsync` returns a code that resolves (within TTL) to the right broadcaster + scopes in cache; `RedeemCodeAsync` with a valid code mints an `AutomationApiToken` named for the device with exactly the minted scopes (default excludes `chat`) and `ExpiresAt = now+30d`, returns the backend URL + one-time secret, and **consumes** the code (a second redeem of the same code fails); an expired or unknown code fails with no token minted; the brute-force guard denies after N bad attempts with `Retry-After`; revoking the paired token (via the existing token API) invalidates the device's access (subsequent `invoke` is rejected); a device requesting `chat` scope without it being granted at mint time does not receive it; `RefreshSelfAsync` rotates the secret in place (same `Id`, new `TokenHash`/`TokenPrefix`, `ExpiresAt` pushed out another 30 days) and the **old** secret stops authenticating immediately after; refreshing a revoked or fully-expired token fails typed (`TOKEN_REVOKED`/`TOKEN_EXPIRED`), never silently re-minting.

---

## 6. Decisions (resolved)

Plugin is a thin Automation-API client, no new control plane, Decks out of scope (D1); generic single-use rate-limited pairing code → scoped token, no copy-paste (D2); paired devices are ordinary revocable P.17 tokens (D3); key feedback via the event stream + public catalog (D4); the plugin is a client artifact under `tools/streamdeck/` (D5); no schema — codes in cache, credential is P.17, two endpoints added (D6); automatic same-machine loopback handoff with manual-code fallback retained for remote/other-machine devices (D7); 30-day self-refreshing device tokens via a token-authed `POST /automation/v1/refresh`, proactive refresh under a 7-day threshold, auto-re-handoff on hard expiry (D8).
