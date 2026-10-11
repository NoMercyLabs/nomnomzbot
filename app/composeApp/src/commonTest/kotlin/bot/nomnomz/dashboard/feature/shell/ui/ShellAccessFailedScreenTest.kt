// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test
import kotlin.test.assertEquals

// Stoney, 2026-10-11: "don't tell me you can't do better than a screen like this for errors right? its way too
// small and is unbranded". The screen carries the brand, says what to do, and keeps the raw code as a footnote.
@OptIn(ExperimentalTestApi::class)
class ShellAccessFailedScreenTest {

    @Test
    fun the_failure_screen_carries_the_brand_and_the_error_code_as_a_footnote() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme { ShellAccessFailedScreen(reason = "HTTP 403", onRetry = {}, onSignOut = {}) }
            }
        }

        onNodeWithText("NomNomzBot").assertExists()
        onNodeWithText("Error code: HTTP 403").assertExists()
    }

    @Test
    fun try_again_retries_and_log_out_signs_out_each_exactly_once() = runComposeUiTest {
        var retries = 0
        var signOuts = 0
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    ShellAccessFailedScreen(reason = "HTTP 403", onRetry = { retries++ }, onSignOut = { signOuts++ })
                }
            }
        }

        onNodeWithText("Try again").performClick()
        assertEquals(1, retries)
        assertEquals(0, signOuts)

        onNodeWithText("Log out").performClick()
        assertEquals(1, retries)
        assertEquals(1, signOuts)
    }
}
