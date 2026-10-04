// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.rewards.ui

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.getUnclippedBoundsInRoot
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.DpRect
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test
import kotlin.test.assertTrue

/** The Rewards header shares the width-driven layout: in a narrow pane every Dutch action stays on one line. */
@OptIn(ExperimentalTestApi::class)
class RewardsHeaderNarrowTest {
    private val actionLabels: List<String> =
        listOf("Synchroniseren met Twitch", "Importeren van Twitch", "Sjablonen bekijken", "Nieuwe beloning")

    @Composable
    private fun Dutch(paneWidth: Int, content: @Composable () -> Unit) {
        AppEnvironment(tag = "nl") { NomNomzTheme { Box(Modifier.width(paneWidth.dp)) { content() } } }
    }

    @Test
    fun every_action_label_stays_on_one_line_in_a_900dp_pane() = runComposeUiTest {
        setContent {
            Dutch(900) {
                RewardsHeader(
                    lifecycle = ManageDecision.Allowed,
                    onNew = {},
                    onBrowseTemplates = {},
                    onSync = {},
                    onImport = {},
                )
            }
        }
        waitForIdle()
        for (label: String in actionLabels) {
            val bounds: DpRect = onNodeWithText(label, useUnmergedTree = true).getUnclippedBoundsInRoot()
            val height: Dp = bounds.bottom - bounds.top
            assertTrue(height <= 24.dp, "\"$label\" wraps, it is $height tall (one line is at most 24dp)")
        }
        val title: DpRect = onNodeWithText("Beloningen", useUnmergedTree = true).getUnclippedBoundsInRoot()
        val titleWidth: Dp = title.right - title.left
        assertTrue(titleWidth >= 80.dp, "the title is only $titleWidth wide")
    }
}
