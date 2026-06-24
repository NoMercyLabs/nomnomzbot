// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the TTS page state machine the screen renders read-only: resolve the active channel, then surface the
// real TTS configuration — or an error if either step fails. The screen is a pure projection of this, so testing
// it proves the page shows the channel's real config (no fabricated values) and degrades cleanly.
class TtsControllerTest {

    @Test
    fun load_surfaces_the_channels_tts_config_on_success() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTtsApi(
                    ApiResult.Ok(
                        TtsConfig(
                            isEnabled = true,
                            defaultVoiceId = "en-US-Brian",
                            maxLength = 200,
                            minPermission = "subscribers",
                            skipBotMessages = true,
                            readUsernames = false,
                        )
                    )
                ),
            )

        controller.load()

        val state: TtsState = controller.state.value
        assertTrue(state is TtsState.Ready)
        val config: TtsConfig = (state as TtsState.Ready).config
        assertEquals(true, config.isEnabled)
        assertEquals("en-US-Brian", config.defaultVoiceId)
        assertEquals(200, config.maxLength)
        assertEquals("subscribers", config.minPermission)
        assertEquals(true, config.skipBotMessages)
        assertEquals(false, config.readUsernames)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeTtsApi(ApiResult.Ok(TtsConfig())),
            )

        controller.load()

        assertTrue(controller.state.value is TtsState.Error)
    }

    @Test
    fun load_errors_when_the_config_call_fails() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTtsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is TtsState.Error)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result
}

private class FakeTtsApi(private val result: ApiResult<TtsConfig>) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> = result
}
