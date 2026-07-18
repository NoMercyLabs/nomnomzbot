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

// The typed VTube Studio facade — the channel's VTS API connection config, its plugin-token authorization, and
// live model/hotkey/expression control (vtube-studio.md §4). Real state only: the connection row and the live
// inventory read over the VTS WebSocket. The state holder depends on this interface and fakes it in tests.
//
// Every route is channel-scoped (`{channelId}` in the path). NOTE: the authorize call BLOCKS up to ~60s while
// the streamer clicks "Allow" in the VTS popup — the state holder runs it off a coroutine and surfaces the
// grant / deny / timeout result.
//
// Backend routes (VtsController):
//   GET    /api/v1/channels/{channelId}/vts/connection                      →  StatusResponseDto<VtsConnectionDto>
//   PUT    /api/v1/channels/{channelId}/vts/connection                      →  StatusResponseDto<VtsConnectionDto>
//   POST   /api/v1/channels/{channelId}/vts/connection/authorize            →  StatusResponseDto<boolean>
//   POST   /api/v1/channels/{channelId}/vts/connection/rotate-bridge-token  →  StatusResponseDto<VtsConnectionDto>
//   GET    /api/v1/channels/{channelId}/vts/inventory                       →  StatusResponseDto<VtsModelInventory>
//   POST   /api/v1/channels/{channelId}/vts/control                         →  StatusResponseDto<VtsRequestResult>
interface VtsApi {
    /** The channel's VTS connection config (mode / endpoint / token flags / enablement / live status). */
    suspend fun connection(channelId: String): ApiResult<VtsConnection>

    /** Upsert the VTS connection config — the desired full state. Returns the persisted row. */
    suspend fun upsertConnection(channelId: String, body: UpsertVtsConnectionBody): ApiResult<VtsConnection>

    /**
     * Request a plugin token from VTS. The streamer must click "Allow" in the VTS popup; the call BLOCKS up to
     * ~60s. Returns `true` when granted, `false` when denied. A transport/timeout maps to an [ApiResult.Failure].
     */
    suspend fun authorize(channelId: String): ApiResult<Boolean>

    /** Rotate the bridge token (for bridge mode). Returns the refreshed connection row. */
    suspend fun rotateBridgeToken(channelId: String): ApiResult<VtsConnection>

    /** The live VTS inventory — available models, hotkeys, and expressions for the control pickers. */
    suspend fun inventory(channelId: String): ApiResult<VtsModelInventory>

    /** Send a raw VTS control request ([requestType] + optional [payloadJson]). Returns the VTS response. */
    suspend fun control(channelId: String, body: VtsControlBody): ApiResult<VtsRequestResult>
}

class RestVtsApi(private val client: ApiClient) : VtsApi {
    override suspend fun connection(channelId: String): ApiResult<VtsConnection> =
        client.getEnvelope("api/v1/channels/$channelId/vts/connection")

    override suspend fun upsertConnection(
        channelId: String,
        body: UpsertVtsConnectionBody,
    ): ApiResult<VtsConnection> = client.putEnvelope("api/v1/channels/$channelId/vts/connection", body)

    override suspend fun authorize(channelId: String): ApiResult<Boolean> =
        client.postEnvelope("api/v1/channels/$channelId/vts/connection/authorize")

    override suspend fun rotateBridgeToken(channelId: String): ApiResult<VtsConnection> =
        client.postEnvelope("api/v1/channels/$channelId/vts/connection/rotate-bridge-token")

    override suspend fun inventory(channelId: String): ApiResult<VtsModelInventory> =
        client.getEnvelope("api/v1/channels/$channelId/vts/inventory")

    override suspend fun control(channelId: String, body: VtsControlBody): ApiResult<VtsRequestResult> =
        client.postEnvelope("api/v1/channels/$channelId/vts/control", body)
}

/**
 * The VTS connection config (backend `VtsConnectionDto`). [mode] is `direct` (the bot opens a WebSocket to
 * [endpoint]) or `bridge`. The plugin + bridge tokens are never echoed — only [hasPluginToken] /
 * [hasBridgeToken] flags. [status] is `unauthorized` | `authorized` | `connected` | `error`.
 */
@Serializable
data class VtsConnection(
    val mode: String = "direct",
    val endpoint: String = "ws://localhost:8001",
    val hasPluginToken: Boolean = false,
    val hasBridgeToken: Boolean = false,
    val eventSubscriptionsMask: Int = 0,
    val isEnabled: Boolean = false,
    val status: String = "unauthorized",
    val lastConnectedAt: String? = null,
)

/**
 * The upsert-connection body (backend `UpsertVtsConnectionRequest`). [endpoint] defaults to
 * `ws://localhost:8001`; [eventSubscriptionsMask] is carried back unchanged so a save never resets it.
 */
@Serializable
data class UpsertVtsConnectionBody(
    val mode: String,
    val endpoint: String? = null,
    val eventSubscriptionsMask: Int? = null,
    val isEnabled: Boolean,
)

/** The live VTS inventory (backend `VtsModelInventory`): models, hotkeys, and expression names. */
@Serializable
data class VtsModelInventory(
    val models: List<VtsModelRef> = emptyList(),
    val hotkeys: List<VtsHotkeyRef> = emptyList(),
    val expressions: List<String> = emptyList(),
)

/** One VTS model (backend `VtsModelRef`): its opaque [id], display [name], and whether it is currently loaded. */
@Serializable
data class VtsModelRef(val id: String = "", val name: String = "", val isLoaded: Boolean = false)

/** One VTS hotkey (backend `VtsHotkeyRef`): its opaque [id], display [name], and [type]. */
@Serializable
data class VtsHotkeyRef(val id: String = "", val name: String = "", val type: String = "")

/** The raw VTS control body (backend `VtsControlRequest`): a VTS [requestType] + optional JSON [payloadJson]. */
@Serializable
data class VtsControlBody(val requestType: String, val payloadJson: String? = null)

/** The VTS control response (backend `VtsRequestResult`): [ok] plus the raw [dataJson] or an [error]. */
@Serializable
data class VtsRequestResult(val ok: Boolean = false, val dataJson: String? = null, val error: String? = null)
