// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test
import kotlin.test.assertTrue

// S-OWN08 regression: AdminController.load() has always set AdminState.error on a failed stats/channels/users/
// system fetch (state/AdminController.kt), but AdminScreen never rendered it anywhere — a failed initial load
// left the platform-admin panel silently empty with no indication anything had gone wrong (a "truthful data,
// not fake enforcement" violation: the failure state existed but was invisible). Mounts the real composable and
// reads what actually renders, the same class of regression ResourceLimitsSectionTest guards against elsewhere.
@OptIn(ExperimentalTestApi::class)
class AdminScreenTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    @Test
    fun a_load_error_renders_as_a_visible_banner() = runComposeUiTest {
        setContent {
            EnglishContent {
                AdminLoadErrorBanner(error = "Failed to reach the platform admin API")
            }
        }

        assertTrue(
            onAllNodesWithText("Failed to reach the platform admin API").fetchSemanticsNodes().isNotEmpty(),
            "a set AdminState.error must render as a visible banner, not be silently dropped",
        )
    }

    @Test
    fun no_error_renders_nothing() = runComposeUiTest {
        setContent {
            EnglishContent {
                AdminLoadErrorBanner(error = null)
            }
        }

        assertTrue(
            onAllNodesWithText("Failed", substring = true).fetchSemanticsNodes().isEmpty(),
            "a null error must render no banner at all",
        )
    }
}
