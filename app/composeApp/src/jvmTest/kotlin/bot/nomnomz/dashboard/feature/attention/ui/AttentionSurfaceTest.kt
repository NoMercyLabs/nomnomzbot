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

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onAllNodesWithTag
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithTag
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import bot.nomnomz.dashboard.feature.home.ui.AttentionInbox
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import kotlin.test.Test
import kotlin.test.assertEquals

// The shell attention surface and the Home inbox rendered from real item shapes, in BOTH locales: the count and
// the most urgent severity show on the trigger, opening it lists the items grouped by severity, and choosing an
// item navigates to the page its deep link names. The Home inbox renders every group, critical first.
@OptIn(ExperimentalTestApi::class)
class AttentionSurfaceTest {
    @Composable
    private fun Pinned(tag: String, content: @Composable () -> Unit) {
        AppEnvironment(tag = tag) { NomNomzTheme { content() } }
    }

    private val webhookDown: ActionRequiredItem =
        ActionRequiredItem(
            id = "webhook-disabled:e1:1",
            kind = "webhook_endpoint_disabled",
            severity = "critical",
            titleKey = "attention_webhook_disabled_title",
            messageKey = "attention_webhook_disabled_message",
            parameters = mapOf("endpointName" to "Discord relay", "failureCount" to "20"),
            deepLinkRoute = "webhooks",
        )

    private val songLost: ActionRequiredItem =
        ActionRequiredItem(
            id = "song-lost:abc",
            kind = "song_request_lost",
            severity = "info",
            titleKey = "attention_song_lost_title",
            messageKey = "attention_song_lost_message",
            parameters = mapOf("trackName" to "Family Ties", "requestedBy" to "viewer_one", "count" to "1"),
            deepLinkRoute = "songrequests",
        )

    private val widgetBroken: ActionRequiredItem =
        ActionRequiredItem(
            id = "widget-build:v2",
            kind = "widget_build_failed",
            severity = "warning",
            titleKey = "attention_widget_build_failed_title",
            messageKey = "attention_widget_build_failed_message",
            parameters = mapOf("widgetName" to "Follower goal", "version" to "2"),
            deepLinkRoute = "widgets",
        )

    @Test
    fun theTriggerShowsTheCount_andAnItemNavigatesToItsDeepLink() = runComposeUiTest {
        val navigated: MutableList<ShellRoute> = mutableListOf()
        setContent {
            Pinned("en") {
                AttentionSurface(items = listOf(songLost, webhookDown), onNavigate = { navigated += it })
            }
        }

        onNodeWithText("2 items need your attention").assertExists()
        onNodeWithText("Webhook turned off: Discord relay").assertDoesNotExist()

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Critical · 1").assertExists()
        onNodeWithText("Info · 1").assertExists()
        onNodeWithText("It failed 20 times in a row", substring = true).assertExists()

        onNodeWithText("Webhook turned off: Discord relay").performClick()
        assertEquals(listOf(ShellRoute.Webhooks), navigated)
        onAllNodesWithText("Webhook turned off: Discord relay").assertCountEquals(0)
    }

    @Test
    fun theSurfaceRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(webhookDown), onNavigate = {}) }
        }

        onNodeWithText("1 punt vraagt je aandacht").assertExists()
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Webhook uitgeschakeld: Discord relay").assertExists()
        onNodeWithText("Kritiek · 1").assertExists()
    }

    private val operatorActing: ActionRequiredItem =
        ActionRequiredItem(
            id = "security-notice:n1",
            kind = "impersonation_started",
            severity = "warning",
            titleKey = "attention_security_impersonation_started_title",
            messageKey = "attention_security_impersonation_started_message",
            parameters = mapOf("operatorName" to "Support Sam", "targetName" to "Mod Mia", "reason" to "Ticket 4821"),
            deepLinkRoute = "roles",
        )

    @Test
    fun anOperatorSecurityNoticeNamesWhoActedAsWhomAndWhy_andOpensRoles() = runComposeUiTest {
        val navigated: MutableList<ShellRoute> = mutableListOf()
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(operatorActing), onNavigate = { navigated += it }) }
        }

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("A NomNomzBot operator is acting as Mod Mia").assertExists()
        onNodeWithText("Support Sam opened a support session on your channel. Reason: Ticket 4821").assertExists()

        onNodeWithText("A NomNomzBot operator is acting as Mod Mia").performClick()
        assertEquals(listOf(ShellRoute.Roles), navigated)
    }

    @Test
    fun anOperatorSecurityNoticeRendersInDutch() = runComposeUiTest {
        setContent { Pinned("nl") { AttentionSurface(items = listOf(operatorActing), onNavigate = {}) } }

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Een NomNomzBot-beheerder handelt als Mod Mia").assertExists()
    }

    @Test
    fun nothingRendersWhenNothingNeedsAttention() = runComposeUiTest {
        setContent { Pinned("en") { AttentionSurface(items = emptyList(), onNavigate = {}) } }

        onAllNodesWithTag(ATTENTION_SURFACE_TAG).assertCountEquals(0)
    }

    @Test
    fun theHomeInboxGroupsEveryItemBySeverity_criticalFirst() = runComposeUiTest {
        val reviewed: MutableList<String> = mutableListOf()
        setContent {
            Pinned("en") {
                AttentionInbox(
                    items = listOf(songLost, widgetBroken, webhookDown),
                    onReview = { reviewed += it.id },
                    onDismiss = {},
                )
            }
        }

        val critical: Float = onNodeWithText("Critical · 1").fetchSemanticsNode().positionInRoot.y
        val warning: Float = onNodeWithText("Warning · 1").fetchSemanticsNode().positionInRoot.y
        val info: Float = onNodeWithText("Info · 1").fetchSemanticsNode().positionInRoot.y
        assertEquals(listOf(critical, warning, info), listOf(critical, warning, info).sorted())

        onNodeWithText("Widget build failed: Follower goal").assertExists()
        onNodeWithText("“Family Ties” for viewer_one", substring = true).assertExists()
        onNodeWithText("Widget build failed: Follower goal").performClick()
        assertEquals(listOf("widget-build:v2"), reviewed)
    }
}
