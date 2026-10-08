// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.obs.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.FeedbackMessage
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ObsApi
import bot.nomnomz.dashboard.core.network.ObsBridgeSetup
import bot.nomnomz.dashboard.core.network.ObsBridgeStatus
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.core.network.ObsFilter
import bot.nomnomz.dashboard.core.network.ObsInput
import bot.nomnomz.dashboard.core.network.ObsProbe
import bot.nomnomz.dashboard.core.network.ObsRawResponseBody
import bot.nomnomz.dashboard.core.network.ObsRequestBatchBody
import bot.nomnomz.dashboard.core.network.ObsScene
import bot.nomnomz.dashboard.core.network.ObsSceneItem
import bot.nomnomz.dashboard.core.network.ObsState
import bot.nomnomz.dashboard.core.network.ObsStats
import bot.nomnomz.dashboard.core.network.ObsStudioModeStatus
import bot.nomnomz.dashboard.core.network.ObsTransition
import bot.nomnomz.dashboard.core.network.ObsVendorRequestBody
import bot.nomnomz.dashboard.core.network.ObsVirtualCamStatus
import bot.nomnomz.dashboard.core.network.UpsertObsConnectionBody
import bot.nomnomz.dashboard.feature.obs.state.ObsController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.runBlocking
import org.jetbrains.compose.resources.StringResource

// Shared defect SH-1 on the OBS connection card: Save waits for the server. A failure keeps the typed values,
// shows the reason next to Save and raises no toast; while the call runs Save cannot fire a second write.
@OptIn(ExperimentalTestApi::class)
class ObsConnectionSaveWaitsTest {
    private val reason: String = "OBS refused the settings."

    @Test
    fun a_failed_save_keeps_the_typed_values_and_shows_the_reason_inline_with_no_toast() = runComposeUiTest {
        val api: StubObsApi = StubObsApi(upsert = { ApiResult.Failure(ApiError(409, "REFUSED", reason)) })
        val feedback: RecordingFeedback = RecordingFeedback()
        setContent { Screen(api, feedback) }
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextInput("my-host")
        waitForIdle()
        onNodeWithText("Save connection").performClick()
        waitForIdle()

        onNodeWithText(reason).assertExists()
        onNodeWithText("my-host").assertExists()
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_save_sends_the_typed_values_and_shows_no_error() = runComposeUiTest {
        val api: StubObsApi = StubObsApi(upsert = { ApiResult.Ok(ObsConnection(host = "my-host")) })
        setContent { Screen(api, RecordingFeedback()) }
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextInput("my-host")
        waitForIdle()
        onNodeWithText("Save connection").performClick()
        waitForIdle()

        assertEquals(listOf("my-host"), api.sent.map { it.host })
        onNodeWithText(reason).assertDoesNotExist()
    }

    @Test
    fun a_second_click_while_the_save_runs_sends_no_second_write() = runComposeUiTest {
        val gate: CompletableDeferred<ApiResult<ObsConnection>> = CompletableDeferred()
        val api: StubObsApi = StubObsApi(upsert = { gate.await() })
        setContent { Screen(api, RecordingFeedback()) }
        waitForIdle()
        onNodeWithText("Save connection").performClick()
        waitForIdle()

        // Pending: the button shows its spinner in place of the label, so there is nothing left to click twice.
        onNodeWithText("Save connection").assertDoesNotExist()
        assertEquals(1, api.sent.size)
        gate.complete(ApiResult.Ok(ObsConnection()))
        waitForIdle()
        onNodeWithText("Save connection").assertExists()
        assertEquals(1, api.sent.size)
    }

    @Composable
    private fun Screen(api: StubObsApi, feedback: Feedback) {
        val controller: ObsController = ObsController(StubChannelsApi(), api, feedback)
        runBlocking { controller.load() }
        val owner: LifecycleOwner =
            object : LifecycleOwner {
                override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
            }
        (owner.lifecycle as LifecycleRegistry).apply {
            currentState = Lifecycle.State.CREATED
            currentState = Lifecycle.State.STARTED
            currentState = Lifecycle.State.RESUMED
        }
        CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            NomNomzTheme {
                AppEnvironment("en") {
                    ObsScreen(controller = controller, role = ManagementRole.Broadcaster, backendOrigin = "http://localhost:5080")
                }
            }
        }
    }
}

private class RecordingFeedback : Feedback {
    var errors: Int = 0

    override fun success(label: StringResource, vararg formatArgs: Any) = Unit

    override fun error(label: StringResource, vararg formatArgs: Any) {
        errors += 1
    }

    override fun info(label: StringResource, vararg formatArgs: Any) = Unit

    override fun emit(message: FeedbackMessage) = Unit
}

private class StubChannelsApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "c1"))

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

// Only the connection read and the upsert answer; every other read fails, which the page treats as best-effort.
private class StubObsApi(private val upsert: suspend () -> ApiResult<ObsConnection>) : ObsApi {
    val sent: MutableList<UpsertObsConnectionBody> = mutableListOf()

    private fun <T> unavailable(): ApiResult<T> = ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun connection(channelId: String): ApiResult<ObsConnection> = ApiResult.Ok(ObsConnection())
    override suspend fun upsertConnection(channelId: String, body: UpsertObsConnectionBody): ApiResult<ObsConnection> {
        sent += body
        return upsert()
    }
    override suspend fun bridgeSetup(channelId: String): ApiResult<ObsBridgeSetup> = unavailable()
    override suspend fun rotateBridgeToken(channelId: String): ApiResult<ObsBridgeSetup> = unavailable()
    override suspend fun bridgeStatus(channelId: String): ApiResult<ObsBridgeStatus> = unavailable()
    override suspend fun probe(channelId: String): ApiResult<ObsProbe> = unavailable()
    override suspend fun state(channelId: String): ApiResult<ObsState> = unavailable()
    override suspend fun scenes(channelId: String): ApiResult<List<ObsScene>> = unavailable()
    override suspend fun inputs(channelId: String): ApiResult<List<ObsInput>> = unavailable()
    override suspend fun switchScene(channelId: String, scene: String): ApiResult<Unit> = unavailable()
    override suspend fun setInputMute(channelId: String, inputName: String, muted: Boolean): ApiResult<Unit> = unavailable()
    override suspend fun setInputVolume(channelId: String, inputName: String, volumeDb: Double): ApiResult<Unit> = unavailable()
    override suspend fun setStreaming(channelId: String, action: Int): ApiResult<Unit> = unavailable()
    override suspend fun setRecording(channelId: String, action: Int): ApiResult<Unit> = unavailable()
    override suspend fun setReplayBuffer(channelId: String, action: Int): ApiResult<Unit> = unavailable()
    override suspend fun saveReplayBuffer(channelId: String): ApiResult<Unit> = unavailable()
    override suspend fun setVirtualCam(channelId: String, action: Int): ApiResult<Unit> = unavailable()
    override suspend fun virtualCamStatus(channelId: String): ApiResult<ObsVirtualCamStatus> = unavailable()
    override suspend fun stats(channelId: String): ApiResult<ObsStats> = unavailable()
    override suspend fun sceneItems(channelId: String, sceneName: String): ApiResult<List<ObsSceneItem>> = unavailable()
    override suspend fun setSourceVisibility(
        channelId: String,
        sceneName: String,
        sourceName: String,
        visible: Boolean,
    ): ApiResult<Unit> = unavailable()
    override suspend fun sceneTransitions(channelId: String): ApiResult<List<ObsTransition>> = unavailable()
    override suspend fun setCurrentTransition(channelId: String, transitionName: String): ApiResult<Unit> = unavailable()
    override suspend fun sourceFilters(channelId: String, sourceName: String): ApiResult<List<ObsFilter>> = unavailable()
    override suspend fun setFilterEnabled(
        channelId: String,
        sourceName: String,
        filterName: String,
        enabled: Boolean,
    ): ApiResult<Unit> = unavailable()
    override suspend fun studioMode(channelId: String): ApiResult<ObsStudioModeStatus> = unavailable()
    override suspend fun setStudioMode(channelId: String, enabled: Boolean): ApiResult<Unit> = unavailable()
    override suspend fun setPreviewScene(channelId: String, scene: String): ApiResult<Unit> = unavailable()
    override suspend fun triggerStudioTransition(channelId: String, durationMs: Int?): ApiResult<Unit> = unavailable()
    override suspend fun triggerMedia(channelId: String, inputName: String, action: Int): ApiResult<Unit> = unavailable()
    override suspend fun refreshBrowser(channelId: String, inputName: String): ApiResult<Unit> = unavailable()
    override suspend fun hotkeys(channelId: String): ApiResult<List<String>> = unavailable()
    override suspend fun triggerHotkey(channelId: String, hotkeyName: String): ApiResult<Unit> = unavailable()
    override suspend fun screenshot(channelId: String, sourceName: String, imageFormat: String): ApiResult<String> =
        unavailable()
    override suspend fun requestBatch(
        channelId: String,
        body: ObsRequestBatchBody,
    ): ApiResult<List<ObsRawResponseBody>> = unavailable()
    override suspend fun callVendor(channelId: String, body: ObsVendorRequestBody): ApiResult<ObsRawResponseBody> =
        unavailable()
}
