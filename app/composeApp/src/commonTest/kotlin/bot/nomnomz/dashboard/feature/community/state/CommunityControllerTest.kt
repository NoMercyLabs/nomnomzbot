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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityTrustLevel
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
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi())

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
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi())

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
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi())

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
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi())

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
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi())

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
            CommunityController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), communityApi, FakeUsersApi())

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
) : CommunityApi {
    // Single-result convenience for the read-only tests (one members() result, default-OK writes).
    constructor(result: ApiResult<List<CommunityMember>>) : this(membersResults = listOf(result))

    var membersCalls: Int = 0
        private set

    val trustCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    val banCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    val unbanCalls: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun topChatters(channelId: String): ApiResult<List<ChatActivityEntry>> =
        ApiResult.Ok(emptyList())

    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> {
        // Walk through the configured sequence; the last entry repeats once the script runs out.
        val index: Int = minOf(membersCalls, membersResults.lastIndex)
        membersCalls += 1
        return membersResults[index]
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
    override suspend fun stats(userId: String): ApiResult<bot.nomnomz.dashboard.core.network.UserStats> =
        ApiResult.Ok(bot.nomnomz.dashboard.core.network.UserStats())

    override suspend fun export(userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun erase(userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}
