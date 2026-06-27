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
import bot.nomnomz.dashboard.core.network.TtsConfigUpdate
import bot.nomnomz.dashboard.core.network.TtsTestRequest
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsVoice
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
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
    fun load_surfaces_the_available_voices() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTtsApi(
                    result = ApiResult.Ok(TtsConfig(defaultVoiceId = "en-US-Brian")),
                    voicesResult =
                        ApiResult.Ok(
                            listOf(
                                TtsVoice(
                                    id = "en-US-Brian",
                                    displayName = "Brian",
                                    locale = "en-US",
                                    provider = "azure",
                                )
                            )
                        ),
                ),
            )

        controller.load()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(1, ready.voices.size)
        assertEquals("Brian", ready.voices.first().displayName)
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

    @Test
    fun save_sends_the_edit_and_reflects_the_backend_echoed_config() = runTest {
        // The backend canonicalizes and echoes the saved config back; the controller adopts THAT, not the
        // value the user typed. Here the echo flips readUsernames on and clamps maxLength, proving the
        // controller surfaces the persisted server values rather than the request.
        val loaded =
            TtsConfig(
                isEnabled = false,
                defaultVoiceId = "en-US-Brian",
                maxLength = 200,
                minPermission = "everyone",
                skipBotMessages = false,
                readUsernames = false,
            )
        val echoed =
            TtsConfig(
                isEnabled = true,
                defaultVoiceId = "en-GB-Sonia",
                maxLength = 300,
                minPermission = "subscribers",
                skipBotMessages = true,
                readUsernames = true,
            )
        val ttsApi = FakeTtsApi(ApiResult.Ok(loaded), updateResult = ApiResult.Ok(echoed))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        val edited: TtsConfig = loaded.copy(isEnabled = true, minPermission = "subscribers")
        controller.save(edited)

        // The whole config is sent for the resolved channel as a TtsConfigUpdate carrying the edited values.
        assertEquals("ch1", ttsApi.lastUpdateChannelId)
        assertEquals(
            TtsConfigUpdate(
                isEnabled = true,
                defaultVoiceId = "en-US-Brian",
                maxLength = 200,
                minPermission = "subscribers",
                skipBotMessages = false,
                readUsernames = false,
            ),
            ttsApi.lastUpdate,
        )

        // State now holds the backend echo, flags the save, and carries no error.
        val state: TtsState = controller.state.value
        assertTrue(state is TtsState.Ready)
        val ready: TtsState.Ready = state as TtsState.Ready
        assertEquals(echoed, ready.config)
        assertTrue(ready.justSaved)
        assertEquals(false, ready.saving)
        assertNull(ready.saveError)
    }

    @Test
    fun save_failure_surfaces_the_error_without_losing_the_loaded_config() = runTest {
        val loaded =
            TtsConfig(
                isEnabled = true,
                defaultVoiceId = "en-US-Brian",
                maxLength = 200,
                minPermission = "everyone",
                skipBotMessages = false,
                readUsernames = false,
            )
        val ttsApi =
            FakeTtsApi(
                ApiResult.Ok(loaded),
                updateResult = ApiResult.Failure(ApiError(500, "ERR", "save boom")),
            )
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.save(loaded.copy(maxLength = 50))

        // The page stays Ready on the loaded config (no data loss) but surfaces the save error, save cleared.
        val state: TtsState = controller.state.value
        assertTrue(state is TtsState.Ready)
        val ready: TtsState.Ready = state as TtsState.Ready
        assertEquals(loaded, ready.config)
        assertEquals("save boom", ready.saveError)
        assertEquals(false, ready.saving)
        assertEquals(false, ready.justSaved)
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
}

private class FakeTtsApi(
    private val result: ApiResult<TtsConfig>,
    private val updateResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig()),
    private val voicesResult: ApiResult<List<TtsVoice>> = ApiResult.Ok(emptyList()),
) : TtsApi {
    var lastUpdate: TtsConfigUpdate? = null
        private set

    var lastUpdateChannelId: String? = null
        private set

    override suspend fun config(channelId: String): ApiResult<TtsConfig> = result

    override suspend fun updateConfig(
        channelId: String,
        update: TtsConfigUpdate,
    ): ApiResult<TtsConfig> {
        lastUpdateChannelId = channelId
        lastUpdate = update
        return updateResult
    }

    override suspend fun voices(channelId: String): ApiResult<List<TtsVoice>> = voicesResult

    override suspend fun testSpeak(channelId: String, request: TtsTestRequest): ApiResult<TtsTestResult> =
        ApiResult.Ok(TtsTestResult())
}
