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
import bot.nomnomz.dashboard.core.network.ChatEmoteCatalogue
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.network.ChatSettings
import bot.nomnomz.dashboard.core.network.NetworkBanResult
import bot.nomnomz.dashboard.core.realtime.HubChatMessage
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.UnconfinedTestDispatcher
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

        // The send hit the send route with the resolved channel, the TRIMMED message, and the default "you" identity.
        assertEquals(listOf(Triple("ch1", "welcome!", "you")), chatApi.sendCalls)
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
    fun send_as_bot_passes_the_bot_identity_through_to_the_send_route() = runTest {
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(ChatMessage(id = "m1", message = "hi"))))
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.send("posted by the bot", senderIdentity = "bot")

        // The chosen identity reaches the backend verbatim so it routes to the bot send path, not the operator one.
        assertEquals(listOf(Triple("ch1", "posted by the bot", "bot")), chatApi.sendCalls)
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
    fun a_normal_send_threads_a_null_reply_target() = runTest {
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(ChatMessage(id = "m1", message = "hi"))))
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.send("hello")

        // A send outside reply mode carries no parent id — the backend posts a normal message, not a reply.
        assertEquals(listOf<String?>(null), chatApi.sendReplyTargets)
        assertNull(controller.replyTarget.value)
    }

    @Test
    fun start_reply_sets_the_reply_target() = runTest {
        val parent = ChatMessage(id = "m1", userId = "u1", displayName = "Viewer", message = "hi")
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(parent)))
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.startReply(parent)

        // Reply mode holds the whole selected message so the composer can name its author.
        assertEquals("m1", controller.replyTarget.value?.id)
        assertEquals("Viewer", controller.replyTarget.value?.displayName)
    }

    @Test
    fun cancel_reply_clears_the_reply_target() = runTest {
        val parent = ChatMessage(id = "m1", userId = "u1", message = "hi")
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(parent)))
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.startReply(parent)
        controller.cancelReply()

        assertNull(controller.replyTarget.value)
    }

    @Test
    fun send_in_reply_mode_threads_the_parent_id_and_clears_reply_mode() = runTest {
        val parent = ChatMessage(id = "m1", userId = "u1", displayName = "Viewer", message = "hi")
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(parent)))
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.startReply(parent)
        controller.send("great point!")

        // The send carried the parent message id as the reply target, and the trimmed body + default identity.
        assertEquals(listOf<String?>("m1"), chatApi.sendReplyTargets)
        assertEquals(listOf(Triple("ch1", "great point!", "you")), chatApi.sendCalls)
        // Reply mode cleared once the reply landed, so the next send is a normal message.
        assertNull(controller.replyTarget.value)
    }

    @Test
    fun reply_mode_survives_a_failed_send_so_the_operator_can_retry() = runTest {
        val parent = ChatMessage(id = "m1", userId = "u1", message = "hi")
        val chatApi =
            FakeChatApi(
                messagesResults = listOf(ApiResult.Ok(listOf(parent))),
                sendResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Bot token expired.")),
            )
        val controller = ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.startReply(parent)
        controller.send("great point!")

        // The parent id still went out on the wire, but the failed send keeps reply mode so the operator can retry
        // without re-selecting the message; the error surfaces over the intact feed.
        assertEquals(listOf<String?>("m1"), chatApi.sendReplyTargets)
        assertEquals("m1", controller.replyTarget.value?.id)
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        assertEquals("Bot token expired.", (state as ChatState.Ready).actionError)
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

        assertEquals(listOf(Triple("ch1", "hello", "you")), chatApi.sendCalls)
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

    @Test
    fun ban_posts_to_the_ban_route_with_the_chosen_scope() = runTest {
        val troll = ChatMessage(id = "m1", userId = "u9", message = "rude")
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(troll)))
        val controller =
            ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.ban("u9", scope = "all_moderated", reason = "spam")

        // The ban hit the ban route with the resolved channel, the target, and the chosen scope.
        assertEquals(listOf(Triple("ch1", "u9", "all_moderated")), chatApi.banCalls)
        assertTrue(controller.state.value is ChatState.Ready)
    }

    @Test
    fun report_files_the_chat_context_and_keeps_the_feed() = runTest {
        val troll = ChatMessage(id = "m1", userId = "u9", username = "trolly", message = "rude")
        val chatApi = FakeChatApi(ApiResult.Ok(listOf(troll)))
        val controller =
            ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.report("u9", userName = "trolly", displayName = "Trolly", reason = "posting scam links")

        // The report carried the reported user + reason from the chat row, and the feed stayed put (non-punishing).
        assertEquals(listOf(Triple("u9", "trolly", "posting scam links")), chatApi.reportCalls)
        assertTrue(controller.state.value is ChatState.Ready)
    }

    @Test
    fun ban_surfaces_the_error_and_keeps_the_feed_when_it_fails() = runTest {
        val line = ChatMessage(id = "m1", userId = "u9", message = "rude")
        val chatApi =
            FakeChatApi(
                messagesResults = listOf(ApiResult.Ok(listOf(line))),
                banResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "Missing scope.")),
            )
        val controller =
            ChatController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), chatApi)

        controller.load()
        controller.ban("u9", scope = "this_channel")

        assertEquals(listOf(Triple("ch1", "u9", "this_channel")), chatApi.banCalls)
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        // The feed is intact and the failure is surfaced.
        assertEquals(listOf("m1"), (state as ChatState.Ready).messages.map { it.id })
        assertEquals("Missing scope.", state.actionError)
    }

    @Test
    fun load_re_resolves_the_active_channel_so_a_switch_re_targets_the_feed() = runTest {
        val channels =
            FakeChannelsApi(
                listOf(
                    ApiResult.Ok(ChannelSummary(id = "ch1")),
                    ApiResult.Ok(ChannelSummary(id = "ch2")),
                )
            )
        val chatApi =
            FakeChatApi(
                messagesResults =
                    listOf(
                        ApiResult.Ok(listOf(ChatMessage(id = "a1", channelId = "ch1", message = "own channel"))),
                        ApiResult.Ok(listOf(ChatMessage(id = "b1", channelId = "ch2", message = "switched channel"))),
                    )
            )
        val controller = ChatController(channels, chatApi)

        controller.load() // resolves ch1
        controller.load() // switcher moved to ch2 — must re-resolve and follow, not replay ch1

        // The channel was re-resolved on each load (no cache-once), so the feed re-targeted ch2.
        assertEquals(listOf("ch1", "ch2"), chatApi.messagesChannels)
        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        // Only ch2's line remains — ch1's feed was dropped on the switch, never bleeding across.
        assertEquals(listOf("b1"), (state as ChatState.Ready).messages.map { it.id })
    }

    @Test
    fun a_moderation_action_after_a_switch_targets_the_switched_channel() = runTest {
        val channels =
            FakeChannelsApi(
                listOf(
                    ApiResult.Ok(ChannelSummary(id = "ch1")),
                    ApiResult.Ok(ChannelSummary(id = "ch2")),
                )
            )
        val chatApi =
            FakeChatApi(
                messagesResults =
                    listOf(
                        ApiResult.Ok(listOf(ChatMessage(id = "a1", channelId = "ch1", userId = "u1", message = "hi"))),
                        ApiResult.Ok(listOf(ChatMessage(id = "b1", channelId = "ch2", userId = "u9", message = "spam"))),
                    )
            )
        val controller = ChatController(channels, chatApi)

        controller.load() // ch1
        controller.load() // switch to ch2
        controller.ban("u9", scope = "this_channel")

        // The ban lands on ch2 — the channel on screen — NOT the first-loaded ch1. Before the fix the stale
        // cached id would have banned in the wrong channel: a real moderation-safety bug.
        assertEquals(listOf(Triple("ch2", "u9", "this_channel")), chatApi.banCalls)
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun subscribe_to_hub_skips_a_duplicate_message_id() = runTest {
        val controller =
            ChatController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeChatApi(ApiResult.Ok(listOf(ChatMessage(id = "hist", channelId = "ch1", message = "history")))),
            )
        controller.load()

        // Collect on an unconfined test dispatcher so the subscription is live immediately and each emission is
        // processed eagerly — the default StandardTestDispatcher parks the hot-flow collector, so its emissions
        // would never run within the test. extraBufferCapacity keeps emit from suspending.
        val events = MutableSharedFlow<HubEvent>(extraBufferCapacity = 16)
        backgroundScope.launch(UnconfinedTestDispatcher(testScheduler)) { controller.subscribeToHub(events) }

        // A live message arrives, then the SAME id is redelivered — EventSub is at-least-once (redelivers on a
        // WebSocket reconnect) and the operator's own line echoes back.
        events.emit(HubEvent.ChatMessage(HubChatMessage(id = "live1", channelId = "ch1", message = "hello")))
        events.emit(HubEvent.ChatMessage(HubChatMessage(id = "live1", channelId = "ch1", message = "hello")))

        val state: ChatState = controller.state.value
        assertTrue(state is ChatState.Ready)
        // The redelivered id appears exactly once; the feed's id-keyed LazyColumn would crash on a duplicate key.
        assertEquals(listOf("hist", "live1"), (state as ChatState.Ready).messages.map { it.id })
    }
}

private class FakeChannelsApi(
    private val results: List<ApiResult<ChannelSummary>>,
) : ChannelsApi {
    // Single-result convenience: the one result repeats for every resolve (single-channel tests).
    constructor(result: ApiResult<ChannelSummary>) : this(listOf(result))

    var primaryChannelCalls: Int = 0
        private set

    // Walk the configured sequence; the last entry repeats once the script runs out — so a second, different
    // entry models the operator switching the active channel between two load() calls.
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> {
        val index: Int = minOf(primaryChannelCalls, results.lastIndex)
        primaryChannelCalls += 1
        return results[index]
    }

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
    private val banResult: ApiResult<NetworkBanResult> =
        ApiResult.Ok(NetworkBanResult(1, 1, emptyList())),
    private val settingsResult: ApiResult<ChatSettings> = ApiResult.Ok(ChatSettings()),
    private val updateSettingsResult: ApiResult<ChatSettings> = ApiResult.Ok(ChatSettings()),
    private val announceResult: ApiResult<Unit> = ApiResult.Ok(Unit),
) : ChatApi {
    // Single-result convenience for the read-only tests (one messages() result, default-OK actions).
    constructor(result: ApiResult<List<ChatMessage>>) : this(messagesResults = listOf(result))

    var messagesCalls: Int = 0
        private set

    // Each send call recorded as (channel, message, senderIdentity) so tests prove the identity is passed through.
    val sendCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    // The reply-parent id threaded on each send (null for a normal send) — parallel to [sendCalls] by index.
    val sendReplyTargets: MutableList<String?> = mutableListOf()
    val deleteCalls: MutableList<Pair<String, String>> = mutableListOf()
    val timeoutCalls: MutableList<Triple<String, String, Int>> = mutableListOf()
    val banCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    // Each report recorded as (targetUserId, targetUsername, reason) so a test proves the chat context is passed on.
    val reportCalls: MutableList<Triple<String, String, String>> = mutableListOf()
    val announceCalls: MutableList<Triple<String, String, String>> = mutableListOf()

    // Each messages() call records the channel it targeted, so a switch test can prove the feed re-scopes.
    val messagesChannels: MutableList<String> = mutableListOf()

    override suspend fun messages(channelId: String, limit: Int): ApiResult<List<ChatMessage>> {
        messagesChannels.add(channelId)
        // Walk through the configured sequence; the last entry repeats once the script runs out.
        val index: Int = minOf(messagesCalls, messagesResults.lastIndex)
        messagesCalls += 1
        return messagesResults[index]
    }

    override suspend fun emotes(channelId: String): ApiResult<List<ChatEmoteCatalogue>> =
        ApiResult.Ok(emptyList())

    override suspend fun send(
        channelId: String,
        message: String,
        senderIdentity: String,
        replyToMessageId: String?,
    ): ApiResult<Unit> {
        sendCalls.add(Triple(channelId, message, senderIdentity))
        sendReplyTargets.add(replyToMessageId)
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

    override suspend fun banUser(
        channelId: String,
        targetTwitchUserId: String,
        scope: String,
        reason: String?,
        durationSeconds: Int?,
    ): ApiResult<NetworkBanResult> {
        banCalls.add(Triple(channelId, targetTwitchUserId, scope))
        return banResult
    }

    override suspend fun fileReport(
        channelId: String,
        targetTwitchUserId: String,
        targetUsername: String,
        targetDisplayName: String?,
        reason: String,
    ): ApiResult<Unit> {
        reportCalls.add(Triple(targetTwitchUserId, targetUsername, reason))
        return ApiResult.Ok(Unit)
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
