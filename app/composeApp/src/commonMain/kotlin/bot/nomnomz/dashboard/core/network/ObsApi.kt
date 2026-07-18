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

import kotlinx.serialization.Serializable

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
//   GET    /api/v1/channels/{channelId}/obs/state                 →  StatusResponseDto<ObsStateDto>
//   GET    /api/v1/channels/{channelId}/obs/scenes                →  StatusResponseDto<IReadOnlyList<ObsSceneDto>>
//   GET    /api/v1/channels/{channelId}/obs/inputs                →  StatusResponseDto<IReadOnlyList<ObsInputDto>>
//   POST   /api/v1/channels/{channelId}/obs/scene                 →  StatusResponseDto<ObsResponse>
//   POST   /api/v1/channels/{channelId}/obs/streaming             →  StatusResponseDto<ObsResponse>
//   POST   /api/v1/channels/{channelId}/obs/recording             →  StatusResponseDto<ObsResponse>
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

    /** The live OBS state (current scene, streaming / recording flags, record timecode). */
    suspend fun state(channelId: String): ApiResult<ObsState>

    /** The channel's OBS scenes, with which one is currently on program. */
    suspend fun scenes(channelId: String): ApiResult<List<ObsScene>>

    /** The channel's OBS inputs (name / kind / mute / volume). */
    suspend fun inputs(channelId: String): ApiResult<List<ObsInput>>

    /** Switch the current program scene to [scene]. */
    suspend fun switchScene(channelId: String, scene: String): ApiResult<Unit>

    /** Control the stream output ([action]: 0 = start, 1 = stop, 2 = toggle — see [ObsToggle]). */
    suspend fun setStreaming(channelId: String, action: Int): ApiResult<Unit>

    /** Control the recording output ([action]: see [ObsRecordAction]). */
    suspend fun setRecording(channelId: String, action: Int): ApiResult<Unit>
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

    override suspend fun setStreaming(channelId: String, action: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/streaming", ObsToggleBody(action = action))

    override suspend fun setRecording(channelId: String, action: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/obs/recording", ObsRecordBody(action = action))
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

/** The streaming-toggle body (backend `ObsToggleRequest`): [action] is an [ObsToggle] value. */
@Serializable
data class ObsToggleBody(val action: Int)

/** The recording-control body (backend `ObsRecordRequest`): [action] is an [ObsRecordAction] value. */
@Serializable
data class ObsRecordBody(val action: Int)
