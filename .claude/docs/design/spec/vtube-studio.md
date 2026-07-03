# Interface Specification — VTube Studio Control

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** VTube Studio Public API (DenchiSoft — default `ws://localhost:8001`; one-time `AuthenticationTokenRequest` → user approves an in-app popup → stored plugin token → per-session `AuthenticationRequest`; requests for models/hotkeys/expressions/movement/color-tint; events via `EventSubscriptionRequest`). Corpus: **`obs-control.md`** (the IDENTICAL transport problem + solution — a localhost WebSocket app reached **directly** on self-host and via a **single-executor browser-source relay** on SaaS; this spec reuses that transport/bridge wholesale); `commands-pipelines.md` (§3.13 `ICommandAction`, trigger registry, `event` kind); `platform-conventions.md` (`IDeploymentProfileService`, `IFieldCipher` AEAD, `IRunOnceGuard`, `IEventBus`); `widgets-overlays.md`/`OverlayHub` (the relay channel); `roles-permissions.md` (Gate-2, mirrors the `obs:*` keys). Locked schema `2026-06-16-database-schema.md` (Domain P — beside `ObsConnection` P.14).
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`; `[ApiVersion("1.0")]`; UUIDv7 `Guid` PKs; `BroadcasterId Guid` tenant scope; soft-delete filter; Newtonsoft.Json; secrets AEAD via `IFieldCipher`.

> **Why.** VTubers are a huge segment, and VTube Studio is the standard rig. Controlling it from the bot (swap models, trigger expressions/hotkeys on a redemption, color-tint on a cheer, react to tracking loss) is high-value. The "how do we reach a localhost app from the cloud" question is **already solved** for OBS — VTS is the same shape (a localhost WebSocket API), so this spec is a near-mechanical parallel of `obs-control.md`: **direct** connection on self-host, the **same single-executor browser-source relay** on SaaS (the relay proxies any localhost WS service — OBS-WS *and* VTS-WS). Full surface mirrored per the external-API-coverage rule, with a generic `vts_request` escape hatch.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Transport mirrors `obs-control.md` exactly.** `IVtsTransport` with `DirectVtsTransport` (self-host → `ws://localhost:8001`) and `BridgeVtsTransport` (SaaS → the existing browser-source relay). The SaaS bridge **reuses the OBS relay**: the single-always-loaded browser source proxies localhost WS services generally, so one relay carries both OBS-WS and VTS-WS, with the same single-executor election (one leader per channel) + idempotent `CommandId`. No second bridge mechanism. |
| D2 | **Plugin-token auth, AEAD-stored.** On first connect the bot runs VTS's `AuthenticationTokenRequest` (the streamer approves the in-VTS popup once); the returned plugin token is stored **AEAD** (`IFieldCipher`) on `VtsConnection` and replayed via `AuthenticationRequest` each session. Token loss/expiry → re-approve. |
| D3 | **Full surface + generic escape hatch.** Typed pipeline actions for the common operations — load model, trigger hotkey, set expression, move model, color tint — **plus `vts_request`** (raw VTS request type + JSON payload) so the entire API is reachable without a spec change (the `obs_request` pattern). |
| D4 | **Events as triggers.** VTS events (model loaded/unloaded, hotkey triggered, model clicked, tracking lost/found) subscribe via `EventSubscriptionRequest` and surface as `vts_event` pipeline triggers (the `obs_event` pattern). Default-subscribed to the low-volume set; opt-in for high-frequency (e.g. per-frame tracking params). |
| D5 | **Schema P.19 `VtsConnection`** (parallel to `ObsConnection` P.14). One connection per channel. Off until configured (default-deny). |

---

## 1. Entities

Domain P. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`VtsConnection`** | **P.19 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` Index **Unique** (one per channel); `Mode string(20)` **[VC:enum]** (`direct`\|`bridge`); `Endpoint string(200)` (default `ws://localhost:8001`; direct only); `PluginTokenCipher text?` **[PII]** (AEAD — the VTS auth token); `BridgeToken string(64)?` (SaaS relay, rotatable); `EventSubscriptionsMask int` (which `vts_event`s are subscribed); `IsEnabled bool` (default false); `Status string(20)`; `LastConnectedAt DateTime?`; `CreatedAt/UpdatedAt/DeletedAt`. |

---

## 2. Domain events (→ pipeline triggers)

Inherit `DomainEventBase`. `VtsEventReceived { string EventType; string PayloadJson; }` published via `IEventBus`, matched to `vts_event` triggers by sub-type (model_loaded / hotkey_triggered / model_clicked / tracking_changed / …). Connection lifecycle (`VtsConnectedEvent`/`VtsDisconnectedEvent`) mirrors OBS.

---

## 3. Service interfaces

Namespace `NomNomzBot.Application.Vts`. `Task<Result<T>>`. Impl in `NomNomzBot.Infrastructure/Vts/`. Mirrors `obs-control.md` §3.

```csharp
public interface IVtsControlService
{
    Task<Result<VtsRequestResult>> SendAsync(Guid broadcasterId, string requestType, string payloadJson, CancellationToken ct = default); // generic (D3)
    Task<Result> LoadModelAsync(Guid broadcasterId, string modelIdOrName, CancellationToken ct = default);
    Task<Result> TriggerHotkeyAsync(Guid broadcasterId, string hotkeyIdOrName, CancellationToken ct = default);
    Task<Result> SetExpressionAsync(Guid broadcasterId, string expressionFile, bool active, CancellationToken ct = default);
    Task<Result> MoveModelAsync(Guid broadcasterId, VtsMove move, CancellationToken ct = default);     // position/rotation/size/time
    Task<Result> ColorTintAsync(Guid broadcasterId, VtsColorTint tint, CancellationToken ct = default);
    Task<Result<VtsModelInventory>> GetInventoryAsync(Guid broadcasterId, CancellationToken ct = default); // models + hotkeys + expressions (for the editor pickers)
}

// Transport split — DirectVtsTransport (self-host) | BridgeVtsTransport (SaaS, via the OBS relay). Mirrors IObsTransport.
public interface IVtsTransport
{
    Task<Result<string>> RequestAsync(Guid broadcasterId, string requestType, string payloadJson, CancellationToken ct = default);
}

public interface IVtsConnectionService
{
    Task<Result<VtsConnectionDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<VtsConnectionDto>> UpsertAsync(Guid broadcasterId, Guid actorUserId, UpsertVtsConnectionRequest request, CancellationToken ct = default);
    Task<Result> AuthorizeAsync(Guid broadcasterId, CancellationToken ct = default);   // runs AuthenticationTokenRequest, stores the AEAD token (D2)
    Task<Result> RotateBridgeTokenAsync(Guid broadcasterId, Guid actorUserId, CancellationToken ct = default);
}

public sealed record VtsMove(double? X, double? Y, double? Rotation, double? Size, double? TimeSeconds, bool Relative);
public sealed record VtsColorTint(byte R, byte G, byte B, byte A, string? MatchArtMeshTag);
public sealed record VtsConnectionDto(string Mode, string Endpoint, bool HasPluginToken, bool IsEnabled, string Status, DateTime? LastConnectedAt);
public sealed record UpsertVtsConnectionRequest(string Mode, string Endpoint, bool IsEnabled);
```

---

## 4. Pipeline actions & triggers

| Action `Type` | Parameters | Behavior |
|---|---|---|
| **`vts_request`** | `{ string RequestType, string PayloadJson }` | raw VTS request (full-surface escape hatch). |
| **`vts_load_model`** | `{ string Model }` | load a model by id/name. |
| **`vts_trigger_hotkey`** | `{ string Hotkey }` | trigger a hotkey by id/name. |
| **`vts_set_expression`** | `{ string Expression, bool Active }` | toggle an expression. |
| **`vts_move_model`** | `{ VtsMove }` | move/rotate/scale the model. |
| **`vts_color_tint`** | `{ VtsColorTint }` | tint art meshes. |

**Trigger:** `vts_event` (sub-types: `model_loaded`, `hotkey_triggered`, `model_clicked`, `tracking_changed`, …), registered as an `event` kind. Default-subscribed to low-volume events; high-frequency tracking opt-in (D4).

---

## 5. REST surface

Controller `VtsController`, `[Route("api/v{version:apiVersion}/vts")]`. `[Authorize]`; Gate-2 keys (mirror the `obs:*` keys).

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/connection` | — | `StatusResponseDto<VtsConnectionDto>` | management / Moderator · `vts:config:read` |
| PUT | `/connection` | `UpsertVtsConnectionRequest` | `StatusResponseDto<VtsConnectionDto>` | management / Broadcaster · `vts:config:write` |
| POST | `/connection/authorize` | — | `StatusResponseDto<bool>` | management / Broadcaster · `vts:config:write` |
| POST | `/connection/rotate-bridge-token` | — | `StatusResponseDto<bool>` | management / Broadcaster · `vts:config:write` |
| GET | `/inventory` | — | `StatusResponseDto<VtsModelInventory>` | management / Moderator · `vts:config:read` |
| POST | `/control` | `{ string RequestType, string PayloadJson }` | `StatusResponseDto<VtsRequestResult>` | management / Moderator · `vts:control` |

Seed in `roles-permissions.md`: **`vts:config:read`** (Moderator 10, `Low`), **`vts:config:write`** (Broadcaster 40, `Low`), **`vts:control`** (Moderator 10, `Low`).

---

## 6. DI & testing

`NomNomzBot.Infrastructure/Vts/DependencyInjection.cs` (`AddVts()`): `IVtsControlService`→`VtsControlService` (Scoped); `IVtsTransport`→`DirectVtsTransport`/`BridgeVtsTransport` by `IDeploymentProfileService.Current` (the `IObsTransport` selection); `IVtsConnectionService`→`VtsConnectionService` (Scoped); `VtsConnectionRepository` (Scoped); the six pipeline actions + `vts_event` trigger source auto-discovered; the bridge reuses the OBS relay/election (`obs-control.md` §4). AEAD via `IFieldCipher`.

**Tests (prove behavior):** `AuthorizeAsync` performs the token handshake and persists the token **AEAD** (ciphertext, not plaintext); a session authenticates with the stored token before any control request; `vts_load_model` issues the right VTS request and a generic `vts_request` passes an arbitrary type through unchanged; on SaaS, control routes through the bridge with a single-executor leader and an idempotent `CommandId` (a duplicate relay delivery executes once); `vts_event` fires the bound pipeline on a subscribed event and high-frequency tracking events are **not** delivered unless opted in; a disabled/unconfigured connection rejects control with a typed failure (no transport call); rotating the bridge token invalidates the old one.

---

## 7. Decisions (resolved)

Transport mirrors `obs-control.md` (direct self-host / shared browser-source relay SaaS) (D1); plugin-token auth stored AEAD (D2); full typed surface + generic `vts_request` (D3); VTS events as `vts_event` triggers, low-volume default-subscribed (D4); schema delta **P.19 `VtsConnection`**, one per channel, opt-in (D5).
