// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.ui

import androidx.compose.runtime.collectAsState
import androidx.compose.runtime.getValue
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesController
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesState
import bot.nomnomz.dashboard.feature.commands.state.InMemoryReplyCatalogue
import bot.nomnomz.dashboard.feature.commands.state.PrimaryChannelOnly
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// The reply editor as rendered: edit a slot, save, and the saved text is what the row shows; reset (after the
// confirm) and the default text is back. Asserted on the rendered nodes and on what reached the catalogue.
@OptIn(ExperimentalTestApi::class)
class BuiltinRepliesDialogTest {
    @Test
    fun edit_then_save_shows_the_new_text_and_reset_shows_the_default_again() = runComposeUiTest {
        val api = InMemoryReplyCatalogue()
        val controller = BuiltinRepliesController(PrimaryChannelOnly(), api)
        runBlocking { controller.open("sr") }

        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    val state: BuiltinRepliesState by controller.state.collectAsState()
                    BuiltinRepliesDialog(state = state, controller = controller)
                }
            }
        }
        waitForIdle()
        onNodeWithText("Already queued by {requested.by}.").assertExists()

        onNodeWithTag("edit-sr-duplicate").performClick()
        waitForIdle()
        onNode(hasSetTextAction()).performTextReplacement("Beat you to it, {requested.by} got there first")
        waitForIdle()
        onNodeWithText("Preview: Beat you to it, StreamFan42 got there first").assertExists()
        onNodeWithTag("save-sr-duplicate").performClick()
        waitForIdle()

        assertEquals(listOf("set sr/duplicate=Beat you to it, {requested.by} got there first"), api.writes)
        onNodeWithText("Beat you to it, {requested.by} got there first").assertExists()
        onNodeWithText("Your text").assertExists()
        onNodeWithText("Added {track.name} to the queue.").assertExists()

        onNodeWithTag("reset-sr-duplicate").performClick()
        waitForIdle()
        onNodeWithText("Reset").performClick()
        waitForIdle()

        assertEquals("reset sr/duplicate", api.writes.last())
        onNodeWithText("Already queued by {requested.by}.").assertExists()
        onNodeWithText("Your text").assertDoesNotExist()
    }
}
