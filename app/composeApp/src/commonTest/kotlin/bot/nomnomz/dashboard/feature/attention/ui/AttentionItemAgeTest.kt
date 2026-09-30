// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.attention.ui

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import kotlin.test.Test
import kotlin.time.Duration.Companion.minutes
import kotlinx.datetime.Clock
import kotlinx.datetime.Instant
import kotlinx.datetime.TimeZone
import kotlinx.datetime.toLocalDateTime

// The "Needs your attention" popover printed "105790m ago" for an item detected about 73 days earlier. The age
// on a row now rolls up to a unit a person reads: minutes, hours, days, then the date.
@OptIn(ExperimentalTestApi::class)
class AttentionItemAgeTest {

    private fun itemDetected(minutesAgo: Long): ActionRequiredItem {
        val detected: Instant = Clock.System.now() - minutesAgo.minutes
        return ActionRequiredItem(
            kind = "attention_test",
            severity = "warning",
            titleKey = "attention_test_title",
            messageKey = "attention_test_message",
            detectedAt = detected.toString(),
            deepLinkRoute = "/",
            id = "item-1",
        )
    }

    private fun androidx.compose.ui.test.ComposeUiTest.showRow(item: ActionRequiredItem) {
        setContent {
            AppEnvironment(tag = "en") { NomNomzTheme { AttentionItemRow(item = item, onOpen = {}) } }
        }
        waitForIdle()
    }

    @Test
    fun an_item_detected_seventy_three_days_ago_shows_its_date_not_raw_minutes() = runComposeUiTest {
        val item: ActionRequiredItem = itemDetected(minutesAgo = 105_790)
        showRow(item)

        val date: String =
            Instant.parse(item.detectedAt).toLocalDateTime(TimeZone.currentSystemDefault()).date.toString()
        onNodeWithText(date).assertExists()
        onNodeWithText("105790m ago").assertDoesNotExist()
    }

    @Test
    fun an_item_detected_three_hours_ago_reads_in_hours() = runComposeUiTest {
        showRow(itemDetected(minutesAgo = 3 * 60 + 5))

        onNodeWithText("3h ago").assertExists()
    }
}
