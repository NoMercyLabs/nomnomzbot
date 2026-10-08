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
import androidx.compose.ui.test.SemanticsNodeInteraction
import androidx.compose.ui.test.hasScrollToNodeAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.onFirst
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performScrollTo
import androidx.compose.ui.test.performScrollToNode
import androidx.compose.ui.test.performTextInput
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

// Shared defect SH-1 on the two inline song request forms: add to queue and block a track. A failure keeps the
// typed text and shows the reason next to the button with no toast; a success sends the call and clears the form.
@OptIn(ExperimentalTestApi::class)
class SongRequestInlineFormsTest {
    private val reason: String = "The server said no."
    private val failure: ApiError = ApiError(500, "SERVER_ERROR", reason)

    @Test
    fun a_failed_add_keeps_the_query_and_shows_the_reason_with_no_toast() = runComposeUiTest {
        val api: InlineSrqApi = InlineSrqApi(addFailure = failure)
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        type(0, "Song or artist name", "Despacito")
        type(1, "Requested by", "viewer1")
        press("Add")

        onNodeWithText("Despacito").assertExists()
        onNodeWithText(reason).assertExists()
        assertEquals(emptyList(), api.added)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_add_sends_the_query_and_clears_the_form() = runComposeUiTest {
        val api: InlineSrqApi = InlineSrqApi()
        open(api, CountingFeedback())
        type(0, "Song or artist name", "Despacito")
        type(1, "Requested by", "viewer1")
        press("Add")

        assertEquals(listOf("Despacito"), api.added)
        onNodeWithText("Despacito").assertDoesNotExist()
    }

    @Test
    fun a_failed_block_keeps_the_typed_fields_and_shows_the_reason_with_no_toast() = runComposeUiTest {
        val api: InlineSrqApi = InlineSrqApi(blockFailure = failure)
        val feedback: CountingFeedback = CountingFeedback()
        open(api, feedback)
        type(0, "Track URI", "spotify:track:abc")
        type(1, "Title", "Bad Song")
        type(2, "Reason (optional)", "too loud")
        press("Block")

        onNodeWithText("too loud").assertExists()
        onNodeWithText("Bad Song").assertExists()
        onNodeWithText(reason).assertExists()
        assertEquals(emptyList(), api.blocked)
        assertEquals(0, feedback.errors)
    }

    @Test
    fun a_successful_block_sends_the_track_and_clears_the_form() = runComposeUiTest {
        val api: InlineSrqApi = InlineSrqApi()
        open(api, CountingFeedback())
        type(0, "Track URI", "spotify:track:abc")
        type(1, "Title", "Bad Song")
        type(2, "Reason (optional)", "too loud")
        press("Block")

        assertEquals(listOf("spotify:track:abc"), api.blocked)
        onNodeWithText("too loud").assertDoesNotExist()
    }

    // The label is a sibling Text of the field, so the field is found by its order among the text inputs the lazy page has composed
    // (the add form's two when scrolled to the top, the block form's three once the page is scrolled down to it).
    private fun ComposeUiTest.type(index: Int, label: String, text: String) {
        scrollToNode(hasText(label))
        val field: SemanticsNodeInteraction = onAllNodes(hasSetTextAction())[index]
        field.performTextInput(text)
        waitForIdle()
    }

    private fun ComposeUiTest.press(label: String) {
        scrollToNode(hasText(label) and androidx.compose.ui.test.hasClickAction())
        onNode(hasText(label) and androidx.compose.ui.test.hasClickAction()).performScrollTo().performClick()
        waitForIdle()
    }

    private fun ComposeUiTest.scrollToNode(matcher: androidx.compose.ui.test.SemanticsMatcher) {
        onAllNodes(hasScrollToNodeAction()).onFirst().performScrollToNode(matcher)
        waitForIdle()
    }

    private fun ComposeUiTest.open(api: SongRequestsApi, feedback: CountingFeedback) {
        val controller: SongRequestsController = SongRequestsController(InlineChannelsApi(), api, feedback = feedback)
        runBlocking { controller.load() }
        setContent {
            withInlineLifecycle {
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
private fun withInlineLifecycle(content: @Composable () -> Unit) {
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

private class InlineSrqApi(
    private val addFailure: ApiError? = null,
    private val blockFailure: ApiError? = null,
) : SongRequestsApi {
    val added: MutableList<String> = mutableListOf()
    val blocked: MutableList<String> = mutableListOf()

    override suspend fun queue(channelId: String): ApiResult<List<QueuedSong>> =
        ApiResult.Ok(listOf(QueuedSong(position = 1, trackName = "Song A", artist = "Artist")))

    override suspend fun skip(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun pause(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun resume(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun remove(channelId: String, position: Int): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun addToQueue(channelId: String, body: MusicSongRequestBody): ApiResult<Unit> {
        addFailure?.let { return ApiResult.Failure(it) }
        added.add(body.query)
        return ApiResult.Ok(Unit)
    }

    override suspend fun promote(channelId: String, position: Int): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun ban(channelId: String, position: Int): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun config(channelId: String): ApiResult<MusicConfig> = ApiResult.Ok(MusicConfig())

    override suspend fun updateConfig(channelId: String, body: UpdateMusicConfigBody): ApiResult<MusicConfig> =
        ApiResult.Ok(MusicConfig())

    override suspend fun playlists(channelId: String, offset: Int, limit: Int): ApiResult<List<MusicPlaylist>> =
        ApiResult.Ok(emptyList())

    override suspend fun srPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("old-token")

    override suspend fun rotateSrPageToken(channelId: String): ApiResult<String> = ApiResult.Ok("new-token")

    override suspend fun blockedTracks(channelId: String, page: Int, take: Int): ApiResult<BlockedTrackPage> =
        ApiResult.Ok(BlockedTrackPage())

    override suspend fun blockTrack(channelId: String, body: BlockTrackBody): ApiResult<BlockedTrack> {
        blockFailure?.let { return ApiResult.Failure(it) }
        blocked.add(body.trackUri)
        return ApiResult.Ok(BlockedTrack())
    }

    override suspend fun unblockTrack(channelId: String, blockedTrackId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)
}

private class InlineChannelsApi : ChannelsApi {
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
