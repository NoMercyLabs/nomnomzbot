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

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.network.UserModerationContext
import kotlin.test.Test

// Twitch's suspicious-user flag must read at a glance on the viewer card and in the moderation queue, and a
// flagged message that was NOT deleted must not offer the held-message actions that assume a deletion.
@OptIn(ExperimentalTestApi::class)
class LowTrustStatusRenderTest {
    @Composable
    private fun English(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") { NomNomzTheme { content() } }
    }

    private fun card(status: String): @Composable () -> Unit = {
        English {
            UserModerationContextBody(
                context = UserModerationContext(userId = "u1", username = "flagged", lowTrustStatus = status),
                heatThreshold = 80,
            )
        }
    }

    @Test
    fun a_restricted_viewer_card_shows_the_restricted_badge_and_what_it_means() = runComposeUiTest {
        setContent(card("restricted"))
        waitForIdle()

        onNodeWithText("Restricted by Twitch").assertExists()
        onNodeWithText("Twitch hides messages from this viewer in chat.", substring = true).assertExists()
        onNodeWithText("Monitored by Twitch").assertDoesNotExist()
    }

    @Test
    fun a_monitored_viewer_card_shows_the_monitored_badge_and_what_it_means() = runComposeUiTest {
        setContent(card("active_monitoring"))
        waitForIdle()

        onNodeWithText("Monitored by Twitch").assertExists()
        onNodeWithText("Their messages stay visible in chat.", substring = true).assertExists()
        onNodeWithText("Restricted by Twitch").assertDoesNotExist()
    }

    @Test
    fun an_unflagged_viewer_card_shows_neither_badge() = runComposeUiTest {
        setContent(card("none"))
        waitForIdle()

        onNodeWithText("flagged").assertExists()
        onNodeWithText("Restricted by Twitch").assertDoesNotExist()
        onNodeWithText("Monitored by Twitch").assertDoesNotExist()
    }

    private fun queueRow(item: ModerationQueueItem): @Composable () -> Unit = {
        English {
            AutomodQueueRow(item = item, manage = ManageDecision.Allowed, onApprove = {}, onDeny = {})
        }
    }

    @Test
    fun a_suspicious_user_queue_item_shows_its_source_status_and_flag_fitting_actions() = runComposeUiTest {
        setContent(
            queueRow(
                ModerationQueueItem(
                    id = "q1",
                    source = "suspicioususer",
                    status = "pending",
                    targetUsernameSnapshot = "flagged",
                    messageContentSnapshot = "hello there",
                    autoModCategory = "restricted",
                )
            )
        )
        waitForIdle()

        onNodeWithText("Suspicious user flagged by Twitch").assertExists()
        onNodeWithText("Restricted by Twitch").assertExists()
        onNodeWithText("hello there").assertExists()
        onNodeWithText("The message was not removed.").assertExists()
        onNodeWithText("Looks fine").assertExists()
        // The AutoMod held-message wording would claim a deletion that never happened.
        onNodeWithText("Approve").assertDoesNotExist()
        onNodeWithText("Deny").assertDoesNotExist()
        onNodeWithText("Flagged for restricted").assertDoesNotExist()
    }

    @Test
    fun an_automod_queue_item_keeps_the_held_message_actions() = runComposeUiTest {
        setContent(
            queueRow(
                ModerationQueueItem(
                    id = "q2",
                    source = "automod",
                    status = "pending",
                    targetUsernameSnapshot = "spammy",
                    messageContentSnapshot = "buy followers",
                    autoModCategory = "spam",
                )
            )
        )
        waitForIdle()

        onNodeWithText("Approve").assertExists()
        onNodeWithText("Deny").assertExists()
        onNodeWithText("Flagged for spam").assertExists()
        onNodeWithText("Suspicious user flagged by Twitch").assertDoesNotExist()
    }
}
