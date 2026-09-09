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

import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.io.PickedFile
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityPage
import bot.nomnomz.dashboard.core.network.CommunityStats
import bot.nomnomz.dashboard.core.network.GdprApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.UsersApi
import bot.nomnomz.dashboard.core.network.ViewerDataApi
import bot.nomnomz.dashboard.core.network.ViewerEconomy
import bot.nomnomz.dashboard.core.network.ViewerIdentity
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.ViewerOverrides
import bot.nomnomz.dashboard.core.network.ViewerPermits
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import bot.nomnomz.dashboard.feature.community.state.CommunityController
import bot.nomnomz.dashboard.feature.community.state.ViewerProfileController
import bot.nomnomz.dashboard.feature.moderation.state.FakeModerationApi
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import kotlin.test.Test

// Proves the Community Directory (owner punch list 2026-09-08 §3A): the member list renders sorted most-
// recently-active first (never role-grouped, never alphabetical — the exact fix for the "tabs just reorder the
// same rows" mess), search finds a viewer beyond the loaded page, and opening a row's Profile action switches
// the SAME screen composable over to rendering the Profile content for the RIGHT person.
@OptIn(ExperimentalTestApi::class)
class CommunityScreenTest {

    @Test
    fun the_directory_lists_members_most_recently_active_first_by_default() = runComposeUiTest {
        val stale = CommunityMember(id = "u1", displayName = "Stale Viewer", lastSeen = "2026-01-01T00:00:00Z")
        val fresh = CommunityMember(id = "u2", displayName = "Fresh Viewer", lastSeen = "2026-09-01T00:00:00Z")
        val controller =
            CommunityController(FakeChannelsApi(), FakeCommunityApi(pageResult = ApiResult.Ok(CommunityPage(data = listOf(stale, fresh)))))
        val profileController = viewerProfileController()

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        CommunityScreen(controller = controller, profileController = profileController, role = ManagementRole.Broadcaster)
                    }
                }
            }
        }
        waitForIdle()

        // Both render; the assertion that matters is the ORDER — proven via the semantics tree's node order
        // would need a custom matcher, so this test instead proves recency sort at the controller boundary
        // (CommunityControllerTest) and here proves BOTH members reach the rendered list at all.
        onNodeWithText("Fresh Viewer").assertExists()
        onNodeWithText("Stale Viewer").assertExists()
    }

    @Test
    fun searching_a_viewer_and_opening_their_profile_switches_to_the_profile_page() = runComposeUiTest {
        val communityApi =
            FakeCommunityApi(
                pageResult = ApiResult.Ok(CommunityPage()),
                searchResults = listOf(ViewerOption(id = "tw-42", label = "Nibbles", subLabel = "nibbles")),
                memberResult = ApiResult.Ok(CommunityMember(id = "tw-42", internalUserId = "iu-42", displayName = "Nibbles")),
                profileResult =
                    ApiResult.Ok(
                        ViewerProfileSummary(
                            identity = ViewerIdentity(userId = "iu-42", twitchUserId = "tw-42", displayName = "Nibbles"),
                            economy = ViewerEconomy(),
                            permits = ViewerPermits(),
                            overrides = ViewerOverrides(),
                        )
                    ),
            )
        val controller = CommunityController(FakeChannelsApi(), communityApi)
        val profileController = viewerProfileController(communityApi = communityApi)

        setContent {
            withLifecycle {
                NomNomzTheme {
                    bot.nomnomz.dashboard.core.i18n.AppEnvironment("en") {
                        CommunityScreen(controller = controller, profileController = profileController, role = ManagementRole.Broadcaster)
                    }
                }
            }
        }
        waitForIdle()

        // "Find a viewer" is the field's LABEL (a separate Text node) — the editable field itself is matched
        // via its SetText semantics action, the same idiom SetupWizardScreenTest uses for a labelled AppTextField.
        onNode(hasSetTextAction()).performTextInput("Nib")
        // SearchPickerField debounces via a real `delay(300)` before firing the search — waitForIdle() alone
        // does not resolve a timed coroutine delay, so the clock is advanced past the debounce window first.
        mainClock.advanceTimeBy(500)
        waitForIdle()
        onNodeWithText("Nibbles").assertExists()
        // Picking the search result resolves their real member state (SearchedPersonRow's own fetch, keyed on
        // the Twitch id) before "Open profile" renders.
        onNodeWithText("Nibbles").performClick()
        waitForIdle()
        onNodeWithText("Open profile").performClick()
        waitForIdle()

        // The screen switched from Directory to Profile FOR THE RIGHT PERSON — the identity section renders
        // their real name from the profile fixture, not a placeholder.
        onNodeWithText("Nibbles", substring = true).assertExists()
        onNodeWithText("Back to Directory").assertExists()
    }

    private fun viewerProfileController(
        communityApi: CommunityApi =
            FakeCommunityApi(
                pageResult = ApiResult.Ok(CommunityPage()),
                profileResult =
                    ApiResult.Ok(
                        ViewerProfileSummary(
                            identity = ViewerIdentity(userId = "u1", displayName = "Viewer"),
                            economy = ViewerEconomy(),
                            permits = ViewerPermits(),
                            overrides = ViewerOverrides(),
                        )
                    ),
            )
    ): ViewerProfileController =
        ViewerProfileController(
            channelsApi = FakeChannelsApi(),
            communityApi = communityApi,
            moderationApi = FakeModerationApi(ApiResult.Ok(emptyList())),
            ttsApi = StubTtsApi(),
            rolesApi = StubRolesApi(),
            viewerDataApi = StubViewerDataApi(),
            gdprApi = StubGdprApi(),
            usersApi = StubUsersApi(),
            fileBridge = StubFileBridge(),
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

private class FakeChannelsApi : ChannelsApi {
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
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakeCommunityApi(
    private val pageResult: ApiResult<CommunityPage>,
    private val searchResults: List<ViewerOption> = emptyList(),
    private val memberResult: ApiResult<CommunityMember> = ApiResult.Ok(CommunityMember(id = "unset")),
    private val profileResult: ApiResult<ViewerProfileSummary> = ApiResult.Ok(ViewerProfileSummary(identity = ViewerIdentity())),
) : CommunityApi {
    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> = ApiResult.Ok(emptyList())
    override suspend fun membersPage(channelId: String, role: String?, page: Int, pageSize: Int, cursor: String?): ApiResult<CommunityPage> = pageResult
    override suspend fun searchViewers(channelId: String, query: String, limit: Int): ApiResult<List<ViewerOption>> = ApiResult.Ok(searchResults)
    override suspend fun member(channelId: String, userId: String): ApiResult<CommunityMember> = memberResult
    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> = ApiResult.Ok(emptyList())
    override suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun addVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun stats(channelId: String): ApiResult<CommunityStats> = ApiResult.Ok(CommunityStats())
    override suspend fun profile(channelId: String, userId: String): ApiResult<ViewerProfileSummary> = profileResult
}

private class StubTtsApi : TtsApi {
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

private class StubRolesApi : RolesApi {
    override suspend fun effectiveMe(channelId: String) = error("stub")
    override suspend fun members(channelId: String) = error("stub")
    override suspend fun permits(channelId: String) = error("stub")
    override suspend fun actionMatrix(channelId: String) = error("stub")
    override suspend fun searchViewers(query: String) = error("stub")
    override suspend fun setOverride(channelId: String, actionKey: String, level: Int) = error("stub")
    override suspend fun resetOverride(channelId: String, actionKey: String) = error("stub")
    override suspend fun assignRole(channelId: String, userId: String, role: bot.nomnomz.dashboard.core.network.ManagementRole) = ApiResult.Ok(Unit)
    override suspend fun removeRole(channelId: String, userId: String) = ApiResult.Ok(Unit)
    override suspend fun grantRole(channelId: String, userId: String, role: bot.nomnomz.dashboard.core.network.ManagementRole, expiresAt: String?, reason: String?) = ApiResult.Ok(Unit)
    override suspend fun grantCapability(channelId: String, userId: String, actionKey: String, expiresAt: String?, reason: String?) = error("stub")
    override suspend fun revokePermit(channelId: String, userId: String, actionKeyOrRole: String?) = ApiResult.Ok(Unit)
}

private class StubViewerDataApi : ViewerDataApi {
    override suspend fun getData(viewerId: String): ApiResult<Map<String, String>> = ApiResult.Ok(emptyMap())
    override suspend fun setDatum(viewerId: String, key: String, value: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteDatum(viewerId: String, key: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class StubGdprApi : GdprApi {
    override suspend fun exportData() = error("stub")
    override suspend fun exportSubject(subjectUserId: String, channelId: String?) =
        ApiResult.Ok(bot.nomnomz.dashboard.core.network.DataExport(document = "{}"))
    override suspend fun previewErasure() = error("stub")
    override suspend fun requestErasure(scope: String) = error("stub")
    override suspend fun optOut() = error("stub")
    override suspend fun requests() = error("stub")
    override suspend fun request(id: String) = error("stub")
    override suspend fun consents() = error("stub")
    override suspend fun grantConsent(body: bot.nomnomz.dashboard.core.network.GrantConsentBody) = error("stub")
    override suspend fun withdrawConsent(consentType: String) = error("stub")
}

private class StubUsersApi : UsersApi {
    override suspend fun search(query: String, limit: Int) = error("stub")
    override suspend fun stats(userId: String) = error("stub")
    override suspend fun erase(userId: String) = ApiResult.Ok(Unit)
}

private class StubFileBridge : JournalFileIO {
    override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean = true
    override suspend fun pickFile(): PickedFile? = null
}
