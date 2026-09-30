// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.time

import androidx.compose.material3.Text
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlinx.datetime.LocalDate
import kotlin.test.Test

// Renders each bucket through the shared wording, in both shipped languages. The live defect was the
// attention popover printing raw minutes ("105790m ago"); this proves the wording each bucket lands on.
@OptIn(ExperimentalTestApi::class)
class ElapsedTextTest {

    private fun renders(tag: String, elapsed: Elapsed, expected: String) = runComposeUiTest {
        setContent { AppEnvironment(tag = tag) { NomNomzTheme { Text(elapsedText(elapsed)) } } }
        waitForIdle()
        onNodeWithText(expected).assertExists()
    }

    @Test
    fun english_wording_per_bucket() {
        renders("en", Elapsed.JustNow, "just now")
        renders("en", Elapsed.Seconds(42), "42s ago")
        renders("en", Elapsed.Minutes(5), "5m ago")
        renders("en", Elapsed.Hours(3), "3h ago")
        renders("en", Elapsed.Days(12), "12d ago")
        renders("en", Elapsed.Date(LocalDate(2026, 7, 18)), "2026-07-18")
    }

    @Test
    fun dutch_wording_per_bucket() {
        renders("nl", Elapsed.JustNow, "zojuist")
        renders("nl", Elapsed.Seconds(42), "42s geleden")
        renders("nl", Elapsed.Minutes(5), "5m geleden")
        renders("nl", Elapsed.Hours(3), "3u geleden")
        renders("nl", Elapsed.Days(12), "12d geleden")
        renders("nl", Elapsed.Date(LocalDate(2026, 7, 18)), "2026-07-18")
    }
}
