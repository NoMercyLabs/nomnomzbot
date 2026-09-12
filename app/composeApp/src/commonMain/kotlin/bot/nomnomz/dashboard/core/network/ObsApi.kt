// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import io.ktor.http.encodeURLQueryComponent
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.JsonElement

// The typed OBS-control facade — the channel's OBS WebSocket connection config, its browser-source bridge, and
// live scene/output control (obs-control.md §4/§5). All real state from the backend: the connection row, the
// bridge instance registry, and the live OBS state read through either a direct socket or the relay bridge — no
// fabricated scenes. The state holder depends on this interface and fakes it in tests without HTTP.
//
// Every route is channel-scoped (`{channelId}` in the path); the active tenant is also carried as X-Channel-Id
// by the shared client, so a channel switch retargets these too.
//
// Backend routes (ObsController):
//   GET    /api/v1/channels/{channelId}/obs/connection            →  StatusResponseDto<ObsConnectionDto>
//   PUT    /api/v1/channels/{channelId}/obs/connection            →  StatusResponseDto<ObsConnectionDto>
//   GET    /api/v1/channels/{channelId}/obs/bridge/setup          →  StatusResponseDto<ObsBridgeSetupDto>
//   POST   /api/v1/channels/{channelId}/obs/bridge/rotate-token   →  StatusResponseDto<ObsBridgeSetupDto>
//   GET    /api/v1/channels/{channelId}/obs/bridge/status         →  StatusResponseDto<ObsBridgeStatusDto>
//   GET    /api/v1/channels/{channelId}/obs/probe                 →  StatusResponseDto<ObsProbeDto>
//   GET    /api/v1/channels/{channelId}/obs/state                 →  StatusResponseDto<ObsStateDto>
//   GET    /api/v1/channels/{channelId}/obs/scenes                →  StatusResponseDto<IReadOnlyList<ObsSceneDto>>
//   GET    /api/v1/channels/{channelId}/obs/inputs                →  StatusResponseDto<IReadOnlyList<ObsInputDto>>
//   POST   /api/v1/channels/{channelId}/obs/scene                 →  StatusResponseDto<ObsResponse>
//   POST   /api/v1/channels/{channelId}/obs/streaming             →  StatusResponseDto<ObsResponse>
//   POST   /api/v1/channels/{channelId}/obs/recording             →  StatusResponseDto<ObsResponse>
//   POST   /api/v1/channels/{channelId}/obs/replay-buffer         →  StatusResponseDto<ObsResponse>
//   POST   /api/v1/channels/{channelId}/obs/replay-buffer/save    →  StatusResponseDto<ObsResponse>
//   POST   /api/v1/channels/{channelId}/obs/virtual-cam           →  StatusResponseDto<ObsResponse>
interface ObsApi {
    /** The channel's OBS connection config (mode / host / port / password + bridge-token flags, enablement). */
    suspend fun connection(channelId: String): ApiResult<ObsConnection>

    /** Upsert the OBS connection config — the desired full state. Returns the persisted (secret-masked) row. */
    suspend fun upsertConnection(channelId: String, body: UpsertObsConnectionBody): ApiResult<ObsConnection>

    /** The browser-source bridge setup — the URL to paste into OBS as a browser source. */
    suspend fun bridgeSetup(channelId: String): ApiResult<ObsBridgeSetup>

    /** Rotate the bridge token (invalidates the old browser-source URL). Returns the fresh setup URL. */
    suspend fun rotateBridgeToken(channelId: String): ApiResult<ObsBridgeSetup>

    /** The live bridge registry — how many browser-source instances are connected and whether one leads. */
    suspend fun bridgeStatus(channelId: String): ApiResult<ObsBridgeStatus>

    /**
     * Actively probe whether OBS is reachable RIGHT NOW — the truthful connect signal. Unlike [state] (which
     * returns a graceful empty 200 even when OBS is offline), a 200 here carries the real outcome: connected, or
     * the failing code. This is the signal the page trusts for "reachable", for direct and bridge modes alike.
     */
    suspend fun probe(channelId: String): ApiResult<ObsProbe>

    /** The live OBS state (current scene, streaming / recording flags, record timecode). */
    suspend fun state(channelId: String): ApiResult<ObsState>

    /** The channel's OBS scenes, with which one is currently on program. */
    suspend fun scenes(channelId: String): ApiResult<List<ObsScene>>

    /** The channel's OBS inputs (name / kind / mute / volume). */
    suspend fun inputs(channelId: String): ApiResult<List<ObsInput>>

    /** Switch the current program scene to [scene]. */
    suspend fun switchScene(channelId: String, scene: String): ApiResult<Unit>

    /** Audio mixer — set an input's mute to an absolute [muted] state. */
    suspend fun setInputMute(channelId: String, inputName: String, muted: Boolean): ApiResult<Unit>

    /** Audio mixer — set an input's volume in decibels ([volumeDb], OBS's dB scale). */
    suspend fun setInputVolume(channelId: String, inputName: String, volumeDb: Double): ApiResult<Unit>

    /** Control the stream output ([action]: 0 = start, 1 = stop, 2 = toggle — see [ObsToggle]). */
    suspend fun setStreaming(channelId: String, action: Int): ApiResult<Unit>

    /** Control the recording output ([action]: see [ObsRecordAction]). */
    suspend fun setRecording(channelId: String, action: Int): ApiResult<Unit>

    /** Control the replay buffer ([action]: 0 = start, 1 = stop, 2 = toggle — see [ObsToggle]). */
    suspend fun setReplayBuffer(channelId: String, action: Int): ApiResult<Unit>

    /** Save the last replay-buffer clip to disk. */
    suspend fun saveReplayBuffer(channelId: String): ApiResult<Unit>

    /** Control the virtual camera output ([action]: 0 = start, 1 = stop, 2 = toggle — see [ObsToggle]). */
    suspend fun setVirtualCam(channelId: String, action: Int): ApiResult<Unit>

    /** Whether the virtual camera output is currently running. */
    suspend fun virtualCamStatus(channelId: String): ApiResult<ObsVirtualCamStatus>

    /** OBS performance stats — CPU/memory load and render/output frame counters. */
    suspend fun stats(channelId: String): ApiResult<ObsStats>

    /** The items placed in [sceneName], with their per-scene visibility — distinct from [inputs], which only
     * sees global audio/video inputs, never per-scene placement. */
    suspend fun sceneItems(channelId: String, sceneName: String): ApiResult<List<ObsSceneItem>>

    /** Hide/show one item within one scene, without touching the underlying input's mute state or its
     * placement in any other scene. */
    suspend fun setSourceVisibility(
        channelId: String,
        sceneName: String,
        sourceName: String,
        visible: Boolean,
    ): ApiResult<Unit>

    /** The scene transitions OBS knows about, with the currently active one flagged. */
    suspend fun sceneTransitions(channelId: String): ApiResult<List<ObsTransition>>

    /** Make [transitionName] the active scene transition. */
    suspend fun setCurrentTransition(channelId: String, transitionName: String): ApiResult<Unit>

    /** The filters attached to [sourceName] (a scene or an input). */
    suspend fun sourceFilters(channelId: String, sourceName: String): ApiResult<List<ObsFilter>>

    /** Enable/disable [filterName] on [sourceName]. */
    suspend fun setFilterEnabled(
        channelId: String,
        sourceName: String,
        filterName: String,
        enabled: Boolean,
    ): ApiResult<Unit>

    /** Whether studio mode (preview/program) is currently on. */
    suspend fun studioMode(channelId: String): ApiResult<ObsStudioModeStatus>

    /** Turn studio mode on or off — a prerequisite for [triggerStudioTransition] to work at all. */
    suspend fun setStudioMode(channelId: String, enabled: Boolean): ApiResult<Unit>

    /** Set the studio-mode preview scene (distinct from [switchScene], which sets the PROGRAM scene) — the
     * scene queued up to become program on the next [triggerStudioTransition]. */
    suspend fun setPreviewScene(channelId: String, scene: String): ApiResult<Unit>

    /** Cut the current preview scene to program. [durationMs] null uses the transition's own configured
     * duration. Requires studio mode to already be on. */
    suspend fun triggerStudioTransition(channelId: String, durationMs: Int? = null): ApiResult<Unit>

    /** Control a media-source input ([action]: see [ObsMediaAction]). */
    suspend fun triggerMedia(channelId: String, inputName: String, action: Int): ApiResult<Unit>

    /** Reload a browser-source input's page. */
    suspend fun refreshBrowser(channelId: String, inputName: String): ApiResult<Unit>

    /** The hotkey names OBS knows about — the enumeration a hotkey-trigger picker needs. */
    suspend fun hotkeys(channelId: String): ApiResult<List<String>>

    /** Fire a hotkey by name (see [hotkeys] for the enumeration). */
    suspend fun triggerHotkey(channelId: String, hotkeyName: String): ApiResult<Unit>

    /** Capture a still image of [sourceName] in [imageFormat] (e.g. `"png"`, `"jpg"`). Returns a ready-to-decode
     * data URI (`data:image/png;base64,...`), never persisted server-side. */
    suspend fun screenshot(channelId: String, sourceName: String, imageFormat: String): ApiResult<String>

    /** Raw OBS-WS request batch (the full surface; power-user/dev only) — one [ObsRawResponseBody] per request,
     * in order. */
    suspend fun requestBatch(channelId: String, body: ObsRequestBatchBody): ApiResult<List<ObsRawResponseBody>>

    /** Third-party OBS plugin vendor request pass-through (power-user/dev only). */
    suspend fun callVendor(channelId: String, body: ObsVendorRequestBody): ApiResult<ObsRawResponseBody>
}

class RestObsApi(private val client: ApiClient) : ObsApi {
    override suspend fun connection(channelId: String): ApiResult<ObsConnection> =
        client.getEnvelope("api/v1/channels/$channelId/obs/connection")

    override suspend fun upsertConnection(
        channelId: String,
        body: UpsertObsConnectionBody,
    ): ApiResult<ObsConnection> = client.putEnvelope("api/v1/channels/$channelId/obs/connection", body)

    override suspend fun bridgeSetup(channelId: String): ApiResult<ObsBridgeSetup> =
        client.getEnvelope("api/v1/channels/$channelId/obs/bridge/setup")

    override suspend fun rotateBridgeToken(channelId: String): ApiResult<ObsBridgeSetup> =
        client.postEnvelope("api/v1/channels/$channelId/obs/bridge/rotate-token")

    override suspend fun bridgeStatus(channelId: String): ApiResult<ObsBridgeStatus> =
        client.getEnvelope("api/v1/channels/$channelId/obs/bridge/status")

    override suspend fun probe(channelId: String): ApiResult<ObsProbe> =
        client.getEnvelope("api/v1/channels/$channelId/obs/probe")

    override suspend fun state(channelId: String): ApiResult<ObsState> =
        client.getEnvelope("api/v1/channels/$channelId/obs/state")

    override suspend fun scenes(channelId: String): ApiResult<List<ObsScene>> =
        client.getEnvelope("api/v1/channels/$channelId/obs/scenes")

    override suspend fun inputs(channelId: String): ApiResult<List<ObsInput>> =
        client.getEnvelope("api/v1/channels/$channelId/obs/inputs")

    // The control POSTs return a StatusResponseDto<ObsResponse>, but the page re-reads live state after every
    // action, so the body is irrelevant here — any 2xx is success.
    override suspend fun switchScene(channelId: String, scene: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/scene", ObsSceneBody(scene = scene))

    override suspend fun setInputMute(channelId: String, inputName: String, muted: Boolean): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/inputs/mute",
            ObsInputMuteBody(inputName = inputName, muted = muted),
        )

    override suspend fun setInputVolume(channelId: String, inputName: String, volumeDb: Double): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/inputs/volume",
            ObsInputVolumeBody(inputName = inputName, volumeDb = volumeDb),
        )

    override suspend fun setStreaming(channelId: String, action: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/streaming", ObsToggleBody(action = action))

    override suspend fun setRecording(channelId: String, action: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/recording", ObsRecordBody(action = action))

    override suspend fun setReplayBuffer(channelId: String, action: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/replay-buffer", ObsToggleBody(action = action))

    override suspend fun saveReplayBuffer(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/replay-buffer/save")

    override suspend fun setVirtualCam(channelId: String, action: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/virtual-cam", ObsToggleBody(action = action))

    override suspend fun virtualCamStatus(channelId: String): ApiResult<ObsVirtualCamStatus> =
        client.getEnvelope("api/v1/channels/$channelId/obs/virtual-cam/status")

    override suspend fun stats(channelId: String): ApiResult<ObsStats> =
        client.getEnvelope("api/v1/channels/$channelId/obs/stats")

    override suspend fun sceneItems(channelId: String, sceneName: String): ApiResult<List<ObsSceneItem>> =
        client.getEnvelope(
            "api/v1/channels/$channelId/obs/scene-items?sceneName=${sceneName.encodeURLQueryComponent()}"
        )

    override suspend fun setSourceVisibility(
        channelId: String,
        sceneName: String,
        sourceName: String,
        visible: Boolean,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/scene-items/visibility",
            ObsSourceVisibilityBody(sceneName = sceneName, sourceName = sourceName, visible = visible),
        )

    override suspend fun sceneTransitions(channelId: String): ApiResult<List<ObsTransition>> =
        client.getEnvelope("api/v1/channels/$channelId/obs/scene-transitions")

    override suspend fun setCurrentTransition(channelId: String, transitionName: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/scene-transitions/current",
            ObsCurrentTransitionBody(transitionName = transitionName),
        )

    override suspend fun sourceFilters(channelId: String, sourceName: String): ApiResult<List<ObsFilter>> =
        client.getEnvelope(
            "api/v1/channels/$channelId/obs/source-filters?sourceName=${sourceName.encodeURLQueryComponent()}"
        )

    override suspend fun setFilterEnabled(
        channelId: String,
        sourceName: String,
        filterName: String,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/source-filters/enabled",
            ObsFilterEnabledBody(sourceName = sourceName, filterName = filterName, enabled = enabled),
        )

    override suspend fun studioMode(channelId: String): ApiResult<ObsStudioModeStatus> =
        client.getEnvelope("api/v1/channels/$channelId/obs/studio-mode")

    override suspend fun setStudioMode(channelId: String, enabled: Boolean): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/studio-mode", ObsStudioModeBody(enabled = enabled))

    override suspend fun setPreviewScene(channelId: String, scene: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/scene/preview", ObsSceneBody(scene = scene))

    override suspend fun triggerStudioTransition(channelId: String, durationMs: Int?): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/studio-mode/transition",
            ObsStudioTransitionBody(durationMs = durationMs),
        )

    override suspend fun triggerMedia(channelId: String, inputName: String, action: Int): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/inputs/media",
            ObsMediaActionBody(inputName = inputName, action = action),
        )

    override suspend fun refreshBrowser(channelId: String, inputName: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/inputs/refresh-browser",
            ObsRefreshBrowserBody(inputName = inputName),
        )

    override suspend fun hotkeys(channelId: String): ApiResult<List<String>> =
        client.getEnvelope("api/v1/channels/$channelId/obs/hotkeys")

    override suspend fun triggerHotkey(channelId: String, hotkeyName: String): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/obs/hotkeys/trigger",
            ObsHotkeyTriggerBody(hotkeyName = hotkeyName),
        )

    override suspend fun screenshot(channelId: String, sourceName: String, imageFormat: String): ApiResult<String> =
        client.postEnvelope(
            "api/v1/channels/$channelId/obs/source-screenshot",
            ObsScreenshotBody(sourceName = sourceName, imageFormat = imageFormat),
        )

    override suspend fun requestBatch(
        channelId: String,
        body: ObsRequestBatchBody,
    ): ApiResult<List<ObsRawResponseBody>> =
        client.postEnvelope("api/v1/channels/$channelId/obs/request/batch", body)

    override suspend fun callVendor(channelId: String, body: ObsVendorRequestBody): ApiResult<ObsRawResponseBody> =
        client.postEnvelope("api/v1/channels/$channelId/obs/request/vendor", body)
}

/**
 * The stream-output control verbs (backend `ObsToggle`, serialized as an integer). Used for the `streaming`
 * POST's `action` field.
 */
object ObsToggle {
    const val Start: Int = 0
    const val Stop: Int = 1
    const val Toggle: Int = 2
}

/**
 * The recording control verbs (backend `RecordAction`, serialized as an integer). Used for the `recording`
 * POST's `action` field. Only start/stop are surfaced by the page today.
 */
object ObsRecordAction {
    const val Start: Int = 0
    const val Stop: Int = 1
    const val Toggle: Int = 2
    const val Pause: Int = 3
    const val Resume: Int = 4
    const val Split: Int = 5
}

/**
 * The OBS connection config (backend `ObsConnectionDto`). [mode] is `direct` (the bot opens a WebSocket to
 * `host:port`) or `bridge` (a browser source in OBS relays through the bot). The password and bridge token are
 * never echoed — only [hasPassword] / [hasBridgeToken] flags. [eventSubscriptionsMask] is the OBS event
 * subscription bitmask; [lastError] surfaces the last connection failure.
 */
@Serializable
data class ObsConnection(
    val mode: String = "direct",
    val host: String? = null,
    val port: Int? = null,
    val hasPassword: Boolean = false,
    val hasBridgeToken: Boolean = false,
    val eventSubscriptionsMask: Int = 0,
    val isEnabled: Boolean = false,
    val lastConnectedAt: String? = null,
    val lastError: String? = null,
)

/**
 * The upsert-connection body (backend `UpsertObsConnectionRequest`) — the desired full config. [password] is
 * write-only: `null` keeps the stored password unchanged, an empty string clears it, any other value sets it.
 * [eventSubscriptionsMask] is carried back unchanged from the current row so a save never resets it.
 */
@Serializable
data class UpsertObsConnectionBody(
    val mode: String,
    val host: String? = null,
    val port: Int? = null,
    val password: String? = null,
    val eventSubscriptionsMask: Int? = null,
    val isEnabled: Boolean,
)

/** The browser-source bridge setup (backend `ObsBridgeSetupDto`): the URL to paste into OBS as a browser source. */
@Serializable
data class ObsBridgeSetup(val bridgeUrl: String = "")

/**
 * The live bridge registry (backend `ObsBridgeStatusDto`): [instanceCount] connected browser sources, whether
 * one is the [hasLeader] (the instance that executes commands), and [leaderSince].
 */
@Serializable
data class ObsBridgeStatus(
    val instanceCount: Int = 0,
    val hasLeader: Boolean = false,
    val leaderSince: String? = null,
)

/**
 * The truthful OBS reachability probe (backend `ObsProbeDto`). Unlike a passive [state] read — which returns a
 * graceful empty 200 when OBS is offline, so a 200 there is NOT proof of connectivity — this reflects a real
 * transport attempt: [connected], plus the failing [errorCode] / [error] (e.g. `OBS_NOT_CONNECTED`,
 * `OBS_DISABLED`, `OBS_BRIDGE_OFFLINE`) when it could not reach OBS. Covers the direct socket and the bridge
 * leader alike.
 */
@Serializable
data class ObsProbe(
    val connected: Boolean = false,
    val errorCode: String? = null,
    val error: String? = null,
)

/** The live OBS state (backend `ObsStateDto`). */
@Serializable
data class ObsState(
    val currentScene: String? = null,
    val streaming: Boolean = false,
    val recording: Boolean = false,
    val recordPaused: Boolean = false,
    val replayBufferActive: Boolean = false,
    val recordTimecode: String? = null,
)

/** One OBS scene (backend `ObsSceneDto`): its [name] and whether it is currently on program. */
@Serializable
data class ObsScene(val name: String = "", val isCurrent: Boolean = false)

/** One OBS input (backend `ObsInputDto`): [name], [kind], and optional mute / volume. */
@Serializable
data class ObsInput(
    val name: String = "",
    val kind: String = "",
    val muted: Boolean? = null,
    val volumeDb: Double? = null,
)

/** The switch-scene body (backend `ObsSceneRequest`). */
@Serializable
data class ObsSceneBody(val scene: String)

/** The mixer mute body (backend `ObsInputMuteRequest`): set [inputName]'s mute to an absolute [muted] state. */
@Serializable
data class ObsInputMuteBody(val inputName: String, val muted: Boolean)

/** The mixer volume body (backend `ObsInputVolumeRequest`): set [inputName]'s [volumeDb] in decibels. */
@Serializable
data class ObsInputVolumeBody(val inputName: String, val volumeDb: Double)

/** The streaming-toggle body (backend `ObsToggleRequest`): [action] is an [ObsToggle] value. */
@Serializable
data class ObsToggleBody(val action: Int)

/** The recording-control body (backend `ObsRecordRequest`): [action] is an [ObsRecordAction] value. */
@Serializable
data class ObsRecordBody(val action: Int)

/**
 * The media-input control verbs (backend `MediaAction`, serialized as an integer). Used for the
 * `inputs/media` POST's `action` field.
 */
object ObsMediaAction {
    const val Play: Int = 0
    const val Pause: Int = 1
    const val Stop: Int = 2
    const val Restart: Int = 3
    const val Next: Int = 4
    const val Previous: Int = 5
}

/** Whether the virtual camera output is currently running (backend `ObsVirtualCamStatusDto`). */
@Serializable
data class ObsVirtualCamStatus(val outputActive: Boolean = false)

/**
 * OBS performance stats (backend `ObsStatsDto`): CPU/memory load and the render-thread vs. output-thread
 * frame counters used to detect dropped frames.
 */
@Serializable
data class ObsStats(
    val cpuUsage: Double = 0.0,
    val memoryUsage: Double = 0.0,
    val activeFps: Double = 0.0,
    val renderTotalFrames: Int = 0,
    val renderSkippedFrames: Int = 0,
    val outputTotalFrames: Int = 0,
    val outputSkippedFrames: Int = 0,
)

/**
 * One item (source instance) placed in a scene (backend `ObsSceneItemDto`) — the per-scene visibility
 * state [ObsInput] cannot express, since a global input can appear in several scenes with a different
 * enabled state in each.
 */
@Serializable
data class ObsSceneItem(val sceneItemId: Int = 0, val sourceName: String = "", val enabled: Boolean = false)

/** The scene-item visibility body (backend `ObsSourceVisibilityRequest`). */
@Serializable
data class ObsSourceVisibilityBody(val sceneName: String, val sourceName: String, val visible: Boolean)

/** One scene transition OBS knows about (backend `ObsTransitionDto`): its [name] and whether it is the
 * one currently selected. */
@Serializable
data class ObsTransition(val name: String = "", val isCurrent: Boolean = false)

/** The transition-select body (backend `ObsCurrentTransitionRequest`). */
@Serializable
data class ObsCurrentTransitionBody(val transitionName: String)

/** The studio-mode-transition body (backend `ObsStudioTransitionRequest`): [durationMs] null uses the
 * transition's own configured duration. */
@Serializable
data class ObsStudioTransitionBody(val durationMs: Int? = null)

/**
 * One filter attached to a source (backend `ObsFilterDto`): [name], [kind], whether it's [enabled], and
 * its position ([index]) in the source's filter chain.
 */
@Serializable
data class ObsFilter(val name: String = "", val kind: String = "", val enabled: Boolean = false, val index: Int = 0)

/** The filter-enable body (backend `ObsFilterEnabledRequest`). */
@Serializable
data class ObsFilterEnabledBody(val sourceName: String, val filterName: String, val enabled: Boolean)

/** Studio mode status (backend `ObsStudioModeStatusDto`). */
@Serializable
data class ObsStudioModeStatus(val enabled: Boolean = false)

/** The studio-mode body (backend `ObsStudioModeRequest`). */
@Serializable
data class ObsStudioModeBody(val enabled: Boolean)

/** The media-input control body (backend `ObsMediaActionRequest`): [action] is an [ObsMediaAction] value. */
@Serializable
data class ObsMediaActionBody(val inputName: String, val action: Int)

/** The browser-source-refresh body (backend `ObsRefreshBrowserRequest`). */
@Serializable
data class ObsRefreshBrowserBody(val inputName: String)

/** The hotkey-trigger body (backend `ObsHotkeyTriggerRequest`). */
@Serializable
data class ObsHotkeyTriggerBody(val hotkeyName: String)

/** The source-screenshot body (backend `ObsScreenshotRequest`). */
@Serializable
data class ObsScreenshotBody(val sourceName: String, val imageFormat: String)

/**
 * One raw OBS-WS request (backend `ObsRequest`) — the power-user pass-through shape. [requestData] is
 * arbitrary JSON (kotlinx.serialization's own [JsonElement], not a typed model), matching the server's
 * `IReadOnlyDictionary<string, object?>?`.
 */
@Serializable
data class ObsRawRequestBody(val requestType: String, val requestData: Map<String, JsonElement>? = null)

/**
 * A raw OBS-WS request batch (backend `ObsRequestBatch`). [execution] is an `ObsBatchExecution` ordinal
 * (0 = SerialRealtime, 1 = SerialFrame, 2 = Parallel).
 */
@Serializable
data class ObsRequestBatchBody(
    val requests: List<ObsRawRequestBody>,
    val execution: Int = 0,
    val haltOnFailure: Boolean = false,
)

/** The outcome of one raw OBS-WS request (backend `ObsResponse`) — the power-user response-viewer shape. */
@Serializable
data class ObsRawResponseBody(val ok: Boolean, val responseData: Map<String, JsonElement>? = null, val error: String? = null)

/** A vendor-request pass-through body (backend `ObsVendorRequest`) — third-party OBS plugin requests. */
@Serializable
data class ObsVendorRequestBody(
    val vendorName: String,
    val requestType: String,
    val requestData: Map<String, JsonElement>? = null,
)
