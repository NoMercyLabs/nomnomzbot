// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.consequences

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.BlastRadiusCategory
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import kotlin.test.Test

/**
 * S-CONSEQ-c2: proves the shared delete dialog RENDERS the four distinct blast-radius states — the real
 * counted dependents, the explicit "nothing references this" sentence, the MINIMUM caveat when the count is
 * only a floor, and a failed lookup that is never collapsed into a zero. Every render is pinned to `en`; the
 * platform default locale is not guaranteed to be English.
 */
@OptIn(ExperimentalTestApi::class)
class DeleteBlastRadiusDialogTest {

    private fun dialogContent(state: BlastRadiusLoadState): @androidx.compose.runtime.Composable () -> Unit = {
        AppEnvironment(tag = "en") {
            NomNomzTheme {
                DeleteBlastRadiusDialog(
                    title = "Delete sound clip",
                    message = "Delete airhorn?",
                    confirmLabel = "Delete",
                    dismissLabel = "Cancel",
                    blastRadius = state,
                    onConfirm = {},
                    onDismiss = {},
                )
            }
        }
    }

    @Test
    fun counted_dependents_render_with_their_real_numbers_and_names() = runComposeUiTest {
        setContent {
            dialogContent(
                BlastRadiusLoadState.Loaded(
                    BlastRadiusSummary(
                        categories =
                            listOf(
                                BlastRadiusCategory(
                                    categoryKey = "blast_radius_category_pipeline_steps",
                                    count = 3,
                                    sample = listOf("Alerts", "Raids"),
                                ),
                                BlastRadiusCategory(
                                    categoryKey = "blast_radius_category_widget_versions",
                                    count = 1,
                                ),
                            )
                    )
                )
            )()
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("3 pipeline steps", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        onNodeWithText("Deleting this affects:", substring = true).assertExists()
        onNodeWithText("3 pipeline steps", substring = true).assertExists()
        // The sample names tell the user WHICH things break, not just how many.
        onNodeWithText("Alerts, Raids", substring = true).assertExists()
        // Singular form where the count is genuinely one.
        onNodeWithText("1 saved version", substring = true).assertExists()
        // An exhaustive count must NOT carry the minimum caveat.
        onNodeWithText("MINIMUM", substring = true).assertDoesNotExist()
    }

    @Test
    fun a_genuine_zero_renders_the_explicit_nothing_references_this_sentence() = runComposeUiTest {
        setContent { dialogContent(BlastRadiusLoadState.Loaded(BlastRadiusSummary()))() }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Nothing else references this", substring = true)
                .fetchSemanticsNodes()
                .isNotEmpty()
        }
        onNodeWithText("Nothing else references this", substring = true).assertExists()
        onNodeWithText("Could not check", substring = true).assertDoesNotExist()
        onNodeWithText("MINIMUM", substring = true).assertDoesNotExist()
    }

    @Test
    fun a_floor_count_says_it_is_a_minimum_instead_of_implying_completeness() = runComposeUiTest {
        setContent {
            dialogContent(
                BlastRadiusLoadState.Loaded(
                    BlastRadiusSummary(
                        categories =
                            listOf(
                                BlastRadiusCategory(
                                    categoryKey = "blast_radius_category_pipeline_steps",
                                    count = 2,
                                )
                            ),
                        isMinimum = true,
                    )
                )
            )()
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("MINIMUM", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        onNodeWithText("2 pipeline steps", substring = true).assertExists()
        onNodeWithText("This is a MINIMUM", substring = true).assertExists()
        onNodeWithText("there may be more", substring = true).assertExists()
    }

    @Test
    fun a_zero_that_is_only_a_floor_never_reads_as_a_verified_nothing() = runComposeUiTest {
        setContent {
            dialogContent(BlastRadiusLoadState.Loaded(BlastRadiusSummary(isMinimum = true)))()
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("MINIMUM", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        // The "nothing" sentence is still shown, but it is explicitly qualified — a channel that resolves
        // resources from variables or custom code has an unknown remainder the scan cannot see.
        onNodeWithText("This is a MINIMUM", substring = true).assertExists()
    }

    @Test
    fun a_failed_lookup_renders_its_own_message_and_never_a_zero() = runComposeUiTest {
        setContent { dialogContent(BlastRadiusLoadState.Failed)() }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Could not check", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        onNodeWithText("Could not check what depends on this", substring = true).assertExists()
        // The single most dangerous confusion: a failed check must never render as "nothing references this".
        onNodeWithText("Nothing else references this", substring = true).assertDoesNotExist()
        // …and unlike Loading, a failed check still lets the operator proceed.
        onNodeWithText("Delete").assertIsEnabled()
    }

    @Test
    fun an_unknown_radius_withholds_the_destructive_confirm() = runComposeUiTest {
        setContent { dialogContent(BlastRadiusLoadState.Loading)() }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("Checking what depends on this", substring = true)
                .fetchSemanticsNodes()
                .isNotEmpty()
        }
        onNodeWithText("Checking what depends on this", substring = true).assertExists()
        onNodeWithText("Nothing else references this", substring = true).assertDoesNotExist()
        onNodeWithText("Delete").assertIsNotEnabled()
    }

    @Test
    fun a_category_key_the_client_does_not_know_is_still_counted_never_dropped() = runComposeUiTest {
        setContent {
            dialogContent(
                BlastRadiusLoadState.Loaded(
                    BlastRadiusSummary(
                        categories =
                            listOf(BlastRadiusCategory(categoryKey = "something_new", count = 7))
                    )
                )
            )()
        }
        waitUntil(timeoutMillis = 5_000) {
            onAllNodesWithText("7 dependent", substring = true).fetchSemanticsNodes().isNotEmpty()
        }
        onNodeWithText("7 dependent of another kind", substring = true).assertExists()
    }
}
