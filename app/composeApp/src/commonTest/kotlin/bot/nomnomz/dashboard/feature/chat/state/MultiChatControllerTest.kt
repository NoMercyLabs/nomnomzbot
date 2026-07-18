// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.state

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatApi
import bot.nomnomz.dashboard.core.network.ChatEmoteCatalogue
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.network.ChatSettings
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.NetworkBanResult
import bot.nomnomz.dashboard.core.realtime.HubChatMessage
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest

// Proves the multi-watch state machine the screen renders: list the watchable channels, add/remove a channel
// (joining/leaving its hub group + merging/dropping its scrollback), and route live hub pushes into the merged
// feed ONLY for channels currently watched — the consequences of the actions, not merely that a call happened.
class MultiChatControllerTest {

    private fun channel(id: String, name: String): ChannelSummary =
        ChannelSummary(id = id, login = name.lowercase(), displayName = name)

    @Test
    fun load_lists_the_watchable_channels_with_no_watched_yet() = runTest {
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha"), channel("b", "Beta")))),
                FakeMultiChatApi(),
                joinChannel = {},
                leaveChannel = {},
            )

        controller.load()

        val state: MultiChatState = controller.state.value
        assertTrue(state is MultiChatState.Ready)
        assertEquals(listOf("a", "b"), (state as MultiChatState.Ready).available.map { it.id })
        assertTrue(state.watched.isEmpty())
        assertTrue(state.messages.isEmpty())
    }

    @Test
    fun add_channel_joins_its_group_and_merges_its_scrollback() = runTest {
        val joined: MutableList<String> = mutableListOf()
        val chat = FakeMultiChatApi()
        chat.messagesByChannel["a"] =
            listOf(
                ChatMessage(id = "m1", channelId = "a", message = "first", timestamp = "2026-07-18T10:00:00Z"),
                ChatMessage(id = "m2", channelId = "a", message = "second", timestamp = "2026-07-18T10:01:00Z"),
            )
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha")))),
                chat,
                joinChannel = { joined.add(it) },
                leaveChannel = {},
            )
        controller.load()

        controller.addChannel("a")

        assertEquals(listOf("a"), joined)
        val ready: MultiChatState.Ready = controller.state.value as MultiChatState.Ready
        assertEquals(listOf("a"), ready.watched.map { it.id })
        // The scrollback landed in the merged feed, ordered by timestamp.
        assertEquals(listOf("m1", "m2"), ready.messages.map { it.id })
    }

    @Test
    fun remove_channel_leaves_its_group_and_drops_only_its_lines() = runTest {
        val left: MutableList<String> = mutableListOf()
        val chat = FakeMultiChatApi()
        chat.messagesByChannel["a"] =
            listOf(ChatMessage(id = "a1", channelId = "a", message = "a-line", timestamp = "2026-07-18T10:00:00Z"))
        chat.messagesByChannel["b"] =
            listOf(ChatMessage(id = "b1", channelId = "b", message = "b-line", timestamp = "2026-07-18T10:00:30Z"))
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha"), channel("b", "Beta")))),
                chat,
                joinChannel = {},
                leaveChannel = { left.add(it) },
            )
        controller.load()
        controller.addChannel("a")
        controller.addChannel("b")

        controller.removeChannel("a")

        assertEquals(listOf("a"), left)
        val ready: MultiChatState.Ready = controller.state.value as MultiChatState.Ready
        // Only channel b remains watched, and only its line survives in the feed.
        assertEquals(listOf("b"), ready.watched.map { it.id })
        assertEquals(listOf("b1"), ready.messages.map { it.id })
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun hub_appends_only_watched_channels_and_dedupes_by_id() = runTest {
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha"), channel("b", "Beta")))),
                FakeMultiChatApi(),
                joinChannel = {},
                leaveChannel = {},
            )
        controller.load()
        controller.addChannel("a") // watch channel a only

        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        // A line for the watched channel appears; a line for an UNWATCHED channel is ignored; a redelivered id is
        // suppressed.
        events.emit(HubEvent.ChatMessage(HubChatMessage(id = "l1", channelId = "a", message = "hi", timestamp = "2026-07-18T11:00:00Z")))
        events.emit(HubEvent.ChatMessage(HubChatMessage(id = "b9", channelId = "b", message = "nope", timestamp = "2026-07-18T11:00:01Z")))
        events.emit(HubEvent.ChatMessage(HubChatMessage(id = "l1", channelId = "a", message = "hi", timestamp = "2026-07-18T11:00:00Z")))

        val ready: MultiChatState.Ready = controller.state.value as MultiChatState.Ready
        assertEquals(listOf("l1"), ready.messages.map { it.id })
    }
}

private class FakeMultiChannelsApi(private val listResult: ApiResult<List<ChannelSummary>>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> =
        ApiResult.Failure(ApiError(0, "UNUSED", "not used here"))

    override suspend fun list(): ApiResult<List<ChannelSummary>> = listResult

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

// Only messages() matters here; every mutating action is unused by this read-only monitoring surface.
private class FakeMultiChatApi : ChatApi {
    val messagesByChannel: MutableMap<String, List<ChatMessage>> = mutableMapOf()

    override suspend fun messages(channelId: String, limit: Int): ApiResult<List<ChatMessage>> =
        ApiResult.Ok(messagesByChannel[channelId] ?: emptyList())

    override suspend fun emotes(channelId: String): ApiResult<List<ChatEmoteCatalogue>> = ApiResult.Ok(emptyList())

    override suspend fun send(
        channelId: String,
        message: String,
        senderIdentity: String,
        replyToMessageId: String?,
    ): ApiResult<Unit> = error("unused")

    override suspend fun deleteMessage(channelId: String, messageId: String): ApiResult<Unit> = error("unused")

    override suspend fun timeout(channelId: String, userId: String, durationSeconds: Int): ApiResult<Unit> =
        error("unused")

    override suspend fun banUser(
        channelId: String,
        targetTwitchUserId: String,
        scope: String,
        reason: String?,
        durationSeconds: Int?,
    ): ApiResult<NetworkBanResult> = error("unused")

    override suspend fun fileReport(
        channelId: String,
        targetTwitchUserId: String,
        targetUsername: String,
        targetDisplayName: String?,
        reason: String,
    ): ApiResult<Unit> = error("unused")

    override suspend fun settings(channelId: String): ApiResult<ChatSettings> = error("unused")

    override suspend fun updateSettings(channelId: String, settings: ChatSettings): ApiResult<ChatSettings> =
        error("unused")

    override suspend fun announce(channelId: String, message: String, color: String): ApiResult<Unit> =
        error("unused")
}
