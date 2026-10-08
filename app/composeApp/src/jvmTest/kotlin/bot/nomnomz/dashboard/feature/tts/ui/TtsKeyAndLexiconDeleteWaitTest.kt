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
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasAnyDescendant
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onLast
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
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
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsLexiconEntry
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.tts.state.TtsQueueController
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// Shared defect SH-1 on two TTS writes: the provider key Save keeps the typed key when the server refuses it,
// and the pronunciation-rule delete stays open until the server answers. Both show the reason inline and raise
// no toast; a success follows through to the server.
@OptIn(ExperimentalTestApi::class)
class TtsKeyAndLexiconDeleteWaitTest {
    private val reason: String = "The server said no."
    private val failure: ApiError = ApiError(500, "SERVER_ERROR", reason)
    private val key: String = "test-key"

    @Test
    fun a_failed_key_save_keeps_the_typed_key_with_the_reason_beside_save_and_no_toast() = runComposeUiTest {
        val api: KeyAndDeleteApi = KeyAndDeleteApi(keyFailure = failure)
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        openTab("Voices")
        onAllNodes(hasSetTextAction()).onLast().performTextInput(key)
        waitForIdle()
        onAllNodesWithText("Save key").onLast().performClick()
        waitForIdle()

        // The key field is a password field: its semantics text is masked, one bullet per typed character.
        assertEquals("•".repeat(key.length), typedKey())
        onNodeWithText(reason, substring = true).assertExists()
        assertEquals(emptyList(), api.keys)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_key_save_sends_the_key_and_empties_the_field() = runComposeUiTest {
        val api: KeyAndDeleteApi = KeyAndDeleteApi()
        open(api, CountingFeedback())
        openTab("Voices")
        onAllNodes(hasSetTextAction()).onLast().performTextInput(key)
        waitForIdle()
        onAllNodesWithText("Save key").onLast().performClick()
        waitForIdle()

        assertEquals(listOf("elevenlabs" to key), api.keys)
        assertEquals("", typedKey())
    }

    @Test
    fun a_failed_lexicon_delete_keeps_the_confirm_open_with_the_reason_and_no_toast() = runComposeUiTest {
        val api: KeyAndDeleteApi = KeyAndDeleteApi(deleteFailure = failure, seed = rule())
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        openTab("Pronunciation")
        onNodeWithContentDescription("Delete rule brb").performClick()
        waitForIdle()
        onAllNodesWithText("Delete").onLast().performClick()
        waitForIdle()

        onNodeWithText("Delete pronunciation rule").assertExists()
        onNodeWithText(reason).assertExists()
        assertEquals(emptyList(), api.deleted)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_lexicon_delete_deletes_on_the_server_and_closes_the_confirm() = runComposeUiTest {
        val api: KeyAndDeleteApi = KeyAndDeleteApi(seed = rule())
        open(api, CountingFeedback())
        openTab("Pronunciation")
        onNodeWithContentDescription("Delete rule brb").performClick()
        waitForIdle()
        onAllNodesWithText("Delete").onLast().performClick()
        waitForIdle()

        assertEquals(listOf("lex-1"), api.deleted)
        onNodeWithText("Delete pronunciation rule").assertDoesNotExist()
        onNodeWithText("brb → be right back").assertDoesNotExist()
    }

    private fun rule(): TtsLexiconEntry =
        TtsLexiconEntry(id = "lex-1", phrase = "brb", replacement = "be right back", matchKind = "word")

    // The raw value of the last text field (the ElevenLabs key). The field masks it on screen.
    private fun ComposeUiTest.typedKey(): String =
        onAllNodes(hasSetTextAction()).onLast().fetchSemanticsNode().config[SemanticsProperties.EditableText].text

    private fun ComposeUiTest.openTab(label: String) {
        onNode(hasClickAction() and hasAnyDescendant(hasText(label)), useUnmergedTree = true).performClick()
        waitForIdle()
    }

    private fun ComposeUiTest.open(api: TtsApi, feedback: CountingFeedback) {
        val controller: TtsController = TtsController(StubChannelsApi(), api, feedback = feedback)
        val queueController: TtsQueueController = TtsQueueController(StubChannelsApi(), api)
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withKeyLifecycle {
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
}

@Composable
private fun withKeyLifecycle(content: @Composable () -> Unit) {
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

// Lexicon list and key writes are real: they record what was sent (or fail); every other call is inert.
private class KeyAndDeleteApi(
    private val keyFailure: ApiError? = null,
    private val deleteFailure: ApiError? = null,
    seed: TtsLexiconEntry? = null,
    base: TtsApi = StubTtsApi(seed = seed),
) : TtsApi by base {
    val keys: MutableList<Pair<String, String>> = mutableListOf()
    val deleted: MutableList<String> = mutableListOf()
    private val entries: MutableList<TtsLexiconEntry> = listOfNotNull(seed).toMutableList()

    override suspend fun lexicon(channelId: String): ApiResult<List<TtsLexiconEntry>> = ApiResult.Ok(entries.toList())

    override suspend fun deleteLexiconEntry(channelId: String, entryId: String): ApiResult<Unit> {
        deleteFailure?.let { return ApiResult.Failure(it) }
        deleted.add(entryId)
        entries.removeAll { it.id == entryId }
        return ApiResult.Ok(Unit)
    }

    override suspend fun setByokKey(
        channelId: String,
        provider: String,
        apiKey: String,
        region: String?,
    ): ApiResult<TtsConfig> {
        keyFailure?.let { return ApiResult.Failure(it) }
        keys.add(provider to apiKey)
        return ApiResult.Ok(TtsConfig(hasElevenLabsByokKey = true))
    }
}
