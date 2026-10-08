// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.DialogResult
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.moderation.state.BanTargetSearch
import kotlinx.coroutines.CompletableDeferred
import kotlin.test.Test
import kotlin.test.assertEquals

// S-MOD-BAN-UNSEEN: the "Moderate a viewer" dialog offers a Twitch account the bot has never seen, and a ban
// then goes out keyed on that account's platform id; a missing account and a failed lookup read differently.
@OptIn(ExperimentalTestApi::class)
class ModerateViewerDialogTest {

    private val unseen: PickerOption =
        PickerOption(id = "777", label = "NewPerson", sublabel = "newperson", avatarUrl = null, createdAt = "2019-03-04")

    @Test
    fun a_lookup_result_is_shown_and_banning_it_sends_the_platform_id() = runComposeUiTest {
        val applied: MutableList<String> = mutableListOf()
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ModerateViewerDialog(
                        searchBanTargets = { BanTargetSearch(options = listOf(unseen)) },
                        onConfirm = { action, userId, duration, reason ->
                            applied += "$action|$userId|$duration|$reason"
                            DialogResult.Done
                        },
                        onDismiss = {},
                    )
                }
            }
        }
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextInput("newperson")
        mainClock.advanceTimeBy(600)
        waitForIdle()

        onNodeWithContentDescription("NewPerson").performClick()
        waitForIdle()
        onNodeWithText("Apply").performClick()
        waitForIdle()

        assertEquals(listOf("ban|777|null|null"), applied)
    }

    @Test
    fun a_missing_account_reads_as_not_found_with_the_typed_name() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ModerateViewerDialog(
                        searchBanTargets = { BanTargetSearch(options = emptyList(), notFoundLogin = "ghostperson") },
                        onConfirm = { _, _, _, _ -> DialogResult.Done },
                        onDismiss = {},
                    )
                }
            }
        }
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextInput("ghostperson")
        mainClock.advanceTimeBy(600)
        waitForIdle()

        onNodeWithText("No Twitch account named ghostperson").assertExists()
    }

    @Test
    fun a_failed_lookup_reads_as_an_error_not_as_not_found() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ModerateViewerDialog(
                        searchBanTargets = { BanTargetSearch(options = emptyList(), lookupFailed = true) },
                        onConfirm = { _, _, _, _ -> DialogResult.Done },
                        onDismiss = {},
                    )
                }
            }
        }
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextInput("someone")
        mainClock.advanceTimeBy(600)
        waitForIdle()

        onNodeWithText("Twitch could not be reached. Try again.").assertExists()
        assertEquals(0, onAllNodesWithText("No Twitch account named someone").fetchSemanticsNodes().size)
    }

    private val reason: String = "Twitch said no"

    // Picks the unseen account from the lookup so the Apply button has a target.
    private fun androidx.compose.ui.test.ComposeUiTest.pickTheUnseenViewer() {
        waitForIdle()
        onAllNodes(hasSetTextAction())[0].performTextInput("newperson")
        mainClock.advanceTimeBy(600)
        waitForIdle()
        onNodeWithContentDescription("NewPerson").performClick()
        waitForIdle()
    }

    @Test
    fun a_failed_ban_keeps_the_dialog_open_and_shows_the_servers_reason() = runComposeUiTest {
        var dismissed: Int = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ModerateViewerDialog(
                        searchBanTargets = { BanTargetSearch(options = listOf(unseen)) },
                        onConfirm = { _, _, _, _ -> DialogResult.Failed(reason) },
                        onDismiss = { dismissed++ },
                    )
                }
            }
        }
        pickTheUnseenViewer()

        onNodeWithText("Apply").performClick()
        waitForIdle()

        onNodeWithText("Moderate a viewer").assertExists()
        onNodeWithText(reason).assertExists()
        assertEquals(0, dismissed)
    }

    @Test
    fun a_successful_ban_closes_the_dialog() = runComposeUiTest {
        var dismissed: Int = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ModerateViewerDialog(
                        searchBanTargets = { BanTargetSearch(options = listOf(unseen)) },
                        onConfirm = { _, _, _, _ -> DialogResult.Done },
                        onDismiss = { dismissed++ },
                    )
                }
            }
        }
        pickTheUnseenViewer()

        onNodeWithText("Apply").performClick()
        waitForIdle()

        assertEquals(1, dismissed)
    }

    @Test
    fun while_the_ban_is_in_flight_the_confirm_cannot_be_pressed_twice_and_the_dialog_stays_open() = runComposeUiTest {
        val answer: CompletableDeferred<DialogResult> = CompletableDeferred()
        var calls: Int = 0
        var dismissed: Int = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ModerateViewerDialog(
                        searchBanTargets = { BanTargetSearch(options = listOf(unseen)) },
                        onConfirm = { _, _, _, _ ->
                            calls++
                            answer.await()
                        },
                        onDismiss = { dismissed++ },
                    )
                }
            }
        }
        pickTheUnseenViewer()

        onNodeWithText("Apply").performClick()
        waitForIdle()
        onNodeWithText("Apply").performClick()
        waitForIdle()

        assertEquals(1, calls)
        assertEquals(0, dismissed)
        onNodeWithText("Moderate a viewer").assertExists()

        answer.complete(DialogResult.Done)
        waitForIdle()

        assertEquals(1, dismissed)
    }
}
