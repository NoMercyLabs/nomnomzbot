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
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityPage
import bot.nomnomz.dashboard.core.network.CommunityStats
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ViewerOption
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Community DIRECTORY page's state machine (owner punch list 2026-09-08 §3A — a search-first list
// for finding someone fast, everything management-shaped now lives on the Profile page instead). This
// controller's whole job: resolve the active channel, list members sorted MOST RECENTLY ACTIVE FIRST by
// default (the default sort the owner asked for, replacing the old role-filter-tabs-as-structure page), page
// through a role filter, and resolve a searched Twitch id to the real member (so the row can tell whether a
// Profile even exists for them). No ban/trust/VIP/shoutout methods live here anymore — those moved to
// [ViewerProfileController] and are proven by ViewerProfileControllerTest.
class CommunityControllerTest {

    @Test
    fun load_surfaces_members_sorted_most_recently_active_first() = runTest {
        // The backend's own ordering (by Twitch id) is NOT recency — the controller must re-sort the fetched
        // page by lastSeen descending, the default the owner asked for ("not alphabetical, not role-grouped").
        val stale = CommunityMember(id = "u1", displayName = "Stale Viewer", lastSeen = "2026-01-01T00:00:00Z")
        val fresh = CommunityMember(id = "u2", displayName = "Fresh Viewer", lastSeen = "2026-09-01T00:00:00Z")
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeCommunityApi(membersPageResults = listOf(ApiResult.Ok(CommunityPage(data = listOf(stale, fresh))))),
            )

        controller.load()

        val state: CommunityState = controller.state.value
        assertTrue(state is CommunityState.Ready)
        assertEquals(listOf("u2", "u1"), (state as CommunityState.Ready).members.map { it.id })
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            CommunityController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeCommunityApi(membersPageResults = listOf(ApiResult.Ok(CommunityPage()))),
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
                FakeCommunityApi(membersPageResults = listOf(ApiResult.Failure(ApiError(500, "ERR", "boom")))),
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
                FakeCommunityApi(membersPageResults = listOf(ApiResult.Ok(CommunityPage()))),
            )

        controller.load()

        assertTrue(controller.state.value is CommunityState.Empty)
    }

    @Test
    fun select_role_loads_the_first_page_of_that_filter() = runTest {
        val allMember = CommunityMember(id = "u1", displayName = "Everyone")
        val vipMember = CommunityMember(id = "u2", displayName = "A Vip", trustLevel = "vip")
        val communityApi =
            FakeCommunityApi(
                // load() consumes the "all" page; selectRole("vip") consumes the "vip" page.
                membersPageResults =
                    listOf(
                        ApiResult.Ok(CommunityPage(data = listOf(allMember))),
                        ApiResult.Ok(CommunityPage(data = listOf(vipMember))),
                    )
            )
        val controller = CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi)

        controller.load()
        controller.selectRole(CommunityRole.Vip)

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
    fun next_page_advances_and_prev_page_steps_back() = runTest {
        val pageOne = CommunityMember(id = "u1", displayName = "Page One")
        val pageTwo = CommunityMember(id = "u2", displayName = "Page Two")
        val communityApi =
            FakeCommunityApi(
                membersPageResults =
                    listOf(
                        ApiResult.Ok(CommunityPage(data = listOf(pageOne), hasMore = true)),
                        ApiResult.Ok(CommunityPage(data = listOf(pageTwo), hasMore = false)),
                        ApiResult.Ok(CommunityPage(data = listOf(pageOne), hasMore = true)),
                    )
            )
        val controller = CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi)
        controller.load()

        controller.nextPage()
        var state: CommunityState.Ready = controller.state.value as CommunityState.Ready
        assertEquals(2, state.page)
        assertEquals(listOf("u2"), state.members.map { it.id })

        controller.prevPage()
        state = controller.state.value as CommunityState.Ready
        assertEquals(1, state.page)
        assertEquals(listOf("u1"), state.members.map { it.id })
    }

    @Test
    fun search_viewers_maps_backend_options_to_picker_options_keyed_on_twitch_id() = runTest {
        val communityApi =
            FakeCommunityApi(
                membersPageResults = listOf(ApiResult.Ok(CommunityPage())),
                searchResults = listOf(ViewerOption(id = "tw-42", label = "Nibbles", subLabel = "nibbles")),
            )
        val controller = CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi)
        controller.load()

        val options: List<PickerOption> = controller.searchViewers("nib")

        assertEquals(listOf("ch1" to "nib"), communityApi.searchCalls)
        assertEquals(1, options.size)
        assertEquals("tw-42", options.first().id)
        assertEquals("Nibbles", options.first().label)
        assertEquals("nibbles", options.first().sublabel)
    }

    @Test
    fun member_detail_resolves_the_real_member_so_a_profile_can_be_opened() = runTest {
        // A search hit only carries a Twitch id; opening a Profile needs the internal Guid — memberDetail is
        // what resolves it, the same real-state lookup the old page used to avoid a synthesized default.
        val resolved = CommunityMember(id = "42", internalUserId = "iu-42", displayName = "Naughty")
        val api = FakeCommunityApi(membersPageResults = listOf(ApiResult.Ok(CommunityPage())), memberResult = ApiResult.Ok(resolved))
        val controller = CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        val member: CommunityMember? = controller.memberDetail("42")

        assertEquals(listOf("42"), api.memberCalls)
        assertNotNull(member)
        assertEquals("iu-42", member.internalUserId)
    }

    @Test
    fun member_detail_is_null_when_the_lookup_fails() = runTest {
        val api = FakeCommunityApi(membersPageResults = listOf(ApiResult.Ok(CommunityPage())))
        val controller = CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        assertNull(controller.memberDetail("99"))
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
    private val membersPageResults: List<ApiResult<CommunityPage>>,
    private val searchResults: List<ViewerOption> = emptyList(),
    private val memberResult: ApiResult<CommunityMember> = ApiResult.Failure(ApiError(404, "NOT_FOUND", "no member")),
) : CommunityApi {
    var pageCallCount: Int = 0
        private set

    val pageCalls: MutableList<Triple<String?, Int, String?>> = mutableListOf()
    val searchCalls: MutableList<Pair<String, String>> = mutableListOf()
    val memberCalls: MutableList<String> = mutableListOf()

    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> = ApiResult.Ok(emptyList())

    override suspend fun membersPage(
        channelId: String,
        role: String?,
        page: Int,
        pageSize: Int,
        cursor: String?,
    ): ApiResult<CommunityPage> {
        pageCalls.add(Triple(role, page, cursor))
        val index: Int = minOf(pageCallCount, membersPageResults.lastIndex)
        pageCallCount += 1
        return membersPageResults[index]
    }

    override suspend fun searchViewers(channelId: String, query: String, limit: Int): ApiResult<List<ViewerOption>> {
        searchCalls.add(channelId to query)
        return ApiResult.Ok(searchResults)
    }

    override suspend fun member(channelId: String, userId: String): ApiResult<CommunityMember> {
        memberCalls.add(userId)
        return memberResult
    }

    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> = ApiResult.Ok(emptyList())

    override suspend fun setTrust(channelId: String, userId: String, level: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun ban(channelId: String, userId: String, reason: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun addVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun removeVip(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun shoutout(channelId: String, targetTwitchUserId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun stats(channelId: String): ApiResult<CommunityStats> = ApiResult.Ok(CommunityStats())

    override suspend fun profile(channelId: String, userId: String): ApiResult<ViewerProfileSummary> =
        ApiResult.Failure(ApiError(404, "NOT_FOUND", "no profile"))
}
