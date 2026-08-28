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
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test

/**
 * S-PIPE-TREE-d2b-UI: the `run_pipeline` block's argument editor must render one labelled field per parameter
 * name the TARGET pipeline declares (`Pipeline.ParameterNamesJson`, surfaced to the client as
 * `PipelineSummary.parameterNames` / `PipelineDetail.parameterNames`) instead of a raw positional/generic
 * argument box — that was the whole gap this slice closes (the backend already binds named args by name;
 * the editor had no UI for it at all). Renders the real [RunPipelineArgumentsField] composable, the same one
 * the step dialog's [TypedParamFields] dispatches to for `run_pipeline`, not a test-only stand-in.
 */
@OptIn(ExperimentalTestApi::class)
class RunPipelineArgumentsFieldTest {

    @Test
    fun a_target_with_declared_parameter_names_renders_one_labelled_field_per_name() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    RunPipelineArgumentsField(
                        targetPipelineName = "give-currency",
                        declaredNamesByPipeline = mapOf("give-currency" to listOf("target_user", "amount")),
                        namedArgsJson = "",
                        onNamedArgsJsonChange = {},
                        argsJson = "",
                        onArgsJsonChange = {},
                    )
                }
            }
        }

        onNodeWithText("Argument “target_user”").assertExists()
        onNodeWithText("Argument “amount”").assertExists()
    }

    @Test
    fun a_target_with_no_declared_parameter_names_falls_back_to_the_positional_editor() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    RunPipelineArgumentsField(
                        targetPipelineName = "legacy-pipeline",
                        declaredNamesByPipeline = emptyMap(),
                        namedArgsJson = "",
                        onNamedArgsJsonChange = {},
                        argsJson = "",
                        onArgsJsonChange = {},
                    )
                }
            }
        }

        // No declared names for the picked target -> the generic positional "args" list header renders, never
        // a per-name field (there are no names to label one with).
        onNodeWithText("Arguments (in order, {{args.1}}, {{args.2}}, …)").assertExists()
    }

    @Test
    fun no_target_picked_yet_shows_the_pick_a_target_hint_instead_of_any_argument_field() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    RunPipelineArgumentsField(
                        targetPipelineName = "",
                        declaredNamesByPipeline = mapOf("give-currency" to listOf("target_user")),
                        namedArgsJson = "",
                        onNamedArgsJsonChange = {},
                        argsJson = "",
                        onArgsJsonChange = {},
                    )
                }
            }
        }

        onNodeWithText("Pick a target pipeline to configure its arguments.").assertExists()
    }
}
