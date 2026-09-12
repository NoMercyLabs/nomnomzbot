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

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChatFilter
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.ModerationHistoryEntry
import bot.nomnomz.dashboard.core.network.ModerationHistoryFilter
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.ModerationStats
import bot.nomnomz.dashboard.core.network.Moderator
import bot.nomnomz.dashboard.core.network.NetworkNukeBatch
import bot.nomnomz.dashboard.core.network.SharedBanSettings
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelperDto
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.UnbanRequest
import bot.nomnomz.dashboard.core.network.ViewerReport
import bot.nomnomz.dashboard.feature.moderation.state.AutomationLine
import kotlin.test.Test

/**
 * Owner request 2026-09-12: the Rules tab was one continuous scroll of 11 unrelated configuration surfaces
 * (blocked terms, AutoMod local + Twitch, custom rules, chat filters, trust weights, spam defense, the
 * escalation ladder, shared bans, shoutout config) at equal visual weight — genuinely overwhelming. It is
 * now split into four job groups ([RulesGroup], private in `ModerationScreen.kt`) behind one tab strip, so
 * only one job's cards are ever on screen.
 *
 * This proves the split on the RENDERED semantics tree, not from source alone: each group's own always-on
 * marker text is visible when that group is selected and gone when it is not — a source-level guard (like
 * [bot.nomnomz.dashboard.feature.moderation.ModerationSectionOwnershipTest]) can prove every card is TAGGED
 * with the right group, but only a render proves the tag actually hides the card from the other three.
 */
@OptIn(ExperimentalTestApi::class)
class ModerationRulesGroupingRenderTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    // One always-rendered marker per group, independent of any loaded list being empty — proves the GROUP
    // switch itself, not a particular card's empty/loaded state.
    private val contentFiltersMarker = "Blocked terms" // terms-header
    private val autoEnforcementMarker = "Trust & Automation" // trust-automation-header
    private val networkMarker = "Shared-chat bans" // shared-bans-header (rendered once sharedBanSettings != null)
    private val generalMarker = "Shoutout announcement" // shoutout-header

    private val groupTabLabels = listOf("Filtering", "Enforcement", "Network", "General")

    @Composable
    private fun RulesPage() {
        BansList(
            section = ModerationSection.Rules,
            bans = emptyList<BannedUser>(),
            modLog = emptyList<ModLogEntry>(),
            shieldEnabled = false,
            blockedTerms = emptyList<String>(),
            automod = AutomodConfig(),
            rules = emptyList<ModerationRule>(),
            moderators = emptyList<Moderator>(),
            chatFilters = emptyList<ChatFilter>(),
            stats = ModerationStats(),
            unbanRequests = emptyList<UnbanRequest>(),
            reports = emptyList<ViewerReport>(),
            automodQueue = emptyList<ModerationQueueItem>(),
            bansAvailable = true,
            blockedTermsAvailable = true,
            shieldAvailable = true,
            escalationPolicy = null,
            sharedBanSettings = SharedBanSettings(),
            nukeBatches = emptyList<NetworkNukeBatch>(),
            historyEntries = emptyList<ModerationHistoryEntry>(),
            historyPage = 0,
            historyHasMore = false,
            historyFilter = ModerationHistoryFilter(),
            shoutoutTemplate = null,
            templateHelpersApi =
                object : TemplateHelpersApi {
                    override suspend fun helpers(
                        context: TemplateHelperContext,
                        eventType: String?,
                    ): ApiResult<List<TemplateHelperDto>> = ApiResult.Ok(emptyList())
                },
            automationLines = emptyList<AutomationLine>(),
            trustPolicy = null,
            spamDefense = null,
            spamDetections = emptyList(),
            spamCampaigns = emptyList(),
            followBotBlocks = emptyList(),
            twitchAutoMod = null,
            trustWeightSumInvalid = false,
            broadcasterManage = ManageDecision.Allowed,
            onSaveTrustPolicy = {},
            onSaveSpamDefense = {},
            onOverturnSpamDetection = {},
            onRestoreFollowBotBatch = {},
            onSaveTwitchAutoMod = {},
            onToggleAutoTimeoutOnHeat = {},
            onSaveHeatTimeoutSeconds = {},
            manage = ManageDecision.Allowed,
            suspiciousManage = ManageDecision.Allowed,
            onSaveEscalation = {},
            onSaveHeatThreshold = {},
            onSaveSharedBans = { _, _ -> },
            onAddTrusted = {},
            onRemoveTrusted = {},
            onRevertNuke = {},
            onHistorySubjectSelected = {},
            onHistoryDateRangeChanged = { _, _ -> },
            onHistoryActionTypeChanged = {},
            onNextHistoryPage = {},
            onPrevHistoryPage = {},
            onResolveUnban = { _, _, _ -> },
            onResolveReport = { _, _ -> },
            onResolveAutomodQueueItem = { _, _ -> },
            onUnban = {},
            onNetworkUnban = {},
            onViewContext = {},
            searchViewers = { emptyList<PickerOption>() },
            searchChannels = { emptyList<PickerOption>() },
            onPerformAction = { _, _, _, _ -> },
            onToggleShield = {},
            onAddModerator = {},
            onRemoveModerator = {},
            onClearChat = {},
            onAddTerm = {},
            onRemoveTerm = {},
            onToggleFilter = {},
            onSaveCapsThreshold = {},
            onSaveEmoteMaxEmotes = {},
            onAddPhrase = {},
            onRemovePhrase = {},
            onAddWhitelist = {},
            onRemoveWhitelist = {},
            onToggleRule = { _, _ -> },
            onDeleteRule = {},
            onCreateRule = { _, _, _, _, _ -> },
            onToggleChatFilter = { _, _ -> },
            onDeleteChatFilter = {},
            onCreateChatFilter = { _, _, _, _, _, _ -> },
            onSendAnnouncement = { _, _ -> },
            onSaveShoutoutTemplate = {},
        )
    }

    @Test
    fun rules_page_opens_on_content_filters_and_hides_the_other_three_groups() {
        runComposeUiTest {
            setContent { EnglishContent { RulesPage() } }
            waitForIdle()

            // The tab strip itself always renders, whichever group is selected — the structure a moderator
            // orients from before picking a job.
            groupTabLabels.forEach { label -> onNodeWithText(label).assertExists() }

            onNodeWithText(contentFiltersMarker).assertExists()
            onNodeWithText(autoEnforcementMarker).assertDoesNotExist()
            onNodeWithText(networkMarker).assertDoesNotExist()
            onNodeWithText(generalMarker).assertDoesNotExist()
        }
    }

    @Test
    fun switching_to_enforcement_shows_only_its_own_cards() {
        runComposeUiTest {
            setContent { EnglishContent { RulesPage() } }
            waitForIdle()

            onNodeWithText("Enforcement").performClick()
            waitForIdle()

            onNodeWithText(autoEnforcementMarker).assertExists()
            onNodeWithText(contentFiltersMarker).assertDoesNotExist()
            onNodeWithText(networkMarker).assertDoesNotExist()
            onNodeWithText(generalMarker).assertDoesNotExist()
        }
    }

    @Test
    fun switching_to_network_shows_only_shared_bans() {
        runComposeUiTest {
            setContent { EnglishContent { RulesPage() } }
            waitForIdle()

            onNodeWithText("Network").performClick()
            waitForIdle()

            onNodeWithText(networkMarker).assertExists()
            onNodeWithText(contentFiltersMarker).assertDoesNotExist()
            onNodeWithText(autoEnforcementMarker).assertDoesNotExist()
            onNodeWithText(generalMarker).assertDoesNotExist()
        }
    }

    @Test
    fun switching_to_general_shows_only_shoutout_config() {
        runComposeUiTest {
            setContent { EnglishContent { RulesPage() } }
            waitForIdle()

            onNodeWithText("General").performClick()
            waitForIdle()

            onNodeWithText(generalMarker).assertExists()
            onNodeWithText(contentFiltersMarker).assertDoesNotExist()
            onNodeWithText(autoEnforcementMarker).assertDoesNotExist()
            onNodeWithText(networkMarker).assertDoesNotExist()
        }
    }
}
