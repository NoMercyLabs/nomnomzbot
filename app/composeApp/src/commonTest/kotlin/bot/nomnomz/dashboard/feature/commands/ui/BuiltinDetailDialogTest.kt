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
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.commands.state.BuiltinDetailController
import bot.nomnomz.dashboard.feature.commands.state.BuiltinDetailState
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesController
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesState
import bot.nomnomz.dashboard.feature.commands.state.InMemoryReplyCatalogue
import bot.nomnomz.dashboard.feature.commands.state.PrimaryChannelOnly
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// A built-in's detail dialog as rendered: its settings and its replies on one surface; a saved cooldown reaches
// the backend; reset first spells out what goes back to default, then does it — settings and replies alike.
@OptIn(ExperimentalTestApi::class)
class BuiltinDetailDialogTest {
    private fun ui(api: InMemoryReplyCatalogue): Pair<BuiltinDetailController, BuiltinRepliesController> {
        val detail = BuiltinDetailController(PrimaryChannelOnly(), api)
        val replies = BuiltinRepliesController(PrimaryChannelOnly(), api)
        runBlocking {
            detail.open("sr")
            replies.open("sr")
        }
        return detail to replies
    }

    @Test
    fun a_saved_cooldown_reaches_the_backend_and_reset_shows_its_consequence_first() = runComposeUiTest {
        val api = InMemoryReplyCatalogue()
        runBlocking { api.setReply("ch1", "sr", "added", "Queued {track.name}!") }
        val (detail, replies) = ui(api)

        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    val detailState: BuiltinDetailState by detail.state.collectAsState()
                    val repliesState: BuiltinRepliesState by replies.state.collectAsState()
                    BuiltinDetailDialog(detailState, detail, repliesState, replies)
                }
            }
        }
        waitForIdle()

        // One surface: the settings and the replies of !sr.
        onNodeWithText("Edit !sr").assertExists()
        onNodeWithText("Leave empty for the default: 5 seconds.").assertExists()
        onNodeWithText("Queued {track.name}!").assertExists()
        onNodeWithTag("builtin-save").assertIsNotEnabled()

        onNode(hasSetTextAction()).performTextReplacement("45")
        waitForIdle()
        onNodeWithTag("builtin-save").performClick()
        waitForIdle()
        assertEquals("settings sr cooldown=45 permission=null", api.writes.last())

        onNodeWithTag("builtin-reset").performClick()
        waitForIdle()
        onNodeWithText(
            "!sr goes back to its defaults: on in chat, not read out by TTS, a 5 second cooldown, usable by " +
                "Everyone. 1 reworded replies go back to their default text.",
        ).assertExists()
        onNodeWithText("Reset").performClick()
        waitForIdle()

        assertEquals("reset sr", api.writes.last())
        onNodeWithText("Added {track.name} to the queue.").assertExists()
        onNodeWithText("Queued {track.name}!").assertDoesNotExist()
    }
}
