# Interface Specification — OBS Control Subsystem

**Status:** Implementable. Code the owner writes from this should compile first-try.
**Sources of truth:** OBS WebSocket v5 protocol (`obsproject/obs-websocket` `docs/generated/protocol.md`, RPC version 1 — verified surface); locked schema `2026-06-16-database-schema.md` (Domain P; `Channels.OverlayToken` A.2 pattern); platform `platform-conventions.md` (`IDeploymentProfileService.Current`, `ICacheService`, `IEventBus`); pipeline `commands-pipelines.md` (`ICommandAction`/`ActionContext` §3.13, trigger kinds §4.1); crypto `gdpr-crypto.md` (`IFieldCipher` AEAD); realtime `frontend.md` §3.2 (`OBSRelayHub` `/hubs/obs`, Redis backplane on SaaS); roles `roles-permissions.md`.
**Conventions (binding):** namespace `NomNomzBot.*`; .NET 10 / C# 14 / EF Core 10; file-scoped namespaces; `Nullable enable`; **explicit types — never `var`** (IDE0008 = error); async all the way; `Result<T>` over exceptions/null; Repository + `IUnitOfWork`; typed-interface DI, no MediatR, no Roslyn; `StatusResponseDto<T>`/`PaginatedResponse<T>`; `[ApiVersion("1.0")]`; Newtonsoft.Json; UUIDv7 `Guid` PKs; `BroadcasterId` `Guid`; soft-delete filter; AGPL header on every source file.

> **Why.** Driving OBS from chat/redemptions/events — scene switches, mute/volume, filters, recording, replay-buffer clips, media playback, hotkeys — **and reacting to OBS events** ("when I switch to BRB, pause song requests", "when recording starts, post a message") is the core of Streamer.bot / Mix It Up, and is mandated by the project rule *"mirror the full external-API manage surface."* OBS WebSocket v5 exposes ~100 requests and a rich event stream; this subsystem covers the whole surface (curated typed actions for the common ops + a generic pass-through for the rest), exposes OBS **events as pipeline triggers**, and does so reliably across both deployment profiles.

---

## 0. Decisions (binding)

| # | Decision |
|---|---|
| D1 | **Two transports, profile-selected (`IObsTransport`).** **Self-host → `DirectObsTransport`**: the bot is local/LAN and connects straight to OBS WebSocket v5 (`ws://host:port`, OBS-WS auth). **SaaS / remote → `BridgeObsTransport`**: a zero-install **browser-source bridge** runs inside OBS, connects out to `OBSRelayHub` and to local `ws://127.0.0.1:4455`, and executes commands locally. Self-host may opt into bridge mode if OBS runs on another box. |
| D2 | **Browser-source bridge is single-executor (anti-racing).** Many bridge instances may connect (the source added to several scenes, OBS reloads). `IObsBridgeRegistry` elects exactly **one leader** per channel; commands and OBS-event forwarding go to the leader only — standbys idle. Leader loss promotes a standby sub-second. So "bridge in every scene" is **safe and improves uptime**, not a race. Cross-node leader state lives in `ICacheService` (Redis backplane on SaaS). |
| D3 | **Every command is idempotent.** Each carries a `CommandId` (Guid); the bridge dedupes recent ids and acks with the result; the bot retries to the leader on no-ack, then fails closed. Guards the split-second a failover could overlap leaders. |
| D4 | **Full surface = generic `obs_request` + curated typed actions.** The generic action issues **any** OBS-WS request (`RequestType` + `RequestData`) — complete coverage for power users; ~18 typed actions (scene, source visibility, mute/volume, filter, record, stream, replay, media, transition, hotkey, screenshot, …) give first-class pipeline-builder UX. `obs_request_batch` and `obs_call_vendor` (plugin pass-through) round it out. |
| D5 | **OBS events are pipeline triggers** (`TriggerKind=obs_event`). The leader subscribes with the `EventSubscription` mask, forwards events up (only the leader → no duplicates), and the bot fires matching pipelines. The four high-volume categories are **opt-in** (off by default — `InputVolumeMeters` alone fires ~20×/s). Event fields surface as `{{obs.event.*}}` template vars. |
| D6 | **Secrets + impact gating.** The OBS-WS password is **AEAD-encrypted via `IFieldCipher`** (write-only API). Scene/source/audio/filter/media/transition/hotkey/screenshot/save-replay floor at **Moderator** (`obs:control`); start/stop **stream/record/replay-buffer/virtual-cam**, the **generic raw request/batch/vendor** (can do anything), and connection config floor at **Broadcaster** (`obs:control:broadcast` / `obs:config:write`). |
| D7 | **Protocol correctness (baked in).** Scene items are addressed by numeric `sceneItemId` via `GetSceneItemId` first — never source name (the engine resolves it). `SetInputVolume` takes `inputVolumeMul` **xor** `inputVolumeDb`. Filter requests key off **source name**. Record/replay output paths arrive via **events** (`RecordStateChanged.outputPath`, `ReplayBufferSaved.savedReplayPath`), not request responses. Default `eventSubscriptions = All` (bits 0–11, excludes high-volume). |
| D8 | **Schema:** **P.14 `ObsConnection`** (soft-delete, one per channel) with a rotatable bridge token + event-mask. No other schema change. |

---

## 1. Entities

Domain P. UUIDv7 PK, `BaseEntity` timestamps, soft-delete filter, `BroadcasterId Guid` tenant scope.

| Table | Schema ref | Scope | Key fields (type) |
|---|---|---|---|
| **`ObsConnection`** | **P.14 (NEW)** `[soft-delete]` `ITenantScoped` | tenant | `Id Guid` PK; `BroadcasterId Guid` FK→`Channels.Id` **Unique** (one per channel); `Mode string(10)` **[VC:enum]** (`direct`\|`bridge`); `Host string(255)?` (default `127.0.0.1`); `Port int?` (default 4455); `PasswordCipher text?` **[PII-shred]** (AEAD via `IFieldCipher`, D6 — never plaintext); `BridgeToken string(36)?` **Unique** (rotatable; authenticates the browser-source bridge to `OBSRelayHub` — **distinct from `OverlayToken`**, higher privilege); `EventSubscriptionsMask int` (default = `All` = bits 0–11; high-volume bits 16–19 opt-in, D5); `IsEnabled bool`; `LastConnectedAt DateTime?`; `LastError string(300)?`; `CreatedAt/UpdatedAt/DeletedAt`. |

Runtime-only (NOT persisted): the elected leader connection id + the live bridge-instance count (held in `ICacheService`, D2); the cached current scene / stream / record state per channel (for `{{obs.*}}` vars, §6).

---

## 2. Domain events

Inherit `DomainEventBase` (platform-conventions §2.0). Published via `IEventBus`.

```csharp
namespace NomNomzBot.Domain.Events;

public sealed record ObsBridgeStateChangedEvent : DomainEventBase   // surfaced to the dashboard status indicator
{
    public required int InstanceCount { get; init; }   // total connected bridges
    public required bool HasLeader { get; init; }       // false = control unavailable
    public string? LastError { get; init; }
}

public sealed record ObsEventReceivedEvent : DomainEventBase        // a forwarded OBS event → drives obs_event triggers (§5/§6)
{
    public required string ObsEventType { get; init; } // e.g. "CurrentProgramSceneChanged"
    public required string DataJson { get; init; }     // the event payload (Newtonsoft), exposed as {{obs.event.*}}
}
```

---

## 3. Service & transport contracts

Namespace `NomNomzBot.Application.Obs`. Fallible methods return `Task<Result<T>>` / `Task<Result>`. Impl in `NomNomzBot.Infrastructure/Obs/`.

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
    Task<Result> SetStreamingAsync(Guid broadcasterId, ObsToggle action, CancellationToken ct = default);             // Broadcaster floor
    Task<Result> SetReplayBufferAsync(Guid broadcasterId, ObsToggle action, CancellationToken ct = default);          // Broadcaster floor
    Task<Result> SaveReplayBufferAsync(Guid broadcasterId, CancellationToken ct = default);                           // SaveReplayBuffer (clip; Moderator floor)
    Task<Result> SetVirtualCamAsync(Guid broadcasterId, ObsToggle action, CancellationToken ct = default);            // Broadcaster floor
    // ── Transitions / media / hotkeys ──
    Task<Result> SetCurrentTransitionAsync(Guid broadcasterId, string transitionName, CancellationToken ct = default);
    Task<Result> TriggerStudioTransitionAsync(Guid broadcasterId, int? durationMs, CancellationToken ct = default);
    Task<Result> TriggerMediaAsync(Guid broadcasterId, string inputName, MediaAction action, CancellationToken ct = default); // TriggerMediaInputAction
    Task<Result> TriggerHotkeyAsync(Guid broadcasterId, string hotkeyName, CancellationToken ct = default);
    Task<Result> RefreshBrowserAsync(Guid broadcasterId, string inputName, CancellationToken ct = default);           // PressInputPropertiesButton(refreshnocache)
    Task<Result<string>> ScreenshotAsync(Guid broadcasterId, string sourceName, string imageFormat, CancellationToken ct = default); // GetSourceScreenshot → base64

    // ── Generic pass-through (full surface) ──
    Task<Result<ObsResponse>> RequestAsync(Guid broadcasterId, ObsRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsResponse>>> RequestBatchAsync(Guid broadcasterId, ObsRequestBatch batch, CancellationToken ct = default);
    Task<Result<ObsResponse>> CallVendorAsync(Guid broadcasterId, string vendorName, string requestType, IReadOnlyDictionary<string, object?>? data, CancellationToken ct = default);

    // ── State (dashboard + {{obs.*}} vars) ──
    Task<Result<ObsStateDto>> GetStateAsync(Guid broadcasterId, CancellationToken ct = default);                      // current scene + stream/record/replay status (from cache; falls back to GetStreamStatus/GetRecordStatus/GetCurrentProgramScene)
    Task<Result<IReadOnlyList<ObsSceneDto>>> GetScenesAsync(Guid broadcasterId, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsInputDto>>> GetInputsAsync(Guid broadcasterId, CancellationToken ct = default);
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
```

Each typed op builds the exact OBS-WS request (D7 nuances applied — e.g. `SetSourceVisibleAsync` first issues `GetSceneItemId` then `SetSceneItemEnabled`) and dispatches via `IObsTransport` with a fresh `CommandId`. Failure (no connection / no leader / OBS error) is a `Result` failure with a stable code, never a throw.

### 3.2 `IObsTransport` (deployment-profile adapter)

```csharp
public interface IObsTransport
{
    Task<Result<ObsResponse>> SendAsync(Guid broadcasterId, Guid commandId, ObsRequest request, CancellationToken ct = default);
    Task<Result<IReadOnlyList<ObsResponse>>> SendBatchAsync(Guid broadcasterId, Guid commandId, ObsRequestBatch batch, CancellationToken ct = default);
}
```

- **`DirectObsTransport`** (self-host) — maintains the OBS-WS v5 connection to `Host:Port`; performs the v5 `Identify` handshake: `auth = base64(sha256(base64(sha256(password + salt)) + challenge))` (binary SHA-256, D7); subscribes with `EventSubscriptionsMask`; sends `Request`(op 6)/`RequestBatch`(op 8); forwards inbound `Event`(op 5) frames to the event pipeline (§6). Connects **only** to the channel's configured endpoint (the streamer's own OBS — no SSRF surface).
- **`BridgeObsTransport`** (SaaS / remote) — routes the command (with `CommandId`) to the **leader** bridge connection via `OBSRelayHub` (`IObsBridgeRegistry.GetLeader`); the leader executes against local OBS and returns the `ObsResponse`; OBS events the leader forwards become `ObsEventReceivedEvent`. No leader → `OBS_BRIDGE_OFFLINE` (graceful, never silent). Cross-node delivery rides the SignalR Redis backplane.

### 3.3 `IObsBridgeRegistry` (single-executor election — D2/D3)

```csharp
public interface IObsBridgeRegistry
{
    Task RegisterAsync(Guid broadcasterId, string connectionId, string obsInstanceId, CancellationToken ct = default);   // on bridge connect; elects leader if none
    Task UnregisterAsync(Guid broadcasterId, string connectionId, CancellationToken ct = default);                        // on disconnect; promotes a standby if the leader left
    Task<string?> GetLeaderAsync(Guid broadcasterId, CancellationToken ct = default);                                     // current executor connection id (null = offline)
    Task<ObsBridgeStatusDto> GetStatusAsync(Guid broadcasterId, CancellationToken ct = default);                          // instance count + hasLeader (dashboard)
}
public sealed record ObsBridgeStatusDto(int InstanceCount, bool HasLeader, DateTime? LeaderSince);
```

Leader = the longest-lived connection for the channel (lowest registration timestamp), stored in `ICacheService` keyed by `BroadcasterId`. Register/Unregister publish `ObsBridgeStateChangedEvent`. The bridge dedupes by `CommandId` (D3); the registry never sends a command to more than one connection (D2).

### 3.4 `IObsConnectionService` (config CRUD)

```csharp
public interface IObsConnectionService
{
    Task<Result<ObsConnectionDto>> GetAsync(Guid broadcasterId, CancellationToken ct = default);            // password → HasPassword:bool only
    Task<Result<ObsConnectionDto>> UpsertAsync(Guid broadcasterId, UpsertObsConnectionRequest request, CancellationToken ct = default);  // AEAD-encrypts password
    Task<Result<string>> RotateBridgeTokenAsync(Guid broadcasterId, CancellationToken ct = default);        // new BridgeToken → invalidates old bridge URL
    Task<Result<ObsBridgeSetupDto>> GetBridgeSetupAsync(Guid broadcasterId, CancellationToken ct = default);// the bridge URL + step-by-step instructions (§4)
    Task<Result<ObsConnectionTestDto>> TestConnectionAsync(Guid broadcasterId, CancellationToken ct = default); // probes via IObsTransport (GetVersion)
}

public sealed record ObsConnectionDto(string Mode, string? Host, int? Port, bool HasPassword, int EventSubscriptionsMask, bool IsEnabled, DateTime? LastConnectedAt, string? LastError);
public sealed record UpsertObsConnectionRequest(string Mode, string? Host, int? Port, string? Password, int? EventSubscriptionsMask, bool IsEnabled);  // Password write-only; null = unchanged
public sealed record ObsBridgeSetupDto(string BridgeUrl, IReadOnlyList<string> Steps, ObsBridgeStatusDto Status);
public sealed record ObsConnectionTestDto(bool Connected, string? ObsVersion, string? Error);
```

---

## 4. The browser-source bridge — reliability, setup, anti-racing (SaaS / remote)

The bridge is a first-party **control-only browser source** (renders nothing, 1×1) served at `{baseUrl}/obs-bridge?token={BridgeToken}`. In OBS it connects to `OBSRelayHub` (auth = `BridgeToken`, **not** the user JWT, **not** `OverlayToken`) and to local `ws://127.0.0.1:{port}` obs-websocket (auth = the vaulted password, delivered to the authenticated bridge over the relay — **never** in the URL).

**Recommended setup — one always-loaded source (the simplest reliable path).** In OBS it is *sources* that load/unload, **not** scenes: a Browser Source stays alive across scene switches **as long as "Shutdown source when not visible" is unchecked** — it does not need to be in the active scene. So the primary instruction is **one** bridge source, added once to a base/main scene with that setting off — it stays connected permanently regardless of which scene is live. One instance, no contention, always reachable.

**Robust to mistakes — single-executor election (the safety net).** Users will still sometimes drop the source into several scenes, or leave "shutdown when not visible" on. So the bridge never *relies* on the instruction being followed: every connected instance registers with `IObsBridgeRegistry`, which elects exactly **one leader**; commands and OBS-event forwarding target the leader only, standbys idle, and a leader that unloads is replaced by a standby in <1 s. `CommandId` dedup (D3) closes the failover-overlap window. Net: the *recommended* setup is one stable source; the election *guarantees* that any setup — multi-scene, reloaded, or misconfigured — still neither races nor drops.

**Setup steps (the dashboard renders these from `GetBridgeSetupAsync`):**
1. OBS → **Tools → WebSocket Server Settings** → enable the server → **Show Connect Info** → copy the password.
2. Dashboard → OBS → paste the password (vaulted), choose **Bridge** mode, **Save**.
3. OBS → **Sources → + → Browser** → name it `NomNomz OBS Bridge` → URL = the shown bridge URL → size `1×1` → **uncheck "Shutdown source when not visible."** Add it **once** to your base/main scene (recommended) — adding it to more scenes is harmless (election dedups).
4. Verify the dashboard shows **"OBS bridge connected — N instances, 1 active."** Zero = misconfigured (the indicator makes failure visible, never silent).

**Self-host** skips the bridge entirely (`Mode=direct` → `DirectObsTransport` to the local OBS). Bridge mode is available to self-host too when OBS runs on a different machine.

---

## 5. Pipeline actions

`ICommandAction` (canonical contract, commands-pipelines §3.13). Registered `Transient`. Config values are template-resolved. Floors per D6.

| Type | Config | OBS-WS request(s) | Floor |
|---|---|---|---|
| `obs_switch_scene` | `scene` | SetCurrentProgramScene | Moderator |
| `obs_set_preview_scene` | `scene` | SetCurrentPreviewScene | Moderator |
| `obs_set_source` | `scene`, `source`, `visible:bool` | GetSceneItemId → SetSceneItemEnabled | Moderator |
| `obs_input_mute` | `input`, `muted:bool` (or `toggle:true`) | SetInputMute / ToggleInputMute | Moderator |
| `obs_input_volume` | `input`, `volume_db` **or** `volume_mul` | SetInputVolume | Moderator |
| `obs_filter` | `source`, `filter`, `enabled:bool` | SetSourceFilterEnabled | Moderator |
| `obs_transition` | `transition?`, `studio:bool`, `duration_ms?` | SetCurrentSceneTransition / TriggerStudioModeTransition | Moderator |
| `obs_media` | `input`, `action: play\|pause\|stop\|restart\|next\|previous` | TriggerMediaInputAction | Moderator |
| `obs_hotkey` | `hotkey_name` | TriggerHotkeyByName | Moderator |
| `obs_refresh_browser` | `input` | PressInputPropertiesButton(`refreshnocache`) | Moderator |
| `obs_screenshot` | `source`, `format` | GetSourceScreenshot | Moderator |
| `obs_save_replay` | — | SaveReplayBuffer (path via `ReplayBufferSaved` event) | Moderator |
| `obs_recording` | `action: start\|stop\|toggle\|pause\|resume\|split` | Start/Stop/Toggle/Pause/Resume Record, SplitRecordFile | Broadcaster |
| `obs_streaming` | `action: start\|stop\|toggle` | Start/Stop/Toggle Stream | Broadcaster |
| `obs_replay_buffer` | `action: start\|stop\|toggle` | Start/Stop/Toggle ReplayBuffer | Broadcaster |
| `obs_virtual_cam` | `action: start\|stop\|toggle` | Start/Stop/Toggle VirtualCam | Broadcaster |
| `obs_request` | `request_type`, `request_data:json?` | **any** request (full surface) | Broadcaster |
| `obs_request_batch` | `requests[]`, `execution?`, `halt_on_failure?` | RequestBatch | Broadcaster |
| `obs_call_vendor` | `vendor`, `request_type`, `request_data:json?` | CallVendorRequest | Broadcaster |

All fail closed (`ActionResult.Fail`) when disconnected / no leader / OBS error, and write the OBS response into `ctx.Variables` (e.g. screenshot base64, `GetStats` fields).

---

## 6. OBS events as pipeline triggers (the automation half)

`TriggerKind=obs_event` (registered with the pipeline trigger registry, commands-pipelines §4.1). A pipeline/event-response binds an OBS event type + optional filter; the leader's forwarded events (`ObsEventReceivedEvent`) match and fire it. Event payload fields are exposed as `{{obs.event.<field>}}`.

**Default-subscribed trigger events** (`eventSubscriptions=All`, bits 0–11):

| Event type | Useful filter | Fires when | Key `{{obs.event.*}}` |
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

**State template vars** (resolved from the cached state the leader pushes, §1): `{{obs.scene}}`, `{{obs.streaming}}`, `{{obs.recording}}`, `{{obs.recordTimecode}}`, `{{obs.replayBuffer}}`.

---

## 7. REST surface

Controller `ObsController`, `[Route("api/v{version:apiVersion}/obs")]`. `[Authorize]`; Gate-2 keys. Cells `<plane> / <Role> · action:key`.

| Verb | Path | Request | Response | Gate |
|---|---|---|---|---|
| GET | `/connection` | — | `StatusResponseDto<ObsConnectionDto>` | management / Broadcaster · `obs:config:read` |
| PUT | `/connection` | `UpsertObsConnectionRequest` | `StatusResponseDto<ObsConnectionDto>` | management / Broadcaster · `obs:config:write` |
| POST | `/connection/test` | — | `StatusResponseDto<ObsConnectionTestDto>` | management / Broadcaster · `obs:config:write` |
| POST | `/connection/bridge-token/rotate` | — | `StatusResponseDto<string>` | management / Broadcaster · `obs:config:write` |
| GET | `/bridge/setup` | — | `StatusResponseDto<ObsBridgeSetupDto>` | management / Broadcaster · `obs:config:read` |
| GET | `/bridge/status` | — | `StatusResponseDto<ObsBridgeStatusDto>` | management / Moderator · `obs:control` |
| GET | `/state` | — | `StatusResponseDto<ObsStateDto>` | management / Moderator · `obs:control` |
| GET | `/scenes` | — | `StatusResponseDto<IReadOnlyList<ObsSceneDto>>` | management / Moderator · `obs:control` |
| GET | `/inputs` | — | `StatusResponseDto<IReadOnlyList<ObsInputDto>>` | management / Moderator · `obs:control` |
| POST | `/scene` | `SwitchSceneRequest(SceneName)` | `StatusResponseDto` | management / Moderator · `obs:control` |
| POST | `/request` | `ObsRequest` | `StatusResponseDto<ObsResponse>` | management / Broadcaster · `obs:control:broadcast` |
| POST | `/streaming` | `ObsToggleRequest(Action)` | `StatusResponseDto` | management / Broadcaster · `obs:control:broadcast` |
| POST | `/recording` | `RecordActionRequest(Action)` | `StatusResponseDto` | management / Broadcaster · `obs:control:broadcast` |

`OBSRelayHub` server methods for the bridge: `RegisterBridge(obsInstanceId)`, `AckCommand(commandId, resultJson)`, `ForwardObsEvent(eventType, dataJson)`; client method `ExecuteObsRequest(commandId, requestType, dataJson)`. Connect-time auth = the channel `BridgeToken` (validate against `ObsConnection`; abort on mismatch) — never the user JWT. Seed `obs:config:read/write`, `obs:control`, `obs:control:broadcast` in `roles-permissions.md`.

---

## 8. DI registration

`NomNomzBot.Infrastructure/Obs/DependencyInjection.cs` (`AddObsControl()`).

| Interface | Implementation | Lifetime | Profile adapter |
|---|---|---|---|
| `IObsControlService` | `ObsControlService` | Scoped | — |
| `IObsTransport` | `DirectObsTransport` / `BridgeObsTransport` | Singleton (holds the connection / hub context) | **self-host → Direct; SaaS → Bridge** (`IDeploymentProfileService.Current`; per-channel `Mode` override) |
| `IObsBridgeRegistry` | `ObsBridgeRegistry` | Singleton | leader state via `ICacheService` (Redis backplane on SaaS) |
| `IObsConnectionService` | `ObsConnectionService` | Scoped | encrypts via `IFieldCipher` |
| `ObsConnectionRepository` | `ObsConnectionRepository` | Scoped | tenant-filtered |
| `ICommandAction` (20 obs actions) | `ObsSwitchSceneAction`, … | Transient | registered with the pipeline action set |
| `obs_event` trigger source | `ObsEventTriggerSource` | Singleton | consumes `ObsEventReceivedEvent`, matches bound pipelines |

`OBSRelayHub` (existing) is extended with the bridge methods (§7). The bridge static asset (`/obs-bridge`) is served by the `web/` public pages (lightweight, like the overlay pages).

---

## 9. Testing — prove behavior

- **Typed action → exact request** — `obs_set_source "Cam" in "Main" visible=false` issues `GetSceneItemId(Main,Cam)` **then** `SetSceneItemEnabled(Main, <id>, false)` (assert both, in order, with the resolved numeric id — D7); `obs_input_volume volume_db=-6` sends `inputVolumeDb=-6` and **omits** `inputVolumeMul` (xor).
- **Single-executor election** — three bridge connections register; exactly one is leader; a command is delivered to the leader **only** (assert standbys receive nothing); killing the leader promotes a standby and the next command reaches the new leader; a duplicated `CommandId` is executed **once** (bridge dedup).
- **Event → trigger** — a forwarded `CurrentProgramSceneChanged{sceneName:"BRB"}` fires a pipeline bound to that event+filter and exposes `{{obs.event.sceneName}}="BRB"`; a `ReplayBufferSaved` exposes `savedReplayPath`; a high-volume event does **not** arrive unless its mask bit is set.
- **Profile + degradation** — self-host resolves `DirectObsTransport`, SaaS resolves `BridgeObsTransport`; with no leader, every control op and `TestConnectionAsync` return `OBS_BRIDGE_OFFLINE` (assert the code; never silent success).
- **Secret custody** — `UpsertAsync` stores `PasswordCipher` as AEAD ciphertext (never plaintext); `GetAsync` returns `HasPassword=true` with no password field; the bridge URL carries only the `BridgeToken`, never the password.
- **Impact gating** — a Moderator can `obs_switch_scene` and `obs_save_replay` but is rejected on `obs_streaming` and `obs_request` (Broadcaster floor).

---

## 10. Decisions (resolved)

Two profile transports — direct OBS-WS v5 self-host, browser-source bridge on SaaS (D1); single-executor election makes a multi-scene bridge safe **and** reliable (D2) with idempotent `CommandId`s (D3); full surface via generic `obs_request`/batch/vendor + ~18 typed actions (D4); OBS events as `obs_event` pipeline triggers, high-volume opt-in (D5); AEAD-encrypted password + impact gating (D6); protocol nuances baked in — `sceneItemId` resolution, volume xor, filter-by-source-name, paths-via-events, default `All` subscription (D7); schema delta P.14 `ObsConnection` with bridge token + event mask (D8).
