// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem

import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.height
import androidx.compose.material3.Text
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsDisplayed
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performKeyInput
import androidx.compose.ui.test.pressKey
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.DialogActionProgressTag
import bot.nomnomz.dashboard.core.designsystem.component.DialogResult
import bot.nomnomz.dashboard.core.designsystem.component.FormDialog
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlinx.coroutines.CompletableDeferred
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Psychology spec X1 and X3 for edit / create forms: Save stays open on failure, a dirty form asks before it
// closes, a clean form closes at once, Save shows progress, and a tall body never hides the footer.
@OptIn(ExperimentalTestApi::class)
class FormDialogTest {
    @Test
    fun a_failed_save_keeps_the_dialog_open_with_the_error_inline() = runComposeUiTest {
        var open: Boolean by mutableStateOf(true)
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    if (open) {
                        FormDialog(
                            title = "Edit timer",
                            saveLabel = "Save timer",
                            cancelLabel = "Cancel",
                            onDismiss = { open = false },
                            save = { DialogResult.Failed("Name already taken") },
                            dirty = true,
                        ) {
                            Text("Body")
                        }
                    }
                }
            }
        }
        onNodeWithText("Save timer").performClick()
        waitForIdle()

        assertTrue(open, "a failed save must keep the dialog open")
        onNodeWithText("Name already taken").assertExists()
        onNodeWithText("Save timer").assertIsEnabled()
    }

    @Test
    fun a_successful_save_closes_the_dialog() = runComposeUiTest {
        var open: Boolean by mutableStateOf(true)
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    if (open) {
                        FormDialog(
                            title = "Edit timer",
                            saveLabel = "Save timer",
                            cancelLabel = "Cancel",
                            onDismiss = { open = false },
                            save = { DialogResult.Done },
                            dirty = true,
                        ) {
                            Text("Body")
                        }
                    }
                }
            }
        }
        onNodeWithText("Save timer").performClick()
        waitForIdle()
        assertTrue(!open, "success closes the dialog, even for a dirty form")
    }

    @Test
    fun save_shows_progress_and_locks_the_dialog_while_the_action_runs() = runComposeUiTest {
        val gate: CompletableDeferred<DialogResult> = CompletableDeferred()
        var runs: Int = 0
        var open: Boolean by mutableStateOf(true)
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    if (open) {
                        FormDialog(
                            title = "Edit timer",
                            saveLabel = "Save timer",
                            cancelLabel = "Cancel",
                            onDismiss = { open = false },
                            save = {
                                runs++
                                gate.await()
                            },
                            dirty = true,
                        ) {
                            Text("Body")
                        }
                    }
                }
            }
        }
        onNodeWithText("Save timer").performClick()
        waitForIdle()

        onNodeWithTag(DialogActionProgressTag, useUnmergedTree = true).assertExists()
        onNodeWithText("Save timer").assertIsNotEnabled()
        onNodeWithText("Cancel").assertIsNotEnabled()
        onNodeWithText("Save timer").performClick()
        waitForIdle()
        assertEquals(1, runs, "a locked Save runs the action once")
        // Esc while saving must neither close nor prompt.
        onNodeWithText("Edit timer").performKeyInput { pressKey(Key.Escape) }
        waitForIdle()
        assertTrue(open)
        onNodeWithText("Discard changes?").assertDoesNotExist()

        gate.complete(DialogResult.Done)
        waitForIdle()
        assertTrue(!open)
    }

    @Test
    fun a_dirty_form_asks_before_cancel_and_the_user_can_keep_editing_or_discard() = runComposeUiTest {
        var open: Boolean by mutableStateOf(true)
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    if (open) {
                        FormDialog(
                            title = "Edit timer",
                            saveLabel = "Save timer",
                            cancelLabel = "Cancel",
                            onDismiss = { open = false },
                            save = { DialogResult.Done },
                            dirty = true,
                        ) {
                            Text("Body")
                        }
                    }
                }
            }
        }
        onNodeWithText("Cancel").performClick()
        waitForIdle()
        assertTrue(open, "a dirty form must not close before the user answers")
        onNodeWithText("Discard changes?").assertExists()

        onNodeWithText("Keep editing").performClick()
        waitForIdle()
        assertTrue(open)
        onNodeWithText("Discard changes?").assertDoesNotExist()
        onNodeWithText("Edit timer").assertExists()

        onNodeWithText("Cancel").performClick()
        waitForIdle()
        onNodeWithText("Discard").performClick()
        waitForIdle()
        assertTrue(!open, "Discard closes the form")
    }

    @Test
    fun a_dirty_form_asks_before_it_closes_on_escape() = runComposeUiTest {
        var open: Boolean by mutableStateOf(true)
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    if (open) {
                        FormDialog(
                            title = "Edit timer",
                            saveLabel = "Save timer",
                            cancelLabel = "Cancel",
                            onDismiss = { open = false },
                            save = { DialogResult.Done },
                            dirty = true,
                        ) {
                            Text("Body")
                        }
                    }
                }
            }
        }
        onNodeWithText("Edit timer").performKeyInput { pressKey(Key.Escape) }
        waitForIdle()
        assertTrue(open, "Esc on a dirty form must not close it")
        onNodeWithText("Discard changes?").assertExists()
    }

    @Test
    fun a_clean_form_closes_at_once_on_cancel_and_on_escape() = runComposeUiTest {
        var open: Boolean by mutableStateOf(true)
        var closes: Int = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    if (open) {
                        FormDialog(
                            title = "Edit timer",
                            saveLabel = "Save timer",
                            cancelLabel = "Cancel",
                            onDismiss = {
                                closes++
                                open = false
                            },
                            save = { DialogResult.Done },
                            dirty = false,
                        ) {
                            Text("Body")
                        }
                    }
                }
            }
        }
        onNodeWithText("Edit timer").performKeyInput { pressKey(Key.Escape) }
        waitForIdle()
        assertTrue(!open, "Esc on a clean form closes it")
        assertEquals(1, closes)
        onNodeWithText("Discard changes?").assertDoesNotExist()
    }

    @Test
    fun a_clean_form_closes_at_once_on_cancel() = runComposeUiTest {
        var open: Boolean by mutableStateOf(true)
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    if (open) {
                        FormDialog(
                            title = "Edit timer",
                            saveLabel = "Save timer",
                            cancelLabel = "Cancel",
                            onDismiss = { open = false },
                            save = { DialogResult.Done },
                        ) {
                            Text("Body")
                        }
                    }
                }
            }
        }
        onNodeWithText("Cancel").performClick()
        waitForIdle()
        assertTrue(!open)
        onNodeWithText("Discard changes?").assertDoesNotExist()
    }

    @Test
    fun save_is_disabled_until_the_form_is_valid() = runComposeUiTest {
        var valid: Boolean by mutableStateOf(false)
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    FormDialog(
                        title = "Edit timer",
                        saveLabel = "Save timer",
                        cancelLabel = "Cancel",
                        onDismiss = {},
                        save = { DialogResult.Done },
                        valid = valid,
                    ) {
                        Text("Body")
                    }
                }
            }
        }
        onNodeWithText("Save timer").assertIsNotEnabled()
        valid = true
        waitForIdle()
        onNodeWithText("Save timer").assertIsEnabled()
    }

    @Test
    fun a_tall_body_scrolls_and_the_footer_stays_visible() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    FormDialog(
                        title = "Edit timer",
                        saveLabel = "Save timer",
                        cancelLabel = "Cancel",
                        onDismiss = {},
                        save = { DialogResult.Done },
                    ) {
                        Column { Text("Top field", modifier = Modifier.height(4000.dp)) }
                    }
                }
            }
        }
        onNodeWithText("Edit timer").assertIsDisplayed()
        onNodeWithText("Save timer").assertIsDisplayed()
        onNodeWithText("Cancel").assertIsDisplayed()
    }
}
