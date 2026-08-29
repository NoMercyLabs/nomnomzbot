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

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.pipelines.state.PickerOption
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/**
 * S046-code-tier-link: the `run_code` step's script field is supposed to link to the real Code Scripts
 * editor — before this slice, picking/creating its bound script went nowhere (a bare id picker with no path
 * to the actual editor). Proves [CodeScriptStepField], the SAME composable [TypedParamFields] dispatches to
 * for the `code_script_id` field, actually fires real navigation-intent callbacks with the correct script id —
 * both for an already-bound script ("open") and for a brand-new one (create-and-bind, mirroring the
 * [bot.nomnomz.dashboard.core.designsystem.component.PipelineBindPicker] pattern already shipped for
 * commands/timers/rewards/event responses) — never a render-without-crashing check.
 */
@OptIn(ExperimentalTestApi::class)
class CodeScriptStepFieldTest {

    @Test
    fun opening_an_already_bound_script_navigates_with_its_exact_id() = runComposeUiTest {
        var openedId: String? = null
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    CodeScriptStepField(
                        scripts = listOf(PickerOption(value = "scr-42", label = "Auto-shoutout")),
                        selectedId = "scr-42",
                        onSelect = {},
                        onOpenScript = { openedId = it },
                        onCreateScript = { null },
                        label = "Script",
                    )
                }
            }
        }

        onNodeWithText("Open in Code Scripts").performClick()

        assertEquals("scr-42", openedId, "opening the bound step's script must navigate with ITS id, not a placeholder")
    }

    @Test
    fun no_script_bound_yet_shows_no_open_action() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    CodeScriptStepField(
                        scripts = emptyList(),
                        selectedId = null,
                        onSelect = {},
                        onOpenScript = { },
                        onCreateScript = { null },
                        label = "Script",
                    )
                }
            }
        }

        onNodeWithText("Open in Code Scripts").assertDoesNotExist()
    }

    @Test
    fun creating_a_new_script_selects_it_then_immediately_navigates_to_its_editor() = runComposeUiTest {
        var selectedId: String? = null
        var openedId: String? = null
        var requestedName: String? = null
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    CodeScriptStepField(
                        scripts = emptyList(),
                        selectedId = selectedId,
                        onSelect = { selectedId = it },
                        onOpenScript = { openedId = it },
                        onCreateScript = { name ->
                            requestedName = name
                            PickerOption(value = "scr-new-1", label = name)
                        },
                        label = "Script",
                    )
                }
            }
        }

        onNodeWithText("Create a new script").performClick()
        // The "Script name" text carries on BOTH the field's floating label and its editable node's semantics —
        // select the actual editable node, not the ambiguous label text (SetupWizardScreenTest's same fix).
        onAllNodes(matcher = hasSetTextAction())[0].performTextInput("Raid greeter")
        onNodeWithText("Create").performClick()
        waitUntil(timeoutMillis = 5_000) { openedId != null }

        assertEquals("Raid greeter", requestedName)
        assertEquals("scr-new-1", selectedId, "the new script must be bound (selected), not just opened")
        assertEquals("scr-new-1", openedId, "creating a script must immediately navigate to ITS real editor")
    }

    @Test
    fun a_failed_create_leaves_create_mode_open_and_never_navigates() = runComposeUiTest {
        var openedId: String? = null
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    CodeScriptStepField(
                        scripts = emptyList(),
                        selectedId = null,
                        onSelect = {},
                        onOpenScript = { openedId = it },
                        onCreateScript = { null },
                        label = "Script",
                    )
                }
            }
        }

        onNodeWithText("Create a new script").performClick()
        onAllNodes(matcher = hasSetTextAction())[0].performTextInput("Whoops")
        onNodeWithText("Create").performClick()
        waitForIdle()

        assertNull(openedId, "a failed create must never fire the open-editor navigation")
        onAllNodes(matcher = hasSetTextAction())[0].assertExists()
    }
}
