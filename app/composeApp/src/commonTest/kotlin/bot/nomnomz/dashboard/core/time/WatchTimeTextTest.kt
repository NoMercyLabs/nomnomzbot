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
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test

@OptIn(ExperimentalTestApi::class)
class WatchTimeTextTest {
    @Test
    fun seconds_and_hours_show_the_same_english_value() = runComposeUiTest {
        setContent {
            AppEnvironment("en") {
                Text(watchTimeText(12_000))
                Text(watchHoursText(12_000 / 3600.0))
            }
        }

        onAllNodesWithText("3h 20m").assertCountEquals(2)
    }

    @Test
    fun seconds_and_hours_show_the_same_dutch_value() = runComposeUiTest {
        setContent {
            AppEnvironment("nl") {
                Text(watchTimeText(12_000))
                Text(watchHoursText(12_000 / 3600.0))
            }
        }

        onAllNodesWithText("3u 20m").assertCountEquals(2)
    }
}
