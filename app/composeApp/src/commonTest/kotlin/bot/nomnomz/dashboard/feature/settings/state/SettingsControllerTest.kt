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
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Settings page state machine: resolve the active channel, then surface the real stream info — or an
// error if either step fails — and write the editable broadcast metadata back through the combined update. The
// screen is a pure projection of this, so testing it proves the page shows the channel's real stream info (no
// fabricated values), sends exactly the edited fields, and degrades cleanly.
class SettingsControllerTest {

    @Test
    fun load_surfaces_the_channels_stream_info_on_success() = runTest {
        val controller =
            SettingsController(
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

        val state: SettingsState = controller.state.value
        assertTrue(state is SettingsState.Ready)
        val info: StreamInfo = (state as SettingsState.Ready).info
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
            SettingsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeStreamApi(ApiResult.Ok(StreamInfo())),
            )

        controller.load()

        assertTrue(controller.state.value is SettingsState.Error)
    }

    @Test
    fun load_errors_when_the_info_call_fails() = runTest {
        val controller =
            SettingsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeStreamApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is SettingsState.Error)
    }

    @Test
    fun save_sends_the_edited_metadata_and_reflects_the_backend_echoed_info() = runTest {
        // The backend resolves the game name → Twitch game id and echoes the canonicalized saved info back; the
        // controller adopts THAT, not the values the user typed. Here the echo canonicalizes the game name
        // casing and reports the live viewer count, proving the controller surfaces the persisted server values
        // rather than the request.
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
            SettingsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), streamApi)
        controller.load()

        controller.save(
            title = "New title",
            gameName = "Hollow Knight Silksong",
            tags = listOf("Speedrun", "English"),
        )

        // Exactly the edited fields are sent for the resolved channel as a StreamInfoUpdate.
        assertEquals("ch1", streamApi.lastUpdateChannelId)
        assertEquals(
            StreamInfoUpdate(
                title = "New title",
                gameName = "Hollow Knight Silksong",
                tags = listOf("Speedrun", "English"),
            ),
            streamApi.lastUpdate,
        )

        // State now holds the backend echo, flags the save, and carries no error.
        val state: SettingsState = controller.state.value
        assertTrue(state is SettingsState.Ready)
        val ready: SettingsState.Ready = state as SettingsState.Ready
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
            SettingsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), streamApi)
        controller.load()

        controller.save(title = "New title", gameName = "Hollow Knight", tags = listOf("English"))

        // The page stays Ready on the loaded info (no data loss) but surfaces the save error, save cleared.
        val state: SettingsState = controller.state.value
        assertTrue(state is SettingsState.Ready)
        val ready: SettingsState.Ready = state as SettingsState.Ready
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
}
