// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test

// Proves the event-response editor tells the truth about a row that follows the platform default: it lists every
// line the bot may pick from, says the bot picks one in the channel's tone, and shows nothing at all for a row
// with its own text (no tone behaviour is claimed there).
@OptIn(ExperimentalTestApi::class)
class ToneLinesNoteTest {

    private val note: String =
        "The bot picks one of these lines each time, in your channel tone. Edit the message to use your own text instead."

    @Test
    fun a_following_row_lists_every_tone_line_with_the_explanation() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    ToneLinesNote(
                        listOf(
                            "{user} followed. Bold of you to commit before seeing the whole stream. Welcome.",
                            "{user} is now a follower. No refunds, no returns, lots of chat.",
                        ),
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText(note).assertExists()
        onNodeWithText("{user} followed. Bold of you to commit before seeing the whole stream. Welcome.")
            .assertExists()
        onNodeWithText("{user} is now a follower. No refunds, no returns, lots of chat.").assertExists()
    }

    @Test
    fun a_row_with_its_own_text_shows_no_note_and_no_lines() = runComposeUiTest {
        setContent { NomNomzTheme { AppEnvironment("en") { ToneLinesNote(emptyList()) } } }
        waitForIdle()

        onNodeWithText(note).assertDoesNotExist()
    }
}
