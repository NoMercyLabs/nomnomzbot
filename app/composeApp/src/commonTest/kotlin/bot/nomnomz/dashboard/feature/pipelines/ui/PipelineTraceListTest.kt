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
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.PipelineCatalogue
import bot.nomnomz.dashboard.core.network.PipelineTraceStep
import bot.nomnomz.dashboard.core.network.RuntimePalette
import bot.nomnomz.dashboard.core.network.PipelineTraceVariableChange
import kotlin.test.Test
import kotlin.test.assertEquals

/**
 * S-PIPE-TEST-TRACE: a pipeline test run shows ONE row per executed step with what it did — the branch an `if`
 * took, each variable change as before -> after, the step's output, and a failed step's error on its own row.
 * Renders are pinned to `en`.
 */
@OptIn(ExperimentalTestApi::class)
class PipelineTraceListTest {

    private val palette: RuntimePalette = PipelineCatalogue.fallbackPalette()

    private val trace: List<PipelineTraceStep> =
        listOf(
            PipelineTraceStep(stepId = "s1", stepType = "if", branch = "then"),
            PipelineTraceStep(
                stepId = "s2",
                stepType = "set_variable",
                variableChanges = listOf(PipelineTraceVariableChange(key = "points", before = "5", after = "10")),
            ),
            PipelineTraceStep(stepId = "s3", stepType = "send_message", output = "Hello chat"),
        )

    @Test
    fun three_steps_render_three_rows_with_branch_change_and_output() = runComposeUiTest {
        setContent { AppEnvironment(tag = "en") { NomNomzTheme { PipelineTraceList(trace, palette) } } }
        waitUntil(timeoutMillis = 2_000) { onAllNodesWithText("Set variable").fetchSemanticsNodes().isNotEmpty() }

        assertEquals(1, onAllNodesWithText("If").fetchSemanticsNodes().size)
        assertEquals(1, onAllNodesWithText("Set variable").fetchSemanticsNodes().size)
        assertEquals(1, onAllNodesWithText("Send message").fetchSemanticsNodes().size)
        onNodeWithText("Took the then branch").assertExists()
        onNodeWithText("points: 5 → 10").assertExists()
        onNodeWithText("Output: Hello chat").assertExists()
    }

    @Test
    fun a_failed_step_shows_its_error_and_an_unset_variable_reads_as_not_set() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineTraceList(
                        listOf(
                            PipelineTraceStep(
                                stepId = "s1",
                                stepType = "set_variable",
                                variableChanges = listOf(PipelineTraceVariableChange("mood", null, "happy")),
                            ),
                            PipelineTraceStep(stepId = "s2", stepType = "ban", error = "Missing scope"),
                        ),
                        palette,
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 2_000) { onAllNodesWithText("Ban").fetchSemanticsNodes().isNotEmpty() }

        onNodeWithText("mood: (not set) → happy").assertExists()
        onNodeWithText("Error: Missing scope").assertExists()
    }

    @Test
    fun rows_show_the_translated_palette_name_for_the_step_type() = runComposeUiTest {
        setContent { AppEnvironment(tag = "nl") { NomNomzTheme { PipelineTraceList(trace, palette) } } }
        waitUntil(timeoutMillis = 2_000) { onAllNodesWithText("Variabele instellen").fetchSemanticsNodes().isNotEmpty() }

        assertEquals(1, onAllNodesWithText("Variabele instellen").fetchSemanticsNodes().size)
        assertEquals(1, onAllNodesWithText("Bericht versturen").fetchSemanticsNodes().size)
        assertEquals(0, onAllNodesWithText("Set variable").fetchSemanticsNodes().size)
    }

    @Test
    fun an_empty_trace_says_so() = runComposeUiTest {
        setContent { AppEnvironment(tag = "en") { NomNomzTheme { PipelineTraceList(emptyList(), palette) } } }
        waitUntil(timeoutMillis = 2_000) {
            onAllNodesWithText("No step details for this run.").fetchSemanticsNodes().isNotEmpty()
        }
        onNodeWithText("No step details for this run.").assertExists()
    }
}
