// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.obs.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ObsApi
import bot.nomnomz.dashboard.core.network.ObsBridgeSetup
import bot.nomnomz.dashboard.core.network.ObsBridgeStatus
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.core.network.ObsInput
import bot.nomnomz.dashboard.core.network.ObsProbe
import bot.nomnomz.dashboard.core.network.ObsRecordAction
import bot.nomnomz.dashboard.core.network.ObsScene
import bot.nomnomz.dashboard.core.network.ObsState
import bot.nomnomz.dashboard.core.network.ObsToggle
import bot.nomnomz.dashboard.core.network.UpsertObsConnectionBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The OBS-control page state-holder (obs-control.md §4): the channel's OBS connection config, the browser-source
// bridge, and — when OBS is reachable — the live scene/output state. It resolves the active channel, reads the
// connection row (fatal when it can't), then best-effort reads the bridge registry and the live OBS state; a
// live read failing (OBS not running) is surfaced inline, never blowing the page away. Writes (save config,
// rotate bridge token, switch scene, toggle streaming/recording) re-read on success so the page reflects truth.
class ObsController(
    private val channelsApi: ChannelsApi,
    private val obsApi: ObsApi,
) {
    private val _state: MutableStateFlow<ObsUiState> = MutableStateFlow(ObsUiState.Loading)

    /** The page render state: loading / ready (config + bridge + optional live) / error. */
    val state: StateFlow<ObsUiState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then read the connection, bridge, and (best-effort) live OBS state. */
    suspend fun load() {
        if (_state.value !is ObsUiState.Ready) _state.value = ObsUiState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ObsUiState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id
        refresh()
    }

    /** Re-read everything and rebuild the ready state (preserving any transient action error). */
    suspend fun refresh() {
        val id: String = channelId ?: return

        // The connection/config read is Broadcaster-gated (obs:config:read). A control-only MODERATOR gets a 403
        // here yet can still control scenes (obs:control), so a FORBIDDEN read is non-fatal: fall back to defaults
        // and keep the page (scene control still works off the probe + live reads). Any other failure (server
        // down, etc.) is a real error and takes the page to its error state as before.
        val connection: ObsConnection =
            when (val result: ApiResult<ObsConnection> = obsApi.connection(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure ->
                    if (result.error.status == HTTP_FORBIDDEN) {
                        ObsConnection()
                    } else {
                        _state.value = ObsUiState.Error(result.error.message)
                        return
                    }
            }

        // Bridge setup + registry — best-effort (bridge mode only meaningfully populates these; a mod is forbidden).
        val bridgeSetup: ObsBridgeSetup? =
            when (val result: ApiResult<ObsBridgeSetup> = obsApi.bridgeSetup(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> null
            }
        val bridgeStatus: ObsBridgeStatus? =
            when (val result: ApiResult<ObsBridgeStatus> = obsApi.bridgeStatus(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> null
            }

        val live: ObsLive = readLiveWithProbe(id)

        val previous: ObsUiState.Ready? = _state.value as? ObsUiState.Ready
        _state.value =
            ObsUiState.Ready(
                connection = connection,
                bridgeSetup = bridgeSetup,
                bridgeStatus = bridgeStatus,
                live = live,
                actionError = previous?.actionError,
            )
    }

    /** Re-read only the live OBS state (probe + scene/output) after a control action, keeping the config in place. */
    suspend fun refreshLive() {
        val id: String = channelId ?: return
        val ready: ObsUiState.Ready = _state.value as? ObsUiState.Ready ?: return
        _state.value = ready.copy(live = readLiveWithProbe(id))
    }

    /**
     * Subscribe to hub events so the bridge indicator + live control reflect a browser-source connect/disconnect
     * the instant it happens, not only on a manual refresh. On an [HubEvent.ObsBridgeStateChanged] for the active
     * channel, re-read the page (bridge status + probe + live). The full-refresh-on-retry path stays as a fallback
     * for surfaces without a live hub connection.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            if (evt is HubEvent.ObsBridgeStateChanged && evt.state.broadcasterId == channelId) {
                refresh()
            }
        }
    }

    /**
     * Persist the connection config. [password] is write-only: `null` keeps the stored one, `""` clears it, any
     * other value sets it. [eventSubscriptionsMask] is carried back from the current row so a save never resets
     * it. Reloads on success; surfaces the error on failure.
     */
    suspend fun saveConnection(
        mode: String,
        host: String?,
        port: Int?,
        password: String?,
        isEnabled: Boolean,
    ) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val current: ObsConnection = (_state.value as? ObsUiState.Ready)?.connection ?: return failWrite(NoChannelError)
        afterWrite(
            obsApi.upsertConnection(
                id,
                UpsertObsConnectionBody(
                    mode = mode,
                    host = host?.ifBlank { null },
                    port = port,
                    password = password,
                    eventSubscriptionsMask = current.eventSubscriptionsMask,
                    isEnabled = isEnabled,
                ),
            )
        )
    }

    /** Rotate the browser-source bridge token (invalidates the old URL). Reloads on success. */
    suspend fun rotateBridgeToken() {
        val id: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(obsApi.rotateBridgeToken(id))
    }

    /** Switch the program scene to [scene], then re-read live state. */
    suspend fun switchScene(scene: String) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        afterLiveAction(obsApi.switchScene(id, scene))
    }

    /** Audio mixer — set an input's mute to [muted], then re-read live state so the toggle reflects OBS. */
    suspend fun setInputMute(inputName: String, muted: Boolean) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        afterLiveAction(obsApi.setInputMute(id, inputName, muted))
    }

    /** Audio mixer — set an input's volume to [volumeDb] decibels, then re-read live state. */
    suspend fun setInputVolume(inputName: String, volumeDb: Double) {
        val id: String = channelId ?: return failWrite(NoChannelError)
        afterLiveAction(obsApi.setInputVolume(id, inputName, volumeDb))
    }

    /** Start or stop the stream based on the current live flag; then re-read live state. */
    suspend fun toggleStreaming() {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val streaming: Boolean = (_state.value as? ObsUiState.Ready)?.live?.state?.streaming == true
        afterLiveAction(obsApi.setStreaming(id, if (streaming) ObsToggle.Stop else ObsToggle.Start))
    }

    /** Start or stop recording based on the current live flag; then re-read live state. */
    suspend fun toggleRecording() {
        val id: String = channelId ?: return failWrite(NoChannelError)
        val recording: Boolean = (_state.value as? ObsUiState.Ready)?.live?.state?.recording == true
        afterLiveAction(obsApi.setRecording(id, if (recording) ObsRecordAction.Stop else ObsRecordAction.Start))
    }

    // ── internals ────────────────────────────────────────────────────────────

    // The truthful reachability read: PROBE first. The passive state read returns a graceful empty 200 even when
    // OBS is offline (so it can't tell reachable from the connect prompt) — trusting it lit up Start Streaming +
    // the mixer while the bridge card said offline. The probe is a real transport attempt, so its `connected`
    // flag is the single signal the page trusts, for direct and bridge modes alike. Only when connected do we
    // pull the live scene/output surface; otherwise the page shows "not reachable" with the probe's reason.
    private suspend fun readLiveWithProbe(id: String): ObsLive {
        val probe: ObsProbe? =
            when (val result: ApiResult<ObsProbe> = obsApi.probe(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> null
            }
        if (probe?.connected != true) {
            return ObsLive(reachable = false, error = probe?.error ?: probe?.errorCode)
        }
        return readLive(id)
    }

    // The live OBS read (state + scenes + inputs) is best-effort — OBS may not be running / connected. A failure
    // becomes an [ObsLive] carrying an error, so the page shows "OBS not reachable" rather than a dead page.
    private suspend fun readLive(id: String): ObsLive {
        val state: ObsState =
            when (val result: ApiResult<ObsState> = obsApi.state(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> return ObsLive(reachable = false, error = result.error.message)
            }
        val scenes: List<ObsScene> =
            when (val result: ApiResult<List<ObsScene>> = obsApi.scenes(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }
        val inputs: List<ObsInput> =
            when (val result: ApiResult<List<ObsInput>> = obsApi.inputs(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }
        return ObsLive(reachable = true, state = state, scenes = scenes, inputs = inputs)
    }

    private suspend fun afterWrite(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> refresh()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private suspend fun afterLiveAction(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> refreshLive()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: ObsUiState = _state.value
        _state.value =
            if (current is ObsUiState.Ready) current.copy(actionError = detail)
            else ObsUiState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."

        /** A forbidden connection/config read (obs:config:read) is non-fatal for a control-only moderator. */
        const val HTTP_FORBIDDEN: Int = 403
    }
}

/** The OBS page render state. */
sealed interface ObsUiState {
    data object Loading : ObsUiState

    /**
     * The channel's OBS config, the browser-source bridge, and the live OBS state (best-effort). [actionError]
     * is non-null only when the last write/control failed — surfaced as a transient banner over the content.
     */
    data class Ready(
        val connection: ObsConnection,
        val bridgeSetup: ObsBridgeSetup?,
        val bridgeStatus: ObsBridgeStatus?,
        val live: ObsLive,
        val actionError: String? = null,
    ) : ObsUiState

    data class Error(val detail: String) : ObsUiState
}

/**
 * The live OBS read. [reachable] is false when OBS could not be queried (not running / not connected), with the
 * reason in [error]; otherwise [state] / [scenes] / [inputs] carry the live control surface.
 */
data class ObsLive(
    val reachable: Boolean,
    val state: ObsState = ObsState(),
    val scenes: List<ObsScene> = emptyList(),
    val inputs: List<ObsInput> = emptyList(),
    val error: String? = null,
)
