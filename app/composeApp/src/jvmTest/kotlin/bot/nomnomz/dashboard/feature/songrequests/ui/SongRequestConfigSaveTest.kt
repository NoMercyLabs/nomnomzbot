// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.songrequests.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasScrollToNodeAction
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlockTrackBody
import bot.nomnomz.dashboard.core.network.BlockedTrack
import bot.nomnomz.dashboard.core.network.BlockedTrackPage
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.MusicConfig
import bot.nomnomz.dashboard.core.network.MusicPlaylist
import bot.nomnomz.dashboard.core.network.MusicSongRequestBody
import bot.nomnomz.dashboard.core.network.QueuedSong
import bot.nomnomz.dashboard.core.network.SongRequestsApi
import bot.nomnomz.dashboard.core.network.UpdateMusicConfigBody
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.songrequests.state.SongRequestsController
import bot.nomnomz.dashboard.feature.tts.ui.CountingFeedback
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.runBlocking

// Shared defect SH-1 on the song request settings Save: a failure keeps the edited values and shows the reason next
// to Save with no toast; a success sends the edited values.
@OptIn(ExperimentalTestApi::class)
class SongRequestConfigSaveTest {
    private val reason: String = "The server said no."
    private val failure: ApiError = ApiError(500, "SERVER_ERROR", reason)

    @Test
    fun a_failed_save_shows_the_reason_with_no_toast() = runComposeUiTest {
        val api: ConfigSaveSrqApi = ConfigSaveSrqApi(saveFailure = failure)
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        press("Spotify")
        press("Save")

        onNodeWithText(reason).assertExists()
        assertEquals(0, feedback.errors)
        assertEquals(emptyList(), api.savedProviders)
    }

    @Test
    fun a_failed_save_keeps_the_edit_so_a_retry_sends_it() = runComposeUiTest {
        val api: ConfigSaveSrqApi = ConfigSaveSrqApi(saveFailure = failure)
        open(api, CountingFeedback())
        press("Spotify")
        press("Save")
        api.saveFailure = null
        press("Save")

        assertEquals(listOf<String?>("spotify"), api.savedProviders)
    }

    @Test
    fun a_successful_save_sends_the_edited_provider() = runComposeUiTest {
        val api: ConfigSaveSrqApi = ConfigSaveSrqApi()
        open(api, CountingFeedback())
        press("Spotify")
        onNode(hasText("Spotify") and hasClickAction()).assertIsSelected()
        press("Save")

        assertEquals(listOf<String?>("spotify"), api.savedProviders)
    }

    private fun ComposeUiTest.press(label: String) {
        onAllNodes(hasScrollToNodeAction()).onFirst().performScrollToNode(hasText(label) and hasClickAction())
        waitForIdle()
        onNode(hasText(label) and hasClickAction()).performScrollTo().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()
    }

    private fun ComposeUiTest.open(api: SongRequestsApi, feedback: CountingFeedback) {
        val controller: SongRequestsController = SongRequestsController(ConfigSaveChannelsApi(), api, feedback = feedback)
        runBlocking { controller.load() }
        setContent {
            withConfigSaveLifecycle {
                NomNomzTheme {
                    AppEnvironment("en") {
                        SongRequestsScreen(controller = controller, role = ManagementRole.Broadcaster)
                    }
                }
            }
        }
        waitForIdle()
    }
}

@Composable
private fun withConfigSaveLifecycle(content: @Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}

private class ConfigSaveSrqApi(
    var saveFailure: ApiError? = null,
) : SongRequestsApi {
    val savedProviders: MutableList<String?> = mutableListOf()

    override suspend fun queue(channelId: String): ApiResult<List<QueuedSong>> =
        ApiResult.Ok(listOf(QueuedSong(position = 1, trackName = "Song A", artist = "Artist")))

    override suspend fun skip(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun pause(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun resume(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun remove(channelId: String, position: Int): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun addToQueue(channelId: String, body: MusicSongRequestBody): ApiResult<Unit> {
        return ApiResult.Ok(Unit)
    }

    override suspend fun promote(channelId: String, position: Int): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun ban(channelId: String, position: Int): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun config(channelId: String): ApiResult<MusicConfig> = ApiResult.Ok(MusicConfig())

    override suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig> {
        saveFailure?.let { return ApiResult.Failure(it) }
        savedProviders.add(body.preferredProvider)
        return ApiResult.Ok(MusicConfig())
    }

    override suspend fun playlists(channelId: String, offset: Int, limit: Int): ApiResult<List<MusicPlaylist>> =
        ApiResult.Ok(emptyList())

    override suspend fun srPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("old-token")

    override suspend fun rotateSrPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("new-token")

    override suspend fun blockedTracks(channelId: String, page: Int, take: Int): ApiResult<BlockedTrackPage> =
        ApiResult.Ok(BlockedTrackPage())

    override suspend fun blockTrack(channelId: String, body: BlockTrackBody): ApiResult<BlockedTrack> {
        return ApiResult.Ok(BlockedTrack())
    }

    override suspend fun unblockTrack(channelId: String, blockedTrackId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}

private class ConfigSaveChannelsApi : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))

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
