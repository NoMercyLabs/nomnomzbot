// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.participant.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performKeyInput
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.pressKey
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.text.AnnotatedString
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred

/**
 * The song-request field keeps what the viewer typed until the server confirms it: a failed request leaves the
 * text in the field and shows the reason inline (data loss X1), a pending request blocks a second submit, and only
 * a success clears the field.
 */
@OptIn(ExperimentalTestApi::class)
class NowPlayingRequestKeepsDraftTest {
    private val failure: ApiResult<Unit> = ApiResult.Failure(ApiError(503, "NO_PROVIDER", "no music provider"))

    // The editable text only: assertTextEquals also folds in the placeholder, which an empty field shows.
    private fun ComposeUiTest.assertFieldText(expected: String) {
        onNode(hasSetTextAction())
            .assert(SemanticsMatcher.expectValue(SemanticsProperties.EditableText, AnnotatedString(expected)))
    }

    // The section title shares the "Request a song" text, so the button is the clickable node carrying it.
    private fun ComposeUiTest.submitButton() = onNode(hasClickAction() and hasText("Request a song"))

    @Composable
    private fun Card(onSubmit: suspend (String) -> ApiResult<Unit>) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { SubmitCard(pendingLimit = 3, subLaneUnlocked = false, onSubmit = onSubmit) }
        }
    }

    @Test
    fun a_failed_request_keeps_the_typed_text_and_shows_the_reason_inline() = runComposeUiTest {
        val sent: MutableList<String> = mutableListOf()
        setContent { Card { text -> sent += text; failure } }

        onNode(hasSetTextAction()).performTextInput("never gonna give you up")
        submitButton().performClick()
        waitForIdle()

        assertEquals(listOf("never gonna give you up"), sent)
        assertFieldText("never gonna give you up")
        onNodeWithText("Action failed: no music provider").assertIsDisplayed()
    }

    @Test
    fun the_text_clears_only_after_the_request_succeeds() = runComposeUiTest {
        val answer: CompletableDeferred<ApiResult<Unit>> = CompletableDeferred()
        val sent: MutableList<String> = mutableListOf()
        setContent { Card { text -> sent += text; answer.await() } }

        onNode(hasSetTextAction()).performTextInput("a song")
        submitButton().performClick()
        waitForIdle()
        // The server has not answered yet: the text is still in the field.
        assertFieldText("a song")

        answer.complete(ApiResult.Ok(Unit))
        waitForIdle()

        assertFieldText("")
        assertEquals(listOf("a song"), sent)
    }

    @Test
    fun a_pending_request_blocks_a_second_submit() = runComposeUiTest {
        val answer: CompletableDeferred<ApiResult<Unit>> = CompletableDeferred()
        val sent: MutableList<String> = mutableListOf()
        setContent { Card { text -> sent += text; answer.await() } }

        onNode(hasSetTextAction()).performTextInput("a song")
        submitButton().performClick()
        waitForIdle()
        onNode(hasSetTextAction()).performKeyInput { pressKey(Key.Enter) }
        waitForIdle()

        assertEquals(listOf("a song"), sent)
    }
}
