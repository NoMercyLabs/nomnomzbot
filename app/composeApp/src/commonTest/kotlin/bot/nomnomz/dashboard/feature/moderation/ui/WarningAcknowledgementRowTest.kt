// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import androidx.compose.foundation.layout.Column
import androidx.compose.material3.Text
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ModerationHistoryActionTypes
import bot.nomnomz.dashboard.core.network.ModerationHistoryEntry
import kotlin.test.Test

// S-WARNING-ACK: a warning row reads "warned" (awaiting acknowledgement) until the viewer acknowledges, then
// "acknowledged"; the viewer card line follows the same two states. Asserts the rendered semantics tree.
@OptIn(ExperimentalTestApi::class)
class WarningAcknowledgementRowTest {

    private fun warning(acknowledgedAt: String?): ModerationHistoryEntry =
        ModerationHistoryEntry(
            id = "w1",
            subjectUserId = "user-1",
            subjectTwitchUserId = "twitch-1",
            actionType = ModerationHistoryActionTypes.Warn,
            moderatorDisplayName = "ModPerson",
            reason = "Too many caps",
            occurredAt = "2026-09-01T12:00:00Z",
            acknowledgedAt = acknowledgedAt,
        )

    @Test
    fun an_unacknowledged_warning_row_says_it_is_awaiting_acknowledgement() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") { NomNomzTheme { HistoryLogEntryRow(entry = warning(acknowledgedAt = null)) } }
        }
        waitForIdle()

        onNodeWithText("Warning", substring = false).assertExists()
        onNodeWithText("Awaiting acknowledgement", substring = false).assertExists()
        onNodeWithText("Acknowledged", substring = false).assertDoesNotExist()
    }

    @Test
    fun an_acknowledged_warning_row_says_acknowledged() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme { HistoryLogEntryRow(entry = warning(acknowledgedAt = "2026-09-01T12:00:40Z")) }
            }
        }
        waitForIdle()

        onNodeWithText("Warning", substring = false).assertExists()
        onNodeWithText("Acknowledged", substring = false).assertExists()
        onNodeWithText("Awaiting acknowledgement", substring = false).assertDoesNotExist()
    }

    @Test
    fun an_acknowledgement_with_no_warning_row_is_labelled_and_shows_no_reason_placeholder() = runComposeUiTest {
        val orphan: ModerationHistoryEntry =
            warning(acknowledgedAt = "2026-09-01T12:00:40Z")
                .copy(actionType = ModerationHistoryActionTypes.WarningAcknowledged, reason = null)
        setContent { AppEnvironment(tag = "en") { NomNomzTheme { HistoryLogEntryRow(entry = orphan) } } }
        waitForIdle()

        onNodeWithText("Warning acknowledged", substring = false).assertExists()
        onNodeWithText("No reason recorded", substring = true).assertDoesNotExist()
    }

    @Test
    fun the_viewer_card_line_follows_warned_then_acknowledged() = runComposeUiTest {
        setContent {
            AppEnvironment(tag = "en") {
                NomNomzTheme {
                    Column {
                        Text(warningStatusText("2026-09-01T12:00:00Z", null))
                        Text(warningStatusText("2026-09-01T12:00:00Z", "2026-09-02T08:00:00Z"))
                    }
                }
            }
        }
        waitForIdle()

        onNodeWithText("not acknowledged yet", substring = true).assertExists()
        onNodeWithText("acknowledged 2026-09-02", substring = true).assertExists()
    }
}
