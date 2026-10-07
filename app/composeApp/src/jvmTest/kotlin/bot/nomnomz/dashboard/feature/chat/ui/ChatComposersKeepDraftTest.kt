// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.text.AnnotatedString
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performKeyInput
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.pressKey
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred

/**
 * Both chat composers (the single-channel Send box and the multi-chat composer) keep the typed line until the
 * server confirms it: a failed send leaves the text in the field (data loss X1), a pending send blocks a second
 * send, and only a success clears the field.
 */
@OptIn(ExperimentalTestApi::class)
class ChatComposersKeepDraftTest {
    private val failure: ApiResult<Unit> = ApiResult.Failure(ApiError(500, "SEND_FAILED", "could not send"))
    private val watched: List<ChannelSummary> = listOf(ChannelSummary(id = "a", login = "alpha", displayName = "Alpha"))

    // The editable text only: assertTextEquals also folds in the placeholder, which an empty field shows.
    private fun ComposeUiTest.assertFieldText(expected: String) {
        onNode(hasSetTextAction())
            .assert(SemanticsMatcher.expectValue(SemanticsProperties.EditableText, AnnotatedString(expected)))
    }

    @Composable
    private fun SingleChannelComposer(onSend: suspend (String, String) -> ApiResult<Unit>) {
        AppEnvironment(tag = "en") {
            NomNomzTheme {
                SendBox(
                    manage = ManageDecision.Allowed,
                    emotes = emptyList(),
                    replyTarget = null,
                    onCancelReply = {},
                    onSend = onSend,
                )
            }
        }
    }

    @Composable
    private fun MultiChannelComposer(onSend: suspend (String, String) -> ApiResult<Unit>) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { Composer(watched = watched, manage = ManageDecision.Allowed, onSend = onSend) }
        }
    }

    @Test
    fun single_channel_composer_keeps_the_text_when_the_send_fails() = runComposeUiTest {
        val sent: MutableList<String> = mutableListOf()
        setContent { SingleChannelComposer { text, _ -> sent += text; failure } }

        onNode(hasSetTextAction()).performTextInput("hello chat")
        onNodeWithText("Send").performClick()
        waitForIdle()

        assertEquals(listOf("hello chat"), sent)
        assertFieldText("hello chat")
    }

    @Test
    fun single_channel_composer_clears_the_text_only_after_the_send_succeeds() = runComposeUiTest {
        val answer: CompletableDeferred<ApiResult<Unit>> = CompletableDeferred()
        val sent: MutableList<String> = mutableListOf()
        setContent { SingleChannelComposer { text, _ -> sent += text; answer.await() } }

        onNode(hasSetTextAction()).performTextInput("hello chat")
        onNodeWithText("Send").performClick()
        waitForIdle()
        // The server has not answered yet: the line is still in the field.
        assertFieldText("hello chat")

        answer.complete(ApiResult.Ok(Unit))
        waitForIdle()

        assertFieldText("")
        assertEquals(listOf("hello chat"), sent)
    }

    @Test
    fun single_channel_composer_blocks_a_second_send_while_one_is_pending() = runComposeUiTest {
        val answer: CompletableDeferred<ApiResult<Unit>> = CompletableDeferred()
        val sent: MutableList<String> = mutableListOf()
        setContent { SingleChannelComposer { text, _ -> sent += text; answer.await() } }

        onNode(hasSetTextAction()).performTextInput("hello chat")
        onNodeWithText("Send").performClick()
        waitForIdle()
        onNode(hasSetTextAction()).performKeyInput { pressKey(Key.Enter) }
        waitForIdle()

        assertEquals(listOf("hello chat"), sent)
    }

    @Test
    fun multi_channel_composer_keeps_the_text_when_the_send_fails() = runComposeUiTest {
        val sent: MutableList<Pair<String, String>> = mutableListOf()
        setContent { MultiChannelComposer { channelId, text -> sent += channelId to text; failure } }

        onNode(hasSetTextAction()).performTextInput("hello chat")
        onNodeWithText("Send").performClick()
        waitForIdle()

        assertEquals(listOf("a" to "hello chat"), sent)
        assertFieldText("hello chat")
    }

    @Test
    fun multi_channel_composer_clears_the_text_only_after_the_send_succeeds_and_sends_once() = runComposeUiTest {
        val answer: CompletableDeferred<ApiResult<Unit>> = CompletableDeferred()
        val sent: MutableList<Pair<String, String>> = mutableListOf()
        setContent { MultiChannelComposer { channelId, text -> sent += channelId to text; answer.await() } }

        onNode(hasSetTextAction()).performTextInput("hello chat")
        onNodeWithText("Send").performClick()
        waitForIdle()
        assertFieldText("hello chat")
        // The Send label is replaced by the progress indicator while the send is pending, so a second click has
        // nothing to hit and the count stays at one.
        assertEquals(1, sent.size)

        answer.complete(ApiResult.Ok(Unit))
        waitForIdle()

        assertFieldText("")
        assertEquals(listOf("a" to "hello chat"), sent)
    }
}
