// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.vts.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextClearance
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
import bot.nomnomz.dashboard.core.network.UpsertVtsConnectionBody
import bot.nomnomz.dashboard.core.network.VtsApi
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.core.network.VtsControlBody
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.core.network.VtsRequestResult
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.vts.state.VtsController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.runBlocking
import org.jetbrains.compose.resources.StringResource

// Shared defect SH-1 on the VTube Studio connection card: Save waits for the server. A failure keeps the typed
// values, shows the reason next to Save and raises no toast; while the call runs Save cannot fire a second write.
@OptIn(ExperimentalTestApi::class)
class VtsConnectionSaveWaitsTest {
    private val reason: String = "VTube Studio refused the settings."

    @Test
    fun a_failed_save_keeps_the_typed_values_and_shows_the_reason_inline_with_no_toast() = runComposeUiTest {
        val api: StubVtsApi = StubVtsApi(upsert = { ApiResult.Failure(ApiError(409, "REFUSED", reason)) })
        val feedback: VtsRecordingFeedback = VtsRecordingFeedback()
        setContent { Screen(api, feedback) }
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextClearance()
        onAllNodes(hasSetTextAction())[0].performTextInput("ws://my-host:8001")
        waitForIdle()
        onNodeWithText("Save connection").performClick()
        waitForIdle()

        onNodeWithText(reason).assertExists()
        onNodeWithText("ws://my-host:8001").assertExists()
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_save_sends_the_typed_values_and_shows_no_error() = runComposeUiTest {
        val api: StubVtsApi = StubVtsApi(upsert = { ApiResult.Ok(VtsConnection(endpoint = "ws://my-host:8001")) })
        setContent { Screen(api, VtsRecordingFeedback()) }
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextClearance()
        onAllNodes(hasSetTextAction())[0].performTextInput("ws://my-host:8001")
        waitForIdle()
        onNodeWithText("Save connection").performClick()
        waitForIdle()

        assertEquals(listOf<String?>("ws://my-host:8001"), api.sent.map { it.endpoint })
        onNodeWithText(reason).assertDoesNotExist()
    }

    @Test
    fun a_second_click_while_the_save_runs_sends_no_second_write() = runComposeUiTest {
        val gate: CompletableDeferred<ApiResult<VtsConnection>> = CompletableDeferred()
        val api: StubVtsApi = StubVtsApi(upsert = { gate.await() })
        setContent { Screen(api, VtsRecordingFeedback()) }
        waitForIdle()
        onNodeWithText("Save connection").performClick()
        waitForIdle()

        // Pending: the button shows its spinner in place of the label, so there is nothing left to click twice.
        onNodeWithText("Save connection").assertDoesNotExist()
        assertEquals(1, api.sent.size)
        gate.complete(ApiResult.Ok(VtsConnection()))
        waitForIdle()
        onNodeWithText("Save connection").assertExists()
        assertEquals(1, api.sent.size)
    }

    @Composable
    private fun Screen(api: StubVtsApi, feedback: Feedback) {
        val controller: VtsController = VtsController(StubVtsChannelsApi(), api, feedback)
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
                AppEnvironment("en") { VtsScreen(controller = controller, role = ManagementRole.Broadcaster) }
            }
        }
    }
}

private class VtsRecordingFeedback : Feedback {
    var errors: Int = 0

    override fun success(label: StringResource, vararg formatArgs: Any) = Unit

    override fun error(label: StringResource, vararg formatArgs: Any) {
        errors += 1
    }

    override fun info(label: StringResource, vararg formatArgs: Any) = Unit

    override fun emit(message: FeedbackMessage) = Unit
}

private class StubVtsChannelsApi : ChannelsApi {
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

// Only the connection read and the upsert answer; the inventory read fails, which the page treats as best-effort.
private class StubVtsApi(private val upsert: suspend () -> ApiResult<VtsConnection>) : VtsApi {
    val sent: MutableList<UpsertVtsConnectionBody> = mutableListOf()

    override suspend fun connection(channelId: String): ApiResult<VtsConnection> = ApiResult.Ok(VtsConnection())

    override suspend fun upsertConnection(channelId: String, body: UpsertVtsConnectionBody): ApiResult<VtsConnection> {
        sent += body
        return upsert()
    }

    override suspend fun authorize(channelId: String): ApiResult<Unit> = ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun rotateBridgeToken(channelId: String): ApiResult<VtsConnection> =
        ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun inventory(channelId: String): ApiResult<VtsModelInventory> =
        ApiResult.Failure(ApiError(404, "STUB", "stub"))

    override suspend fun control(channelId: String, body: VtsControlBody): ApiResult<VtsRequestResult> =
        ApiResult.Failure(ApiError(404, "STUB", "stub"))
}
