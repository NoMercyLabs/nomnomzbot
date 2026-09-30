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

import androidx.compose.runtime.MutableState
import androidx.compose.runtime.mutableStateOf
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.PixelMap
import androidx.compose.ui.graphics.luminance
import androidx.compose.ui.graphics.toPixelMap
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsMatcher
import androidx.compose.ui.test.assert
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// The blocked-terms add row and per-term remove icon, plus the four moderation page headings — defects seen on
// the live dashboard: "Add" rendered as a text button, the remove icon read as a generic node to assistive tech,
// and the "Enforcement Rules" page was headed "Moderation".
@OptIn(ExperimentalTestApi::class)
class ModerationBlockedTermsUiTest {

    @Test
    fun add_is_the_filled_primary_button_and_submits_the_typed_term() = runComposeUiTest {
        val added: MutableList<Pair<String, Boolean>> = mutableListOf()
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    AddTermRow(manage = ManageDecision.Allowed, onAdd = { term, everywhere -> added += term to everywhere })
                }
            }
        }
        waitForIdle()
        onNode(hasSetTextAction()).performTextInput("  spoilers ")
        waitForIdle()

        val add = onNode(hasText("Add") and hasClickAction())
        add.assert(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Button))
        val pixels: PixelMap = add.captureToImage().toPixelMap()
        val fill: Color = pixels[pixels.width / 2, 3]
        assertTrue(fill.alpha > 0.9f && fill.luminance() > 0.7f, "Add must be the filled primary Button, was $fill")

        add.performClick()
        waitForIdle()
        assertEquals(listOf("spoilers" to false), added)
    }

    @Test
    fun the_remove_icon_is_a_named_button_and_removes_its_term() = runComposeUiTest {
        val removed: MutableList<String> = mutableListOf()
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    BlockedTermRow(term = "spoilers", manage = ManageDecision.Allowed, onRemove = { removed += "spoilers" })
                }
            }
        }
        waitForIdle()

        val remove = onNodeWithContentDescription("Remove spoilers")
        remove.assert(SemanticsMatcher.expectValue(SemanticsProperties.Role, Role.Button))
        remove.performClick()
        waitForIdle()
        assertEquals(listOf("spoilers"), removed)
    }

    @Test
    fun each_moderation_page_is_headed_with_the_name_of_its_nav_item() = runComposeUiTest {
        val expected: Map<ModerationSection, String> =
            mapOf(
                ModerationSection.Desk to "Moderation",
                ModerationSection.Queue to "Review Queue",
                ModerationSection.Rules to "Enforcement Rules",
                ModerationSection.History to "History",
            )
        val section: MutableState<ModerationSection> = mutableStateOf(ModerationSection.Desk)
        setContent { AppEnvironment(tag = "en") { NomNomzTheme { ModerationPageHeader(section.value) } } }
        waitForIdle()

        expected.forEach { (page, heading) ->
            section.value = page
            waitForIdle()
            onNodeWithText(heading).assertExists("the ${page.name} page must be headed \"$heading\"")
        }
    }
}
