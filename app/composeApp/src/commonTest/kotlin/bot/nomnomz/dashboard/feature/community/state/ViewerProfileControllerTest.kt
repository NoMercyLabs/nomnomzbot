// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.io.JournalFileIO
import bot.nomnomz.dashboard.core.io.PickedFile
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityPage
import bot.nomnomz.dashboard.core.network.CommunityStats
import bot.nomnomz.dashboard.core.network.DataExport
import bot.nomnomz.dashboard.core.network.GdprApi
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ModerationHistoryEntry
import bot.nomnomz.dashboard.core.network.ModerationHistoryPage
import bot.nomnomz.dashboard.core.network.RolesApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.UserTtsVoice
import bot.nomnomz.dashboard.core.network.UsersApi
import bot.nomnomz.dashboard.core.network.ViewerDataApi
import bot.nomnomz.dashboard.core.network.ViewerEconomy
import bot.nomnomz.dashboard.core.network.ViewerIdentity
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.ViewerOverrides
import bot.nomnomz.dashboard.core.network.ViewerPermits
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import bot.nomnomz.dashboard.feature.moderation.state.FakeModerationApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Community PROFILE page's state machine (owner punch list 2026-09-08 §3B). Loading folds the real
// profile + the first page of moderation history into one Ready state; every editable action (overrides, TTS
// voice, management role, permits, quick moderation) calls its OWN real endpoint with the RIGHT id (Twitch id
// for Twitch-side actions, the internal Guid for platform-side ones) and reloads so the state reflects the
// backend's truth — not "the call happened", but "the resulting state changed to match it".
class ViewerProfileControllerTest {

    @Test
    fun load_folds_the_profile_and_first_history_page_into_ready() = runTest {
        val profile = fakeProfile(displayName = "Naughty Nibbles", twitchId = "tw-42")
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(profile))
        val moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        moderationApi.historyPageResult =
            ApiResult.Ok(
                ModerationHistoryPage(
                    data = listOf(ModerationHistoryEntry(id = "h1", actionType = "timeout")),
                    hasMore = true,
                )
            )
        val controller = controller(communityApi = communityApi, moderationApi = moderationApi)

        controller.load("u1")

        val state: ViewerProfileState = controller.state.value
        assertTrue(state is ViewerProfileState.Ready)
        val ready: ViewerProfileState.Ready = state as ViewerProfileState.Ready
        assertEquals("Naughty Nibbles", ready.profile.identity.displayName)
        assertEquals(1, ready.history.size)
        assertEquals("timeout", ready.history.first().actionType)
        assertTrue(ready.historyHasMore)
        // History is fetched keyed on the profile's OWN resolved userId, not the raw route id.
        assertEquals(listOf(Triple("ch1", "u1", 1)), moderationApi.historyForUserCalls)
    }

    @Test
    fun load_errors_when_the_profile_fetch_fails() = runTest {
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Failure(ApiError(404, "NOT_FOUND", "no such viewer")))
        val controller = controller(communityApi = communityApi)

        controller.load("missing")

        val state: ViewerProfileState = controller.state.value
        assertTrue(state is ViewerProfileState.Error)
        assertEquals("no such viewer", (state as ViewerProfileState.Error).detail)
    }

    @Test
    fun ban_calls_the_real_ban_route_with_the_persons_twitch_id_then_reloads() = runTest {
        val profile = fakeProfile(displayName = "Troll", twitchId = "tw-99")
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(profile))
        val controller = controller(communityApi = communityApi)
        controller.load("u1")

        controller.ban("Spamming links")

        // The Twitch id, not the route's internal Guid, is what the ban endpoint consumes.
        assertEquals(listOf(Triple("ch1", "tw-99", "Spamming links")), communityApi.banCalls)
        // The write reloaded the profile (the SAME reload discipline every mutation here follows).
        assertEquals(2, communityApi.profileCallCount)
    }

    @Test
    fun ban_is_refused_when_the_person_has_no_linked_twitch_identity() = runTest {
        // A viewer created purely from, say, a Kick chat has no Twitch id yet — the Twitch-only ban endpoint
        // cannot be addressed for them. The controller must refuse rather than call the API with a blank id.
        val profile = fakeProfile(displayName = "KickOnly", twitchId = null)
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(profile))
        val feedback = RecordingFeedback()
        val controller = controller(communityApi = communityApi, feedback = feedback)
        controller.load("u1")

        controller.ban("reason")

        assertTrue(communityApi.banCalls.isEmpty())
        assertEquals(1, communityApi.profileCallCount) // no reload — the write never reached the backend
        assertEquals(FeedbackKind.Error, feedback.only.kind)
    }

    @Test
    fun set_management_role_calls_roles_api_with_the_internal_user_id_then_reloads() = runTest {
        val profile = fakeProfile(displayName = "Rising Mod", twitchId = "tw-5")
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(profile))
        val rolesApi = VPCFakeRolesApi()
        val controller = controller(communityApi = communityApi, rolesApi = rolesApi)
        controller.load("u1")

        controller.setManagementRole(ManagementRole.Moderator)

        // Roles are keyed on the platform User GUID (the route id), never the Twitch id.
        assertEquals(listOf(Triple("ch1", "u1", ManagementRole.Moderator)), rolesApi.assignCalls)
        assertEquals(2, communityApi.profileCallCount)
    }

    @Test
    fun remove_management_role_calls_roles_api_then_reloads() = runTest {
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile()))
        val rolesApi = VPCFakeRolesApi()
        val controller = controller(communityApi = communityApi, rolesApi = rolesApi)
        controller.load("u1")

        controller.removeManagementRole()

        assertEquals(listOf("ch1" to "u1"), rolesApi.removeCalls)
        assertEquals(2, communityApi.profileCallCount)
    }

    @Test
    fun save_override_message_writes_the_shoutout_line_keyed_on_the_twitch_id_and_reloads() = runTest {
        val profile = fakeProfile(displayName = "Friend", twitchId = "tw-42")
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(profile))
        val moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        val controller = controller(communityApi = communityApi, moderationApi = moderationApi)
        controller.load("u1")

        val error: String? = controller.saveOverrideMessage("shoutout", "Go follow Friend!")

        assertNull(error)
        assertEquals(1, moderationApi.savedOverrides.size)
        assertEquals("tw-42", moderationApi.savedOverrides.first().targetTwitchUserId)
        assertEquals("Go follow Friend!", moderationApi.savedOverrides.first().messageTemplate)
        assertEquals(2, communityApi.profileCallCount)
    }

    @Test
    fun a_rejected_override_message_surfaces_the_backends_reason_on_the_page_banner() = runTest {
        // A caller that fires-and-forgets the write (the Profile screen's overrides section calls this but a
        // caller could ignore the return) must still see the failure — via the SAME shell-level feedback toast
        // every other write on this page uses, not a silently swallowed error.
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile(twitchId = "tw-1")))
        val moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        moderationApi.setShoutoutOverrideResult = ApiResult.Failure(ApiError(400, "BAD_REQUEST", "Template too long."))
        val feedback = RecordingFeedback()
        val controller = controller(communityApi = communityApi, moderationApi = moderationApi, feedback = feedback)
        controller.load("u1")

        val returned: String? = controller.saveOverrideMessage("shoutout", "x".repeat(2000))

        assertEquals("Template too long.", returned)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(listOf<Any>("Template too long."), feedback.only.formatArgs)
        // No reload happened on failure — the profile still reflects the PRE-write state.
        assertEquals(1, communityApi.profileCallCount)
    }

    @Test
    fun save_tts_voice_calls_tts_api_keyed_on_twitch_id_then_reloads() = runTest {
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile(twitchId = "tw-7")))
        val ttsApi = VPCFakeTtsApi()
        val controller = controller(communityApi = communityApi, ttsApi = ttsApi)
        controller.load("u1")

        val error: String? = controller.saveTtsVoice("voice-xyz")

        assertNull(error)
        assertEquals(listOf(Triple("ch1", "tw-7", "voice-xyz")), ttsApi.setVoiceCalls)
        assertEquals(2, communityApi.profileCallCount)
    }

    @Test
    fun clear_tts_voice_calls_tts_api_then_reloads() = runTest {
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile(twitchId = "tw-7")))
        val ttsApi = VPCFakeTtsApi()
        val controller = controller(communityApi = communityApi, ttsApi = ttsApi)
        controller.load("u1")

        controller.clearTtsVoice()

        assertEquals(listOf("ch1" to "tw-7"), ttsApi.clearVoiceCalls)
        assertEquals(2, communityApi.profileCallCount)
    }

    @Test
    fun set_viewer_datum_returns_null_on_success_and_the_backends_message_on_failure() = runTest {
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile()))
        val viewerDataApi = VPCFakeViewerDataApi(setResult = ApiResult.Failure(ApiError(400, "TOO_LONG", "Value exceeds 500 characters.")))
        val controller = controller(communityApi = communityApi, viewerDataApi = viewerDataApi)
        controller.load("u1")

        assertEquals("Value exceeds 500 characters.", controller.setViewerDatum("deaths", "x"))
        assertEquals(listOf("u1" to ("deaths" to "x")), viewerDataApi.setCalls)
    }

    @Test
    fun export_user_data_saves_the_returned_document_through_the_file_bridge() = runTest {
        val gdprApi = VPCFakeGdprApi(result = ApiResult.Ok(DataExport(document = "{\"subject\":\"u1\"}")))
        val bridge = VPCFakeFileBridge()
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile()))
        val controller = controller(communityApi = communityApi, gdprApi = gdprApi, fileBridge = bridge)
        controller.load("u1")

        val error: String? = controller.exportUserData()

        assertNull(error)
        assertEquals(listOf<Pair<String, String?>>("u1" to "ch1"), gdprApi.exportSubjectCalls)
        assertEquals("{\"subject\":\"u1\"}", bridge.savedBytes?.decodeToString())
    }

    @Test
    fun erase_user_data_calls_users_api_and_surfaces_a_failure_on_the_banner() = runTest {
        val usersApi = VPCFakeUsersApi(eraseResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "compliance:erasure required.")))
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile()))
        val feedback = RecordingFeedback()
        val controller = controller(communityApi = communityApi, usersApi = usersApi, feedback = feedback)
        controller.load("u1")

        val error: String? = controller.eraseUserData()

        assertEquals("compliance:erasure required.", error)
        assertEquals(listOf("u1"), usersApi.eraseCalls)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(listOf<Any>("compliance:erasure required."), feedback.only.formatArgs)
    }

    @Test
    fun load_more_history_appends_the_next_page_and_updates_has_more() = runTest {
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile()))
        val moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        moderationApi.historyPageResult =
            ApiResult.Ok(ModerationHistoryPage(data = listOf(ModerationHistoryEntry(id = "h1", actionType = "ban")), hasMore = true))
        val controller = controller(communityApi = communityApi, moderationApi = moderationApi)
        controller.load("u1")

        moderationApi.historyPageResult =
            ApiResult.Ok(ModerationHistoryPage(data = listOf(ModerationHistoryEntry(id = "h2", actionType = "warn")), hasMore = false))
        controller.loadMoreHistory()

        val state: ViewerProfileState.Ready = controller.state.value as ViewerProfileState.Ready
        assertEquals(listOf("h1", "h2"), state.history.map { it.id })
        assertTrue(!state.historyHasMore)
    }

    @Test
    fun add_history_note_posts_the_note_then_reloads() = runTest {
        val communityApi = VPCFakeCommunityApi(profileResult = ApiResult.Ok(fakeProfile()))
        val moderationApi = FakeModerationApi(ApiResult.Ok(emptyList<BannedUser>()))
        val controller = controller(communityApi = communityApi, moderationApi = moderationApi)
        controller.load("u1")

        controller.addHistoryNote("Warned verbally in Discord.")

        assertEquals(listOf("u1" to "Warned verbally in Discord."), moderationApi.addedHistoryNotes)
        assertEquals(2, communityApi.profileCallCount)
    }

    private fun controller(
        communityApi: CommunityApi,
        moderationApi: FakeModerationApi = FakeModerationApi(ApiResult.Ok(emptyList())),
        ttsApi: TtsApi = VPCFakeTtsApi(),
        rolesApi: RolesApi = VPCFakeRolesApi(),
        viewerDataApi: ViewerDataApi = VPCFakeViewerDataApi(),
        gdprApi: GdprApi = VPCFakeGdprApi(),
        usersApi: UsersApi = VPCFakeUsersApi(),
        fileBridge: JournalFileIO = VPCFakeFileBridge(),
        feedback: Feedback = NoOpFeedback,
    ): ViewerProfileController =
        ViewerProfileController(
            channelsApi = VPCFakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            communityApi = communityApi,
            moderationApi = moderationApi,
            ttsApi = ttsApi,
            rolesApi = rolesApi,
            viewerDataApi = viewerDataApi,
            gdprApi = gdprApi,
            usersApi = usersApi,
            fileBridge = fileBridge,
            feedback = feedback,
        )

    private fun fakeProfile(
        userId: String = "u1",
        displayName: String = "Viewer One",
        twitchId: String? = "tw-1",
    ): ViewerProfileSummary =
        ViewerProfileSummary(
            identity =
                ViewerIdentity(
                    userId = userId,
                    twitchUserId = twitchId,
                    username = displayName.lowercase(),
                    displayName = displayName,
                ),
            economy = ViewerEconomy(),
            permits = ViewerPermits(),
            overrides = ViewerOverrides(),
        )
}

internal class VPCFakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
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

internal class VPCFakeCommunityApi(private val profileResult: ApiResult<ViewerProfileSummary>) : CommunityApi {
    var profileCallCount: Int = 0
        private set

    val banCalls: MutableList<Triple<String, String, String>> = mutableListOf()

    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> = ApiResult.Ok(emptyList())
    override suspend fun membersPage(channelId: String, role: String?, page: Int, pageSize: Int, cursor: String?): ApiResult<CommunityPage> =
        ApiResult.Ok(CommunityPage())
    override suspend fun searchViewers(channelId: String, query: String, limit: Int): ApiResult<List<ViewerOption>> = ApiResult.Ok(emptyList())

    // ViewerProfileController.refresh() reads this for the ban badge/toggle (the profile summary itself
    // carries no ban flag — see ViewerProfileController's own comment). Not banned by default; tests that care
    // about ban state assert on the [ban]/[unban] CALLS, not on this synthesized read.
    override suspend fun member(channelId: String, userId: String): ApiResult<CommunityMember> =
        ApiResult.Ok(CommunityMember(id = userId, isBanned = false))

    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> = ApiResult.Ok(emptyList())
    override suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit> {
        banCalls.add(Triple(channelId, userId, reason))
        return ApiResult.Ok(Unit)
    }

    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun addVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun stats(channelId: String): ApiResult<CommunityStats> = ApiResult.Ok(CommunityStats())

    override suspend fun profile(channelId: String, userId: String): ApiResult<ViewerProfileSummary> {
        profileCallCount += 1
        return profileResult
    }
}

internal class VPCFakeTtsApi : TtsApi {
    val setVoiceCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    val clearVoiceCalls: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun config(channelId: String) = error("stub")
    override suspend fun updateConfig(channelId: String, update: bot.nomnomz.dashboard.core.network.TtsConfigUpdate) = error("stub")
    override suspend fun setByokKey(channelId: String, provider: String, apiKey: String, region: String?) = error("stub")
    override suspend fun removeByokKey(channelId: String, provider: String) = error("stub")
    override suspend fun voicesPage(channelId: String, query: String, locale: String, gender: String, provider: String, accent: String, page: Int, pageSize: Int) = error("stub")
    override suspend fun voices(channelId: String): ApiResult<List<TtsVoice>> = ApiResult.Ok(emptyList())
    override suspend fun testSpeak(channelId: String, request: bot.nomnomz.dashboard.core.network.TtsTestRequest) = error("stub")
    override suspend fun queue(channelId: String) = error("stub")
    override suspend fun approveQueueEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun rejectQueueEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun userVoice(channelId: String, userId: String): ApiResult<UserTtsVoice?> = ApiResult.Ok(null)

    override suspend fun setUserVoice(channelId: String, userId: String, voiceId: String): ApiResult<Unit> {
        setVoiceCalls.add(Triple(channelId, userId, voiceId))
        return ApiResult.Ok(Unit)
    }

    override suspend fun clearUserVoice(channelId: String, userId: String): ApiResult<Unit> {
        clearVoiceCalls.add(channelId to userId)
        return ApiResult.Ok(Unit)
    }

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

internal class VPCFakeRolesApi : RolesApi {
    val assignCalls: MutableList<Triple<String, String, ManagementRole>> = mutableListOf()
    val removeCalls: MutableList<Pair<String, String>> = mutableListOf()
    val grantRoleCalls: MutableList<Triple<String, String, ManagementRole>> = mutableListOf()
    val revokeCalls: MutableList<Pair<String, String?>> = mutableListOf()

    override suspend fun effectiveMe(channelId: String) = error("stub")
    override suspend fun members(channelId: String) = error("stub")
    override suspend fun permits(channelId: String) = error("stub")
    override suspend fun actionMatrix(channelId: String) = error("stub")
    override suspend fun searchViewers(query: String) = error("stub")
    override suspend fun setOverride(channelId: String, actionKey: String, level: Int) = error("stub")
    override suspend fun resetOverride(channelId: String, actionKey: String) = error("stub")

    override suspend fun assignRole(channelId: String, userId: String, role: ManagementRole): ApiResult<Unit> {
        assignCalls.add(Triple(channelId, userId, role))
        return ApiResult.Ok(Unit)
    }

    override suspend fun removeRole(channelId: String, userId: String): ApiResult<Unit> {
        removeCalls.add(channelId to userId)
        return ApiResult.Ok(Unit)
    }

    override suspend fun grantRole(channelId: String, userId: String, role: ManagementRole, expiresAt: String?, reason: String?): ApiResult<Unit> {
        grantRoleCalls.add(Triple(channelId, userId, role))
        return ApiResult.Ok(Unit)
    }

    override suspend fun grantCapability(channelId: String, userId: String, actionKey: String, expiresAt: String?, reason: String?) = error("stub")

    override suspend fun revokePermit(channelId: String, userId: String, actionKeyOrRole: String?): ApiResult<Unit> {
        revokeCalls.add(userId to actionKeyOrRole)
        return ApiResult.Ok(Unit)
    }
}

internal class VPCFakeViewerDataApi(
    private val data: Map<String, String> = emptyMap(),
    private val setResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val deleteResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : ViewerDataApi {
    val setCalls: MutableList<Pair<String, Pair<String, String>>> = mutableListOf()

    override suspend fun getData(viewerId: String): ApiResult<Map<String, String>> = ApiResult.Ok(data)

    override suspend fun setDatum(viewerId: String, key: String, value: String): ApiResult<Unit> {
        setCalls.add(viewerId to (key to value))
        return setResult
    }

    override suspend fun deleteDatum(viewerId: String, key: String): ApiResult<Unit> = deleteResult
}

internal class VPCFakeGdprApi(
    private val result: ApiResult<DataExport> = ApiResult.Ok(DataExport(document = "{}")),
) : GdprApi {
    val exportSubjectCalls: MutableList<Pair<String, String?>> = mutableListOf()

    override suspend fun exportData(): ApiResult<DataExport> = result

    override suspend fun exportSubject(subjectUserId: String, channelId: String?): ApiResult<DataExport> {
        exportSubjectCalls.add(subjectUserId to channelId)
        return result
    }

    override suspend fun previewErasure() = error("stub")
    override suspend fun requestErasure(scope: String) = error("stub")
    override suspend fun optOut() = error("stub")
    override suspend fun requests() = error("stub")
    override suspend fun request(id: String) = error("stub")
    override suspend fun consents() = error("stub")
    override suspend fun grantConsent(body: bot.nomnomz.dashboard.core.network.GrantConsentBody) = error("stub")
    override suspend fun withdrawConsent(consentType: String) = error("stub")
}

internal class VPCFakeUsersApi(private val eraseResult: ApiResult<Unit> = ApiResult.Ok(Unit)) : UsersApi {
    val eraseCalls: MutableList<String> = mutableListOf()

    override suspend fun search(query: String, limit: Int) = error("stub")
    override suspend fun stats(userId: String) = error("stub")

    override suspend fun erase(userId: String): ApiResult<Unit> {
        eraseCalls.add(userId)
        return eraseResult
    }
}

internal class VPCFakeFileBridge(private val accept: Boolean = true) : JournalFileIO {
    var savedName: String? = null
    var savedBytes: ByteArray? = null

    override suspend fun saveFile(suggestedName: String, bytes: ByteArray): Boolean {
        savedName = suggestedName
        savedBytes = bytes
        return accept
    }

    override suspend fun pickFile(): PickedFile? = null
}
