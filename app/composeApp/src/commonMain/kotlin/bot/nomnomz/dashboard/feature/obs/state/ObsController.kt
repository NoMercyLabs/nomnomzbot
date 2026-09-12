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
import bot.nomnomz.dashboard.core.network.ObsFilter
import bot.nomnomz.dashboard.core.network.ObsInput
import bot.nomnomz.dashboard.core.network.ObsProbe
import bot.nomnomz.dashboard.core.network.ObsRawResponseBody
import bot.nomnomz.dashboard.core.network.ObsRecordAction
import bot.nomnomz.dashboard.core.network.ObsRequestBatchBody
import bot.nomnomz.dashboard.core.network.ObsScene
import bot.nomnomz.dashboard.core.network.ObsSceneItem
import bot.nomnomz.dashboard.core.network.ObsState
import bot.nomnomz.dashboard.core.network.ObsStats
import bot.nomnomz.dashboard.core.network.ObsStudioModeStatus
import bot.nomnomz.dashboard.core.network.ObsToggle
import bot.nomnomz.dashboard.core.network.ObsTransition
import bot.nomnomz.dashboard.core.network.ObsVendorRequestBody
import bot.nomnomz.dashboard.core.network.ObsVirtualCamStatus
import bot.nomnomz.dashboard.core.network.UpsertObsConnectionBody
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonElement
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.obs_action_error
import nomnomzbot.composeapp.generated.resources.obs_no_channel_error
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.getString

// The power-user raw batch/vendor surface's own JSON instance — pretty-printed for the response viewer, lenient
// on input so a hand-typed request forgives trailing commas/etc. Kept local to this file rather than shared with
// [bot.nomnomz.dashboard.core.network.ApiClient]'s internal instance, which this state holder has no access to.
private val PassthroughJson: Json = Json {
    prettyPrint = true
    ignoreUnknownKeys = true
    isLenient = true
}

// The OBS-control page state-holder (obs-control.md §4): the channel's OBS connection config, the browser-source
// bridge, and — when OBS is reachable — the live scene/output state. It resolves the active channel, reads the
// connection row (fatal when it can't), then best-effort reads the bridge registry and the live OBS state; a
// live read failing (OBS not running) is surfaced inline, never blowing the page away. Writes (save config,
// rotate bridge token, switch scene, toggle streaming/recording) re-read on success so the page reflects truth.
@OptIn(ExperimentalResourceApi::class)
class ObsController(
    private val channelsApi: ChannelsApi,
    private val obsApi: ObsApi,
    private val feedback: Feedback = NoOpFeedback,
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

        _state.value =
            ObsUiState.Ready(
                connection = connection,
                bridgeSetup = bridgeSetup,
                bridgeStatus = bridgeStatus,
                live = live,
            )
    }

    /** Re-read only the live OBS state (probe + scene/output) after a control action, keeping the config in place. */
    suspend fun refreshLive() {
        val id: String = channelId ?: return
        val ready: ObsUiState.Ready = _state.value as? ObsUiState.Ready ?: return
        _state.value = ready.copy(live = readLiveWithProbe(id))
    }

    /**
     * Subscribe to hub events so the bridge indicator + live control reflect a browser-source connect/disconnect,
     * or the OBS WebSocket connection (re)establishing, the instant it happens — not only on a manual refresh.
     * [HubEvent.ObsBridgeStateChanged] and [HubEvent.ObsLiveStateChanged] (the latter fired the moment the direct
     * OBS connection comes up, carrying the REAL current stream/record status so an already-live/recording session
     * shows up immediately instead of waiting for a future start/stop event) both re-read the page for the active
     * channel. The full-refresh-on-retry path stays as a fallback for surfaces without a live hub connection.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.collect { evt ->
            val matchesChannel: Boolean =
                when (evt) {
                    is HubEvent.ObsBridgeStateChanged -> evt.state.broadcasterId == channelId
                    is HubEvent.ObsLiveStateChanged -> evt.state.broadcasterId == channelId
                    else -> false
                }
            if (matchesChannel) refresh()
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
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        val current: ObsConnection = (_state.value as? ObsUiState.Ready)?.connection ?: return failWrite(getString(Res.string.obs_no_channel_error))
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
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterWrite(obsApi.rotateBridgeToken(id))
    }

    /** Switch the program scene to [scene], then re-read live state. */
    suspend fun switchScene(scene: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.switchScene(id, scene))
    }

    /** Audio mixer — set an input's mute to [muted], then re-read live state so the toggle reflects OBS. */
    suspend fun setInputMute(inputName: String, muted: Boolean) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setInputMute(id, inputName, muted))
    }

    /** Audio mixer — set an input's volume to [volumeDb] decibels, then re-read live state. */
    suspend fun setInputVolume(inputName: String, volumeDb: Double) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setInputVolume(id, inputName, volumeDb))
    }

    /** Start or stop the stream based on the current live flag; then re-read live state. */
    suspend fun toggleStreaming() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        val streaming: Boolean = (_state.value as? ObsUiState.Ready)?.live?.state?.streaming == true
        afterLiveAction(obsApi.setStreaming(id, if (streaming) ObsToggle.Stop else ObsToggle.Start))
    }

    /** Start or stop recording based on the current live flag; then re-read live state. */
    suspend fun toggleRecording() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        val recording: Boolean = (_state.value as? ObsUiState.Ready)?.live?.state?.recording == true
        afterLiveAction(obsApi.setRecording(id, if (recording) ObsRecordAction.Stop else ObsRecordAction.Start))
    }

    /** Start or stop the replay buffer based on the current live flag; then re-read live state. */
    suspend fun toggleReplayBuffer() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        val active: Boolean = (_state.value as? ObsUiState.Ready)?.live?.state?.replayBufferActive == true
        afterLiveAction(obsApi.setReplayBuffer(id, if (active) ObsToggle.Stop else ObsToggle.Start))
    }

    /** Save the last replay-buffer clip to disk; then re-read live state. */
    suspend fun saveReplayBuffer() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.saveReplayBuffer(id))
    }

    /** Start or stop the virtual camera based on its current status; then re-read live state. */
    suspend fun toggleVirtualCam() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        val active: Boolean = (_state.value as? ObsUiState.Ready)?.live?.virtualCamActive == true
        afterLiveAction(obsApi.setVirtualCam(id, if (active) ObsToggle.Stop else ObsToggle.Start))
    }

    /** Pause the current recording; then re-read live state. */
    suspend fun pauseRecording() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setRecording(id, ObsRecordAction.Pause))
    }

    /** Resume a paused recording; then re-read live state. */
    suspend fun resumeRecording() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setRecording(id, ObsRecordAction.Resume))
    }

    /** Split the current recording into a new file; then re-read live state. */
    suspend fun splitRecording() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setRecording(id, ObsRecordAction.Split))
    }

    /** Turn studio mode (preview/program) on or off; then re-read live state. */
    suspend fun setStudioMode(enabled: Boolean) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setStudioMode(id, enabled))
    }

    /**
     * Queue [scene] as the studio-mode preview scene. OBS-WS exposes no read for "the current preview scene", so
     * the picker cannot highlight a selection the way the program-scene picker does — this is fire-and-forget.
     */
    suspend fun setPreviewScene(scene: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setPreviewScene(id, scene))
    }

    /** Cut the current preview scene to program (requires studio mode already on); then re-read live state. */
    suspend fun triggerStudioTransition() {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.triggerStudioTransition(id))
    }

    /** Make [transitionName] the active scene transition; then re-read live state. */
    suspend fun setCurrentTransition(transitionName: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.setCurrentTransition(id, transitionName))
    }

    /** Fire a hotkey by name (see [ObsLive.hotkeys] for the enumeration); then re-read live state. */
    suspend fun triggerHotkey(hotkeyName: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.triggerHotkey(id, hotkeyName))
    }

    /** Play/pause/restart/stop/skip a media-source input; then re-read live state. */
    suspend fun triggerMedia(inputName: String, action: Int) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        afterLiveAction(obsApi.triggerMedia(id, inputName, action))
    }

    /** Reload a browser-source input's page. Fire-and-forget — nothing in the live surface reflects it. */
    suspend fun refreshBrowserSource(inputName: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        when (val result: ApiResult<*> = obsApi.refreshBrowser(id, inputName)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Load the items placed in [sceneName] with their per-scene visibility — an on-demand view (not part of the
     * periodic live refresh) since it depends on which scene the operator picked to inspect.
     */
    suspend fun loadSceneItems(sceneName: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        when (val result: ApiResult<List<ObsSceneItem>> = obsApi.sceneItems(id, sceneName)) {
            is ApiResult.Ok -> setSceneItemsView(ObsSceneItemsView(sceneName, result.value))
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Hide/show [sourceName] within [sceneName], then reload that scene's item list to reflect it. */
    suspend fun setSourceVisible(sceneName: String, sourceName: String, visible: Boolean) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        when (val result: ApiResult<Unit> = obsApi.setSourceVisibility(id, sceneName, sourceName, visible)) {
            is ApiResult.Ok -> loadSceneItems(sceneName)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Load the filters attached to [sourceName] — an on-demand view (not part of the periodic live refresh)
     * since it depends on which source the operator picked to inspect.
     */
    suspend fun loadSourceFilters(sourceName: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        when (val result: ApiResult<List<ObsFilter>> = obsApi.sourceFilters(id, sourceName)) {
            is ApiResult.Ok -> setFiltersView(ObsFiltersView(sourceName, result.value))
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /** Enable/disable [filterName] on [sourceName], then reload that source's filter list to reflect it. */
    suspend fun setFilterEnabled(sourceName: String, filterName: String, enabled: Boolean) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        when (val result: ApiResult<Unit> = obsApi.setFilterEnabled(id, sourceName, filterName, enabled)) {
            is ApiResult.Ok -> loadSourceFilters(sourceName)
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    /**
     * Capture a still image of [sourceName] and store it for display — an on-demand view (not part of the
     * periodic live refresh) since it depends on which source/format the operator picked. The decode from the
     * returned data URI to a renderable bitmap happens in the UI layer ([bot.nomnomz.dashboard.core.media]),
     * so this only stores the raw data URI (or the failure).
     */
    suspend fun captureScreenshot(sourceName: String, imageFormat: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        when (val result: ApiResult<String> = obsApi.screenshot(id, sourceName, imageFormat)) {
            is ApiResult.Ok ->
                setScreenshotView(ObsScreenshotView(sourceName, imageDataUri = result.value, error = null))
            is ApiResult.Failure ->
                setScreenshotView(ObsScreenshotView(sourceName, imageDataUri = null, error = result.error.message))
        }
    }

    /**
     * Power-user surface: parse [requestsJson] as a JSON [ObsRequestBatchBody] and send it as a raw OBS-WS
     * request batch, storing the pretty-printed response (or a parse/API error) for display. A malformed
     * payload never reaches the network — it fails as a local parse error instead.
     */
    suspend fun runRawBatch(requestsJson: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        val body: ObsRequestBatchBody =
            try {
                PassthroughJson.decodeFromString(requestsJson)
            } catch (e: Exception) {
                setPassthroughResult(batchResult = "Invalid JSON: ${e.message}")
                return
            }
        when (val result: ApiResult<List<ObsRawResponseBody>> = obsApi.requestBatch(id, body)) {
            is ApiResult.Ok -> setPassthroughResult(batchResult = PassthroughJson.encodeToString(result.value))
            is ApiResult.Failure -> setPassthroughResult(batchResult = "Error: ${result.error.message}")
        }
    }

    /**
     * Power-user surface: send a vendor request pass-through. [requestDataJson] is optional free-form JSON
     * (blank = no data); a malformed payload never reaches the network — it fails as a local parse error.
     */
    suspend fun runRawVendor(vendorName: String, requestType: String, requestDataJson: String) {
        val id: String = channelId ?: return failWrite(getString(Res.string.obs_no_channel_error))
        val data: Map<String, JsonElement>? =
            if (requestDataJson.isBlank()) null
            else
                try {
                    PassthroughJson.decodeFromString(requestDataJson)
                } catch (e: Exception) {
                    setPassthroughResult(vendorResult = "Invalid JSON: ${e.message}")
                    return
                }
        val body = ObsVendorRequestBody(vendorName = vendorName, requestType = requestType, requestData = data)
        when (val result: ApiResult<ObsRawResponseBody> = obsApi.callVendor(id, body)) {
            is ApiResult.Ok -> setPassthroughResult(vendorResult = PassthroughJson.encodeToString(result.value))
            is ApiResult.Failure -> setPassthroughResult(vendorResult = "Error: ${result.error.message}")
        }
    }

    // ── internals ────────────────────────────────────────────────────────────

    private fun setSceneItemsView(view: ObsSceneItemsView) {
        val ready: ObsUiState.Ready = _state.value as? ObsUiState.Ready ?: return
        _state.value = ready.copy(sceneItemsView = view)
    }

    private fun setFiltersView(view: ObsFiltersView) {
        val ready: ObsUiState.Ready = _state.value as? ObsUiState.Ready ?: return
        _state.value = ready.copy(filtersView = view)
    }

    private fun setScreenshotView(view: ObsScreenshotView) {
        val ready: ObsUiState.Ready = _state.value as? ObsUiState.Ready ?: return
        _state.value = ready.copy(screenshotView = view)
    }

    private fun setPassthroughResult(batchResult: String? = null, vendorResult: String? = null) {
        val ready: ObsUiState.Ready = _state.value as? ObsUiState.Ready ?: return
        _state.value =
            ready.copy(
                batchResult = batchResult ?: ready.batchResult,
                vendorResult = vendorResult ?: ready.vendorResult,
            )
    }

    /** One best-effort sub-read of the live surface: [default] on failure, so one flaky OBS-WS request never
     * blanks the whole page — only the section it feeds. */
    private suspend fun <T> bestEffort(default: T, call: suspend () -> ApiResult<T>): T =
        when (val result: ApiResult<T> = call()) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> default
        }

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

    // The live OBS read (state + scenes + inputs + outputs/studio surface) is best-effort — OBS may not be
    // running / connected. A failure on the PRIMARY state read becomes an [ObsLive] carrying an error, so the
    // page shows "OBS not reachable" rather than a dead page. The secondary reads (scenes, inputs, virtual cam,
    // stats, transitions, studio mode) each fall back to an empty/false default on their own failure, so one
    // flaky sub-read never blanks the whole live surface.
    private suspend fun readLive(id: String): ObsLive {
        val state: ObsState =
            when (val result: ApiResult<ObsState> = obsApi.state(id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> return ObsLive(reachable = false, error = result.error.message)
            }
        val scenes: List<ObsScene> = bestEffort(emptyList()) { obsApi.scenes(id) }
        val inputs: List<ObsInput> = bestEffort(emptyList()) { obsApi.inputs(id) }
        val virtualCamActive: Boolean = bestEffort(ObsVirtualCamStatus()) { obsApi.virtualCamStatus(id) }.outputActive
        val stats: ObsStats? = bestEffort(null) { obsApi.stats(id) }
        val transitions: List<ObsTransition> = bestEffort(emptyList()) { obsApi.sceneTransitions(id) }
        val studioModeEnabled: Boolean = bestEffort(ObsStudioModeStatus()) { obsApi.studioMode(id) }.enabled
        val hotkeys: List<String> = bestEffort(emptyList()) { obsApi.hotkeys(id) }
        return ObsLive(
            reachable = true,
            state = state,
            scenes = scenes,
            inputs = inputs,
            virtualCamActive = virtualCamActive,
            stats = stats,
            transitions = transitions,
            studioModeEnabled = studioModeEnabled,
            hotkeys = hotkeys,
        )
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

    // The page is already showing content — a control/write failure announces on the shell-level feedback
    // toast (dismissable, see core/feedback/FeedbackHost.kt) rather than blowing the page away. Only when
    // the page has nothing to show yet (still Loading, or already in Error) does a failure become the page's
    // own Error state.
    private fun failWrite(detail: String) {
        val current: ObsUiState = _state.value
        if (current is ObsUiState.Ready) feedback.error(Res.string.obs_action_error, detail)
        else _state.value = ObsUiState.Error(detail)
    }

    private companion object {
        /** A forbidden connection/config read (obs:config:read) is non-fatal for a control-only moderator. */
        const val HTTP_FORBIDDEN: Int = 403
    }
}

/** The OBS page render state. */
sealed interface ObsUiState {
    data object Loading : ObsUiState

    /**
     * The channel's OBS config, the browser-source bridge, and the live OBS state (best-effort). A write/control
     * failure announces on the shell-level feedback toast rather than a field here — see [ObsController.failWrite].
     */
    data class Ready(
        val connection: ObsConnection,
        val bridgeSetup: ObsBridgeSetup?,
        val bridgeStatus: ObsBridgeStatus?,
        val live: ObsLive,
        /** The scene-items view the operator last opened (scene picker → per-item visibility), if any. */
        val sceneItemsView: ObsSceneItemsView? = null,
        /** The source-filters view the operator last opened (source picker → per-filter enable), if any. */
        val filtersView: ObsFiltersView? = null,
        /** The last screenshot the operator captured, if any. */
        val screenshotView: ObsScreenshotView? = null,
        /** The pretty-printed response (or error) from the last raw batch request, if any. */
        val batchResult: String? = null,
        /** The pretty-printed response (or error) from the last raw vendor request, if any. */
        val vendorResult: String? = null,
    ) : ObsUiState

    data class Error(val detail: String) : ObsUiState
}

/**
 * The live OBS read. [reachable] is false when OBS could not be queried (not running / not connected), with the
 * reason in [error]; otherwise [state] / [scenes] / [inputs] carry the live control surface, and
 * [virtualCamActive] / [stats] / [transitions] / [studioModeEnabled] carry the outputs/studio surface (each
 * independently best-effort — a single failed sub-read degrades to its default rather than blanking the page).
 */
data class ObsLive(
    val reachable: Boolean,
    val state: ObsState = ObsState(),
    val scenes: List<ObsScene> = emptyList(),
    val inputs: List<ObsInput> = emptyList(),
    val virtualCamActive: Boolean = false,
    val stats: ObsStats? = null,
    val transitions: List<ObsTransition> = emptyList(),
    val studioModeEnabled: Boolean = false,
    /** The hotkey names OBS knows about — the enumeration a hotkey-trigger picker needs. */
    val hotkeys: List<String> = emptyList(),
    val error: String? = null,
)

/** The scene-items view (obs-control.md — per-scene source visibility): the [sceneName] the operator picked to
 * inspect and its current [items]. */
data class ObsSceneItemsView(val sceneName: String, val items: List<ObsSceneItem>)

/** The source-filters view: the [sourceName] the operator picked to inspect and its current [filters]. */
data class ObsFiltersView(val sourceName: String, val filters: List<ObsFilter>)

/** The last screenshot capture: the [sourceName] it was taken of, and either [imageDataUri] (success) or
 * [error] (failure) — never both. */
data class ObsScreenshotView(val sourceName: String, val imageDataUri: String?, val error: String?)
