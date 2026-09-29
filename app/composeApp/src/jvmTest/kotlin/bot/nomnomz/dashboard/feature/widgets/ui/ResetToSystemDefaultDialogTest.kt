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
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.core.network.WidgetVersionSummary
import kotlinx.coroutines.CompletableDeferred
import kotlin.test.Test
import kotlin.test.assertEquals

// The reset confirm must state the consequence before it can run: which version holds the streamer's edit (the
// real number from the backend history), that the overlay switches to the NomNomzBot version, and that settings
// are kept. Confirm runs the reset; "Keep my edit" does not.
@OptIn(ExperimentalTestApi::class)
class ResetToSystemDefaultDialogTest {
    private val alerts: WidgetSummary =
        WidgetSummary(id = "alerts", name = "Alerts", source = "first_party", galleryItemId = "g1", isCustomized = true)

    @Composable
    private fun English(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") { NomNomzTheme { content() } }
    }

    @Test
    fun the_dialog_names_the_live_edited_version_and_confirm_runs_the_reset() = runComposeUiTest {
        var confirmed = 0
        var dismissed = 0
        // v1 = catalogue, v2 = the channel edit (live), v3 = an earlier reset attempt whose build failed.
        val history: List<WidgetVersionSummary> =
            listOf(
                WidgetVersionSummary(id = "v3", versionNumber = 3, buildStatus = "error"),
                WidgetVersionSummary(id = "v2", versionNumber = 2, buildStatus = "success"),
                WidgetVersionSummary(id = "v1", versionNumber = 1, buildStatus = "success"),
            )
        setContent {
            English {
                ResetToSystemDefaultDialog(
                    widget = alerts,
                    loadVersions = { ApiResult.Ok(history) },
                    onConfirm = { confirmed++ },
                    onDismiss = { dismissed++ },
                )
            }
        }

        onNodeWithText("Reset Alerts to the system default?").assertExists()
        onNodeWithText("stays in Versions as version 2", substring = true).assertExists()
        onNodeWithText("Your settings stay as they are.", substring = true).assertExists()

        onNodeWithText("Keep my edit").performClick()
        assertEquals(1, dismissed)
        assertEquals(0, confirmed)

        onNodeWithText("Reset to default").performClick()
        assertEquals(1, confirmed)
    }

    @Test
    fun confirm_waits_for_the_version_history_before_it_can_run() = runComposeUiTest {
        val pending: CompletableDeferred<ApiResult<List<WidgetVersionSummary>>> = CompletableDeferred()
        var confirmed = 0
        setContent {
            English {
                ResetToSystemDefaultDialog(
                    widget = alerts,
                    loadVersions = { pending.await() },
                    onConfirm = { confirmed++ },
                    onDismiss = {},
                )
            }
        }

        onNodeWithText("Reset to default").assertIsNotEnabled()
        onNodeWithText("Reset to default").performClick()
        assertEquals(0, confirmed)
    }
}
