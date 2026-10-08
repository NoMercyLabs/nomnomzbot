// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mediashare.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performTextReplacement
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
import bot.nomnomz.dashboard.core.network.MediaShareApi
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.core.network.MediaShareRequest
import bot.nomnomz.dashboard.core.network.UpdateMediaShareConfigBody
import bot.nomnomz.dashboard.feature.mediashare.state.MediaShareController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.runBlocking
import org.jetbrains.compose.resources.StringResource

// Shared defect SH-1 on the Media Share config card: Save waits for the server (pending, one write per click), a
// failure keeps every typed value and shows the reason next to Save with no toast on top.
@OptIn(ExperimentalTestApi::class)
class MediaShareConfigSaveWaitsTest {
    private val reason: String = "The server said no."
    private val maxDurationField: SemanticsMatcher = hasSetTextAction() and hasText("111")

    @Test
    fun a_failed_save_keeps_the_typed_value_shows_the_reason_and_raises_no_toast() = runComposeUiTest {
        val api: StubApi = StubApi(update = { ApiResult.Failure(ApiError(409, "REFUSED", reason)) })
        val feedback: ConfigSaveFeedback = ConfigSaveFeedback()
        setContent { Screen(api, feedback) }
        waitForIdle()
        onNode(maxDurationField).performTextReplacement("250")
        onNodeWithText("Save").performScrollTo().performClick()
        waitForIdle()

        onNodeWithText(reason).assertExists()
        onNodeWithText("250").assertExists()
        assertEquals(1, api.sent.size)
        assertEquals(250, api.sent.single().maxDurationSeconds)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_save_sends_the_typed_value_and_shows_no_failure() = runComposeUiTest {
        val api: StubApi = StubApi(update = { body -> ApiResult.Ok(MediaShareConfig(maxDurationSeconds = body.maxDurationSeconds)) })
        setContent { Screen(api, ConfigSaveFeedback()) }
        waitForIdle()
        onNode(maxDurationField).performTextReplacement("250")
        onNodeWithText("Save").performScrollTo().performClick()
        waitForIdle()

        assertEquals(listOf(250), api.sent.map { it.maxDurationSeconds })
        onNodeWithText(reason).assertDoesNotExist()
        onNodeWithText("250").assertExists()
    }

    @Test
    fun a_second_click_while_the_save_runs_sends_no_second_write() = runComposeUiTest {
        val gate: CompletableDeferred<ApiResult<MediaShareConfig>> = CompletableDeferred()
        val api: StubApi = StubApi(update = { gate.await() })
        setContent { Screen(api, ConfigSaveFeedback()) }
        waitForIdle()
        onNodeWithText("Save").performScrollTo().performClick()
        waitForIdle()

        // Pending: the button shows its spinner in place of the label, so there is nothing left to click twice.
        onNodeWithText("Save").assertDoesNotExist()
        assertEquals(1, api.sent.size)
        gate.complete(ApiResult.Ok(MediaShareConfig()))
        waitForIdle()
        onNodeWithText("Save").assertExists()
        assertEquals(1, api.sent.size)
    }

    @androidx.compose.runtime.Composable
    private fun Screen(api: StubApi, feedback: Feedback) {
        val controller: MediaShareController = MediaShareController(api, feedback)
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
        androidx.compose.runtime.CompositionLocalProvider(LocalLifecycleOwner provides owner) {
            NomNomzTheme {
                AppEnvironment("en") { MediaShareScreen(controller = controller, role = ManagementRole.Broadcaster) }
            }
        }
    }
}

private class ConfigSaveFeedback : Feedback {
    var errors: Int = 0

    override fun success(label: StringResource, vararg formatArgs: Any) = Unit

    override fun error(label: StringResource, vararg formatArgs: Any) {
        errors += 1
    }

    override fun info(label: StringResource, vararg formatArgs: Any) = Unit

    override fun emit(message: FeedbackMessage) = Unit
}

private class StubApi(private val update: suspend (UpdateMediaShareConfigBody) -> ApiResult<MediaShareConfig>) : MediaShareApi {
    val sent: MutableList<UpdateMediaShareConfigBody> = mutableListOf()

    override suspend fun queue(status: String?): ApiResult<List<MediaShareRequest>> = ApiResult.Ok(emptyList())

    override suspend fun next(): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun approve(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun reject(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun skip(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun played(id: String): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun reorder(id: String, position: Int): ApiResult<MediaShareRequest> = error("stub")

    override suspend fun config(): ApiResult<MediaShareConfig> = ApiResult.Ok(MediaShareConfig(maxDurationSeconds = 111))

    override suspend fun updateConfig(body: UpdateMediaShareConfigBody): ApiResult<MediaShareConfig> {
        sent += body
        return update(body)
    }
}
