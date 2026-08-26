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
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import kotlin.test.Test

/**
 * S-CONSEQ-b: proves the pipeline delete confirm dialog actually RENDERS the backend's counted blast radius —
 * plain-language dependent counts, the explicit "nothing references this" sentence for a genuine zero, and a
 * distinct failure message that never collapses to "0 dependents" (which would read as verified-safe when it
 * is actually unknown, and could cost the operator data). Every render is pinned to `en` — the platform
 * default locale is not guaranteed to be English.
 */
@OptIn(ExperimentalTestApi::class)
class PipelineDeleteConfirmDialogTest {

    @Test
    fun a_referenced_pipeline_renders_the_real_counted_dependents() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineDeleteConfirmDialog(
                        pipelineName = "Raid handler",
                        blastRadius =
                            BlastRadiusLoadState.Loaded(
                                PipelineBlastRadiusSummary(
                                    commandCount = 4,
                                    chatTriggerCount = 0,
                                    timerCount = 2,
                                    eventResponseCount = 0,
                                )
                            ),
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("This disables 4 commands and 2 timers.", substring = true).fetchSemanticsNodes().size > 0
        }
        onNodeWithText("This disables 4 commands and 2 timers.", substring = true).assertExists()
    }

    @Test
    fun an_unreferenced_pipeline_renders_the_explicit_nothing_references_sentence() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineDeleteConfirmDialog(
                        pipelineName = "Unused pipeline",
                        blastRadius = BlastRadiusLoadState.Loaded(PipelineBlastRadiusSummary()),
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Nothing else references this pipeline.", substring = true).fetchSemanticsNodes().size > 0
        }
        onNodeWithText("Nothing else references this pipeline.", substring = true).assertExists()
    }

    @Test
    fun a_failed_lookup_renders_its_own_distinct_message_never_zero() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineDeleteConfirmDialog(
                        pipelineName = "Some pipeline",
                        blastRadius = BlastRadiusLoadState.Failed,
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Could not check what depends on this pipeline", substring = true)
                .fetchSemanticsNodes()
                .size > 0
        }
        onNodeWithText("Could not check what depends on this pipeline", substring = true).assertExists()
        // A failed check must never silently read as a verified-safe zero — the "nothing references this"
        // sentence must NOT be showing instead of the failure message.
        onNodeWithText("Nothing else references this pipeline.").assertDoesNotExist()
    }

    @Test
    fun a_loading_lookup_renders_its_own_distinct_message() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineDeleteConfirmDialog(
                        pipelineName = "Some pipeline",
                        blastRadius = BlastRadiusLoadState.Loading,
                        onConfirm = {},
                        onDismiss = {},
                    )
                }
            }
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Checking what depends on this pipeline", substring = true).fetchSemanticsNodes().size > 0
        }
        onNodeWithText("Checking what depends on this pipeline", substring = true).assertExists()
    }
}
