# Interface Specification — OBS Control Subsystem

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** OBS WebSocket v5 protocol (`obsproject/obs-websocket` `docs/generated/protocol.md`, RPC version 1 — verified surface); locked schema `2026-06-16-database-schema.md` (Domain P; `Channels.OverlayToken` A.2 pattern); platform `platform-conventions.md` (`IDeploymentProfileService.Current`, `ICacheService`, `IEventBus`); pipeline `commands-pipelines.md` (`ICommandAction` with `PipelineExecutionContext` / `ActionDefinition` / `ActionResult` §3.13; OBS events ride the event-response executor); crypto `gdpr-crypto.md` (`IFieldCipher` AEAD); realtime `frontend.md` §3.2 (`OBSRelayHub` `/hubs/obs`, Redis backplane on SaaS); roles `roles-permissions.md`.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`/`PaginatedResponse<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json; UUIDv7 `Guid` PKs; `BroadcasterId` `Guid`; soft-delete filter; AGPL header on every source file.

> **Why.** Driving OBS from chat/redemptions/events — scene switches, mute/volume, filters, recording, replay-buffer clips, media playback, hotkeys — **and reacting to OBS events** ("when I switch to BRB, pause song requests", "when recording starts, post a message") is the core of Streamer.bot / Mix It Up, and is mandated by the project rule *"mirror the full external-API manage surface."* OBS WebSocket v5 exposes ~100 requests and a rich event stream; this subsystem covers the whole surface (16 curated typed actions for the common ops + 3 generic pass-through actions for the rest), exposes OBS **events as event-response triggers**, and does so reliably over both transports (direct socket and browser-source bridge).

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Two transports, selected per channel by `ObsConnection.Mode` (`ObsTransportRouter`).** `IObsTransport` is implemented by `ObsTransportRouter`, which reads the channel's stored `Mode` on every send. `direct` → `DirectObsTransport`: the bot connects straight to OBS WebSocket v5 (`ws://host:port`, OBS-WS auth), lazily on the first send. `bridge` → `BridgeObsTransport`: a zero-install **browser-source bridge** runs inside OBS, connects out to `OBSRelayHub` and to local `ws://127.0.0.1:{port}`, and executes commands locally. A new connection defaults to `direct`; the deployment profile does not select the transport, so a self-host bot may use bridge mode when OBS runs on another box, and a hosted bot may not reach a LAN OBS directly. **Fail closed:** a channel with no `ObsConnection` row, or with `IsEnabled=false`, gets `OBS_DISABLED` before any transport runs. |
| D2 | **Browser-source bridge is single-executor (anti-racing).** Many bridge instances may connect (the source added to several scenes, OBS reloads). `IObsBridgeRegistry` derives exactly **one leader** per channel — the longest-lived connection — and the server pushes commands to that connection only; standbys idle. A standby becomes leader the moment the leader's entry leaves the book, so a promotion needs no election round. So "bridge in every scene" is **safe and improves uptime** for commands, not a race. Cross-node leader state lives in `ICacheService` (Redis on SaaS). `ForwardObsEvent` is accepted from any authenticated bridge connection; it is not yet filtered to the leader on the server. |
| D3 | **Every command carries a `CommandId` (Guid, UUIDv7).** The bridge transport registers it in `ObsBridgeCommandBook` before the push; the hub's `AckCommand` settles it. Only a bridge of the same channel can settle a command, and a duplicate ack for an unknown or settled id is a no-op. One push, then a 15 s wait: no ack → `OBS_TIMEOUT`; the bridge dropping mid-command → `OBS_BRIDGE_OFFLINE`. There is no retry and no bridge-side dedupe; the transport fails closed. |
| D4 | **Full surface = generic `obs_request` + curated typed actions.** 19 pipeline actions are registered. The three generic ones issue **any** OBS-WS request: `obs_request` (`RequestType` + `RequestData`), `obs_request_batch`, and `obs_call_vendor` (plugin pass-through). The 16 typed ones (scene, preview scene, source visibility, mute, volume, filter, transition, media, hotkey, browser refresh, screenshot, save replay, recording, streaming, replay buffer, virtual cam) give first-class pipeline-builder UX. |
| D5 | **OBS events are event-response triggers.** Every received OBS event (direct socket, or forwarded by a bridge) becomes an `ObsEventReceivedEvent`; `ObsEventTriggerSource` dispatches it through `IEventResponseExecutor` under the key `obs.<EventType>` (e.g. `obs.CurrentProgramSceneChanged`), and the bound responses run. The subscription mask (`EventSubscriptionsMask`) decides which categories OBS sends; the high-volume categories are **opt-in** (off by default — `InputVolumeMeters` alone fires ~20×/s). Event fields surface as `{obs.event.<field>}` template vars. Nothing is written to the activity feed; binding a response is the opt-in. |
| D6 | **Secrets + impact gating.** The OBS-WS password is **AEAD-encrypted via `IFieldCipher`** (write-only API: null keeps, empty string clears). Scene/source/audio/filter/media/transition/hotkey/screenshot, replay buffer, virtual cam and studio mode floor at **Moderator** (`obs:control`). Start/stop **stream** and **recording** (`obs:control:broadcast`), the **generic raw request/batch/vendor** routes (they can do anything; `obs:control:broadcast`), and connection config (`obs:config:read` / `obs:config:write`) floor at **Broadcaster**. These floors apply to the REST surface (§7); the OBS pipeline actions carry no floor of their own. |
| D7 | **Protocol correctness (baked in).** Scene items are addressed by numeric `sceneItemId` via `GetSceneItemId` first — never source name (the engine resolves it). `SetInputVolume` takes `inputVolumeMul` **xor** `inputVolumeDb`. Filter requests key off **source name**. Record/replay output paths arrive via **events** (`RecordStateChanged.outputPath`, `ReplayBufferSaved.savedReplayPath`), not request responses. Default `eventSubscriptions = All` (bits 0–11, excludes high-volume). |
| D8 | **Schema:** **P.14 `ObsConnection`** (soft-delete, one per channel) with a rotatable bridge token + event-mask. No other schema change. |

---

## 1. Entities

Domain P. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`ObsConnection`** | **P.14 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` **Unique** (one per channel); `Mode string(10)` **[VC:enum]** (`direct`\|`bridge`); `Host string(255)?` (default `127.0.0.1`); `Port int?` (default 4455); `PasswordCipher text?` **[PII-shred]** (AEAD via `IFieldCipher`, D6 — never plaintext); `BridgeToken string(36)?` **Unique** (rotatable; authenticates the browser-source bridge to `OBSRelayHub` — **distinct from `OverlayToken`**, higher privilege); `EventSubscriptionsMask int` (default = `All` = bits 0–11; high-volume bits 16–19 opt-in, D5); `IsEnabled bool`; `LastConnectedAt DateTime?`; `LastError string(300)?`; `CreatedAt/UpdatedAt/DeletedAt`. |

Runtime-only (NOT persisted): the bridge entry list per channel — connection id + connect time, from which the leader and the instance count derive (held in `ICacheService`, 12 h entry TTL, D2). OBS state (scene, stream, record) is read live from OBS on each `GetStateAsync`; it is not cached.

---

## 2. Domain events

Inherit `DomainEventBase` (platform-conventions §2.0). Published via `IEventBus`. Namespace `NomNomzBot.Domain.Obs.Events`; the types are `sealed class`es with `required ... { get; init; }` members.

```csharp
namespace NomNomzBot.Domain.Obs.Events;

public sealed class ObsBridgeStateChangedEvent : DomainEventBase   // published by ObsBridgeRegistry on every bridge join/leave; drives the dashboard status indicator
{
    public required int InstanceCount { get; init; }   // total connected bridges
    public required bool HasLeader { get; init; }       // false = control unavailable
    public string? LastError { get; init; }
}

public sealed class ObsEventReceivedEvent : DomainEventBase        // a received OBS event → ObsEventTriggerSource → obs.<EventType> responses (§6)
{
    public required string ObsEventType { get; init; } // e.g. "CurrentProgramSceneChanged"
    public required string DataJson { get; init; }     // the raw eventData JSON, exposed as {obs.event.<field>}
}

public sealed class ObsConnectionEstablishedEvent : DomainEventBase // direct socket (re)connected; carries the REAL stream/record status read right then
{
    public required bool Streaming { get; init; }
    public required bool Recording { get; init; }
}
```

`ObsAutomationStateForwarder` projects `ObsEventReceivedEvent` into `ObsStreamingStateChangedEvent(Active)`, `ObsRecordingStateChangedEvent(Active, Paused)` and `ObsInputMuteStateChangedEvent(InputName, Muted)`, which feed the public `obs.streaming.changed`, `obs.recording.changed` and `obs.mute.changed` automation events.

---

## 3. Service & transport contracts

Namespace `NomNomzBot.Application.Obs` (contracts in `Services/`, DTOs in `Dtos/`). Fallible methods return `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Obs/`. Services are bound by the `I<X>Service` convention scan, not an `AddObsControl()` call (§8).

### 3.1 `IObsControlService` (typed ops + raw + state)

```csharp
public interface IObsControlService
{
    // ── Scenes / items ──
    Task<Result> SwitchSceneAsync(Guid broadcasterId, string sceneName, CancellationToken ct = default);              // SetCurrentProgramScene
    Task<Result> SetPreviewSceneAsync(Guid broadcasterId, string sceneName, CancellationToken ct = default);          // SetCurrentPreviewScene
    Task<Result> SetSourceVisibleAsync(Guid broadcasterId, string sceneName, string sourceName, bool visible, CancellationToken ct = default); // GetSceneItemId → SetSceneItemEnabled (D7)
    // ── Audio / inputs ──
    Task<Result> SetInputMuteAsync(Guid broadcasterId, string inputName, bool muted, CancellationToken ct = default); // SetInputMute
    Task<Result> ToggleInputMuteAsync(Guid broadcasterId, string inputName, CancellationToken ct = default);          // ToggleInputMute
    Task<Result> SetInputVolumeAsync(Guid broadcasterId, string inputName, double? volumeDb, double? volumeMul, CancellationToken ct = default); // one xor (D7)
    // ── Filters ──
    Task<Result> SetFilterEnabledAsync(Guid broadcasterId, string sourceName, string filterName, bool enabled, CancellationToken ct = default); // SetSourceFilterEnabled (by source name, D7)
    // ── Outputs ──
    Task<Result> SetRecordingAsync(Guid broadcasterId, RecordAction action, CancellationToken ct = default);          // Start/Stop/Toggle/Pause/Resume/Split
    Task<Result> SetStreamingAsync(Guid broadcasterId, ObsToggle action, CancellationToken ct = default);
    Task<Result> SetReplayBufferAsync(Guid broadcasterId, ObsToggle action, CancellationToken ct = default);
    Task<Result> SaveReplayBufferAsync(Guid broadcasterId, CancellationToken ct = default);                           // SaveReplayBuffer (clip)
    Task<Result> SetVirtualCamAsync(Guid broadcasterId, ObsToggle action, CancellationToken ct = default);
    // ── Transitions / studio mode / media / hotkeys ──
    Task<Result> SetCurrentTransitionAsync(Guid broadcasterId, string transitionName, CancellationToken ct = default);
    Task<Result> TriggerStudioTransitionAsync(Guid broadcasterId, int? durationMs, CancellationToken ct = default);   // only works once studio mode is on
    Task<Result> SetStudioModeEnabledAsync(Guid broadcasterId, bool enabled, CancellationToken ct = default);         // turns studio mode on/off
    Task<Result> TriggerMediaAsync(Guid broadcasterId, string inputName, MediaAction action, CancellationToken ct = default); // TriggerMediaInputAction
    Task<Result> TriggerHotkeyAsync(Guid broadcasterId, string hotkeyName, CancellationToken ct = default);
    Task<Result<IReadOnlyList<string>>> GetHotkeyListAsync(Guid broadcasterId, CancellationToken ct = default);       // the picker source for TriggerHotkeyAsync
    Task<Result> RefreshBrowserAsync(Guid broadcasterId, string inputName, CancellationToken ct = default);           // PressInputPropertiesButton(refreshnocache)
    Task<Result<string>> ScreenshotAsync(Guid broadcasterId, string sourceName, string imageFormat, CancellationToken ct = default); // GetSourceScreenshot → data URI (data:image/png;base64,…), never persisted

    // ── Generic pass-through (full surface) ──
    Task<Result<ObsResponse>> RequestAsync(Guid broadcasterId, ObsRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsResponse>>> RequestBatchAsync(Guid broadcasterId, ObsRequestBatch batch, CancellationToken ct = default);
    Task<Result<ObsResponse>> CallVendorAsync(Guid broadcasterId, string vendorName, string requestType, IReadOnlyDictionary<string, object?>? data, CancellationToken ct = default);

    // ── State reads (dashboard) — each one is a live OBS request, not a cache read ──
    Task<Result<ObsStateDto>> GetStateAsync(Guid broadcasterId, CancellationToken ct = default);                      // one batch: GetCurrentProgramScene + GetStreamStatus + GetRecordStatus + GetReplayBufferStatus
    Task<Result<IReadOnlyList<ObsSceneDto>>> GetScenesAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsInputDto>>> GetInputsAsync(Guid broadcasterId, CancellationToken ct = default);      // global audio/video inputs only
    Task<Result<IReadOnlyList<ObsSceneItemDto>>> GetSceneItemListAsync(Guid broadcasterId, string sceneName, CancellationToken ct = default); // per-scene placement + visibility
    Task<Result<ObsVirtualCamStatusDto>> GetVirtualCamStatusAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<ObsStatsDto>> GetStatsAsync(Guid broadcasterId, CancellationToken ct = default);                      // GetStats: CPU/memory + render/output frame counters
    Task<Result<IReadOnlyList<ObsTransitionDto>>> GetSceneTransitionListAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsFilterDto>>> GetSourceFilterListAsync(Guid broadcasterId, string sourceName, CancellationToken ct = default);
    Task<Result<ObsStudioModeStatusDto>> GetStudioModeEnabledAsync(Guid broadcasterId, CancellationToken ct = default);
}

public enum ObsToggle { Start, Stop, Toggle }
public enum RecordAction { Start, Stop, Toggle, Pause, Resume, Split }
public enum MediaAction { Play, Pause, Stop, Restart, Next, Previous }   // → OBS_WEBSOCKET_MEDIA_INPUT_ACTION_*
public sealed record ObsRequest(string RequestType, IReadOnlyDictionary<string, object?>? RequestData);
public sealed record ObsRequestBatch(IReadOnlyList<ObsRequest> Requests, ObsBatchExecution Execution = ObsBatchExecution.SerialRealtime, bool HaltOnFailure = false);
public enum ObsBatchExecution { SerialRealtime = 0, SerialFrame = 1, Parallel = 2 }
public sealed record ObsResponse(bool Ok, IReadOnlyDictionary<string, object?>? ResponseData, string? Error);
public sealed record ObsStateDto(string? CurrentScene, bool Streaming, bool Recording, bool RecordPaused, bool ReplayBufferActive, string? RecordTimecode);
public sealed record ObsSceneDto(string Name, bool IsCurrent);
public sealed record ObsInputDto(string Name, string Kind, bool? Muted, double? VolumeDb);
public sealed record ObsSceneItemDto(int SceneItemId, string SourceName, bool Enabled);
public sealed record ObsVirtualCamStatusDto(bool OutputActive);
public sealed record ObsTransitionDto(string Name, bool IsCurrent);
public sealed record ObsFilterDto(string Name, string Kind, bool Enabled, int Index);
public sealed record ObsStudioModeStatusDto(bool Enabled);
public sealed record ObsStatsDto(double CpuUsage, double MemoryUsage, double ActiveFps, int RenderTotalFrames, int RenderSkippedFrames, int OutputTotalFrames, int OutputSkippedFrames);
```

Each typed op builds the exact OBS-WS request (D7 nuances applied — e.g. `SetSourceVisibleAsync` first issues `GetSceneItemId` then `SetSceneItemEnabled`) and dispatches via `IObsTransport` with a fresh `CommandId`. Failure (no connection / no leader / OBS error) is a `Result` failure with a stable code, never a throw. The codes: `OBS_DISABLED`, `OBS_NOT_CONNECTED`, `OBS_BRIDGE_OFFLINE`, `OBS_WRONG_MODE`, `OBS_TIMEOUT`, `OBS_ERROR`.

### 3.2 `IObsTransport` (per-channel `Mode` router)

```csharp
public interface IObsTransport
{
    Task<Result<ObsResponse>> SendAsync(Guid broadcasterId, Guid commandId, ObsRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsResponse>>> SendBatchAsync(Guid broadcasterId, Guid commandId, ObsRequestBatch batch, CancellationToken ct = default);
}
```

- **`ObsTransportRouter`** (the registered `IObsTransport`, singleton) — on every call it opens a short DI scope, loads the channel's `ObsConnection`, and delegates. No row or `IsEnabled=false` → `OBS_DISABLED` (fail closed, before any transport runs). `Mode == "bridge"` → `BridgeObsTransport`; anything else → `DirectObsTransport`. The choice is per channel and per call, so changing `Mode` takes effect on the next command.
- **`DirectObsTransport`** — holds one OBS-WS v5 connection per channel to the channel's own `Host:Port` (no other host is ever dialed — no SSRF surface). Connects **lazily on the first send**: Hello (op 0) → Identify (op 1; when a password is set, `auth = base64(sha256(base64(sha256(password + salt)) + challenge))`, binary SHA-256, D7; subscribes with `EventSubscriptionsMask`) → Identified (op 2). Sends `Request`(op 6) / `RequestBatch`(op 8) and awaits the correlated response (op 7/9) with a 10 s timeout. Inbound `Event`(op 5) frames publish `ObsEventReceivedEvent` (§6). A dropped socket fails every in-flight request, records `LastError` on the row, and the next send re-dials. A successful connect publishes `ObsConnectionEstablishedEvent`.
- **`BridgeObsTransport`** — asks `IObsBridgeRegistry.GetLeaderAsync`; no leader → `OBS_BRIDGE_OFFLINE` (graceful, never silent). Otherwise it serialises the command to `{kind:"request"|"batch", …}` JSON, registers the `CommandId` in `ObsBridgeCommandBook`, pushes it through `IObsBridgePusher` to the leader connection, and awaits the ack for 15 s (D3). A batch travels as one payload; the bridge folds it into one ack, so a bridge batch returns a single-element `ObsResponse` list. The host's `ObsBridgePusher` sends over `IHubContext<OBSRelayHub, IOBSRelayClient>`, so cross-node delivery rides the SignalR backplane. Without the host, `UnavailableObsBridgePusher` is the standalone fallback.

### 3.3 `IObsBridgeRegistry` (single-executor leader — D2/D3)

```csharp
public interface IObsBridgeRegistry
{
    Task RegisterAsync(Guid broadcasterId, string connectionId, DateTime connectedAt, CancellationToken ct = default);  // on bridge connect
    Task UnregisterAsync(Guid broadcasterId, string connectionId, CancellationToken ct = default);                      // on disconnect
    Task<string?> GetLeaderAsync(Guid broadcasterId, CancellationToken ct = default);                                   // current executor connection id (null = offline)
    Task<ObsBridgeStatusDto> GetStatusAsync(Guid broadcasterId, CancellationToken ct = default);                        // instance count + hasLeader (dashboard)
}
public sealed record ObsBridgeStatusDto(int InstanceCount, bool HasLeader, DateTime? LeaderSince);

public interface IObsBridgePusher   // implemented in the API host over the SignalR hub context
{
    Task PushExecuteAsync(string connectionId, Guid commandId, string payloadJson, CancellationToken ct = default);
}
```

The registry keeps the entry list `(ConnectionId, ConnectedAt)` per channel in `ICacheService` (12 h TTL). The leader is **derived, not elected**: the entry with the lowest `ConnectedAt`, so a newly opened second OBS never steals execution mid-stream, and a standby takes over as soon as the leader's entry is removed. Register/Unregister publish `ObsBridgeStateChangedEvent`. The server pushes a command to one connection only (D2).

**`OBSRelayHub`** (`/hubs/obs`, `[AllowAnonymous]`, connect-time auth = `?token={BridgeToken}`; a missing, unknown or disabled token aborts the connection; never the user JWT):

- `OnConnectedAsync` resolves the channel from the token (the token is the tenant selector), registers the connection, then calls the client method `SetObsCredentials(string? obsPassword, int obsPort)` — the sealed OBS-WS password (null when passwordless) and the channel's local port (default 4455), delivered over the authenticated relay and never in the URL. `OnDisconnectedAsync` unregisters.
- Server methods the bridge calls: `AckCommand(Guid commandId, bool ok, string? responseDataJson, string? error)` and `ForwardObsEvent(string eventType, string? eventDataJson)`. The same relay also carries `ForwardVtsEvent(string eventType, string? payloadJson)` for VTube Studio (`vtube-studio.md` D1).
- Client method the server calls: `ExecuteObsRequest(Guid commandId, string payloadJson)` (`IOBSRelayClient`).

### 3.4 `IObsConnectionService` (config CRUD)

```csharp
public interface IObsConnectionService
{
    Task<Result<ObsConnectionDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);            // defaults when none stored; password → HasPassword:bool only; reads never write
    Task<Result<ObsConnectionDto>> UpsertAsync(Guid broadcasterId, UpsertObsConnectionRequest request, CancellationToken ct = default);  // AEAD-seals the password; null keeps, "" clears
    Task<Result<ObsBridgeSetupDto>> RotateBridgeTokenAsync(Guid broadcasterId, string backendUrl, CancellationToken ct = default);  // new BridgeToken; the old setup URL stops authenticating at once
    Task<Result<ObsBridgeSetupDto>> GetBridgeSetupAsync(Guid broadcasterId, string backendUrl, CancellationToken ct = default);     // the bridge URL; mints a token on first ask
    Task<string?> GetPasswordForTransportAsync(Guid broadcasterId, CancellationToken ct = default);          // the opened password for a TRANSPORT only (direct socket, or the relay push); never on an API response
}

public sealed record ObsConnectionDto(string Mode, string? Host, int? Port, bool HasPassword, bool HasBridgeToken, int EventSubscriptionsMask, bool IsEnabled, DateTime? LastConnectedAt, string? LastError);
public sealed record UpsertObsConnectionRequest   // [Required] Mode matches ^(direct|bridge)$; Host ≤255; Port 1–65535; Password ≤255, write-only
{
    public string Mode { get; init; }
    public string? Host { get; init; }
    public int? Port { get; init; }
    public string? Password { get; init; }
    public int? EventSubscriptionsMask { get; init; }
    public bool IsEnabled { get; init; }
}
public sealed record ObsBridgeSetupDto(string BridgeUrl);
public sealed record ObsProbeDto(bool Connected, string? ErrorCode, string? Error);   // GET probe — an active reachability check (§7)
```

`backendUrl` is the public origin the controller resolves (`Request.ResolvePublicOrigin(configuration)`), so the bridge URL points at the address the streamer's browser source can actually reach. Connection testing is the `GET probe` route, which sends a harmless `GetVersion` through `IObsControlService.RequestAsync` and reports the real outcome for the direct socket and the bridge leader alike; there is no `TestConnectionAsync`.

---

## 4. The browser-source bridge — reliability, setup, anti-racing (bridge mode)

The bridge is a first-party **control-only browser source** (renders nothing, 1×1) served at `{baseUrl}/obs-bridge?token={BridgeToken}`. In OBS it connects to `OBSRelayHub` (auth = `BridgeToken`, **not** the user JWT, **not** `OverlayToken`) and to local `ws://127.0.0.1:{port}` obs-websocket (auth = the vaulted password, delivered to the authenticated bridge over the relay — **never** in the URL).

**Recommended setup — one always-loaded source (the simplest reliable path).** In OBS it is *sources* that load/unload, **not** scenes: a Browser Source stays alive across scene switches **as long as "Shutdown source when not visible" is unchecked** — it does not need to be in the active scene. So the primary instruction is **one** bridge source, added once to a base/main scene with that setting off — it stays connected permanently regardless of which scene is live. One instance, no contention, always reachable.

**Robust to mistakes — single-executor leader (the safety net).** Users will still sometimes drop the source into several scenes, or leave "shutdown when not visible" on. So the bridge never *relies* on the instruction being followed: every connected instance registers with `IObsBridgeRegistry`, which derives exactly **one leader**; the server pushes commands to the leader only, standbys idle, and when the leader unloads the next-oldest connection becomes leader on the next command. Net: the *recommended* setup is one stable source; the leader rule means any setup — multi-scene, reloaded, or misconfigured — still never races a command.

**Setup steps (the dashboard renders these; `GetBridgeSetupAsync` supplies the bridge URL):**
1. OBS → **Tools → WebSocket Server Settings** → enable the server → **Show Connect Info** → copy the password.
2. Dashboard → OBS → paste the password (vaulted), choose **Bridge** mode, **Save**.
3. OBS → **Sources → + → Browser** → name it `NomNomz OBS Bridge` → URL = the shown bridge URL → size `1×1` → **uncheck "Shutdown source when not visible."** Add it **once** to your base/main scene (recommended) — adding it to more scenes is harmless (election dedups).
4. Verify the dashboard shows **"OBS bridge connected — N instances, 1 active."** Zero = misconfigured (the indicator makes failure visible, never silent).

A channel in `Mode=direct` skips the bridge entirely (`DirectObsTransport` to the configured `Host:Port`, the default for a new connection). Bridge mode is available on every deployment when OBS runs on a different machine.

---

## 5. Pipeline actions

`ICommandAction` (canonical contract, commands-pipelines §3.13): `string ActionType`, `LocalizedText Category` (every OBS action shares `pipeline.category.obs`), `LocalizedText Description`, optional `Fields` (the typed-field schema the builder renders) and `ResolvesOwnTemplates`, and `Task<ActionResult> ExecuteAsync(PipelineExecutionContext ctx, ActionDefinition action)`. All 19 OBS actions derive from `ObsActionBase` and are found by the `ICommandAction` scan, registered `Transient`. Config values are template-resolved by the engine; a whole-value `{variable}` reference resolves against `ctx.Variables`. The actions carry no floor of their own; the D6 floors gate the REST surface (§7).

| Type | Config | OBS-WS request(s) |
|---|---|---|
| `obs_switch_scene` | `scene` | SetCurrentProgramScene |
| `obs_set_preview_scene` | `scene` | SetCurrentPreviewScene |
| `obs_set_source` | `scene`, `source`, `visible:bool` | GetSceneItemId → SetSceneItemEnabled |
| `obs_input_mute` | `input`, `muted:bool` (or `toggle:true`) | SetInputMute / ToggleInputMute |
| `obs_input_volume` | `input`, `volume_db` **or** `volume_mul` | SetInputVolume |
| `obs_filter` | `source`, `filter`, `enabled:bool` | SetSourceFilterEnabled |
| `obs_transition` | `transition?`, `studio:bool`, `duration_ms?` | SetCurrentSceneTransition / TriggerStudioModeTransition |
| `obs_media` | `input`, `action: play\|pause\|stop\|restart\|next\|previous` | TriggerMediaInputAction |
| `obs_hotkey` | `hotkey_name` | TriggerHotkeyByName |
| `obs_refresh_browser` | `input` | PressInputPropertiesButton(`refreshnocache`) |
| `obs_screenshot` | `source`, `format` | GetSourceScreenshot |
| `obs_save_replay` | — | SaveReplayBuffer (path via `ReplayBufferSaved` event) |
| `obs_recording` | `action: start\|stop\|toggle\|pause\|resume\|split` | Start/Stop/Toggle/Pause/Resume Record, SplitRecordFile |
| `obs_streaming` | `action: start\|stop\|toggle` | Start/Stop/Toggle Stream |
| `obs_replay_buffer` | `action: start\|stop\|toggle` | Start/Stop/Toggle ReplayBuffer |
| `obs_virtual_cam` | `action: start\|stop\|toggle` | Start/Stop/Toggle VirtualCam |
| `obs_request` | `request_type`, `request_data:json?` | **any** request (full surface) |
| `obs_request_batch` | `requests[]`, `execution?`, `halt_on_failure?` | RequestBatch |
| `obs_call_vendor` | `vendor`, `request_type`, `request_data:json?` | CallVendorRequest |

All fail closed (`ActionResult.Failure(...)`) when disconnected / no leader / OBS error, and on failure write the outcome into `ctx.Variables["obs.last_error"]` so a pipeline can react.

---

## 6. OBS events as event-response triggers (the automation half)

OBS events are dispatched by `ObsEventTriggerSource` (an `IEventHandler<ObsEventReceivedEvent>`) through `IEventResponseExecutor` under the key `obs.<EventType>`; an event response bound to that key runs. There is no separate `TriggerKind`. The executor call passes no user. Event payload fields are exposed as `{obs.event.<field>}`: flat fields become one variable each (strings as-is, `null` as empty, numbers/booleans as their text), nested objects ride as raw JSON, and `{obs.event.type}` always carries the event type. A malformed payload still fires the trigger with the type variable alone. The template helper is registered as the prefix family `obs.event.<field>` (event-source contexts only), so a field is not a fixed list.

**Default-subscribed trigger events** (`eventSubscriptions=All`, bits 0–11):

| Event type | Useful filter | Fires when | Key `{obs.event.*}` |
|---|---|---|---|
| `CurrentProgramSceneChanged` | `scene_name` | program scene switches | `sceneName` |
| `CurrentPreviewSceneChanged` | `scene_name` | preview scene switches | `sceneName` |
| `SceneItemEnableStateChanged` | `scene`, `source` | a source is shown/hidden | `sceneName`, `sceneItemId`, `sceneItemEnabled` |
| `InputMuteStateChanged` | `input_name` | an input is muted/unmuted | `inputName`, `inputMuted` |
| `StreamStateChanged` | `state` (e.g. `STARTED`) | stream starts/stops | `outputActive`, `outputState` |
| `RecordStateChanged` | `state` | recording starts/stops/pauses | `outputActive`, `outputState`, `outputPath` |
| `ReplayBufferStateChanged` | `state` | replay buffer toggles | `outputActive`, `outputState` |
| `ReplayBufferSaved` | — | a clip is saved | `savedReplayPath` |
| `StudioModeStateChanged` | — | studio mode toggles | `studioModeEnabled` |
| `CurrentSceneTransitionChanged` | — | active transition changes | `transitionName` |
| `MediaInputPlaybackStarted` / `MediaInputPlaybackEnded` | `input_name` | a media source starts/ends | `inputName` |
| `VirtualcamStateChanged` | — | virtual cam toggles | `outputActive`, `outputState` |
| `VendorEvent` | `vendor`, `event_type` | a plugin (advanced-scene-switcher, Tuna…) emits | `vendorName`, `eventType`, `eventData` |
| `ExitStarted` | — | OBS is closing | — |

**High-volume (opt-in only — set the bit on `EventSubscriptionsMask`, D5):** `InputVolumeChanged`, `InputActiveStateChanged`, `InputShowStateChanged`, `SceneItemTransformChanged`, `InputVolumeMeters`. The dashboard warns on enable (`InputVolumeMeters` ≈ 20 events/s per active input).

**No state template vars.** Only `{obs.event.<field>}` is registered. There are no `{obs.scene}` / `{obs.streaming}` / `{obs.recording}` variables; a pipeline that needs the live state calls a state read (§3.1) or reacts to the state event.

---

## 7. REST surface

Controller `ObsController`, `[Route("api/v{version:apiVersion}/channels/{channelId:guid}/obs")]` (channel-routed like every other management controller so the tenant rides the route). `[Authorize]`; Gate-2 keys via `[RequireAction]`. Cells `<plane> / <Role> · action:key`. Reads of OBS state (`state`, `scenes`, `inputs`, `scene-items`, `virtual-cam/status`, `stats`, `scene-transitions`, `source-filters`, `studio-mode`, `hotkeys`) return an empty/disconnected payload at **200** when OBS is simply unreachable (`OBS_DISABLED`, `OBS_NOT_CONNECTED`, `OBS_BRIDGE_OFFLINE`, `OBS_WRONG_MODE`), so the dashboard shows its connect prompt instead of a 500; a 200 there is not proof of connectivity — use `GET probe`. Genuine failures still surface.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/connection` | — | `StatusResponseDto<ObsConnectionDto>` | management / Broadcaster · `obs:config:read` |
| PUT | `/connection` | `UpsertObsConnectionRequest` | `StatusResponseDto<ObsConnectionDto>` | management / Broadcaster · `obs:config:write` |
| GET | `/bridge/setup` | — | `StatusResponseDto<ObsBridgeSetupDto>` | management / Broadcaster · `obs:config:write` |
| POST | `/bridge/rotate-token` | — | `StatusResponseDto<ObsBridgeSetupDto>` | management / Broadcaster · `obs:config:write` |
| GET | `/bridge/status` | — | `StatusResponseDto<ObsBridgeStatusDto>` | management / Broadcaster · `obs:config:read` |
| GET | `/probe` | — | `StatusResponseDto<ObsProbeDto>` | management / Moderator · `obs:control` |
| GET | `/state` | — | `StatusResponseDto<ObsStateDto>` | management / Moderator · `obs:control` |
| GET | `/scenes` | — | `StatusResponseDto<IReadOnlyList<ObsSceneDto>>` | management / Moderator · `obs:control` |
| GET | `/inputs` | — | `StatusResponseDto<IReadOnlyList<ObsInputDto>>` | management / Moderator · `obs:control` |
| GET | `/scene-items?sceneName=` | — | `StatusResponseDto<IReadOnlyList<ObsSceneItemDto>>` | management / Moderator · `obs:control` |
| GET | `/virtual-cam/status` | — | `StatusResponseDto<ObsVirtualCamStatusDto>` | management / Moderator · `obs:control` |
| GET | `/stats` | — | `StatusResponseDto<ObsStatsDto>` | management / Moderator · `obs:control` |
| GET | `/scene-transitions` | — | `StatusResponseDto<IReadOnlyList<ObsTransitionDto>>` | management / Moderator · `obs:control` |
| GET | `/source-filters?sourceName=` | — | `StatusResponseDto<IReadOnlyList<ObsFilterDto>>` | management / Moderator · `obs:control` |
| GET | `/studio-mode` | — | `StatusResponseDto<ObsStudioModeStatusDto>` | management / Moderator · `obs:control` |
| GET | `/hotkeys` | — | `StatusResponseDto<IReadOnlyList<string>>` | management / Moderator · `obs:control` |
| POST | `/scene` | `ObsSceneRequest(Scene)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/scene/preview` | `ObsSceneRequest(Scene)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/scene-items/visibility` | `ObsSourceVisibilityRequest(SceneName, SourceName, Visible)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/scene-transitions/current` | `ObsCurrentTransitionRequest(TransitionName)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/studio-mode` | `ObsStudioModeRequest(Enabled)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/studio-mode/transition` | `ObsStudioTransitionRequest(DurationMs?)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/source-filters/enabled` | `ObsFilterEnabledRequest(SourceName, FilterName, Enabled)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/inputs/mute` | `ObsInputMuteRequest(InputName, Muted)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/inputs/volume` | `ObsInputVolumeRequest(InputName, VolumeDb)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/inputs/media` | `ObsMediaActionRequest(InputName, Action)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/inputs/refresh-browser` | `ObsRefreshBrowserRequest(InputName)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/hotkeys/trigger` | `ObsHotkeyTriggerRequest(HotkeyName)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/source-screenshot` | `ObsScreenshotRequest(SourceName, ImageFormat)` | `StatusResponseDto<string>` (data URI) | management / Moderator · `obs:control` |
| POST | `/replay-buffer` | `ObsToggleRequest(Action)` | `StatusResponseDto` | management / Moderator · `obs:control` (write-expensive rate tier) |
| POST | `/replay-buffer/save` | — | `StatusResponseDto` | management / Moderator · `obs:control` (write-expensive rate tier) |
| POST | `/virtual-cam` | `ObsToggleRequest(Action)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/streaming` | `ObsToggleRequest(Action)` | `StatusResponseDto` | management / Broadcaster · `obs:control:broadcast` |
| POST | `/recording` | `ObsRecordRequest(Action)` | `StatusResponseDto` | management / Broadcaster · `obs:control:broadcast` |
| POST | `/request` | `ObsRequest` | `StatusResponseDto<ObsResponse>` | management / Broadcaster · `obs:control:broadcast` |
| POST | `/request/batch` | `ObsRequestBatch` | `StatusResponseDto<IReadOnlyList<ObsResponse>>` | management / Broadcaster · `obs:control:broadcast` |
| POST | `/request/vendor` | `ObsVendorRequest(VendorName, RequestType, RequestData?)` | `StatusResponseDto<ObsResponse>` | management / Broadcaster · `obs:control:broadcast` |

`OBSRelayHub` methods and the bridge connect-time auth are specified in §3.3. The actions `obs:config:read`, `obs:config:write`, `obs:control`, `obs:control:broadcast` are seeded by `ActionDefinitionSeeder` (`obs:control` at Moderator; `obs:control:broadcast` at Broadcaster, `DangerTier.Critical`).

---

## 8. DI registration

Registered in `NomNomzBot.Infrastructure/DependencyInjection.cs` (there is no `AddObsControl()`). The two services are bound by the `I<X>Service` convention scan; the transports, registry and command book are explicit singletons.

| Interface | Implementation | Lifetime | Note |
|---|---|---|---|
| `IObsControlService` | `ObsControlService` | convention-bound | — |
| `IObsConnectionService` | `ObsConnectionService` | convention-bound | seals the password via `IFieldCipher` |
| `IObsTransport` | `ObsTransportRouter` | Singleton | per-channel `ObsConnection.Mode` selects the transport (D1); `OBS_DISABLED` when the connection is missing or disabled |
| `DirectObsTransport` | (concrete, resolved by the router) | Singleton | holds the per-channel OBS-WS sockets; `IObsSocketFactory` → `ClientObsSocketFactory` (Singleton) |
| `BridgeObsTransport` | (concrete, resolved by the router) | Singleton | routes to the leader bridge |
| `IObsBridgeRegistry` | `ObsBridgeRegistry` | Singleton | entry list via `ICacheService` (Redis on SaaS) |
| `ObsBridgeCommandBook` | (concrete) | Singleton | in-flight bridge commands, shared with the VTS bridge; per node |
| `IObsBridgePusher` | `ObsBridgePusher` (API host, `Program.cs`) | Singleton | `UnavailableObsBridgePusher` is the `TryAdd` standalone fallback |
| `ICommandAction` (19 obs actions) | `ObsSwitchSceneAction`, … | Transient | found by the `ICommandAction` scan |
| `IEventHandler<ObsEventReceivedEvent>` | `ObsEventTriggerSource` | scan | dispatches `obs.<EventType>` event responses |
| `IEventHandler<ObsEventReceivedEvent>` | `ObsAutomationStateForwarder` | scan | projects state events for the public automation API |

`OBSRelayHub` (`/hubs/obs`, §3.3) is mapped in `Program.cs`. The bridge page (`/obs-bridge?token=…`) is served by `ObsBridgeHostController` in the API project (`[Route("obs-bridge")]`, `[AllowAnonymous]`, anonymous rate-limit policy, hidden from the OpenAPI document). It is one inline `<script>` with a per-response CSP nonce, renders a 1×1 transparent surface, and carries no secret: the `?token=` only gates the hub connection. The script speaks the SignalR JSON hub protocol by hand, opens the local `ws://127.0.0.1:{port}` OBS-WS v5 connection (Hello → Identify, with the auth hash when the password arrives via `SetObsCredentials`), runs `request`/`batch` payloads, acks with `AckCommand`, and forwards events via `ForwardObsEvent`. The same page carries the VTube Studio leg.

---

## 9. Testing — prove behavior

- **Typed action → exact request** — `obs_set_source "Cam" in "Main" visible=false` issues `GetSceneItemId(Main,Cam)` **then** `SetSceneItemEnabled(Main, <id>, false)` (assert both, in order, with the resolved numeric id — D7); `obs_input_volume volume_db=-6` sends `inputVolumeDb=-6` and **omits** `inputVolumeMul` (xor).
- **Single-executor leader** — three bridge connections register; exactly one (the lowest `ConnectedAt`) is leader; a command is pushed to the leader **only** (assert standbys receive nothing); unregistering the leader makes the next-oldest connection leader and the next command reaches it; a duplicate ack for a settled `CommandId` is a no-op, and an ack from another channel's bridge cannot settle it.
- **Event → trigger** — a forwarded `CurrentProgramSceneChanged{sceneName:"BRB"}` runs the event response bound to `obs.CurrentProgramSceneChanged` and exposes `{obs.event.sceneName}="BRB"` and `{obs.event.type}`; a `ReplayBufferSaved` exposes `savedReplayPath`; a high-volume event does **not** arrive unless its mask bit is set; a malformed payload still fires with the type variable alone.
- **Mode routing + degradation** — a `direct` connection resolves `DirectObsTransport` and a `bridge` connection resolves `BridgeObsTransport` (assert which transport ran); a missing or `IsEnabled=false` connection returns `OBS_DISABLED` before any transport runs; a `bridge` channel with no leader returns `OBS_BRIDGE_OFFLINE` from every control op and from `GET probe` (assert the code; never silent success); an unanswered bridge command returns `OBS_TIMEOUT`.
- **Secret custody** — `UpsertAsync` stores `PasswordCipher` as AEAD ciphertext (never plaintext); `GetAsync` returns `HasPassword=true` with no password field; the bridge URL carries only the `BridgeToken`, never the password.
- **Impact gating** — a Moderator passes `POST /scene` and `POST /replay-buffer/save` but is rejected on `POST /streaming`, `POST /recording` and `POST /request` (Broadcaster floor, `obs:control:broadcast`).

---

## 10. Decisions (resolved)

Two transports selected per channel by `ObsConnection.Mode` through `ObsTransportRouter` — direct OBS-WS v5 or the browser-source bridge — failing closed with `OBS_DISABLED` (D1); a derived single leader makes a multi-scene bridge safe for commands (D2) with `CommandId`-correlated acks and a 15 s timeout (D3); full surface via 19 actions: 16 typed plus generic `obs_request`/batch/vendor (D4); OBS events as `obs.<EventType>` event-response triggers with `{obs.event.<field>}` vars, high-volume opt-in (D5); AEAD-encrypted password + REST impact gating (D6); protocol nuances baked in — `sceneItemId` resolution, volume xor, filter-by-source-name, paths-via-events, default `All` subscription (D7); schema delta P.14 `ObsConnection` with bridge token + event mask (D8).
