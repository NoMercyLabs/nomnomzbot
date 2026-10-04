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

import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.width
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.getUnclippedBoundsInRoot
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.DpRect
import androidx.compose.ui.unit.dp
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * The page header's trailing slot follows its own width: beside the title when it fits, under the title when it
 * does not, so a long localized label never wraps. One layout serves every screen that passes a trailing slot.
 */
@OptIn(ExperimentalTestApi::class)
class PageHeaderTest {
    private val actionLabels: List<String> =
        listOf("Synchroniseren met Twitch", "Importeren van Twitch", "Sjablonen bekijken")

    @Composable
    private fun Dutch(paneWidth: Int, content: @Composable () -> Unit) {
        AppEnvironment(tag = "nl") { NomNomzTheme { Box(Modifier.width(paneWidth.dp)) { content() } } }
    }

    @Test
    fun long_trailing_labels_stay_on_one_line_and_the_title_stays_readable_in_a_700dp_pane() = runComposeUiTest {
        setContent {
            Dutch(700) {
                PageHeader(title = "Beloningen") {
                    for (label: String in actionLabels) {
                        Button(onClick = {}) { Text(text = label) }
                    }
                }
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
