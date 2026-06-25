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

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatApi
import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.realtime.HubChatMessage
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.filterIsInstance

// The Chat page's state-holder (frontend-ia.md §3 — the Chat group). Resolves the active channel, then loads
// its real recent chat from the backend (persisted from EventSub `channel.chat.message`; no fabricated lines).
// It also drives the page's live actions — send a message as the bot, delete a single message, timeout a
// chatter — each of which re-loads on success so the feed always reflects the backend's truth. The screen
// renders [state]; a retry / reconnect (or a poll tick) calls [load] again.
//
// Real-time: [subscribeToHub] can be called once after [load] to forward live ChatMessage hub invocations
// from DashboardHubClient directly into the Ready state so new messages appear without a poll. The existing
// poll path remains the fallback when no hub client is wired.
class ChatController(
    private val channelsApi: ChannelsApi,
    private val chatApi: ChatApi,
) {
    private val _state: MutableStateFlow<ChatState> = MutableStateFlow(ChatState.Loading)

    /** The page render state: loading / ready (with the messages) / empty / error. */
    val state: StateFlow<ChatState> = _state.asStateFlow()

    // The channel the reads/writes target — resolved by [load] and reused by every action so a send / mod
    // action never has to re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /**
     * Resolve the active channel (once), then load its recent chat. Subsequent calls keep the resolved channel
     * and only re-fetch the feed — so a poll tick (or a post-action reload) re-reads chat without flashing the
     * Loading state or re-resolving the channel. The first resolve failure surfaces as Error.
     */
    suspend fun load() {
        val channel: String =
            channelId
                ?: when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                    is ApiResult.Failure -> {
                        _state.value = ChatState.Error(result.error.message)
                        return
                    }
                    is ApiResult.Ok -> result.value.id.also { channelId = it }
                }

        when (val result: ApiResult<List<ChatMessage>> = chatApi.messages(channel)) {
            is ApiResult.Failure -> {
                val current: ChatState = _state.value
                // A refresh failure over an existing feed surfaces the error without dropping the messages;
                // a first-load failure (nothing rendered yet) becomes the full Error state.
                _state.value =
                    if (current is ChatState.Ready) current.copy(actionError = result.error.message)
                    else ChatState.Error(result.error.message)
            }
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) ChatState.Empty
                    else ChatState.Ready(result.value)
        }
    }

    /**
     * Subscribe to [hubEvents], prepending each incoming [HubEvent.ChatMessage] to the Ready feed so new
     * messages appear instantly without waiting for a poll tick. Must be called from a coroutine scope that
     * outlives the page (e.g. the screen's LaunchedEffect). The subscription is cancelled when that scope
     * cancels — no explicit teardown is needed.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.filterIsInstance<HubEvent.ChatMessage>().collect { evt ->
            val current: ChatState = _state.value
            if (current is ChatState.Ready) {
                val newLine: ChatMessage = evt.message.toLocalMessage()
                // Prepend the live message and cap the feed to 200 lines so the list doesn't grow unbounded.
                val capped: List<ChatMessage> = (listOf(newLine) + current.messages).take(200)
                _state.value = current.copy(messages = capped)
            }
        }
    }

    /** Send [message] to chat as the bot, then reload so the sent line appears. Surfaces the error on failure. */
    suspend fun send(message: String) {
        val trimmed: String = message.trim()
        if (trimmed.isEmpty()) return
        val channel: String = channelId ?: return failAction(NoChannelError)
        afterAction(chatApi.send(channel, trimmed))
    }

    /** Delete the single message [messageId], then reload so it drops from the feed. The screen confirms first. */
    suspend fun deleteMessage(messageId: String) {
        val channel: String = channelId ?: return failAction(NoChannelError)
        afterAction(chatApi.deleteMessage(channel, messageId))
    }

    /** Timeout [userId] for [durationSeconds], then reload. The screen confirms this first (destructive). */
    suspend fun timeout(userId: String, durationSeconds: Int = ChatApi.DEFAULT_TIMEOUT_SECONDS) {
        val channel: String = channelId ?: return failAction(NoChannelError)
        afterAction(chatApi.timeout(channel, userId, durationSeconds))
    }

    // An action either reloads the feed (success) or surfaces its error over the current Ready list without
    // losing it (failure) — so a failed send / delete / timeout leaves the page intact with a visible reason.
    private suspend fun afterAction(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failAction(result.error.message)
        }
    }

    private fun failAction(detail: String) {
        val current: ChatState = _state.value
        _state.value =
            if (current is ChatState.Ready) current.copy(actionError = detail)
            else ChatState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

// ─── Hub adapter ─────────────────────────────────────────────────────────────

private fun HubChatMessage.toLocalMessage(): ChatMessage =
    ChatMessage(
        id = id,
        channelId = channelId,
        userId = userId,
        username = username,
        displayName = displayName,
        userType = userType,
        color = color,
        message = message,
        messageType = messageType,
        isCommand = isCommand,
        isCheer = isCheer,
        bitsAmount = if (bitsAmount > 0) bitsAmount else null,
        replyToMessageId = replyToMessageId,
        timestamp = timestamp,
    )

/** The Chat page render state. */
sealed interface ChatState {
    data object Loading : ChatState

    /**
     * The channel's recent chat is listed (oldest first). [actionError] is non-null only when the last
     * send / delete / timeout (or a refresh) failed — the screen surfaces it as a transient banner while
     * keeping the feed rendered.
     */
    data class Ready(val messages: List<ChatMessage>, val actionError: String? = null) : ChatState

    data object Empty : ChatState

    data class Error(val detail: String) : ChatState
}
