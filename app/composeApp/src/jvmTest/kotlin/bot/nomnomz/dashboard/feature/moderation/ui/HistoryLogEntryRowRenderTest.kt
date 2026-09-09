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
import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ModerationHistoryActionTypes
import bot.nomnomz.dashboard.core.network.ModerationHistoryEntry
import kotlin.test.Test

/**
 * Owner punch list 2026-09-08 §12: "each newly-surfaced action kind renders distinguishably from the
 * others." Asserts the rendered semantics tree, not merely that the composable did not crash — every
 * [ModerationHistoryActionTypes] kind must produce its OWN label text, distinct from every other kind's,
 * and a warning row (the kind previously invisible outside a raw event) must be readable exactly like a
 * ban/timeout/unban/note row on the same screen.
 */
@OptIn(ExperimentalTestApi::class)
class HistoryLogEntryRowRenderTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    private fun entry(id: String, actionType: String, reason: String? = "Test reason $id") =
        ModerationHistoryEntry(
            id = id,
            subjectUserId = "user-1",
            subjectTwitchUserId = "twitch-1",
            actionType = actionType,
            moderatorDisplayName = "ModPerson",
            reason = reason,
            occurredAt = "2026-09-01T12:00:00Z",
        )

    @Test
    fun everyDeclaredKindRendersItsOwnDistinctLabel_onTheSameScreen() {
        val entries =
            ModerationHistoryActionTypes.All.map { kind -> entry(id = kind, actionType = kind) }

        runComposeUiTest {
            setContent {
                EnglishContent { Column { entries.forEach { HistoryLogEntryRow(entry = it) } } }
            }
            waitForIdle()

            // Each kind's label text exists exactly once — proves no two kinds collapsed onto the same
            // rendered word, which is what "distinguishable" actually requires.
            onNodeWithText("Ban", substring = false).assertExists()
            onNodeWithText("Timeout", substring = false).assertExists()
            onNodeWithText("Warning", substring = false).assertExists()
            onNodeWithText("Unban", substring = false).assertExists()
            onNodeWithText("Message deleted", substring = false).assertExists()
            onNodeWithText("AutoMod hold", substring = false).assertExists()
            onNodeWithText("Filter hit", substring = false).assertExists()
            onNodeWithText("Report upheld", substring = false).assertExists()
            onNodeWithText("Note", substring = false).assertExists()
        }
    }

    @Test
    fun aWarningRow_rendersReadableJustLikeABanOrTimeoutRow() {
        // The punch list's specific complaint: warnings were only a raw event, never a browsable row. This
        // proves the row now carries the same information a ban/timeout row does — kind, reason, moderator.
        val warn = entry(id = "w1", actionType = ModerationHistoryActionTypes.Warn, reason = "Spamming caps")

        runComposeUiTest {
            setContent { EnglishContent { HistoryLogEntryRow(entry = warn) } }
            waitForIdle()

            onNodeWithText("Warning", substring = false).assertExists()
            onNodeWithText("Spamming caps", substring = true).assertExists()
            onNodeWithText("ModPerson", substring = true).assertExists()
        }
    }

    @Test
    fun aTimeoutRow_showsItsDurationAlongsideTheKindBadge() {
        val timeout =
            entry(id = "t1", actionType = ModerationHistoryActionTypes.Timeout).copy(durationSeconds = 600)

        runComposeUiTest {
            setContent { EnglishContent { HistoryLogEntryRow(entry = timeout) } }
            waitForIdle()

            onNodeWithText("Timeout", substring = false).assertExists()
            onNodeWithText("600", substring = true).assertExists()
        }
    }

    @Test
    fun aRowWithNoReason_showsATruthfulPlaceholder_neverABlankLine() {
        val noReason = entry(id = "n1", actionType = ModerationHistoryActionTypes.Ban, reason = null)

        runComposeUiTest {
            setContent { EnglishContent { HistoryLogEntryRow(entry = noReason) } }
            waitForIdle()

            onNodeWithText("No reason recorded", substring = false).assertExists()
        }
    }
}
