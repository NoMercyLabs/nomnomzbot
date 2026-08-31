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
import bot.nomnomz.dashboard.core.realtime.HubChannelEvent
import bot.nomnomz.dashboard.core.realtime.HubChatMessage
import bot.nomnomz.dashboard.core.realtime.HubConnectionState
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.MutableStateFlow
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

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun shield_mode_begin_push_marks_the_channel_active_and_end_clears_it() = runTest {
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

        // A real shield-mode-begin push for an UNWATCHED channel is ignored.
        events.emit(HubEvent.ChannelEvent(HubChannelEvent(type = "shield_mode_begin", broadcasterId = "b", userId = "mod-1", userDisplayName = "ModMax", timestamp = "2026-07-18T12:00:00Z")))
        assertTrue((controller.state.value as MultiChatState.Ready).shieldModeActiveChannelIds.isEmpty())

        // The real push for the watched channel toggles the state field on.
        events.emit(HubEvent.ChannelEvent(HubChannelEvent(type = "shield_mode_begin", broadcasterId = "a", userId = "mod-1", userDisplayName = "ModMax", timestamp = "2026-07-18T12:00:01Z")))
        assertEquals(setOf("a"), (controller.state.value as MultiChatState.Ready).shieldModeActiveChannelIds)

        // The matching end push clears it again.
        events.emit(HubEvent.ChannelEvent(HubChannelEvent(type = "shield_mode_end", broadcasterId = "a", userId = "mod-1", userDisplayName = "ModMax", timestamp = "2026-07-18T12:05:00Z")))
        assertTrue((controller.state.value as MultiChatState.Ready).shieldModeActiveChannelIds.isEmpty())
    }

    @Test
    fun send_message_calls_chat_send_with_the_target_channel_and_text() = runTest {
        val chat = FakeMultiChatApi()
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha")))),
                chat,
                joinChannel = {},
                leaveChannel = {},
            )
        controller.load()
        controller.addChannel("a")

        controller.sendMessage("a", "hello chat")

        assertEquals(listOf(Triple("a", "hello chat", "you")), chat.sent)
    }

    @Test
    fun send_message_failure_surfaces_as_action_error_without_touching_the_feed() = runTest {
        val chat = FakeMultiChatApi()
        chat.sendResult = ApiResult.Failure(ApiError(500, "SEND_FAILED", "could not send"))
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha")))),
                chat,
                joinChannel = {},
                leaveChannel = {},
            )
        controller.load()
        controller.addChannel("a")

        controller.sendMessage("a", "hello chat")

        val ready: MultiChatState.Ready = controller.state.value as MultiChatState.Ready
        assertEquals("could not send", ready.actionError)
        assertTrue(ready.messages.isEmpty())
    }

    @Test
    fun timeout_calls_the_moderation_timeout_endpoint_with_the_target_user_and_channel() = runTest {
        val chat = FakeMultiChatApi()
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha")))),
                chat,
                joinChannel = {},
                leaveChannel = {},
            )
        controller.load()
        controller.addChannel("a")

        controller.timeoutUser("a", "user-42", durationSeconds = 300)

        assertEquals(listOf(Triple("a", "user-42", 300)), chat.timedOut)
    }

    @Test
    fun delete_message_calls_the_delete_endpoint_and_drops_the_line_from_the_feed() = runTest {
        val chat = FakeMultiChatApi()
        chat.messagesByChannel["a"] =
            listOf(ChatMessage(id = "m1", channelId = "a", message = "spam", timestamp = "2026-07-18T10:00:00Z"))
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha")))),
                chat,
                joinChannel = {},
                leaveChannel = {},
            )
        controller.load()
        controller.addChannel("a")

        controller.deleteMessage("a", "m1")

        assertEquals(listOf(Pair("a", "m1")), chat.deleted)
        val ready: MultiChatState.Ready = controller.state.value as MultiChatState.Ready
        assertTrue(ready.messages.isEmpty())
    }

    @Test
    fun ban_user_calls_the_ban_endpoint_scoped_to_this_channel() = runTest {
        val chat = FakeMultiChatApi()
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha")))),
                chat,
                joinChannel = {},
                leaveChannel = {},
            )
        controller.load()
        controller.addChannel("a")

        controller.banUser("a", "user-99")

        assertEquals(listOf(Triple("a", "user-99", "this_channel")), chat.banned)
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun reconnect_rejoins_the_previously_watched_channels_not_a_default_set() = runTest {
        val joined: MutableList<String> = mutableListOf()
        val controller =
            MultiChatController(
                FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha"), channel("b", "Beta"), channel("c", "Gamma")))),
                FakeMultiChatApi(),
                joinChannel = { joined.add(it) },
                leaveChannel = {},
            )
        controller.load()
        controller.addChannel("a")
        controller.addChannel("b")
        joined.clear() // only care about what happens ACROSS the reconnect from here on

        val connectionState = MutableStateFlow(HubConnectionState.Connected)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) {
            controller.subscribeToConnectionState(connectionState)
        }

        // Simulate a drop and recovery.
        connectionState.value = HubConnectionState.Reconnecting
        connectionState.value = HubConnectionState.Connected

        // The exact watched set (a, b) is re-subscribed — never channel c, which was never selected.
        assertEquals(listOf("a", "b"), joined)
    }

    @Test
    fun the_watch_list_survives_a_fresh_controller_instance_restoring_from_the_store() = runTest {
        val store = FakeWatchListStore()
        val channelsApi = FakeMultiChannelsApi(ApiResult.Ok(listOf(channel("a", "Alpha"), channel("b", "Beta"))))
        val firstSessionJoined: MutableList<String> = mutableListOf()
        val firstController =
            MultiChatController(
                channelsApi,
                FakeMultiChatApi(),
                joinChannel = { firstSessionJoined.add(it) },
                leaveChannel = {},
                watchListStore = store,
            )
        firstController.load()
        firstController.addChannel("a")

        // A fresh controller instance — simulating an app restart — restores the persisted watch list on load,
        // re-joining it, rather than landing on an empty/default watched set.
        val restoredJoined: MutableList<String> = mutableListOf()
        val restartedController =
            MultiChatController(
                channelsApi,
                FakeMultiChatApi(),
                joinChannel = { restoredJoined.add(it) },
                leaveChannel = {},
                watchListStore = store,
            )
        restartedController.load()

        val ready: MultiChatState.Ready = restartedController.state.value as MultiChatState.Ready
        assertEquals(listOf("a"), ready.watched.map { it.id })
        assertEquals(listOf("a"), restoredJoined)
    }
}

private class FakeWatchListStore : WatchListStore {
    private var saved: List<String> = emptyList()

    override fun read(): List<String> = saved

    override fun write(channelIds: List<String>) {
        saved = channelIds
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

// messages() serves scrollback; send/deleteMessage/timeout/banUser record every call so the composer and
// inline-moderation tests can assert the real API method fired with the right channel/user/text, not merely
// that no exception was thrown.
private class FakeMultiChatApi : ChatApi {
    val messagesByChannel: MutableMap<String, List<ChatMessage>> = mutableMapOf()
    val sent: MutableList<Triple<String, String, String>> = mutableListOf()
    val deleted: MutableList<Pair<String, String>> = mutableListOf()
    val timedOut: MutableList<Triple<String, String, Int>> = mutableListOf()
    val banned: MutableList<Triple<String, String, String>> = mutableListOf()
    var sendResult: ApiResult<Unit> = ApiResult.Ok(Unit)
    var deleteResult: ApiResult<Unit> = ApiResult.Ok(Unit)
    var timeoutResult: ApiResult<Unit> = ApiResult.Ok(Unit)
    var banResult: ApiResult<NetworkBanResult> = ApiResult.Ok(NetworkBanResult())

    override suspend fun messages(channelId: String, limit: Int): ApiResult<List<ChatMessage>> =
        ApiResult.Ok(messagesByChannel[channelId] ?: emptyList())

    override suspend fun emotes(channelId: String): ApiResult<List<ChatEmoteCatalogue>> = ApiResult.Ok(emptyList())

    override suspend fun send(
        channelId: String,
        message: String,
        senderIdentity: String,
        replyToMessageId: String?,
    ): ApiResult<Unit> {
        sent += Triple(channelId, message, senderIdentity)
        return sendResult
    }

    override suspend fun deleteMessage(channelId: String, messageId: String): ApiResult<Unit> {
        deleted += Pair(channelId, messageId)
        return deleteResult
    }

    override suspend fun timeout(channelId: String, userId: String, durationSeconds: Int): ApiResult<Unit> {
        timedOut += Triple(channelId, userId, durationSeconds)
        return timeoutResult
    }

    override suspend fun banUser(
        channelId: String,
        targetTwitchUserId: String,
        scope: String,
        reason: String?,
        durationSeconds: Int?,
    ): ApiResult<NetworkBanResult> {
        banned += Triple(channelId, targetTwitchUserId, scope)
        return banResult
    }

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
