// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.vts.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.UpsertVtsConnectionBody
import bot.nomnomz.dashboard.core.network.VtsApi
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.core.network.VtsControlBody
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.core.network.VtsRequestResult
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The VTube Studio page state-holder (vtube-studio.md §4): the channel's VTS connection config, its plugin-token
// authorization, and — when authorized — the live model/hotkey/expression inventory for the control pickers. It
// resolves the active channel, reads the connection row (fatal when it can't), then best-effort reads the
// inventory. The blocking authorize call (up to ~60s while the streamer clicks Allow in VTS) is a distinct
// suspend fun returning a typed outcome so the screen surfaces grant / deny / timeout in its own words.
class VtsController(
    private val channelsApi: ChannelsApi,
    private val vtsApi: VtsApi,
) {
    private val _state: MutableStateFlow<VtsUiState> = MutableStateFlow(VtsUiState.Loading)

    /** The page render state: loading / ready (config + optional inventory) / error. */
    val state: StateFlow<VtsUiState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then read the connection and (best-effort) the live inventory. */
    suspend fun load() {
        if (_state.value !is VtsUiState.Ready) _state.value = VtsUiState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = VtsUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id
        refresh()
    }

    /** Re-read the connection + inventory and rebuild the ready state (preserving any transient action error). */
    suspend fun refresh() {
        val id: String = channelId ?: return

        val connection: VtsConnection =
            when (val result: ApiResult<VtsConnection> = vtsApi.connection(id)) {
                is ApiResult.Failure -> {
                    _state.value = VtsUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // Inventory only populates when VTS is authorized + connected — best-effort, null otherwise.
        val inventory: VtsModelInventory? =
            when (val result: ApiResult<VtsModelInventory> = vtsApi.inventory(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> null
            }

        val previous: VtsUiState.Ready? = _state.value as? VtsUiState.Ready
        _state.value =
            VtsUiState.Ready(connection = connection, inventory = inventory, actionError = previous?.actionError)
    }

    /**
     * Persist the connection config. [endpoint] defaults to `ws://localhost:8001` when blank;
     * [eventSubscriptionsMask] is carried back from the current row so a save never resets it. Reloads on
     * success; surfaces the error on failure.
     */
    suspend fun saveConnection(mode: String, endpoint: String?, isEnabled: Boolean) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val current: VtsConnection = (_state.value as? VtsUiState.Ready)?.connection ?: return failWrite(NoChannelError)
        afterWrite(
            vtsApi.upsertConnection(
                id,
                UpsertVtsConnectionBody(
                    mode = mode,
                    endpoint = endpoint?.ifBlank { null },
                    eventSubscriptionsMask = current.eventSubscriptionsMask,
                    isEnabled = isEnabled,
                ),
            )
        )
    }

    /**
     * Request a plugin token from VTS. BLOCKS up to ~60s while the streamer clicks "Allow" in the VTS popup.
     * Returns [VtsAuthorizeOutcome]: Granted (→ refreshes so status flips to authorized), Denied, or Failed
     * (transport / timeout). The screen drives its own "waiting" spinner around this call.
     */
    suspend fun authorize(): VtsAuthorizeOutcome {
        val id: String = channelId ?: run {
            failWrite(NoChannelError)
            return VtsAuthorizeOutcome.Failed
        }
        return when (val result: ApiResult<Boolean> = vtsApi.authorize(id)) {
            is ApiResult.Ok ->
                if (result.value) {
                    refresh()
                    VtsAuthorizeOutcome.Granted
                } else {
                    VtsAuthorizeOutcome.Denied
                }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                VtsAuthorizeOutcome.Failed
            }
        }
    }

    /** Rotate the bridge token (bridge mode). Reloads on success; surfaces the error on failure. */
    suspend fun rotateBridgeToken() {
        val id: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(vtsApi.rotateBridgeToken(id))
    }

    /**
     * Fire a raw VTS control request ([requestType] + optional [payloadJson]) — the seam behind load-model,
     * trigger-hotkey, and set-expression. Re-reads inventory on success (a model swap changes what's loaded).
     * Returns the VTS response so the screen can show ok / error, or null when there's no active channel.
     */
    suspend fun control(requestType: String, payloadJson: String?): VtsRequestResult? {
        val id: String = channelId ?: run {
            failWrite(NoChannelError)
            return null
        }
        return when (
            val result: ApiResult<VtsRequestResult> =
                vtsApi.control(id, VtsControlBody(requestType = requestType, payloadJson = payloadJson))
        ) {
            is ApiResult.Ok -> {
                refresh()
                result.value
            }
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
        }
    }

    // ── internals ────────────────────────────────────────────────────────────

    private suspend fun afterWrite(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> refresh()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: VtsUiState = _state.value
        _state.value =
            if (current is VtsUiState.Ready) current.copy(actionError = detail)
            else VtsUiState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The typed result of the blocking authorize call — the screen maps each to a localized message. */
enum class VtsAuthorizeOutcome {
    /** The streamer clicked Allow; a plugin token was granted (status flips to authorized). */
    Granted,

    /** The streamer declined the VTS popup. */
    Denied,

    /** The request failed or timed out (VTS not running, wrong endpoint, popup ignored). */
    Failed,
}

/** The VTS page render state. */
sealed interface VtsUiState {
    data object Loading : VtsUiState

    /**
     * The channel's VTS config and (when authorized) the live inventory for the control pickers. [actionError]
     * is non-null only when the last write/control failed — surfaced as a transient banner over the content.
     */
    data class Ready(
        val connection: VtsConnection,
        val inventory: VtsModelInventory?,
        val actionError: String? = null,
    ) : VtsUiState

    data class Error(val detail: String) : VtsUiState
}
