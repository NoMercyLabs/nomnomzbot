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
import androidx.compose.ui.test.onAllNodesWithContentDescription
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
                AttentionSurface(items = listOf(songLost, webhookDown), onNavigate = { navigated += it }, onDismiss = {})
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

    private val heatTimeoutFailed: ActionRequiredItem =
        ActionRequiredItem(
            id = "heat-timeout-failed:e1",
            kind = "heat_auto_timeout_failed",
            severity = "warning",
            titleKey = "attention_heat_timeout_failed_title",
            messageKey = "attention_heat_timeout_failed_message",
            parameters = mapOf("username" to "heatedviewer", "error" to "missing scope"),
            deepLinkRoute = "moderation",
        )

    @Test
    fun aFailedHeatTimeoutNamesTheViewerAndTheReason_inBothLocales() = runComposeUiTest {
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(heatTimeoutFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("heatedviewer was not timed out").assertExists()
        onNodeWithText("Twitch refused the timeout: missing scope", substring = true).assertExists()
    }

    @Test
    fun aFailedHeatTimeoutRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(heatTimeoutFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("heatedviewer kreeg geen time-out").assertExists()
        onNodeWithText("Twitch weigerde de time-out: missing scope", substring = true).assertExists()
    }

    private val unbanAppeal: ActionRequiredItem =
        ActionRequiredItem(
            id = "unban-request:r1",
            kind = "unban_request",
            severity = "info",
            titleKey = "attention_unban_request_title",
            messageKey = "attention_unban_request_message",
            parameters = mapOf("username" to "appealingviewer", "text" to "I was wrongly banned"),
            deepLinkRoute = "moderation",
        )

    @Test
    fun anUnbanAppealNamesTheViewerAndQuotesTheirText_andOpensModeration() = runComposeUiTest {
        val navigated: MutableList<ShellRoute> = mutableListOf()
        setContent {
            Pinned("en") {
                AttentionSurface(items = listOf(unbanAppeal), onNavigate = { navigated += it }, onDismiss = {})
            }
        }

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("appealingviewer asked to be unbanned").assertExists()
        onNodeWithText("Appeal: I was wrongly banned").assertExists()

        onNodeWithText("appealingviewer asked to be unbanned").performClick()
        assertEquals(listOf(ShellRoute.Moderation), navigated)
    }

    @Test
    fun anUnbanAppealRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(unbanAppeal), onNavigate = {}, onDismiss = {}) }
        }

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("appealingviewer vraagt om een unban").assertExists()
        onNodeWithText("Verzoek: I was wrongly banned").assertExists()
    }

    @Test
    fun theSurfaceRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(webhookDown), onNavigate = {}, onDismiss = {}) }
        }

        onNodeWithText("1 punt vraagt je aandacht").assertExists()
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Webhook uitgeschakeld: Discord relay").assertExists()
        onNodeWithText("Kritiek · 1").assertExists()
    }

    private val deleteFailed: ActionRequiredItem =
        ActionRequiredItem(
            id = "automod-delete-failed:abc",
            kind = "automod_delete_failed",
            severity = "warning",
            titleKey = "attention_automod_delete_failed_title",
            messageKey = "attention_automod_delete_failed_message",
            parameters = mapOf("ruleName" to "no links", "userName" to "spammer_one", "count" to "3"),
            deepLinkRoute = "moderation",
        )

    @Test
    fun aFailedAutoModDeleteNamesTheRuleAndTheChatter_inBothLocales() = runComposeUiTest {
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(deleteFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("AutoMod could not delete a message").assertExists()
        onNodeWithText("The rule “no links” tried to delete a message from spammer_one", substring = true)
            .assertExists()
        onNodeWithText("Failed deletions in the last day: 3", substring = true).assertExists()
    }

    @Test
    fun aFailedAutoModDeleteRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(deleteFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("AutoMod kon een bericht niet verwijderen").assertExists()
        onNodeWithText("De regel “no links” probeerde een bericht van spammer_one", substring = true).assertExists()
    }

    private fun sharedBan(reason: String, parameters: Map<String, String>): ActionRequiredItem =
        ActionRequiredItem(
            id = "shared-ban:$reason:abc",
            kind = "shared_ban_not_applied",
            severity = "warning",
            titleKey = "attention_shared_ban_title",
            messageKey = "attention_shared_ban_${reason}_message",
            parameters = parameters,
            deepLinkRoute = "moderation",
        )

    @Test
    fun aSharedBanFromAnUntrustedChannelNamesTheViewerAndTheCount_inBothLocales() = runComposeUiTest {
        val item: ActionRequiredItem =
            sharedBan("origin_not_trusted", mapOf("count" to "2", "targetName" to "raider_one"))
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(item), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("A shared ban was not applied").assertExists()
        onNodeWithText("raider_one was banned in a channel you do not trust", substring = true).assertExists()
        onNodeWithText("in the last day: 2", substring = true).assertExists()
    }

    @Test
    fun aSharedBanOutsideASharedSessionRendersInDutch() = runComposeUiTest {
        val item: ActionRequiredItem =
            sharedBan("no_shared_session", mapOf("count" to "1", "targetName" to "raider_one"))
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(item), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Een gedeelde ban is niet toegepast").assertExists()
        onNodeWithText("raider_one is verbannen", substring = true).assertExists()
        onNodeWithText("gedeelde chat", substring = true).assertExists()
    }

    @Test
    fun aSharedBanTwitchRefusedQuotesTwitchsReason() = runComposeUiTest {
        val item: ActionRequiredItem =
            sharedBan(
                "twitch_ban_failed",
                mapOf("count" to "1", "targetName" to "raider_one", "detail" to "missing scope"),
            )
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(item), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Twitch refused the ban for raider_one: missing scope", substring = true).assertExists()
    }

    @Test
    fun aSharedBanTwitchRefusedWithoutAReasonDropsTheQuote() = runComposeUiTest {
        val item: ActionRequiredItem =
            sharedBan("twitch_ban_failed", mapOf("count" to "1", "targetName" to "raider_two"))
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(item), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Twitch refused the ban for raider_two.", substring = true).assertExists()
        onNodeWithText("raider_two:", substring = true).assertDoesNotExist()
    }

    private val filterFailed: ActionRequiredItem =
        ActionRequiredItem(
            id = "filter-action-failed:abc",
            kind = "chat_filter_action_failed",
            severity = "warning",
            titleKey = "attention_filter_action_failed_title",
            messageKey = "attention_filter_action_failed_message",
            parameters =
                mapOf("filter" to "no links", "username" to "spammer_one", "action" to "timeout", "reason" to "missing scope"),
            deepLinkRoute = "moderation",
        )

    @Test
    fun aFailedFilterActionNamesTheFilterTheChatterTheActionAndTheReason_inBothLocales() = runComposeUiTest {
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(filterFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("The no links filter could not act on spammer_one").assertExists()
        onNodeWithText("timeout", substring = true).assertExists()
        onNodeWithText("missing scope", substring = true).assertExists()
    }

    @Test
    fun aFailedFilterActionRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(filterFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Het filter no links kon niets doen met spammer_one").assertExists()
        onNodeWithText("missing scope", substring = true).assertExists()
    }

    private val massBanFailed: ActionRequiredItem =
        ActionRequiredItem(
            id = "mass-ban-failed:abc",
            kind = "mass_ban_failed",
            severity = "warning",
            titleKey = "attention_mass_ban_failed_title",
            messageKey = "attention_mass_ban_failed_message",
            parameters = mapOf("channelLogin" to "stoney_eagle", "failedCount" to "3", "totalCount" to "12"),
            deepLinkRoute = "moderation",
        )

    @Test
    fun aFailedMassBanSaysHowManyOfHowManyAccountsWereNotBanned() = runComposeUiTest {
        setContent {
            Pinned("en") { AttentionSurface(items = listOf(massBanFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("The mass ban in stoney_eagle did not finish").assertExists()
        onNodeWithText("3 of 12 accounts could not be banned", substring = true).assertExists()
        onNodeWithText("retry them from Moderation", substring = true).assertExists()
    }

    @Test
    fun aFailedMassBanRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(massBanFailed), onNavigate = {}, onDismiss = {}) }
        }
        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("De massale ban in stoney_eagle is niet afgerond").assertExists()
        onNodeWithText("3 van de 12 accounts konden niet worden verbannen", substring = true).assertExists()
        onNodeWithText("probeer ze opnieuw via Moderatie", substring = true).assertExists()
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
            Pinned("en") {
                AttentionSurface(items = listOf(operatorActing), onNavigate = { navigated += it }, onDismiss = {})
            }
        }

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("A NomNomzBot operator is acting as Mod Mia").assertExists()
        onNodeWithText("Support Sam opened a support session on your channel. Reason: Ticket 4821").assertExists()

        onNodeWithText("A NomNomzBot operator is acting as Mod Mia").performClick()
        assertEquals(listOf(ShellRoute.Roles), navigated)
    }

    @Test
    fun anOperatorSecurityNoticeRendersInDutch() = runComposeUiTest {
        setContent {
            Pinned("nl") { AttentionSurface(items = listOf(operatorActing), onNavigate = {}, onDismiss = {}) }
        }

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onNodeWithText("Een NomNomzBot-beheerder handelt als Mod Mia").assertExists()
    }

    @Test
    fun nothingRendersWhenNothingNeedsAttention() = runComposeUiTest {
        setContent { Pinned("en") { AttentionSurface(items = emptyList(), onNavigate = {}, onDismiss = {}) } }

        onAllNodesWithTag(ATTENTION_SURFACE_TAG).assertCountEquals(0)
    }

    @Test
    fun theDismissGlyphHandsItsOwnItemToOnDismiss_withoutNavigating() = runComposeUiTest {
        val navigated: MutableList<ShellRoute> = mutableListOf()
        val dismissed: MutableList<String> = mutableListOf()
        setContent {
            Pinned("en") {
                AttentionSurface(
                    items = listOf(songLost, webhookDown),
                    onNavigate = { navigated += it },
                    onDismiss = { dismissed += it.id },
                )
            }
        }

        onNodeWithTag(ATTENTION_SURFACE_TAG).performClick()
        onAllNodesWithContentDescription("Dismiss").assertCountEquals(2)
        onAllNodesWithContentDescription("Dismiss")[0].performClick()

        assertEquals(listOf("webhook-disabled:e1:1"), dismissed)
        assertEquals(emptyList(), navigated)
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
