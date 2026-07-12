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
import bot.nomnomz.dashboard.core.network.ChannelPersonality
import bot.nomnomz.dashboard.core.network.ChannelSettingsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the "Bot personality" card state machine: resolve the active channel, surface its real current tone +
// selectable set, and persist a selection by ADOPTING the backend's echoed tone (never the requested string
// blindly) — degrading to an error state when channel resolution or either call fails. The screen is a pure
// projection of this, so testing it proves the card shows the channel's real tone and writes the chosen one.
class PersonalityControllerTest {

    @Test
    fun load_surfaces_the_channels_current_tone_and_selectable_set() = runTest {
        val controller =
            PersonalityController(
                FakePersonalityChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakePersonalityApi(
                    ApiResult.Ok(
                        ChannelPersonality(
                            personality = "sassy",
                            available = listOf("informative", "friendly", "sassy", "hype", "chill"),
                        )
                    )
                ),
            )

        controller.load()

        val state: PersonalityState = controller.state.value
        assertTrue(state is PersonalityState.Ready)
        val ready: PersonalityState.Ready = state as PersonalityState.Ready
        assertEquals("sassy", ready.current)
        assertEquals(listOf("informative", "friendly", "sassy", "hype", "chill"), ready.available)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            PersonalityController(
                FakePersonalityChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakePersonalityApi(ApiResult.Ok(ChannelPersonality())),
            )

        controller.load()

        assertTrue(controller.state.value is PersonalityState.Error)
    }

    @Test
    fun load_errors_when_the_personality_call_fails() = runTest {
        val controller =
            PersonalityController(
                FakePersonalityChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakePersonalityApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is PersonalityState.Error)
    }

    @Test
    fun select_sends_the_tone_and_adopts_the_backend_echo() = runTest {
        // The backend normalizes + echoes the saved tone; the controller adopts THAT, not the requested string.
        // Sending "SASSY" (upper) and getting back "sassy" (canonical) proves the controller trusts the server.
        val api =
            FakePersonalityApi(
                ApiResult.Ok(ChannelPersonality("informative", listOf("informative", "sassy"))),
                setResult = ApiResult.Ok(ChannelPersonality("sassy", listOf("informative", "sassy"))),
            )
        val controller =
            PersonalityController(FakePersonalityChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.select("SASSY")

        // Exactly the requested tone is sent for the resolved channel.
        assertEquals("ch1", api.lastSetChannelId)
        assertEquals("SASSY", api.lastSetTone)

        // State now holds the backend echo (canonical "sassy"), flags the save, and carries no error.
        val state: PersonalityState = controller.state.value
        assertTrue(state is PersonalityState.Ready)
        val ready: PersonalityState.Ready = state as PersonalityState.Ready
        assertEquals("sassy", ready.current)
        assertTrue(ready.justSaved)
        assertEquals(false, ready.saving)
    }

    @Test
    fun select_failure_surfaces_the_error_and_keeps_the_current_tone() = runTest {
        val api =
            FakePersonalityApi(
                ApiResult.Ok(ChannelPersonality("informative", listOf("informative", "sassy"))),
                setResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Requires Broadcaster.")),
            )
        val controller =
            PersonalityController(FakePersonalityChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), api)
        controller.load()

        controller.select("sassy")

        // Stays Ready on the unchanged tone (no wrong tone shown) and surfaces the rejection.
        val state: PersonalityState = controller.state.value
        assertTrue(state is PersonalityState.Ready)
        val ready: PersonalityState.Ready = state as PersonalityState.Ready
        assertEquals("informative", ready.current)
        assertEquals("Requires Broadcaster.", ready.saveError)
        assertEquals(false, ready.saving)
    }
}

private class FakePersonalityChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> =
        ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun channelScopes(channelId: String) = error("stub")

    override suspend fun startChannelBotConnect(channelId: String) = error("stub")

    override suspend fun channelBotStatus(channelId: String) = error("stub")

    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakePersonalityApi(
    private val getResult: ApiResult<ChannelPersonality>,
    private val setResult: ApiResult<ChannelPersonality> = ApiResult.Ok(ChannelPersonality()),
) : ChannelSettingsApi {
    var lastSetChannelId: String? = null
        private set

    var lastSetTone: String? = null
        private set

    override suspend fun getPersonality(channelId: String): ApiResult<ChannelPersonality> = getResult

    override suspend fun setPersonality(
        channelId: String,
        tone: String,
    ): ApiResult<ChannelPersonality> {
        lastSetChannelId = channelId
        lastSetTone = tone
        return setResult
    }
}
