// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.giveaways.ui

import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.CodePoolDetail
import bot.nomnomz.dashboard.feature.giveaways.state.PoolDetailState
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.CompletableDeferred

/** Pasted codes are the operator's only copy: a failed add keeps them, a confirmed add clears them. */
@OptIn(ExperimentalTestApi::class)
class ManagePoolDialogAddCodesTest {

    private val ready: PoolDetailState =
        PoolDetailState.Ready(CodePoolDetail(id = "p1", name = "keys", codes = emptyList()))

    // The field's real text (the Text semantics also carry the placeholder, so read EditableText only).
    private fun ComposeUiTest.fieldText(): String =
        onNode(SemanticsMatcher.keyIsDefined(SemanticsProperties.EditableText))
            .fetchSemanticsNode()
            .config[SemanticsProperties.EditableText]
            .text

    private fun ComposeUiTest.show(onAddCodes: suspend (String, List<String>) -> String?) {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme { ManagePoolDialog(state = ready, onDismiss = {}, onAddCodes = onAddCodes) }
            }
        }
        waitForIdle()
        onNode(hasSetTextAction()).performTextInput("ABC-1\nABC-2")
    }

    @Test
    fun a_failed_add_keeps_the_pasted_codes_and_shows_the_reason_inline() = runComposeUiTest {
        val calls: MutableList<Pair<String, List<String>>> = mutableListOf()
        show { poolId, codes ->
            calls += poolId to codes
            "server said no"
        }
        onNodeWithText("Add codes").performClick()
        waitForIdle()

        assertEquals(listOf("p1" to listOf("ABC-1", "ABC-2")), calls)
        assertEquals("ABC-1\nABC-2", fieldText())
        onNodeWithText("server said no", substring = true).assertExists()
    }

    @Test
    fun a_confirmed_add_clears_the_field() = runComposeUiTest {
        show { _, _ -> null }
        onNodeWithText("Add codes").performClick()
        waitForIdle()

        assertEquals("", fieldText())
    }

    @Test
    fun the_codes_stay_and_the_button_shows_progress_while_the_server_has_not_answered() = runComposeUiTest {
        val answer: CompletableDeferred<String?> = CompletableDeferred()
        var calls = 0
        show { _, _ ->
            calls++
            answer.await()
        }
        onNodeWithText("Add codes").performClick()
        waitForIdle()

        assertEquals("ABC-1\nABC-2", fieldText())
        // The button shows progress instead of its label, so there is nothing to click twice.
        onNodeWithText("Add codes").assertDoesNotExist()
        assertEquals(1, calls)

        answer.complete(null)
        waitForIdle()
        assertEquals("", fieldText())
    }
}
