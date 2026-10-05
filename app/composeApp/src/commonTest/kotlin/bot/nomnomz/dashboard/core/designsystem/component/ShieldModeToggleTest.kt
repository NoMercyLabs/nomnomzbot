// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Shield Mode must never turn on by accident (incident 2026-10-05: a one-click "Enable" row on Multi-chat,
// with nothing on it saying Shield Mode, locked down another streamer's live chat). The shared toggle is the
// ONE control every page renders, so the guarantees live here: the row names Shield Mode and the channel and
// says what it does; turning it ON goes through a confirm dialog and only the confirm calls back; cancel calls
// nothing; turning it OFF is immediate (ending a lockdown must never need a second click).
@OptIn(ExperimentalTestApi::class)
class ShieldModeToggleTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    private fun ComposeUiTest.hasText(text: String, substring: Boolean = false): Boolean =
        onAllNodesWithText(text, substring = substring).fetchSemanticsNodes().isNotEmpty()

    @Test
    fun row_names_shield_mode_the_channel_the_effect_and_the_off_state() = runComposeUiTest {
        setContent {
            EnglishContent {
                ShieldModeToggle(enabled = false, manage = ManageDecision.Allowed, onToggle = {}, channelName = "kitte")
            }
        }

        onNodeWithText("Shield Mode for kitte").assertIsDisplayed()
        onNodeWithText("Restricts chat in kitte for everyone while it is on.").assertIsDisplayed()
        onNodeWithText("Off").assertIsDisplayed()
        assertTrue(!hasText("On"), "an off row must not claim to be on")
    }

    @Test
    fun row_shows_the_on_state_when_active() = runComposeUiTest {
        setContent {
            EnglishContent {
                ShieldModeToggle(enabled = true, manage = ManageDecision.Allowed, onToggle = {}, channelName = "kitte")
            }
        }

        onNodeWithText("On").assertIsDisplayed()
        assertTrue(!hasText("Off"), "an active row must not claim to be off")
    }

    @Test
    fun turning_on_asks_first_and_only_the_confirm_calls_back() = runComposeUiTest {
        val calls: MutableList<Boolean> = mutableListOf()
        setContent {
            EnglishContent {
                ShieldModeToggle(
                    enabled = false,
                    manage = ManageDecision.Allowed,
                    onToggle = { calls.add(it) },
                    channelName = "kitte",
                )
            }
        }

        onNodeWithText("Turn on Shield Mode…").performClick()
        waitForIdle()

        assertEquals(emptyList(), calls, "a single click must never turn Shield Mode on")
        onNodeWithText("Turn on Shield Mode for kitte?").assertIsDisplayed()
        onNodeWithText("This restricts chat in kitte for everyone until you turn Shield Mode off.").assertIsDisplayed()

        onNodeWithText("Turn on Shield Mode").performClick()
        waitForIdle()

        assertEquals(listOf(true), calls, "confirming turns Shield Mode on exactly once")
        assertTrue(!hasText("Turn on Shield Mode for kitte?"), "the dialog closes after confirm")
    }

    @Test
    fun cancelling_the_confirm_calls_nothing_and_closes_the_dialog() = runComposeUiTest {
        val calls: MutableList<Boolean> = mutableListOf()
        setContent {
            EnglishContent {
                ShieldModeToggle(
                    enabled = false,
                    manage = ManageDecision.Allowed,
                    onToggle = { calls.add(it) },
                    channelName = "kitte",
                )
            }
        }

        onNodeWithText("Turn on Shield Mode…").performClick()
        waitForIdle()
        onNodeWithText("Cancel").performClick()
        waitForIdle()

        assertEquals(emptyList(), calls, "cancel must not touch Shield Mode")
        assertTrue(!hasText("Turn on Shield Mode for kitte?"), "the dialog closes after cancel")
    }

    @Test
    fun turning_off_is_immediate_with_no_dialog() = runComposeUiTest {
        val calls: MutableList<Boolean> = mutableListOf()
        setContent {
            EnglishContent {
                ShieldModeToggle(
                    enabled = true,
                    manage = ManageDecision.Allowed,
                    onToggle = { calls.add(it) },
                    channelName = "kitte",
                )
            }
        }

        onNodeWithText("Turn off Shield Mode").performClick()
        waitForIdle()

        assertEquals(listOf(false), calls, "turning off needs no confirm")
        assertTrue(!hasText("Turn on Shield Mode for kitte?"), "no dialog opens when turning off")
    }

    @Test
    fun without_a_channel_name_the_row_still_names_shield_mode_and_its_effect() = runComposeUiTest {
        setContent {
            EnglishContent {
                ShieldModeToggle(enabled = false, manage = ManageDecision.Allowed, onToggle = {})
            }
        }

        onNodeWithText("Shield Mode").assertIsDisplayed()
        onNodeWithText("Restricts this channel's chat for everyone while it is on.").assertIsDisplayed()
    }
}
