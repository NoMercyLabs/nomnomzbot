// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

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
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.WidgetSummary
import kotlin.test.Test
import kotlin.test.assertTrue

/**
 * A 1366 px window is a Medium/Expanded class, yet the content pane beside the sidebar is about 1100 dp, and the
 * Dutch action labels claim nearly all of it. The row must pick its layout from the width it was given, so the
 * name keeps a readable column instead of wrapping one letter per line.
 */
@OptIn(ExperimentalTestApi::class)
class WidgetRowNarrowContentTest {
    @Composable
    private fun Dutch(paneWidth: Int, content: @Composable () -> Unit) {
        AppEnvironment(tag = "nl") { NomNomzTheme { Box(Modifier.width(paneWidth.dp)) { content() } } }
    }

    private fun assertNameStaysReadable(paneWidth: Int, tag: String) = runComposeUiTest {
        val widget =
            WidgetSummary(
                id = "w-1",
                name = "Chat berichten en volgers overlay",
                description = "Laatst gestart",
                isEnabled = true,
                eventSubscriptions = listOf("follow", "cheer"),
                galleryItemId = "g-1",
                galleryUpdateAvailable = true,
            )
        setContent {
            Dutch(paneWidth) {
                WidgetList(
                    widgets = listOf(widget),
                    manage = ManageDecision.Allowed,
                    onToggle = { _, _ -> },
                    onDelete = {},
                    onRename = {},
                    onClone = {},
                    onEditCode = {},
                    onVersions = {},
                    onSettings = {},
                    onCatalogueAction = {},
                    onTest = { _, _ -> ApiResult.Ok("ok") },
                    onRotateToken = {},
                )
            }
        }
        waitForIdle()
        val bounds: DpRect = onNodeWithText("Chat berichten en volgers overlay", useUnmergedTree = true).getUnclippedBoundsInRoot()
        val width: Dp = bounds.right - bounds.left
        val height: Dp = bounds.bottom - bounds.top
        assertTrue(width >= 160.dp, "$tag: the name column is only $width wide (one letter per line)")
        assertTrue(height <= 56.dp, "$tag: the name wraps to $height tall; it must stay one or two lines")
    }

    @Test
    fun the_name_stays_readable_in_a_1100dp_pane() = assertNameStaysReadable(1100, "1100dp")

    @Test
    fun the_name_stays_readable_in_a_1250dp_pane() = assertNameStaysReadable(1250, "1250dp")
}
