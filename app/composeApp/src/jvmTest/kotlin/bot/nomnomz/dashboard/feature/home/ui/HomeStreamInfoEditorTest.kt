// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.home.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextReplacement
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.StreamApi
import bot.nomnomz.dashboard.core.network.StreamInfo
import bot.nomnomz.dashboard.core.network.StreamInfoUpdate
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.streaminfo.state.StreamInfoController
import bot.nomnomz.dashboard.feature.streaminfo.ui.StreamInfoScreen
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// S-IA-STREAM part B: the Home stream info dialog and the Stream page tab edit the stream with ONE editor over ONE
// StreamInfoController, so a title typed in either place reaches the same channel stream write.
@OptIn(ExperimentalTestApi::class)
class HomeStreamInfoEditorTest {
    private val info: StreamInfo = StreamInfo(title = "Old title", gameName = "Old game", tags = listOf("english"))

    @Test
    fun saving_from_the_home_dialog_sends_the_typed_title_and_reports_the_save() = runComposeUiTest {
        val streamApi: RecordingStreamApi = RecordingStreamApi(info)
        val controller: StreamInfoController = StreamInfoController(OneChannelApi, streamApi)
        var savedCount: Int = 0
        setContent {
            EnglishContent {
                HomeStreamInfoDialog(controller = controller, onSaved = { savedCount += 1 }, onDismiss = {})
            }
        }
        waitUntil { onAllNodes(hasSetTextAction()).fetchSemanticsNodes().isNotEmpty() }
        onAllNodes(hasSetTextAction())[0].performTextReplacement("New title")
        onNodeWithText("Save").performClick()
        waitUntil { savedCount == 1 }

        assertEquals("ch1", streamApi.lastChannelId)
        assertEquals(StreamInfoUpdate(title = "New title", gameName = "Old game", tags = listOf("english")), streamApi.lastUpdate)
        onNodeWithText("Saved").assertExists()
    }

    @Test
    fun a_refused_save_keeps_the_dialog_open_with_the_typed_title_and_the_reason() = runComposeUiTest {
        val streamApi: RecordingStreamApi =
            RecordingStreamApi(info, ApiResult.Failure(ApiError(status = 400, code = null, message = "Twitch refused the title.")))
        val controller: StreamInfoController = StreamInfoController(OneChannelApi, streamApi)
        var savedCount: Int = 0
        setContent {
            EnglishContent {
                HomeStreamInfoDialog(controller = controller, onSaved = { savedCount += 1 }, onDismiss = {})
            }
        }
        waitUntil { onAllNodes(hasSetTextAction()).fetchSemanticsNodes().isNotEmpty() }
        onAllNodes(hasSetTextAction())[0].performTextReplacement("Bad title")
        onNodeWithText("Save").performClick()
        waitUntil { streamApi.lastUpdate != null }
        waitForIdle()

        assertEquals("Bad title", streamApi.lastUpdate?.title)
        assertEquals(0, savedCount)
        onNodeWithText("Bad title").assertExists()
        onNodeWithText("Couldn't save your stream info: Twitch refused the title.").assertExists()
    }

    @Test
    fun saving_from_the_stream_page_sends_the_same_update() = runComposeUiTest {
        val streamApi: RecordingStreamApi = RecordingStreamApi(info)
        val controller: StreamInfoController = StreamInfoController(OneChannelApi, streamApi)
        setContent {
            EnglishContent { StreamInfoScreen(controller = controller, role = ManagementRole.Broadcaster) }
        }
        waitUntil { onAllNodes(hasSetTextAction()).fetchSemanticsNodes().isNotEmpty() }
        onAllNodes(hasSetTextAction())[0].performTextReplacement("New title")
        onNodeWithText("Save").performClick()
        waitUntil { streamApi.lastUpdate != null }

        assertEquals("ch1", streamApi.lastChannelId)
        assertEquals(StreamInfoUpdate(title = "New title", gameName = "Old game", tags = listOf("english")), streamApi.lastUpdate)
    }

    @Test
    fun nothing_is_sent_before_save_is_pressed() = runComposeUiTest {
        val streamApi: RecordingStreamApi = RecordingStreamApi(info)
        val controller: StreamInfoController = StreamInfoController(OneChannelApi, streamApi)
        setContent {
            EnglishContent {
                var open: Boolean by remember { mutableStateOf(true) }
                if (open) HomeStreamInfoDialog(controller = controller, onSaved = {}, onDismiss = { open = false })
            }
        }
        waitUntil { onAllNodes(hasSetTextAction()).fetchSemanticsNodes().isNotEmpty() }
        onAllNodes(hasSetTextAction())[0].performTextReplacement("Typed but not saved")
        onNodeWithText("Close").performClick()
        waitForIdle()

        assertNull(streamApi.lastUpdate)
    }
}

// The screens collect the controller flow with collectAsStateWithLifecycle(), which needs a LocalLifecycleOwner.
@Composable
private fun EnglishContent(content: @Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    CompositionLocalProvider(LocalLifecycleOwner provides owner) {
        AppEnvironment(tag = "en") { NomNomzTheme { content() } }
    }
}

private object OneChannelApi : ChannelsApi {
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

private class RecordingStreamApi(
    private val loaded: StreamInfo,
    private val updateResult: ApiResult<StreamInfo>? = null,
) : StreamApi {
    var lastChannelId: String? = null
        private set
    var lastUpdate: StreamInfoUpdate? = null
        private set

    override suspend fun info(channelId: String): ApiResult<StreamInfo> = ApiResult.Ok(loaded)

    override suspend fun update(channelId: String, update: StreamInfoUpdate): ApiResult<StreamInfo> {
        lastChannelId = channelId
        lastUpdate = update
        return updateResult ?: ApiResult.Ok(loaded.copy(title = update.title))
    }

    override suspend fun searchCategories(channelId: String, query: String) = error("stub")

    override suspend fun searchChannels(channelId: String, query: String) = error("stub")
}
