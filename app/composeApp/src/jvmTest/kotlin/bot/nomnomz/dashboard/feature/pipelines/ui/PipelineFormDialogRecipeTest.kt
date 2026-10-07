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
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.feature.pipelines.state.PipelineRecipe
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

/** The create dialog offers "Start empty" plus every recipe; Create hands the chosen recipe to the caller. */
@OptIn(ExperimentalTestApi::class)
class PipelineFormDialogRecipeTest {

    private val titles: List<String> =
        listOf("Every 5th redemption", "Chance to chain", "Random reply", "Count and remember")

    private data class Submit(val name: String, val recipe: PipelineRecipe?)

    @Test
    fun lists_start_empty_and_all_four_recipes_with_start_empty_selected() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineFormDialog(
                        editor = PipelineEditor(id = null, name = "Mine", description = ""),
                        onDismiss = {},
                        onSubmit = { _, _, _ -> },
                    )
                }
            }
        }
        waitForIdle()
        onNodeWithText("Start empty").assertIsSelected()
        for (title: String in titles) onNodeWithText(title).assertExists()
    }

    @Test
    fun selecting_a_recipe_and_pressing_create_submits_that_recipe() = runComposeUiTest {
        val submits: MutableList<Submit> = mutableListOf()
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineFormDialog(
                        editor = PipelineEditor(id = null, name = "Mine", description = ""),
                        onDismiss = {},
                        onSubmit = { name, _, recipe -> submits += Submit(name, recipe) },
                    )
                }
            }
        }
        waitForIdle()
        onNodeWithText("Random reply").performClick()
        onNodeWithText("Random reply").assertIsSelected()
        onNodeWithText("Create").performClick()
        assertEquals(listOf("Mine"), submits.map { it.name })
        assertEquals("random_reply", submits.single().recipe?.id)
    }

    @Test
    fun pressing_create_without_choosing_submits_no_recipe() = runComposeUiTest {
        val submits: MutableList<Submit> = mutableListOf()
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineFormDialog(
                        editor = PipelineEditor(id = null, name = "Mine", description = ""),
                        onDismiss = {},
                        onSubmit = { name, _, recipe -> submits += Submit(name, recipe) },
                    )
                }
            }
        }
        waitForIdle()
        onNodeWithText("Create").performClick()
        assertNull(submits.single().recipe)
    }

    @Test
    fun the_rename_dialog_has_no_recipe_picker() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    PipelineFormDialog(
                        editor = PipelineEditor(id = "p1", name = "Mine", description = ""),
                        onDismiss = {},
                        onSubmit = { _, _, _ -> },
                    )
                }
            }
        }
        waitForIdle()
        onNodeWithText("Start empty").assertDoesNotExist()
    }
}
