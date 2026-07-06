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
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.ChatApi
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.network.ChatSettings
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the Chat page state machine the screen renders: resolve the active channel, surface the real recent
// chat (empty as Empty, a failure of either step as Error), and act on it — send a line as the bot, delete a
// message, timeout a chatter. Each action must hit the right backend route with the resolved channel, reload
// on success so the feed reflects the backend's truth, and surface a failure over the intact feed. The screen
// is a pure projection of this, so testing it proves the page acts on real data and degrades cleanly.
class ChatControllerTest {

    @Test
    fun load_surfaces_the_recent_chat_on_success() = runTest {
        val controller =
            ChatController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeChatApi(
                    ApiResult.Ok(
                        listOf(
                            ChatMessage(id = "m1", userId = "u1", displayName = "Stoney_Eagle", message = "hey"),
                            ChatMessage(id = "m2", userId = "u2", displayName = "Viewer Two", message = "yo"),
                        )
                    )
                ),
            )

        controller.load()

        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        val messages: List<ChatMessage> = (state as ChatState.Ready).messages
        assertEquals(2, messages.size)
        assertEquals("m1", messages[0].id)
        assertEquals("hey", messages[0].message)
        assertEquals("Viewer Two", messages[1].displayName)
        assertNull(state.actionError)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            ChatController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeChatApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Error)
        assertEquals("none onboarded", (state as ChatState.Error).detail)
    }

    @Test
    fun load_errors_when_the_messages_call_fails() = runTest {
        val controller =
            ChatController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeChatApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Error)
        assertEquals("boom", (state as ChatState.Error).detail)
    }

    @Test
    fun load_is_empty_when_the_channel_has_no_chat() = runTest {
        val controller =
            ChatController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeChatApi(ApiResult.Ok(emptyList())),
            )

        controller.load()

        assertTrue(controller.state.value is ChatState.Empty)
    }

    @Test
    fun send_posts_the_trimmed_message_without_reloading_the_feed() = runTest {
        val first = ChatMessage(id = "m1", userId = "u1", displayName = "Viewer", message = "hi")
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(first)))
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.send("  welcome!  ")

        // The send hit the send route with the resolved channel and the TRIMMED message.
        assertEquals(listOf("ch1" to "welcome!"), chatApi.sendCalls)
        // No reload after the send: the sent line comes back over the live hub (EventSub echoes it), so reloading
        // here would race persistence and clobber the hub-appended line — the "one message late" bug. Only the
        // initial load fetched the feed.
        assertEquals(1, chatApi.messagesCalls)
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        assertEquals(listOf("m1"), (state as ChatState.Ready).messages.map { it.id })
        assertNull(state.actionError)
    }

    @Test
    fun send_ignores_a_blank_draft() = runTest {
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(ChatMessage(id = "m1", message = "hi"))))
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.send("   ")

        // Nothing was sent and no reload happened — a blank draft is a no-op.
        assertTrue(chatApi.sendCalls.isEmpty())
        assertEquals(1, chatApi.messagesCalls)
    }

    @Test
    fun send_surfaces_the_error_and_keeps_the_feed_when_it_fails() = runTest {
        val line = ChatMessage(id = "m1", userId = "u1", message = "hi")
        val chatApi =
            FakeChatApi(
                messagesResults = listOf(ApiResult.Ok(listOf(line))),
                sendResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Bot token expired.")),
            )
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.send("hello")

        assertEquals(listOf("ch1" to "hello"), chatApi.sendCalls)
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        // The feed is intact (still the one line) and the failure is surfaced on the Ready state.
        assertEquals(listOf("m1"), (state as ChatState.Ready).messages.map { it.id })
        assertEquals("Bot token expired.", state.actionError)
        // The failed send did not trigger a reload.
        assertEquals(1, chatApi.messagesCalls)
    }

    @Test
    fun delete_message_calls_the_delete_route_then_reloads_without_it() = runTest {
        val keep = ChatMessage(id = "m1", userId = "u1", message = "fine")
        val spam = ChatMessage(id = "m2", userId = "u2", message = "spam")
        val chatApi =
            FakeChatApi(
                messagesResults =
                    listOf(
                        ApiResult.Ok(listOf(keep, spam)),
                        ApiResult.Ok(listOf(keep)),
                    )
            )
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.deleteMessage("m2")

        // The delete hit the delete route with the resolved channel and the message id.
        assertEquals(listOf("ch1" to "m2"), chatApi.deleteCalls)
        // The feed reloaded without the deleted line.
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        assertEquals(listOf("m1"), (state as ChatState.Ready).messages.map { it.id })
        assertNull(state.actionError)
    }

    @Test
    fun delete_message_surfaces_the_error_and_keeps_the_feed_when_it_fails() = runTest {
        val spam = ChatMessage(id = "m2", userId = "u2", message = "spam")
        val chatApi =
            FakeChatApi(
                messagesResults = listOf(ApiResult.Ok(listOf(spam))),
                deleteResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.deleteMessage("m2")

        assertEquals(listOf("ch1" to "m2"), chatApi.deleteCalls)
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        // The line is still there and the failure is surfaced.
        assertEquals(listOf("m2"), (state as ChatState.Ready).messages.map { it.id })
        assertEquals("Missing scope.", state.actionError)
        assertEquals(1, chatApi.messagesCalls)
    }

    @Test
    fun timeout_calls_the_timeout_route_with_the_default_duration_then_reloads() = runTest {
        val troll = ChatMessage(id = "m1", userId = "u9", message = "rude")
        val chatApi =
            FakeChatApi(
                messagesResults =
                    listOf(
                        ApiResult.Ok(listOf(troll)),
                        ApiResult.Ok(emptyList()),
                    )
            )
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.timeout("u9")

        // The timeout hit the moderation route with the resolved channel, the chatter, and the default length.
        assertEquals(
            listOf(Triple("ch1", "u9", ChatApi.DEFAULT_TIMEOUT_SECONDS)),
            chatApi.timeoutCalls,
        )
        // The feed reloaded after the action (here the chatter's messages cleared out).
        assertTrue(controller.state.value is ChatState.Empty)
    }

    @Test
    fun timeout_surfaces_the_error_and_keeps_the_feed_when_it_fails() = runTest {
        val troll = ChatMessage(id = "m1", userId = "u9", message = "rude")
        val chatApi =
            FakeChatApi(
                messagesResults = listOf(ApiResult.Ok(listOf(troll))),
                timeoutResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.timeout("u9", durationSeconds = 30)

        assertEquals(listOf(Triple("ch1", "u9", 30)), chatApi.timeoutCalls)
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        // The feed is intact and the failure is surfaced.
        assertEquals(listOf("m1"), (state as ChatState.Ready).messages.map { it.id })
        assertEquals("Missing scope.", state.actionError)
        assertEquals(1, chatApi.messagesCalls)
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

private class FakeChatApi(
    private val messagesResults: List<ApiResult<List<ChatMessage>>>,
    private val sendResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val deleteResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val timeoutResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val settingsResult: ApiResult<ChatSettings> = ApiResult.Ok(ChatSettings()),
    private val updateSettingsResult: ApiResult<ChatSettings> = ApiResult.Ok(ChatSettings()),
    private val announceResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : ChatApi {
    // Single-result convenience for the read-only tests (one messages() result, default-OK actions).
    constructor(result: ApiResult<List<ChatMessage>>) : this(messagesResults = listOf(result))

    var messagesCalls: Int = 0
        private set

    val sendCalls: MutableList<Pair<String, String>> = mutableListOf()
    val deleteCalls: MutableList<Pair<String, String>> = mutableListOf()
    val timeoutCalls: MutableList<Triple<String, String, Int>> = mutableListOf()
    val announceCalls: MutableList<Triple<String, String, String>> = mutableListOf()

    override suspend fun messages(channelId: String, limit: Int): ApiResult<List<ChatMessage>> {
        // Walk through the configured sequence; the last entry repeats once the script runs out.
        val index: Int = minOf(messagesCalls, messagesResults.lastIndex)
        messagesCalls += 1
        return messagesResults[index]
    }

    override suspend fun send(channelId: String, message: String): ApiResult<Unit> {
        sendCalls.add(channelId to message)
        return sendResult
    }

    override suspend fun deleteMessage(channelId: String, messageId: String): ApiResult<Unit> {
        deleteCalls.add(channelId to messageId)
        return deleteResult
    }

    override suspend fun timeout(
        channelId: String,
        userId: String,
        durationSeconds: Int,
    ): ApiResult<Unit> {
        timeoutCalls.add(Triple(channelId, userId, durationSeconds))
        return timeoutResult
    }

    override suspend fun settings(channelId: String): ApiResult<ChatSettings> = settingsResult

    override suspend fun updateSettings(
        channelId: String,
        settings: ChatSettings,
    ): ApiResult<ChatSettings> = updateSettingsResult

    override suspend fun announce(
        channelId: String,
        message: String,
        color: String,
    ): ApiResult<Unit> {
        announceCalls.add(Triple(channelId, message, color))
        return announceResult
    }
}
