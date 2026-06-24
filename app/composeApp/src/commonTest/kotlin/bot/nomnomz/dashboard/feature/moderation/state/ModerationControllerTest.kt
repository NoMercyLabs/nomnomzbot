// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModerationApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Moderation page state machine the read-only screen renders: resolve the active channel, then
// surface the real banned-viewer list — Empty when there are none, or Error if either step fails. The screen
// is a pure projection of this, so testing it proves the page shows real data and degrades cleanly.
class ModerationControllerTest {

    @Test
    fun load_surfaces_the_channels_banned_viewers_on_success() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(
                    ApiResult.Ok(
                        listOf(
                            BannedUser(
                                id = "u1",
                                username = "trolly",
                                displayName = "Trolly",
                                reason = "Spamming links",
                                bannedBy = "ModBot",
                                bannedAt = "2026-06-24T18:05:00Z",
                            )
                        )
                    )
                ),
            )

        controller.load()

        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        val bans: List<BannedUser> = (state as ModerationState.Ready).bans
        assertEquals(1, bans.size)
        assertEquals("Trolly", bans.first().displayName)
        assertEquals("Spamming links", bans.first().reason)
    }

    @Test
    fun load_is_empty_when_no_one_is_banned() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is ModerationState.Empty)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeModerationApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is ModerationState.Error)
    }

    @Test
    fun load_errors_when_the_bans_call_fails() = runTest {
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeModerationApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is ModerationState.Error)
    }

    @Test
    fun unban_lifts_the_ban_then_reloads_the_remaining_list() = runTest {
        val trolly =
            BannedUser(id = "u1", username = "trolly", displayName = "Trolly", reason = "Spam")
        val griefer =
            BannedUser(id = "u2", username = "griefer", displayName = "Griefer", reason = "Raid")
        val moderationApi =
            FakeModerationApi(
                // First load returns both; after the unban succeeds the reload returns only the other one.
                bansResults =
                    listOf(ApiResult.Ok(listOf(trolly, griefer)), ApiResult.Ok(listOf(griefer))),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )

        controller.load()
        controller.unban("u1")

        // The unban hit the real route with the resolved channel + the user's id.
        assertEquals(listOf("ch1" to "u1"), moderationApi.unbanCalls)
        // The list reloaded and now reflects the post-unban state — the unbanned viewer is gone.
        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        val bans: List<BannedUser> = (state as ModerationState.Ready).bans
        assertEquals(listOf("u2"), bans.map { it.id })
        assertNull(state.actionError)
    }

    @Test
    fun unban_surfaces_the_error_and_keeps_the_list_when_it_fails() = runTest {
        val trolly =
            BannedUser(id = "u1", username = "trolly", displayName = "Trolly", reason = "Spam")
        val moderationApi =
            FakeModerationApi(
                bansResults = listOf(ApiResult.Ok(listOf(trolly))),
                unbanResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller =
            ModerationController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                moderationApi,
            )

        controller.load()
        controller.unban("u1")

        assertEquals(listOf("ch1" to "u1"), moderationApi.unbanCalls)
        val state: ModerationState = controller.state.value
        assertTrue(state is ModerationState.Ready)
        // The list is untouched and the failure is surfaced on the Ready state.
        assertEquals(listOf("u1"), (state as ModerationState.Ready).bans.map { it.id })
        assertEquals("Missing scope.", state.actionError)
        // Only the initial load fetched bans; the failed unban did not trigger a reload.
        assertEquals(1, moderationApi.bansCalls)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeModerationApi(
    private val bansResults: List<ApiResult<List<BannedUser>>>,
    private val unbanResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : ModerationApi {
    // Single-result convenience for the read-only tests (one bans() result, default-OK unban).
    constructor(
        result: ApiResult<List<BannedUser>>
    ) : this(bansResults = listOf(result))

    var bansCalls: Int = 0
        private set

    val unbanCalls: MutableList<Pair<String, String>> = mutableListOf()

    override suspend fun bans(channelId: String): ApiResult<List<BannedUser>> {
        // Walk through the configured sequence; the last entry repeats once the script runs out.
        val index: Int = minOf(bansCalls, bansResults.lastIndex)
        bansCalls += 1
        return bansResults[index]
    }

    override suspend fun unban(channelId: String, userId: String): ApiResult<Unit> {
        unbanCalls.add(channelId to userId)
        return unbanResult
    }
}
