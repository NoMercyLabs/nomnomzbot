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
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Community page state machine the screen renders: resolve the active channel, then surface the real
// member list — empty as Empty, a failure of either step as Error. The screen is a pure projection of this, so
// testing it proves the page shows real viewers (no fabricated lists) and degrades cleanly.
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
            )

        controller.load()

        assertTrue(controller.state.value is CommunityState.Empty)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeCommunityApi(private val result: ApiResult<List<CommunityMember>>) : CommunityApi {
    override suspend fun members(channelId: String): ApiResult<List<CommunityMember>> = result
}
