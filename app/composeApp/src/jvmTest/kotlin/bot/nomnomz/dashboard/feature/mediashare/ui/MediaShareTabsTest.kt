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
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.MediaShareApi
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.core.network.MediaShareRequest
import bot.nomnomz.dashboard.core.network.UpdateMediaShareConfigBody
import bot.nomnomz.dashboard.feature.mediashare.state.MediaShareController
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking
import org.jetbrains.compose.resources.StringResource

// The Media Share page is two tabs. Queue lists what the API returns and carries none of the settings; Settings
// carries every setting, sends a changed one to the API, and shows no queue rows.
@OptIn(ExperimentalTestApi::class)
class MediaShareTabsTest {
    private val rows: List<MediaShareRequest> =
        listOf(
            MediaShareRequest(id = "a1", title = "First clip", status = "pending"),
            MediaShareRequest(id = "a2", title = "Second clip", status = "approved"),
        )
    private val maxQueueField: SemanticsMatcher = hasSetTextAction() and hasText("37")

    @Test
    fun the_queue_tab_shows_the_items_the_api_returns_and_none_of_the_settings() = runComposeUiTest {
        val api: TabsFakeApi = TabsFakeApi(rows)
        setContent { Host(api) { controller -> MediaShareQueueScreen(controller = controller, role = ManagementRole.Broadcaster) } }
        waitForIdle()

        onNodeWithText("First clip").assertExists()
        onNodeWithText("Second clip").assertExists()
        onNodeWithText("Save").assertDoesNotExist()
        onNodeWithText("Max queue length").assertDoesNotExist()
    }

    @Test
    fun the_settings_tab_sends_a_changed_setting_to_the_api_and_shows_no_queue_rows() = runComposeUiTest {
        val api: TabsFakeApi = TabsFakeApi(rows)
        setContent { Host(api) { controller -> MediaShareSettingsScreen(controller = controller, role = ManagementRole.Broadcaster) } }
        waitForIdle()

        onNodeWithText("First clip").assertDoesNotExist()
        onNode(maxQueueField).performTextReplacement("44")
        onNodeWithText("Save").performScrollTo().performClick()
        waitForIdle()

        assertEquals(listOf(44), api.sent.map { it.maxQueueLength })
    }

    @androidx.compose.runtime.Composable
    private fun Host(api: TabsFakeApi, screen: @androidx.compose.runtime.Composable (MediaShareController) -> Unit) {
        val controller: MediaShareController = MediaShareController(api, QuietFeedback())
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
            NomNomzTheme { AppEnvironment("en") { screen(controller) } }
        }
    }
}

private class QuietFeedback : Feedback {
    override fun success(label: StringResource, vararg formatArgs: Any) = Unit

    override fun error(label: StringResource, vararg formatArgs: Any) = Unit

    override fun info(label: StringResource, vararg formatArgs: Any) = Unit

    override fun emit(message: FeedbackMessage) = Unit
}

private class TabsFakeApi(private val rows: List<MediaShareRequest>) : MediaShareApi {
    val sent: MutableList<UpdateMediaShareConfigBody> = mutableListOf()

    override suspend fun queue(status: String?): ApiResult<List<MediaShareRequest>> = ApiResult.Ok(rows)

    override suspend fun next(): ApiResult<MediaShareRequest> = error("fake")

    override suspend fun approve(id: String): ApiResult<MediaShareRequest> = error("fake")

    override suspend fun reject(id: String): ApiResult<MediaShareRequest> = error("fake")

    override suspend fun skip(id: String): ApiResult<MediaShareRequest> = error("fake")

    override suspend fun played(id: String): ApiResult<MediaShareRequest> = error("fake")

    override suspend fun reorder(id: String, position: Int): ApiResult<MediaShareRequest> = error("fake")

    override suspend fun config(): ApiResult<MediaShareConfig> = ApiResult.Ok(MediaShareConfig(maxQueueLength = 37))

    override suspend fun updateConfig(body: UpdateMediaShareConfigBody): ApiResult<MediaShareConfig> {
        sent += body
        return ApiResult.Ok(MediaShareConfig(maxQueueLength = body.maxQueueLength))
    }
}
