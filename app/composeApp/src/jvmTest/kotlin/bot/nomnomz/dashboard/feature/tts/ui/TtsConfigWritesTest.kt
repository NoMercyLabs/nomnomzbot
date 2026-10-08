// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasAnyDescendant
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isToggleable
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelNamePronunciation
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsConfigUpdate
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.tts.state.TtsQueueController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// Shared defect SH-1 on the TTS config writes: the settings Save, the Reset confirm and the channel-name
// pronunciation Save. A failure keeps what the user typed, shows the reason where they are looking, raises no
// toast on top, and a success follows through to the server.
@OptIn(ExperimentalTestApi::class)
class TtsConfigWritesTest {
    private val reason: String = "The server said no."
    private val failure: ApiError = ApiError(500, "SERVER_ERROR", reason)

    @Test
    fun a_failed_settings_save_keeps_the_edit_and_shows_the_reason_beside_save_without_a_toast() = runComposeUiTest {
        val api: ConfigWritesApi = ConfigWritesApi(updateFailure = failure)
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        onAllNodes(isToggleable(), useUnmergedTree = true)[0].performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()
        onNodeWithText("Save").performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        onNodeWithText("Couldn't save TTS settings: $reason").assertExists()
        onNodeWithText("Save").assertExists()
        assertEquals(emptyList(), api.updates)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_settings_save_sends_the_edited_value() = runComposeUiTest {
        val api: ConfigWritesApi = ConfigWritesApi()
        open(api, CountingFeedback())
        onAllNodes(isToggleable(), useUnmergedTree = true)[0].performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()
        onNodeWithText("Save").performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(listOf(false), api.updates.map { it.isEnabled })
        onNodeWithText("Saved").assertExists()
    }

    @Test
    fun a_failed_reset_keeps_the_confirm_open_with_the_reason_and_no_toast() = runComposeUiTest {
        val api: ConfigWritesApi = ConfigWritesApi(resetFailure = failure)
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        onNodeWithText("Reset to defaults").performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()
        onNodeWithText("Reset settings").performClick()
        waitForIdle()

        onNodeWithText("Reset TTS settings to defaults").assertExists()
        onNodeWithText(reason).assertExists()
        assertEquals(0, api.resets)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_reset_closes_the_confirm_and_resets_on_the_server() = runComposeUiTest {
        val api: ConfigWritesApi = ConfigWritesApi()
        open(api, CountingFeedback())
        onNodeWithText("Reset to defaults").performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()
        onNodeWithText("Reset settings").performClick()
        waitForIdle()

        assertEquals(1, api.resets)
        onNodeWithText("Reset TTS settings to defaults").assertDoesNotExist()
    }

    @Test
    fun a_failed_name_pronunciation_save_keeps_the_typed_text_with_the_reason_and_no_toast() = runComposeUiTest {
        val api: ConfigWritesApi = ConfigWritesApi(nameFailure = failure)
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        openPronunciationTab()
        onAllNodes(hasSetTextAction()).onFirst().performTextInput("Jaydee")
        waitForIdle()
        onAllNodesWithText("Save").onFirst().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        onNodeWithText("Jaydee").assertExists()
        onNodeWithText(reason, substring = true).assertExists()
        assertEquals(emptyList(), api.names)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_name_pronunciation_save_sends_the_trimmed_value() = runComposeUiTest {
        val api: ConfigWritesApi = ConfigWritesApi()
        open(api, CountingFeedback())
        openPronunciationTab()
        onAllNodes(hasSetTextAction()).onFirst().performTextInput("Jaydee")
        waitForIdle()
        onAllNodesWithText("Save").onFirst().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(listOf<String?>("Jaydee"), api.names)
    }

    private fun ComposeUiTest.open(api: TtsApi, feedback: CountingFeedback) {
        val controller: TtsController = TtsController(StubChannelsApi(), api, feedback = feedback)
        val queueController: TtsQueueController = TtsQueueController(StubChannelsApi(), api)
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withConfigLifecycle {
                NomNomzTheme {
                    AppEnvironment("en") {
                        TtsScreen(
                            controller = controller,
                            queueController = queueController,
                            role = ManagementRole.Broadcaster,
                        )
                    }
                }
            }
        }
        waitForIdle()
    }

    private fun ComposeUiTest.openPronunciationTab() {
        onNode(hasClickAction() and hasAnyDescendant(hasText("Pronunciation")), useUnmergedTree = true).performClick()
        waitForIdle()
    }
}

@Composable
private fun withConfigLifecycle(content: @Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}

// The loaded config is enabled and the defaults are disabled, so Reset has a change to confirm and the toggle gives Save an edit.
private class ConfigWritesApi(
    private val updateFailure: ApiError? = null,
    private val resetFailure: ApiError? = null,
    private val nameFailure: ApiError? = null,
    base: TtsApi = StubTtsApi(),
) : TtsApi by base {
    val updates: MutableList<TtsConfigUpdate> = mutableListOf()
    val names: MutableList<String?> = mutableListOf()
    var resets: Int = 0

    override suspend fun config(channelId: String): ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig(isEnabled = true))

    override suspend fun configDefaults(channelId: String): ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig(isEnabled = false))

    override suspend fun updateConfig(channelId: String, update: TtsConfigUpdate): ApiResult<TtsConfig> {
        updateFailure?.let { return ApiResult.Failure(it) }
        updates.add(update)
        return ApiResult.Ok(TtsConfig(isEnabled = update.isEnabled ?: false))
    }

    override suspend fun resetConfig(channelId: String): ApiResult<TtsConfig> {
        resetFailure?.let { return ApiResult.Failure(it) }
        resets += 1
        return ApiResult.Ok(TtsConfig(isEnabled = false))
    }

    override suspend fun setChannelNamePronunciation(
        channelId: String,
        pronunciation: String?,
    ): ApiResult<ChannelNamePronunciation> {
        nameFailure?.let { return ApiResult.Failure(it) }
        names.add(pronunciation)
        return ApiResult.Ok(ChannelNamePronunciation(pronunciation = pronunciation))
    }
}
