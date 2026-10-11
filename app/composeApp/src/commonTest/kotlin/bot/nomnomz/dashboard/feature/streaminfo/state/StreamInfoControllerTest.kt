// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.streaminfo.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Stream page Info and presets tab state machine: resolve the active channel, surface the real stream
// info (or an error), and write the editable broadcast metadata back through the combined update. These tests
// moved here from the Settings controller test when the stream info editor moved from Settings to Stream.
class StreamInfoControllerTest {

    @Test
    fun load_surfaces_the_channels_stream_info_on_success() = runTest {
        val controller =
            StreamInfoController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeStreamApi(
                    ApiResult.Ok(
                        StreamInfo(
                            title = "Speedrunning all night",
                            gameName = "Hollow Knight",
                            tags = listOf("Speedrun", "English"),
                            isLive = true,
                            viewerCount = 42,
                            language = "en",
                        )
                    )
                ),
            )

        controller.load()

        val state: StreamInfoState = controller.state.value
        assertTrue(state is StreamInfoState.Ready)
        val info: StreamInfo = (state as StreamInfoState.Ready).info
        assertEquals("Speedrunning all night", info.title)
        assertEquals("Hollow Knight", info.gameName)
        assertEquals(listOf("Speedrun", "English"), info.tags)
        assertEquals(true, info.isLive)
        assertEquals(42, info.viewerCount)
        assertEquals("en", info.language)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            StreamInfoController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeStreamApi(ApiResult.Ok(StreamInfo())),
            )

        controller.load()

        assertTrue(controller.state.value is StreamInfoState.Error)
    }

    @Test
    fun load_errors_when_the_info_call_fails() = runTest {
        val controller =
            StreamInfoController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeStreamApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is StreamInfoState.Error)
    }

    @Test
    fun save_sends_the_edited_metadata_and_reflects_the_backend_echoed_info() = runTest {
        // The backend resolves the game name to a Twitch game id and echoes the canonicalized saved info back; the
        // controller adopts THAT, not the values the user typed.
        val loaded =
            StreamInfo(
                title = "Old title",
                gameName = "Hollow Knight",
                tags = listOf("English"),
                isLive = false,
                viewerCount = 0,
                language = "en",
            )
        val echoed =
            StreamInfo(
                title = "New title",
                gameName = "Hollow Knight: Silksong",
                tags = listOf("Speedrun", "English"),
                isLive = true,
                viewerCount = 7,
                language = "en",
            )
        val streamApi = FakeStreamApi(ApiResult.Ok(loaded), updateResult = ApiResult.Ok(echoed))
        val controller =
            StreamInfoController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), streamApi)
        controller.load()

        controller.save(
            title = "New title",
            gameName = "Hollow Knight Silksong",
            tags = listOf("Speedrun", "English"),
        )

        assertEquals("ch1", streamApi.lastUpdateChannelId)
        assertEquals(
            StreamInfoUpdate(
                title = "New title",
                gameName = "Hollow Knight Silksong",
                tags = listOf("Speedrun", "English"),
            ),
            streamApi.lastUpdate,
        )

        val state: StreamInfoState = controller.state.value
        assertTrue(state is StreamInfoState.Ready)
        val ready: StreamInfoState.Ready = state as StreamInfoState.Ready
        assertEquals(echoed, ready.info)
        assertTrue(ready.justSaved)
        assertEquals(false, ready.saving)
        assertNull(ready.saveError)
    }

    @Test
    fun save_failure_surfaces_the_error_without_losing_the_loaded_info() = runTest {
        val loaded =
            StreamInfo(
                title = "Old title",
                gameName = "Hollow Knight",
                tags = listOf("English"),
                isLive = true,
                viewerCount = 12,
                language = "en",
            )
        val streamApi =
            FakeStreamApi(
                ApiResult.Ok(loaded),
                updateResult = ApiResult.Failure(ApiError(500, "ERR", "save boom")),
            )
        val controller =
            StreamInfoController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), streamApi)
        controller.load()

        controller.save(title = "New title", gameName = "Hollow Knight", tags = listOf("English"))

        val state: StreamInfoState = controller.state.value
        assertTrue(state is StreamInfoState.Ready)
        val ready: StreamInfoState.Ready = state as StreamInfoState.Ready
        assertEquals(loaded, ready.info)
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
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakeStreamApi(
    private val result: ApiResult<StreamInfo>,
    private val updateResult: ApiResult<StreamInfo> = ApiResult.Ok(StreamInfo()),
) : StreamApi {
    var lastUpdate: StreamInfoUpdate? = null
        private set

    var lastUpdateChannelId: String? = null
        private set

    override suspend fun info(channelId: String): ApiResult<StreamInfo> = result

    override suspend fun update(
        channelId: String,
        update: StreamInfoUpdate,
    ): ApiResult<StreamInfo> {
        lastUpdateChannelId = channelId
        lastUpdate = update
        return updateResult
    }

    override suspend fun searchCategories(channelId: String, query: String) = error("stub")

    override suspend fun searchChannels(channelId: String, query: String) = error("stub")
}
