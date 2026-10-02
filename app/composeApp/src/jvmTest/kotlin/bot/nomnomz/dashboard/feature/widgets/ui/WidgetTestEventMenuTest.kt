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

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.WidgetSummary
import kotlin.test.Test
import kotlin.test.assertEquals

/** The row's "Test" fires the event the operator picks, so every event a widget listens to can be tried. */
@OptIn(ExperimentalTestApi::class)
class WidgetTestEventMenuTest {
    @Composable
    private fun English(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") { NomNomzTheme { content() } }
    }

    @Composable
    private fun Row(widget: WidgetSummary, fired: MutableList<String>) {
        English {
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
                onTest = { _, event ->
                    fired += event
                    ApiResult.Ok("fired $event")
                },
                onRotateToken = {},
            )
        }
    }

    @Test
    fun a_widget_with_several_events_lets_the_operator_pick_which_one_fires() = runComposeUiTest {
        val fired: MutableList<String> = mutableListOf()
        val widget = WidgetSummary(id = "w-1", name = "Alerts", eventSubscriptions = listOf("follow", "cheer"))
        setContent { Row(widget, fired) }

        onNodeWithContentDescription("Fire a test event at overlay Alerts").performClick()
        waitForIdle()
        onNodeWithText("follow").assertExists()
        onNodeWithText("cheer").performClick()
        waitForIdle()

        assertEquals(listOf("cheer"), fired)
        onNodeWithText("Test: fired cheer", useUnmergedTree = true).assertExists()
    }

    @Test
    fun a_widget_with_one_event_fires_it_without_a_menu() = runComposeUiTest {
        val fired: MutableList<String> = mutableListOf()
        val widget = WidgetSummary(id = "w-1", name = "Alerts", eventSubscriptions = listOf("follow"))
        setContent { Row(widget, fired) }

        onNodeWithContentDescription("Fire a test event at overlay Alerts").performClick()
        waitForIdle()

        assertEquals(listOf("follow"), fired)
        onNodeWithText("follow").assertDoesNotExist()
    }
}
