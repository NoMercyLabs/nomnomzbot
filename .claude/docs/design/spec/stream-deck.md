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
    Task<Result<PairingRedemptionDto>> RedeemCodeAsync(string code, DeviceInfo device, CancellationToken ct = default);
}

public sealed record MintPairingCodeRequest(string DeviceLabel, IReadOnlyList<string> Scopes); // scopes ⊆ invoke|events|read
public sealed record PairingCodeDto(string Code, DateTime ExpiresAt);
public sealed record DeviceInfo(string Kind, string? Name);   // Kind: "streamdeck" | "touchportal" | "mobile" | …
public sealed record PairingRedemptionDto(string BackendUrl, string Token, IReadOnlyList<string> Scopes);
```

`MintCodeAsync` defaults scopes to `invoke`+`events`+`read` (never `chat` for a key-bound device unless explicitly added). `RedeemCodeAsync` calls `IAutomationApiTokenService.CreateAsync` (token name = `"{DeviceInfo.Kind}: {DeviceLabel}"`) and returns the one-time secret + the resolved `BackendUrl` (from `IDeploymentProfileService.Current`).

---

## 4. REST surface

`AutomationPairingController`. Two endpoints across the two planes (see `automation-api.md` §4–§5).

| Verb | Path | Auth | Request | Response | Gate / guard |
|---|---|---|---|---|---|
| POST | `/api/v1/automation/pair-codes` | JWT | `MintPairingCodeRequest` | `StatusResponseDto<PairingCodeDto>` | management / Broadcaster · `automation:tokens:write` (minting a pairing code = minting a token) |
| POST | `/automation/v1/pair` | **code only** | `{ string Code, DeviceInfo Device }` | `StatusResponseDto<PairingRedemptionDto>` | unauthenticated-but-code-gated; `IRateLimiter` brute-force guard (per-IP + global code-attempt bucket) |

No new Gate-2 keys — pairing reuses `automation:tokens:write` (mint) and the code itself authorizes redemption.

---

## 5. DI & testing

`AddAutomationApi()` (`automation-api.md`) also registers `IAutomationPairingService`→`AutomationPairingService` (Scoped). The `/automation/v1/pair` route is on the data-plane route group but **before** the API-token auth handler (it has no token yet — the code is the credential).

**Tests (prove behavior):** `MintCodeAsync` returns a code that resolves (within TTL) to the right broadcaster + scopes in cache; `RedeemCodeAsync` with a valid code mints an `AutomationApiToken` named for the device with exactly the minted scopes (default excludes `chat`), returns the backend URL + one-time secret, and **consumes** the code (a second redeem of the same code fails); an expired or unknown code fails with no token minted; the brute-force guard denies after N bad attempts with `Retry-After`; revoking the paired token (via the existing token API) invalidates the device's access (subsequent `invoke` is rejected); a device requesting `chat` scope without it being granted at mint time does not receive it.

---

## 6. Decisions (resolved)

Plugin is a thin Automation-API client, no new control plane, Decks out of scope (D1); generic single-use rate-limited pairing code → scoped token, no copy-paste (D2); paired devices are ordinary revocable P.17 tokens (D3); key feedback via the event stream + public catalog (D4); the plugin is a client artifact under `tools/streamdeck/` (D5); no schema — codes in cache, credential is P.17, two endpoints added (D6).
