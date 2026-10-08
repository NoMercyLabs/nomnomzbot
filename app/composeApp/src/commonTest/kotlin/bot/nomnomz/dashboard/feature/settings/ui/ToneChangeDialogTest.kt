// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.DialogResult
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.settings.state.ToneChange
import kotlin.test.Test
import kotlin.test.assertEquals

// Proves a tone change names its consequence BEFORE it is saved: how many active event responses follow the
// default and change voice, and how many keep their own text. Nothing is confirmed until the operator says so.
@OptIn(ExperimentalTestApi::class)
class ToneChangeDialogTest {

    @Test
    fun the_dialog_shows_the_following_and_own_text_counts_and_confirms_only_on_request() = runComposeUiTest {
        var confirmed = 0
        var dismissed = 0
        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    ToneChangeDialog(
                        change = ToneChange(tone = "sassy", following = 7, own = 2),
                        toneLabel = "Sassy",
                        onDismiss = { dismissed++ },
                        onConfirm = {
                            confirmed++
                            DialogResult.Done
                        },
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText("Change the bot voice to Sassy?").assertExists()
        onNodeWithText(
                "Event responses that speak in your channel tone: 7. They change voice. " +
                    "Event responses with their own text: 2. They stay the same."
            )
            .assertExists()
        assertEquals(0, confirmed)

        onNodeWithText("Change voice").performClick()
        waitForIdle()

        assertEquals(1, confirmed)
        assertEquals(1, dismissed)
    }

    @Test
    fun cancelling_dismisses_without_confirming() = runComposeUiTest {
        var confirmed = 0
        var dismissed = 0
        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    ToneChangeDialog(
                        change = ToneChange(tone = "hype", following = 0, own = 9),
                        toneLabel = "Hype",
                        onDismiss = { dismissed++ },
                        onConfirm = {
                            confirmed++
                            DialogResult.Done
                        },
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText("Cancel").performClick()
        waitForIdle()

        assertEquals(0, confirmed)
        assertEquals(1, dismissed)
    }

    @Test
    fun unreadable_counts_are_stated_instead_of_invented() = runComposeUiTest {
        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    ToneChangeDialog(
                        change = ToneChange(tone = "chill", following = null, own = null),
                        toneLabel = "Chill",
                        onDismiss = {},
                        onConfirm = { DialogResult.Done },
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText(
                "Event responses that speak in your channel tone change voice. " +
                    "Event responses with their own text stay the same. The counts could not be loaded."
            )
            .assertExists()
    }

    @Test
    fun a_failed_confirm_keeps_the_dialog_open_with_the_reason_inline() = runComposeUiTest {
        var dismissed = 0
        setContent {
            NomNomzTheme {
                AppEnvironment("en") {
                    ToneChangeDialog(
                        change = ToneChange(tone = "sassy", following = 1, own = 1),
                        toneLabel = "Sassy",
                        onDismiss = { dismissed++ },
                        onConfirm = { DialogResult.Failed("Requires Broadcaster.") },
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText("Change voice").performClick()
        waitForIdle()

        onNodeWithText("Change the bot voice to Sassy?").assertExists()
        onNodeWithText("Requires Broadcaster.").assertExists()
        assertEquals(0, dismissed)
    }
}
