// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsNodeInteraction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performKeyInput
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.performTextInputSelection
import androidx.compose.ui.test.pressKey
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.text.TextRange
import androidx.compose.ui.text.input.TextFieldValue
import bot.nomnomz.dashboard.core.designsystem.component.TemplateVariableField
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.feature.pipelines.state.DeclaredVariable
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * The template field's `{` variable list: typing `{` opens it, a pick (click, or Down + Enter) replaces the
 * typed `{fragment` with `{key}` at the cursor, and Esc closes it. Asserts the resulting text and cursor, the
 * state the streamer actually keeps, not that something rendered.
 */
@OptIn(ExperimentalTestApi::class)
class TemplateVariableFieldTest {

    private val helpers: List<TemplateHelperDto> =
        listOf(
            TemplateHelperDto(key = "channel.name", descriptionKey = "desc.channel", sample = "NoMercy"),
            TemplateHelperDto(key = "user.name", descriptionKey = "desc.user", sample = "Viewer42"),
        )

    private fun ComposeUiTest.host(): () -> TextFieldValue {
        var current: TextFieldValue by mutableStateOf(TextFieldValue("Hello , welcome", TextRange(6)))
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    TemplateVariableField(
                        value = current,
                        onValueChange = { current = it },
                        label = "Message",
                        declared = listOf(DeclaredVariable(name = "winner", sample = "Sam")),
                        helpers = helpers,
                    )
                }
            }
        }
        return { current }
    }

    private fun ComposeUiTest.openWithBrace(): SemanticsNodeInteraction {
        val field: SemanticsNodeInteraction = onNode(hasSetTextAction())
        field.performClick()
        // The click moves the caret to the end; put it back after "Hello " before typing.
        field.performTextInputSelection(TextRange(6))
        field.performTextInput("{")
        waitForIdle()
        return field
    }

    @Test
    fun clicking_a_row_replaces_the_open_brace_with_the_token_and_puts_the_cursor_after_it() = runComposeUiTest {
        val value: () -> TextFieldValue = host()
        openWithBrace()

        onNodeWithText("{user.name}").performClick()
        waitForIdle()

        assertEquals("Hello {user.name}, welcome", value().text)
        assertEquals(TextRange("Hello {user.name}".length), value().selection)
        onNodeWithContentDescription("Template variables").assertDoesNotExist()
    }

    @Test
    fun down_then_enter_picks_the_highlighted_row() = runComposeUiTest {
        val value: () -> TextFieldValue = host()
        val field: SemanticsNodeInteraction = openWithBrace()

        // Rows: winner (declared), channel.name, user.name. Two downs reach the last row.
        field.performKeyInput { pressKey(Key.DirectionDown) }
        field.performKeyInput { pressKey(Key.DirectionDown) }
        field.performKeyInput { pressKey(Key.Enter) }
        waitForIdle()

        assertEquals("Hello {user.name}, welcome", value().text)
        assertEquals(TextRange("Hello {user.name}".length), value().selection)
    }

    @Test
    fun escape_closes_the_list_and_leaves_the_text_alone() = runComposeUiTest {
        val value: () -> TextFieldValue = host()
        val field: SemanticsNodeInteraction = openWithBrace()
        onNodeWithContentDescription("Template variables").assertExists()

        field.performKeyInput { pressKey(Key.Escape) }
        waitForIdle()

        onNodeWithContentDescription("Template variables").assertDoesNotExist()
        assertEquals("Hello {, welcome", value().text)
    }

    @Test
    fun a_query_with_no_match_shows_the_no_match_line() = runComposeUiTest {
        host()
        val field: SemanticsNodeInteraction = openWithBrace()
        field.performTextInput("zzz")
        waitForIdle()

        onNodeWithText("No variable matches").assertExists()
    }
}
