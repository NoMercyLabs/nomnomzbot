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
import bot.nomnomz.dashboard.core.network.ChannelBasics
import bot.nomnomz.dashboard.core.network.ChannelPersonality
import bot.nomnomz.dashboard.core.network.ChannelSettingsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.UpdateBasicsBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the "Bot basics" card's state-holder: load resolves the active channel and surfaces the backend's
// real basics; save PUTs the edited body and the card ADOPTS the server's echoed values (not the requested
// ones), so a normalized/rejected write never leaves a wrong value shown.
class BasicsControllerTest {

    @Test
    fun load_resolves_the_channel_and_surfaces_its_basics() = runTest {
        val api =
            FakeBasicsApi(getResult = ApiResult.Ok(ChannelBasics(prefix = "?", locale = "nl", autoJoin = false)))
        val controller = BasicsController(FakeBasicsChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)

        controller.load()

        val ready: BasicsState.Ready = controller.state.value as BasicsState.Ready
        assertEquals("?", ready.loaded.prefix)
        assertEquals("nl", ready.loaded.locale)
        assertEquals(false, ready.loaded.autoJoin)
    }

    @Test
    fun save_puts_the_body_and_adopts_the_echoed_values() = runTest {
        val api =
            FakeBasicsApi(
                getResult = ApiResult.Ok(ChannelBasics(prefix = "!")),
                // The server echoes a normalized prefix — the card must adopt THIS, not the requested one.
                updateResult = ApiResult.Ok(ChannelBasics(prefix = "~", locale = "en", autoJoin = true)),
            )
        val controller = BasicsController(FakeBasicsChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.save(UpdateBasicsBody(prefix = "~", locale = "en", autoJoin = true))

        assertEquals("ch1", api.lastChannelId)
        assertEquals("~", api.lastBody?.prefix)
        val ready: BasicsState.Ready = controller.state.value as BasicsState.Ready
        assertEquals("~", ready.loaded.prefix)
        assertEquals("en", ready.loaded.locale)
        assertTrue(ready.justSaved)
    }

    @Test
    fun a_failed_save_surfaces_the_error_and_keeps_the_prior_values() = runTest {
        val api =
            FakeBasicsApi(
                getResult = ApiResult.Ok(ChannelBasics(prefix = "!")),
                updateResult = ApiResult.Failure(ApiError(400, "VALIDATION_FAILED", "bad prefix")),
            )
        val controller = BasicsController(FakeBasicsChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.save(UpdateBasicsBody(prefix = "a b"))

        val ready: BasicsState.Ready = controller.state.value as BasicsState.Ready
        assertEquals("bad prefix", ready.saveError)
        assertEquals("!", ready.loaded.prefix, "a rejected write must not change the shown value")
    }
}

private class FakeBasicsChannelsApi(private val primary: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = primary

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun channelScopes(channelId: String) = error("stub")

    override suspend fun startChannelBotConnect(channelId: String) = error("stub")

    override suspend fun channelBotStatus(channelId: String) = error("stub")

    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeBasicsApi(
    private val getResult: ApiResult<ChannelBasics>,
    private val updateResult: ApiResult<ChannelBasics> = ApiResult.Ok(ChannelBasics()),
) : ChannelSettingsApi {
    var lastChannelId: String? = null
        private set

    var lastBody: UpdateBasicsBody? = null
        private set

    override suspend fun getPersonality(channelId: String): ApiResult<ChannelPersonality> = error("unused")

    override suspend fun setPersonality(channelId: String, tone: String): ApiResult<ChannelPersonality> =
        error("unused")

    override suspend fun getBasics(channelId: String): ApiResult<ChannelBasics> = getResult

    override suspend fun updateBasics(
        channelId: String,
        body: UpdateBasicsBody,
    ): ApiResult<ChannelBasics> {
        lastChannelId = channelId
        lastBody = body
        return updateResult
    }
}
