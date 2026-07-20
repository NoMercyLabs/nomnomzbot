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

import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.network.AnalyticsApi
import bot.nomnomz.dashboard.core.network.AnalyticsSummary
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityStats
import bot.nomnomz.dashboard.core.network.CommunityPage
import bot.nomnomz.dashboard.core.network.CommunityTrustLevel
import bot.nomnomz.dashboard.core.network.DailyMetricRow
import bot.nomnomz.dashboard.core.network.StreamAnalytics
import bot.nomnomz.dashboard.core.network.StreamListItem
import bot.nomnomz.dashboard.core.network.TopViewerEntry
import bot.nomnomz.dashboard.core.network.ViewerAnalyticsProfile
import bot.nomnomz.dashboard.core.network.ViewerEngagementDay
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.ViewerProfilePage
import bot.nomnomz.dashboard.core.network.WatchStreak
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Community page state machine the screen renders: resolve the active channel, surface the real
// member list (empty as Empty, a failure of either step as Error), and manage each member — set their trust
// level, ban them, or lift a ban. Each action must hit the right backend route with the resolved channel,
// reload on success so the list reflects the backend's truth, and surface a failure over the intact list.
// The screen is a pure projection of this, so testing it proves the page acts on real data and degrades
// cleanly.
class CommunityControllerTest {

    @Test
    fun load_surfaces_the_community_members_on_success() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(
                    ApiResult.Ok(
                        listOf(
                            CommunityMember(
                                id = "u1",
                                username = "stoney_eagle",
                                displayName = "Stoney_Eagle",
                                trustLevel = "moderator",
                            ),
                            CommunityMember(id = "u2", displayName = "Viewer Two"),
                        )
                    )
                ),
                FakeUsersApi(),
                FakeViewerDataApi(),
            )

        controller.load()

        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        val members: List<CommunityMember> = (state as CommunityState.Ready).members
        assertEquals(2, members.size)
        assertEquals("Stoney_Eagle", members[0].displayName)
        assertEquals("moderator", members[0].trustLevel)
        assertEquals("u2", members[1].id)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeCommunityApi(ApiResult.Ok(emptyList())),
                FakeUsersApi(),
                FakeViewerDataApi(),
            )

        controller.load()

        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Error)
        assertEquals("none onboarded", (state as CommunityState.Error).detail)
    }

    @Test
    fun load_errors_when_the_members_call_fails() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
                FakeUsersApi(),
                FakeViewerDataApi(),
            )

        controller.load()

        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Error)
        assertEquals("boom", (state as CommunityState.Error).detail)
    }

    @Test
    fun load_is_empty_when_the_community_has_no_members() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(ApiResult.Ok(emptyList())),
                FakeUsersApi(),
                FakeViewerDataApi(),
            )

        controller.load()

        assertTrue(controller.state.value is CommunityState.Empty)
    }

    @Test
    fun set_trust_calls_the_trust_route_then_reloads_the_updated_member() = runTest {
        val viewer = CommunityMember(id = "u1", displayName = "Viewer One", trustLevel = "viewer")
        val communityApi =
            FakeCommunityApi(
                // The reload after the trust write returns the member at the new level.
                membersResults =
                    listOf(
                        ApiResult.Ok(listOf(viewer)),
                        ApiResult.Ok(listOf(viewer.copy(trustLevel = CommunityTrustLevel.Vip))),
                    )
            )
        val controller =
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi(), FakeViewerDataApi())

        controller.load()
        controller.setTrust("u1", CommunityTrustLevel.Vip)

        // The write hit the trust route with the resolved channel, the member, and the chosen level.
        assertEquals(listOf(Triple("ch1", "u1", CommunityTrustLevel.Vip)), communityApi.trustCalls)
        // The list reloaded and the member's badge now reflects the new level.
        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        assertEquals(CommunityTrustLevel.Vip, (state as CommunityState.Ready).members.first().trustLevel)
        assertNull(state.actionError)
    }

    @Test
    fun set_trust_surfaces_the_error_and_keeps_the_list_when_it_fails() = runTest {
        val viewer = CommunityMember(id = "u1", displayName = "Viewer One", trustLevel = "viewer")
        val communityApi =
            FakeCommunityApi(
                membersResults = listOf(ApiResult.Ok(listOf(viewer))),
                trustResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller =
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi(), FakeViewerDataApi())

        controller.load()
        controller.setTrust("u1", CommunityTrustLevel.Moderator)

        assertEquals(listOf(Triple("ch1", "u1", CommunityTrustLevel.Moderator)), communityApi.trustCalls)
        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        // The list is intact (still the viewer, unchanged) and the failure is surfaced on the Ready state.
        assertEquals(listOf("u1"), (state as CommunityState.Ready).members.map { it.id })
        assertEquals("viewer", state.members.first().trustLevel)
        assertEquals("Missing scope.", state.actionError)
        // Only the initial load fetched members; the failed write did not trigger a reload.
        assertEquals(1, communityApi.membersCalls)
    }

    @Test
    fun ban_calls_the_ban_route_then_reloads_with_the_member_banned() = runTest {
        val troll = CommunityMember(id = "u1", displayName = "Troll", isBanned = false)
        val communityApi =
            FakeCommunityApi(
                membersResults =
                    listOf(
                        ApiResult.Ok(listOf(troll)),
                        ApiResult.Ok(listOf(troll.copy(isBanned = true))),
                    )
            )
        val controller =
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi(), FakeViewerDataApi())

        controller.load()
        controller.ban("u1", "Spamming links")

        // The write hit the ban route with the resolved channel, the member, and the reason.
        assertEquals(listOf(Triple("ch1", "u1", "Spamming links")), communityApi.banCalls)
        // The list reloaded and the member now reads as banned.
        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        assertTrue((state as CommunityState.Ready).members.first().isBanned)
        assertNull(state.actionError)
    }

    @Test
    fun ban_surfaces_the_error_and_keeps_the_list_when_it_fails() = runTest {
        val troll = CommunityMember(id = "u1", displayName = "Troll", isBanned = false)
        val communityApi =
            FakeCommunityApi(
                membersResults = listOf(ApiResult.Ok(listOf(troll))),
                banResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller =
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi(), FakeViewerDataApi())

        controller.load()
        controller.ban("u1", "Spamming links")

        assertEquals(listOf(Triple("ch1", "u1", "Spamming links")), communityApi.banCalls)
        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        // The list is intact (still not banned) and the failure is surfaced.
        assertEquals(false, (state as CommunityState.Ready).members.first().isBanned)
        assertEquals("Missing scope.", state.actionError)
        assertEquals(1, communityApi.membersCalls)
    }

    @Test
    fun unban_calls_the_unban_route_then_reloads_with_the_member_cleared() = runTest {
        val troll = CommunityMember(id = "u1", displayName = "Troll", isBanned = true)
        val communityApi =
            FakeCommunityApi(
                membersResults =
                    listOf(
                        ApiResult.Ok(listOf(troll)),
                        ApiResult.Ok(listOf(troll.copy(isBanned = false))),
                    )
            )
        val controller =
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi(), FakeViewerDataApi())

        controller.load()
        controller.unban("u1")

        // The write hit the unban route with the resolved channel and the member.
        assertEquals(listOf("ch1" to "u1"), communityApi.unbanCalls)
        // The list reloaded and the member is no longer banned.
        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        assertEquals(false, (state as CommunityState.Ready).members.first().isBanned)
        assertNull(state.actionError)
    }

    @Test
    fun unban_surfaces_the_error_and_keeps_the_list_when_it_fails() = runTest {
        val troll = CommunityMember(id = "u1", displayName = "Troll", isBanned = true)
        val communityApi =
            FakeCommunityApi(
                membersResults = listOf(ApiResult.Ok(listOf(troll))),
                unbanResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller =
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi(), FakeViewerDataApi())

        controller.load()
        controller.unban("u1")

        assertEquals(listOf("ch1" to "u1"), communityApi.unbanCalls)
        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        // The list is intact (still banned) and the failure is surfaced.
        assertTrue((state as CommunityState.Ready).members.first().isBanned)
        assertEquals("Missing scope.", state.actionError)
        assertEquals(1, communityApi.membersCalls)
    }

    @Test
    fun set_viewer_datum_returns_null_on_success() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(ApiResult.Ok(emptyList())),
                FakeUsersApi(),
                FakeViewerDataApi(),
            )

        // A successful upsert returns null (no error to surface) — the consequence the dialog keys off.
        assertNull(controller.setViewerDatum("u1", "deaths", "5"))
    }

    @Test
    fun set_viewer_datum_surfaces_the_backend_error_on_failure() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(ApiResult.Ok(emptyList())),
                FakeUsersApi(),
                FakeViewerDataApi(
                    setResult =
                        ApiResult.Failure(ApiError(400, "TOO_LONG", "Value exceeds 500 characters."))
                ),
            )

        // An over-cap value is rejected by the backend; the controller returns its verbatim message so the
        // dialog can show WHY the save failed rather than a generic error.
        assertEquals(
            "Value exceeds 500 characters.",
            controller.setViewerDatum("u1", "deaths", "x"),
        )
    }

    @Test
    fun get_viewer_analytics_reads_the_channel_profile_by_internal_user_id() = runTest {
        val member = CommunityMember(id = "u1", internalUserId = "iu1", displayName = "Viewer One")
        val analytics =
            FakeAnalyticsApi(
                profile = ViewerAnalyticsProfile(
                    viewerUserId = "iu1",
                    totalMessages = 42,
                    totalRedemptions = 3,
                    isSubscriber = true,
                    subTier = "1000",
                )
            )
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(ApiResult.Ok(listOf(member))),
                FakeUsersApi(),
                FakeViewerDataApi(),
                analytics,
            )

        controller.load()
        val profile: ViewerAnalyticsProfile? = controller.getViewerAnalytics(member)

        // The channel-scoped profile is addressed by the resolved channel + the member's INTERNAL id (not the
        // Twitch id) — the moderator-readable path that works for a foreign viewer.
        assertEquals("ch1", analytics.requestedChannelId)
        assertEquals("iu1", analytics.requestedViewerId)
        assertEquals(42, profile?.totalMessages)
        assertEquals(3, profile?.totalRedemptions)
        assertTrue(profile?.isSubscriber == true)
    }

    @Test
    fun get_viewer_analytics_is_null_when_the_member_has_no_internal_id() = runTest {
        val member = CommunityMember(id = "u1", displayName = "Viewer One") // no internalUserId
        val analytics = FakeAnalyticsApi()
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(ApiResult.Ok(listOf(member))),
                FakeUsersApi(),
                FakeViewerDataApi(),
                analytics,
            )

        controller.load()

        // No internal id → no analytics call at all (the profile route can't be addressed).
        assertNull(controller.getViewerAnalytics(member))
        assertNull(analytics.requestedViewerId)
    }

    @Test
    fun get_viewer_data_returns_the_stored_map() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(ApiResult.Ok(emptyList())),
                FakeUsersApi(),
                FakeViewerDataApi(data = mapOf("deaths" to "12", "favorite_game" to "Elden Ring")),
            )

        val data: Map<String, String>? = controller.getViewerData("u1")
        assertEquals(mapOf("deaths" to "12", "favorite_game" to "Elden Ring"), data)
    }

    @Test
    fun select_role_loads_the_first_page_of_that_role() = runTest {
        val allMember = CommunityMember(id = "u1", displayName = "Everyone")
        val vipMember = CommunityMember(id = "u2", displayName = "A Vip", trustLevel = "vip")
        val communityApi =
            FakeCommunityApi(
                // load() consumes the "all" page; selectRole("vip") consumes the "vip" page.
                membersResults =
                    listOf(
                        ApiResult.Ok(listOf(allMember)),
                        ApiResult.Ok(listOf(vipMember)),
                    )
            )
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                communityApi,
                FakeUsersApi(),
                FakeViewerDataApi(),
            )

        controller.load()
        controller.selectRole(CommunityRole.Vip)

        // The role-filtered fetch threaded the "vip" role and reset to the first page, and the list swapped.
        val lastCall: Triple<String?, Int, String?> = communityApi.pageCalls.last()
        assertEquals(CommunityRole.Vip, lastCall.first)
        assertEquals(1, lastCall.second)
        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        val ready: CommunityState.Ready = state as CommunityState.Ready
        assertEquals(CommunityRole.Vip, ready.role)
        assertEquals(listOf("u2"), ready.members.map { it.id })
    }

    @Test
    fun search_viewers_maps_backend_options_to_picker_options_keyed_on_twitch_id() = runTest {
        val communityApi =
            FakeCommunityApi(
                membersResults = listOf(ApiResult.Ok(emptyList())),
                searchResults =
                    listOf(ViewerOption(id = "tw-42", label = "Nibbles", subLabel = "nibbles")),
            )
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                communityApi,
                FakeUsersApi(),
                FakeViewerDataApi(),
            )
        controller.load()

        val options: List<PickerOption> = controller.searchViewers("nib")

        // The search hit the resolved channel, and each option carries the Twitch id the ban/vip/trust writes key on.
        assertEquals(listOf("ch1" to "nib"), communityApi.searchCalls)
        assertEquals(1, options.size)
        assertEquals("tw-42", options.first().id)
        assertEquals("Nibbles", options.first().label)
        assertEquals("nibbles", options.first().sublabel)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
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

private class FakeCommunityApi(
    private val membersResults: List<ApiResult<List<CommunityMember>>>,
    private val trustResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val banResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val unbanResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val searchResults: List<ViewerOption> = emptyList(),
) : CommunityApi {
    override suspend fun stats(channelId: String): ApiResult<CommunityStats> =
        ApiResult.Ok(CommunityStats())

    // Single-result convenience for the read-only tests (one members() result, default-OK writes).
    constructor(result: ApiResult<List<CommunityMember>>) : this(membersResults = listOf(result))

    // The controller loads the list through membersPage; membersCalls counts those page fetches. Each call walks
    // the configured script so a post-write reload observes the next scripted state (the last entry repeats).
    var membersCalls: Int = 0
        private set

    val trustCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    val banCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    val unbanCalls: MutableList<Pair<String, String>> = mutableListOf()

    /** Each membersPage call recorded as (role, page, cursor) — proves the role tab / paging thread through. */
    val pageCalls: MutableList<Triple<String?, Int, String?>> = mutableListOf()
    val searchCalls: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> =
        ApiResult.Ok(emptyList())

    private fun nextMembersResult(): ApiResult<List<CommunityMember>> {
        val index: Int = minOf(membersCalls, membersResults.lastIndex)
        membersCalls += 1
        return membersResults[index]
    }

    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> = nextMembersResult()

    override suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ): ApiResult<CommunityPage> {
        pageCalls.add(Triple(role, page, cursor))
        return when (val result: ApiResult<List<CommunityMember>> = nextMembersResult()) {
            is ApiResult.Ok ->
                ApiResult.Ok(CommunityPage(data = result.value, hasMore = false, total = result.value.size))
            is ApiResult.Failure -> ApiResult.Failure(result.error)
        }
    }

    override suspend fun searchViewers(
        channelId: String,
        query: String,
        limit: Int,
    ): ApiResult<List<ViewerOption>> {
        searchCalls.add(channelId to query)
        return ApiResult.Ok(searchResults)
    }

    override suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit> {
        trustCalls.add(Triple(channelId, userId, level))
        return trustResult
    }

    override suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit> {
        banCalls.add(Triple(channelId, userId, reason))
        return banResult
    }

    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> {
        unbanCalls.add(channelId to userId)
        return unbanResult
    }

    override suspend fun addVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeUsersApi : bot.nomnomz.dashboard.core.network.UsersApi {
    override suspend fun search(
        query: String,
        limit: Int,
    ): ApiResult<List<bot.nomnomz.dashboard.core.network.UserSearchResult>> = ApiResult.Ok(emptyList())

    override suspend fun stats(userId: String): ApiResult<bot.nomnomz.dashboard.core.network.UserStats> =
        ApiResult.Ok(bot.nomnomz.dashboard.core.network.UserStats())

    override suspend fun export(userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun erase(userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeViewerDataApi(
    private val data: Map<String, String> = emptyMap(),
    private val setResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val deleteResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : bot.nomnomz.dashboard.core.network.ViewerDataApi {
    override suspend fun getData(viewerId: String): ApiResult<Map<String, String>> = ApiResult.Ok(data)

    override suspend fun setDatum(viewerId: String, key: String, value: String): ApiResult<Unit> = setResult

    override suspend fun deleteDatum(viewerId: String, key: String): ApiResult<Unit> = deleteResult
}

private class FakeAnalyticsApi(
    private val profile: ViewerAnalyticsProfile = ViewerAnalyticsProfile(),
) : AnalyticsApi {
    var requestedChannelId: String? = null
    var requestedViewerId: String? = null

    override suspend fun summary(channelId: String, from: String, to: String): ApiResult<AnalyticsSummary> =
        ApiResult.Ok(AnalyticsSummary())

    override suspend fun daily(channelId: String, from: String, to: String): ApiResult<List<DailyMetricRow>> =
        ApiResult.Ok(emptyList())

    override suspend fun streams(channelId: String): ApiResult<List<StreamListItem>> = ApiResult.Ok(emptyList())

    override suspend fun streamDetail(channelId: String, streamId: String): ApiResult<StreamAnalytics> =
        ApiResult.Ok(StreamAnalytics())

    override suspend fun topViewers(
        channelId: String,
        metric: String,
        from: String,
        to: String,
        top: Int,
    ): ApiResult<List<TopViewerEntry>> = ApiResult.Ok(emptyList())

    override suspend fun listViewers(
        channelId: String,
        search: String?,
        sort: String,
        followersOnly: Boolean?,
        subscribersOnly: Boolean?,
        page: Int,
        pageSize: Int,
    ): ApiResult<ViewerProfilePage> = ApiResult.Ok(ViewerProfilePage())

    override suspend fun viewerProfile(
        channelId: String,
        viewerUserId: String,
    ): ApiResult<ViewerAnalyticsProfile> {
        requestedChannelId = channelId
        requestedViewerId = viewerUserId
        return ApiResult.Ok(profile)
    }

    override suspend fun viewerEngagement(
        channelId: String,
        viewerUserId: String,
        from: String,
        to: String,
    ): ApiResult<List<ViewerEngagementDay>> = ApiResult.Ok(emptyList())

    override suspend fun viewerStreak(channelId: String, viewerUserId: String): ApiResult<WatchStreak> =
        ApiResult.Ok(WatchStreak())

    override suspend fun setAnalyticsOptOut(
        channelId: String,
        viewerUserId: String,
        optedOut: Boolean,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
}
