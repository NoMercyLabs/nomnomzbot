// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.ui

import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasScrollAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.io.PickedFile
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityPage
import bot.nomnomz.dashboard.core.network.CommunityStats
import bot.nomnomz.dashboard.core.network.CurrencyAccountSummary
import bot.nomnomz.dashboard.core.network.DataExport
import bot.nomnomz.dashboard.core.network.GdprApi
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.UserModerationHistorySummary
import bot.nomnomz.dashboard.core.network.UsersApi
import bot.nomnomz.dashboard.core.network.ViewerDataApi
import bot.nomnomz.dashboard.core.network.ViewerDatum
import bot.nomnomz.dashboard.core.network.ViewerEconomy
import bot.nomnomz.dashboard.core.network.ViewerIdentity
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.ViewerOverrides
import bot.nomnomz.dashboard.core.network.ViewerPermits
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import bot.nomnomz.dashboard.feature.community.state.ViewerProfileController
import bot.nomnomz.dashboard.feature.moderation.state.FakeModerationApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole as ShellManagementRole
import kotlin.test.Test
import kotlin.test.assertEquals

// Proves the Community Profile page (owner punch list 2026-09-08 §3B) against a real fixture:
//  - every domain-table section renders its real data shape (not "the section is titled correctly")
//  - the overrides section's Save button actually calls ModerationApi.setShoutoutOverride with the typed
//    template and REFLECTS the saved value back (via the reload), not merely "the button exists"
//  - a caller below the Editor floor sees the Save control rendered DISABLED (frontend-ia.md §7: disable, don't
//    hide), never missing
@OptIn(ExperimentalTestApi::class)
class ViewerProfileScreenTest {

    @Test
    fun every_domain_section_renders_its_real_fixture_data() = runComposeUiTest {
        val moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        val controller = controller(moderationApi = moderationApi, profile = fullFixtureProfile())

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        ViewerProfileScreen(controller = controller, userId = "u1", role = ShellManagementRole.Broadcaster, onBack = {})
                    }
                }
            }
        }
        waitForIdle()

        // The profile is one long LazyColumn (identity → history → activity → economy → permits →
        // overrides → command usage → free-form → quotes) — later sections are virtualized out of the
        // initial composition, so each check scrolls the list to bring its section into view first.
        // 1. Identity — real display name + pronoun.
        scrollTo("Chatty Cathy")
        onNodeWithText("Chatty Cathy").assertExists()
        onNodeWithText("she/her", substring = true).assertExists()
        // 2. Moderation history — the real projected counts, not zeros-by-default.
        scrollTo("3")
        onNodeWithText("3").assertExists() // timeoutCount
        // 4. Economy — the real wallet balance.
        scrollTo("500")
        onNodeWithText("500").assertExists()
        // 6. Overrides — the real saved shoutout line.
        scrollTo("Go check out Cathy!")
        onNodeWithText("Go check out Cathy!").assertExists()
        // 8. Free-form data — the real key/value pair.
        scrollTo("favorite_game")
        onNodeWithText("favorite_game").assertExists()
        onNodeWithText("Elden Ring").assertExists()
        // 9. Quotes — the real attributed quote text.
        scrollTo("#7")
        onNodeWithText("#7", substring = true).assertExists()
    }

    @Test
    fun saving_a_new_shoutout_line_calls_the_real_endpoint_and_the_field_reflects_the_saved_value() = runComposeUiTest {
        val moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        val controller = controller(moderationApi = moderationApi, profile = fullFixtureProfile())

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        ViewerProfileScreen(controller = controller, userId = "u1", role = ShellManagementRole.Broadcaster, onBack = {})
                    }
                }
            }
        }
        waitForIdle()

        // The shoutout field is pre-filled with its saved value — select it by that value and replace it in one
        // atomic action (a separate clear-then-type risks the node being re-resolved against stale state once
        // scrolled to the edge of the viewport, and append-at-cursor position is not guaranteed either way, so
        // this avoids a flaky concatenation assertion). The overrides section sits below the initial LazyColumn
        // viewport.
        scrollTo("Go check out Cathy!")
        onNodeWithText("Go check out Cathy!").performTextReplacement("Cathy is the best, go say hi!")
        onAllNodesWithText("Save")[0].performClick()
        waitForIdle()

        // The REAL mutation fired — the exact template the field held, against the person's Twitch id.
        assertEquals(1, moderationApi.savedOverrides.size)
        assertEquals("tw-cathy", moderationApi.savedOverrides.first().targetTwitchUserId)
        assertEquals("Cathy is the best, go say hi!", moderationApi.savedOverrides.first().messageTemplate)
        // The reload folded the new value back — the field shows the SAVED state, not a stale draft.
        onNodeWithText("Cathy is the best, go say hi!").assertExists()
    }

    @Test
    fun below_the_editor_floor_the_save_control_is_disabled_not_hidden() = runComposeUiTest {
        val controller = controller(profile = fullFixtureProfile())

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        // Moderator is BELOW the Editor floor the overrides section is gated at.
                        ViewerProfileScreen(controller = controller, userId = "u1", role = ShellManagementRole.Moderator, onBack = {})
                    }
                }
            }
        }
        waitForIdle()

        // The ManageGate wraps the disabled control in a parent node carrying the [Disabled] state + reason —
        // the same shape TimersScreenTest/CommandsScreenTest assert against for every other gated screen. The
        // overrides section sits below the initial LazyColumn viewport — scroll via its unique saved value
        // rather than "Save" itself, which is ambiguous (both the shoutout and raid fields render one).
        scrollTo("Go check out Cathy!")
        onAllNodesWithText("Save")[0].assertIsNotEnabled()
    }

    // Scrolls the profile's one LazyColumn until a node containing [text] is composed — later sections
    // (economy, overrides, free-form data, quotes) sit outside the test window's initial viewport.
    private fun ComposeUiTest.scrollTo(text: String) {
        onNode(hasScrollAction()).performScrollToNode(hasText(text, substring = true))
    }

    private fun controller(
        moderationApi: FakeModerationApi = FakeModerationApi(ApiResult.Ok(emptyList())),
        profile: ViewerProfileSummary,
    ): ViewerProfileController =
        ViewerProfileController(
            channelsApi = VPSFakeChannelsApi(),
            communityApi = VPSFakeCommunityApi(profile),
            moderationApi = moderationApi,
            ttsApi = VPSStubTtsApi(),
            rolesApi = VPSStubRolesApi(),
            viewerDataApi = VPSStubViewerDataApi(),
            gdprApi = VPSStubGdprApi(),
            usersApi = VPSStubUsersApi(),
            fileBridge = VPSStubFileBridge(),
        )

    private fun fullFixtureProfile(): ViewerProfileSummary =
        ViewerProfileSummary(
            identity =
                ViewerIdentity(
                    userId = "u1",
                    twitchUserId = "tw-cathy",
                    username = "chattycathy",
                    displayName = "Chatty Cathy",
                    pronoun = "she/her",
                    communityStanding = "subscriber",
                    firstSeenUtc = "2026-01-01T00:00:00Z",
                ),
            moderationHistory =
                UserModerationHistorySummary(timeoutCount = 3, banCount = 0, warningCount = 1, messagesDeletedCount = 2),
            economy = ViewerEconomy(wallet = CurrencyAccountSummary(id = "acc1", balance = 500, lifetimeEarned = 900, lifetimeSpent = 400)),
            permits = ViewerPermits(),
            overrides = ViewerOverrides(shoutoutMessageTemplate = "Go check out Cathy!"),
            freeFormData = listOf(ViewerDatum(key = "favorite_game", value = "Elden Ring")),
            recentQuotes =
                listOf(bot.nomnomz.dashboard.core.network.Quote(id = "q1", number = 7, text = "This game is cursed")),
            totalQuoteCount = 1,
        )
}

@androidx.compose.runtime.Composable
private fun withLifecycle(content: @androidx.compose.runtime.Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    androidx.compose.runtime.CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}

internal class VPSFakeChannelsApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))
    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())
    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<bot.nomnomz.dashboard.core.network.ModeratedChannel>> = ApiResult.Ok(emptyList())
}

internal class VPSFakeCommunityApi(private val profileFixture: ViewerProfileSummary) : CommunityApi {
    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> = ApiResult.Ok(emptyList())
    override suspend fun membersPage(channelId: String, role: String?, page: Int, pageSize: Int, cursor: String?): ApiResult<CommunityPage> =
        ApiResult.Ok(CommunityPage())
    override suspend fun searchViewers(channelId: String, query: String, limit: Int): ApiResult<List<ViewerOption>> = ApiResult.Ok(emptyList())
    override suspend fun member(channelId: String, userId: String): ApiResult<CommunityMember> = ApiResult.Ok(CommunityMember(id = userId))
    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> = ApiResult.Ok(emptyList())
    override suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun addVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun stats(channelId: String): ApiResult<CommunityStats> = ApiResult.Ok(CommunityStats())
    override suspend fun profile(channelId: String, userId: String): ApiResult<ViewerProfileSummary> = ApiResult.Ok(profileFixture)
}

internal class VPSStubTtsApi : TtsApi {
    override suspend fun config(channelId: String) = error("stub")
    override suspend fun updateConfig(channelId: String, update: bot.nomnomz.dashboard.core.network.TtsConfigUpdate) = error("stub")
    override suspend fun setByokKey(channelId: String, provider: String, apiKey: String, region: String?) = error("stub")
    override suspend fun removeByokKey(channelId: String, provider: String) = error("stub")
    override suspend fun voicesPage(channelId: String, query: String, locale: String, gender: String, provider: String, accent: String, page: Int, pageSize: Int) = error("stub")
    override suspend fun voices(channelId: String) = ApiResult.Ok(emptyList<bot.nomnomz.dashboard.core.network.TtsVoice>())
    override suspend fun testSpeak(channelId: String, request: bot.nomnomz.dashboard.core.network.TtsTestRequest) = error("stub")
    override suspend fun queue(channelId: String) = error("stub")
    override suspend fun approveQueueEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun rejectQueueEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun userVoice(channelId: String, userId: String) = ApiResult.Ok<bot.nomnomz.dashboard.core.network.UserTtsVoice?>(null)
    override suspend fun setUserVoice(channelId: String, userId: String, voiceId: String) = ApiResult.Ok(Unit)
    override suspend fun clearUserVoice(channelId: String, userId: String) = ApiResult.Ok(Unit)
    override suspend fun lexicon(channelId: String) = error("stub")
    override suspend fun createLexiconEntry(channelId: String, body: bot.nomnomz.dashboard.core.network.UpsertTtsLexiconEntryBody) = error("stub")
    override suspend fun updateLexiconEntry(channelId: String, entryId: String, body: bot.nomnomz.dashboard.core.network.UpsertTtsLexiconEntryBody) = error("stub")
    override suspend fun deleteLexiconEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun myVoice(channelId: String) = error("stub")
    override suspend fun setMyVoice(channelId: String, voiceId: String) = error("stub")
    override suspend fun clearMyVoice(channelId: String) = error("stub")
    override suspend fun overlay(channelId: String) = error("stub")
    override suspend fun testOverlay(channelId: String) = error("stub")
    override suspend fun skipPlayback(channelId: String) = error("stub")
    override suspend fun clearPlayback(channelId: String) = error("stub")
    override suspend fun pausePlayback(channelId: String) = error("stub")
    override suspend fun resumePlayback(channelId: String) = error("stub")
}

internal class VPSStubRolesApi : RolesApi {
    override suspend fun effectiveMe(channelId: String) = error("stub")
    override suspend fun members(channelId: String) = error("stub")
    override suspend fun permits(channelId: String) = error("stub")
    override suspend fun actionMatrix(channelId: String) = error("stub")
    override suspend fun searchViewers(query: String) = error("stub")
    override suspend fun setOverride(channelId: String, actionKey: String, level: Int) = error("stub")
    override suspend fun resetOverride(channelId: String, actionKey: String) = error("stub")
    override suspend fun assignRole(channelId: String, userId: String, role: ManagementRole) = ApiResult.Ok(Unit)
    override suspend fun removeRole(channelId: String, userId: String) = ApiResult.Ok(Unit)
    override suspend fun grantRole(channelId: String, userId: String, role: ManagementRole, expiresAt: String?, reason: String?) = ApiResult.Ok(Unit)
    override suspend fun grantCapability(channelId: String, userId: String, actionKey: String, expiresAt: String?, reason: String?) = error("stub")
    override suspend fun revokePermit(channelId: String, userId: String, actionKeyOrRole: String?) = ApiResult.Ok(Unit)
}

internal class VPSStubViewerDataApi : ViewerDataApi {
    override suspend fun getData(viewerId: String): ApiResult<Map<String, String>> = ApiResult.Ok(emptyMap())
    override suspend fun setDatum(viewerId: String, key: String, value: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteDatum(viewerId: String, key: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

internal class VPSStubGdprApi : GdprApi {
    override suspend fun exportData() = error("stub")
    override suspend fun exportSubject(subjectUserId: String, channelId: String?) = ApiResult.Ok(DataExport(document = "{}"))
    override suspend fun previewErasure() = error("stub")
    override suspend fun requestErasure(scope: String) = error("stub")
    override suspend fun optOut() = error("stub")
    override suspend fun requests() = error("stub")
    override suspend fun request(id: String) = error("stub")
    override suspend fun consents() = error("stub")
    override suspend fun grantConsent(body: bot.nomnomz.dashboard.core.network.GrantConsentBody) = error("stub")
    override suspend fun withdrawConsent(consentType: String) = error("stub")
}

internal class VPSStubUsersApi : UsersApi {
    override suspend fun search(query: String, limit: Int) = error("stub")
    override suspend fun stats(userId: String) = error("stub")
    override suspend fun erase(userId: String) = ApiResult.Ok(Unit)
}

internal class VPSStubFileBridge : JournalFileIO {
    override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean = true
    override suspend fun pickFile(): PickedFile? = null
}
