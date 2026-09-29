// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BillingEntitlement
import bot.nomnomz.dashboard.core.network.ChannelBotStatusDetail
import bot.nomnomz.dashboard.core.network.ChannelScopesResponse
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.OAuthStart
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertIs
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the white-label bot card reads the channel's plan: a plan without its own bot lands the card in a state that
// explains instead of offering Connect, a plan with one offers it, and an unreadable plan never hides a bot the
// server might allow. The plan is read for the channel the card resolved, not some other one.
class ChannelBotControllerTest {

    @Test
    fun a_plan_without_an_own_bot_marks_the_card_as_not_allowed() = runTest {
        val asked: MutableList<String> = mutableListOf()
        val controller = ChannelBotController(BotChannels()) { channelId: String ->
            asked += channelId
            ApiResult.Ok(BillingEntitlement(tierKey = "base", allowsCustomBotName = false))
        }

        controller.load()

        val ready: ChannelBotState.Ready = assertIs(controller.state.value)
        assertFalse(ready.ownBotAllowed)
        assertFalse(ready.connected)
        assertEquals(listOf("ch1"), asked)
    }

    @Test
    fun a_plan_with_an_own_bot_offers_connect() = runTest {
        val controller = ChannelBotController(BotChannels()) {
            ApiResult.Ok(BillingEntitlement(tierKey = "pro", allowsCustomBotName = true))
        }

        controller.load()

        val ready: ChannelBotState.Ready = assertIs(controller.state.value)
        assertTrue(ready.ownBotAllowed)
    }

    @Test
    fun an_unreadable_plan_keeps_connect_offered_because_the_server_decides() = runTest {
        val controller = ChannelBotController(BotChannels()) {
            ApiResult.Failure(ApiError(status = 403, code = "FORBIDDEN", message = "no billing read"))
        }

        controller.load()

        val ready: ChannelBotState.Ready = assertIs(controller.state.value)
        assertTrue(ready.ownBotAllowed)
    }
}

/** A channel `ch1` with no own bot connected. */
private class BotChannels : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))
    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())
    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String): ApiResult<ChannelScopesResponse> =
        ApiResult.Ok(ChannelScopesResponse())
    override suspend fun startChannelBotConnect(channelId: String): ApiResult<OAuthStart> =
        ApiResult.Ok(OAuthStart(authorizeUrl = "https://id.twitch.tv/oauth2/authorize", state = "nonce"))
    override suspend fun channelBotStatus(channelId: String): ApiResult<ChannelBotStatusDetail> =
        ApiResult.Ok(ChannelBotStatusDetail(connected = false))
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}
