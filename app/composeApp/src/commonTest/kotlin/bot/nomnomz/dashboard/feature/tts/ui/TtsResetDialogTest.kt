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

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.tts.state.TtsResetChange
import bot.nomnomz.dashboard.feature.tts.state.TtsResetField
import bot.nomnomz.dashboard.feature.tts.state.TtsResetValue
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// The "Reset to defaults" confirm: shows the diff (current → default) and what stays untouched, lets the
// operator confirm only when something would actually change, and speaks both shipped languages.
@OptIn(ExperimentalTestApi::class)
class TtsResetDialogTest {

    private val changes: List<TtsResetChange> =
        listOf(
            TtsResetChange(TtsResetField.MaxCharacters, TtsResetValue.Number(120), TtsResetValue.Number(500)),
            TtsResetChange(TtsResetField.Mode, TtsResetValue.Choice("byok"), TtsResetValue.Choice("client_edge")),
            TtsResetChange(TtsResetField.ProfanityCensor, TtsResetValue.Flag(false), TtsResetValue.Flag(true)),
            TtsResetChange(TtsResetField.MinBits, TtsResetValue.Number(100), TtsResetValue.Number(null)),
        )

    @Test
    fun it_lists_each_change_as_current_to_default_and_confirm_fires_the_reset() = runComposeUiTest {
        var confirmed = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme { TtsResetDialog(changes = changes, onConfirm = { confirmed++ }, onDismiss = {}) }
            }
        }
        waitForIdle()

        onNodeWithText("Max message length").assertExists()
        onNodeWithText("120 → 500").assertExists()
        onNodeWithText("Dispatch mode").assertExists()
        onNodeWithText("Bring your own key → Client (Edge)").assertExists()
        onNodeWithText("Filter profanity").assertExists()
        onNodeWithText("Off → On").assertExists()
        onNodeWithText("100 → None").assertExists()
        // The dialog states what a reset leaves alone.
        assertTrue(
            onAllNodesWithText("not touched", substring = true).fetchSemanticsNodes().isNotEmpty(),
            "the confirm must say the BYOK keys, voices and pronunciation rules are not touched",
        )

        onNodeWithText("Reset settings").assertIsEnabled().performClick()
        assertEquals(1, confirmed)
    }

    @Test
    fun with_nothing_to_change_it_says_so_and_the_confirm_is_disabled() = runComposeUiTest {
        var confirmed = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme { TtsResetDialog(changes = emptyList(), onConfirm = { confirmed++ }, onDismiss = {}) }
            }
        }
        waitForIdle()

        assertTrue(onAllNodesWithText("already at its default", substring = true).fetchSemanticsNodes().isNotEmpty())
        onNodeWithText("Reset settings").assertIsNotEnabled()
        assertEquals(0, confirmed)
    }

    @Test
    fun cancel_dismisses_without_confirming() = runComposeUiTest {
        var confirmed = 0
        var dismissed = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    TtsResetDialog(changes = changes, onConfirm = { confirmed++ }, onDismiss = { dismissed++ })
                }
            }
        }
        waitForIdle()

        onNodeWithText("Cancel").performClick()

        assertEquals(1, dismissed)
        assertEquals(0, confirmed)
    }

    @Test
    fun it_renders_in_dutch_from_resource_keys() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "nl") {
                NomNomzTheme { TtsResetDialog(changes = changes, onConfirm = {}, onDismiss = {}) }
            }
        }
        waitForIdle()

        onNodeWithText("TTS-instellingen terugzetten naar standaard").assertExists()
        onNodeWithText("Instellingen terugzetten").assertIsEnabled()
        onNodeWithText("Annuleren").assertExists()
        onNodeWithText("Maximale berichtlengte").assertExists()
        onNodeWithText("Eigen sleutel gebruiken → Client (Edge)").assertExists()
        onNodeWithText("Off → On").assertDoesNotExist()
        onNodeWithText("Uit → Aan").assertExists()
        onNodeWithText("100 → Geen").assertExists()
    }
}
