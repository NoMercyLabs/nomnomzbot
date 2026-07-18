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
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.filterIsInstance

// The multi-channel chat-watch page's state-holder (frontend-ia.md — the Chat group; owner requirement
// 2026-07-10: "a viewer+ should be able to view multiple chats at once so mods can monitor multiple channels").
// It lists the channels the caller owns/moderates (from GET /channels), lets the operator pick several to watch
// at once, and renders their live chat merged into ONE feed with each line tagged by its channel — the
// cross-channel half of combined chat (the cross-platform half, a YouTube/Kick channel, is just one more channel
// in the picker, since the backend models it as its own channel).
//
// Realtime: ONE hub connection watches many channels concurrently — [joinChannel] adds a channel's group,
// [leaveChannel] drops just that one. Every DashboardHub `ChatMessage` push carries its `channelId`, so
// [subscribeToHub] routes each live line to the merged feed only when its channel is currently watched. Per-
// channel scrollback loads from the REST history endpoint as each channel is added. All real data — nothing is
// fabricated. This controller depends on [joinChannel]/[leaveChannel] as plain functions (the screen wires them
// to a dedicated DashboardHubClient) so the state machine stays fakeable without a socket.
class MultiChatController(
    private val channelsApi: ChannelsApi,
    private val chatApi: ChatApi,
    private val joinChannel: (channelId: String) -> Unit,
    private val leaveChannel: (channelId: String) -> Unit,
) {
    private val _state: MutableStateFlow<MultiChatState> = MutableStateFlow(MultiChatState.Loading)

    /** The page render state: loading / ready (pickable channels + watched set + merged feed) / error. */
    val state: StateFlow<MultiChatState> = _state.asStateFlow()

    /**
     * Load the channels the caller can watch — every channel they own or moderate (`GET /api/v1/channels`,
     * already access-scoped, so the picker is correct regardless of the exact read floor). Lands Ready with an
     * empty watched set; the operator picks channels to start watching. A load failure surfaces as Error.
     */
    suspend fun load() {
        if (_state.value !is MultiChatState.Ready) _state.value = MultiChatState.Loading
        when (val result: ApiResult<List<ChannelSummary>> = channelsApi.list()) {
            is ApiResult.Failure -> _state.value = MultiChatState.Error(result.error.message)
            is ApiResult.Ok -> {
                val ready: MultiChatState.Ready? = _state.value as? MultiChatState.Ready
                _state.value =
                    MultiChatState.Ready(
                        available = result.value,
                        watched = ready?.watched ?: emptyList(),
                        messages = ready?.messages ?: emptyList(),
                    )
            }
        }
    }

    /**
     * Start watching [channelId]: add it to the watched set, join its hub group (live push), and load its recent
     * scrollback merged into the feed. A no-op when the channel is already watched or the page isn't Ready.
     */
    suspend fun addChannel(channelId: String) {
        val ready: MultiChatState.Ready = _state.value as? MultiChatState.Ready ?: return
        val channel: ChannelSummary = ready.available.firstOrNull { it.id == channelId } ?: return
        if (ready.watched.any { it.id == channelId }) return

        joinChannel(channelId)
        _state.value = ready.copy(watched = ready.watched + channel)

        // Load the channel's persisted scrollback and merge it in (deduped, capped, newest last).
        when (val result: ApiResult<List<ChatMessage>> = chatApi.messages(channelId)) {
            is ApiResult.Failure -> {
                val current: MultiChatState.Ready = _state.value as? MultiChatState.Ready ?: return
                _state.value = current.copy(actionError = result.error.message)
            }
            is ApiResult.Ok -> {
                val current: MultiChatState.Ready = _state.value as? MultiChatState.Ready ?: return
                _state.value = current.copy(messages = merge(current.messages, result.value))
            }
        }
    }

    /**
     * Stop watching [channelId]: leave its hub group and drop its lines from the merged feed, leaving every other
     * watched channel untouched. A no-op when the channel isn't watched.
     */
    fun removeChannel(channelId: String) {
        val ready: MultiChatState.Ready = _state.value as? MultiChatState.Ready ?: return
        if (ready.watched.none { it.id == channelId }) return

        leaveChannel(channelId)
        _state.value =
            ready.copy(
                watched = ready.watched.filterNot { it.id == channelId },
                messages = ready.messages.filterNot { it.channelId == channelId },
            )
    }

    /**
     * Forward live [hubEvents] into the merged feed: append each incoming [HubEvent.ChatMessage] whose channel is
     * currently watched (routing by `channelId`), deduped by id and capped. Ignores pushes for channels not in the
     * watched set (the one shared connection may carry a socket-opening primary channel we aren't watching). Must
     * run from a scope that outlives the page; cancels with that scope.
     */
    suspend fun subscribeToHub(hubEvents: SharedFlow<HubEvent>) {
        hubEvents.filterIsInstance<HubEvent.ChatMessage>().collect { evt ->
            val line: ChatMessage = evt.message.toLocalMessage()
            val ready: MultiChatState.Ready = _state.value as? MultiChatState.Ready ?: return@collect
            // Route by channelId — only surface a line for a channel we are actively watching.
            if (ready.watched.none { it.id == line.channelId }) return@collect
            // Skip a non-blank id already present (EventSub is at-least-once; scrollback may overlap live).
            if (line.id.isNotEmpty() && ready.messages.any { it.id == line.id }) return@collect
            _state.value = ready.copy(messages = merge(ready.messages, listOf(line)))
        }
    }

    // Merge new lines into the feed: dedupe by non-blank id, sort by timestamp (ISO-8601 sorts lexically), and
    // cap so the merged list stays bounded across many busy channels.
    private fun merge(existing: List<ChatMessage>, incoming: List<ChatMessage>): List<ChatMessage> {
        val seen: MutableSet<String> = existing.mapNotNull { it.id.ifEmpty { null } }.toMutableSet()
        val merged: MutableList<ChatMessage> = existing.toMutableList()
        for (line: ChatMessage in incoming) {
            if (line.id.isNotEmpty() && !seen.add(line.id)) continue
            merged += line
        }
        return merged.sortedBy { it.timestamp }.takeLast(MAX_MESSAGES)
    }

    private companion object {
        const val MAX_MESSAGES: Int = 300
    }
}

/** The multi-channel chat-watch page render state. */
sealed interface MultiChatState {
    data object Loading : MultiChatState

    /**
     * [available] is every channel the caller can watch (the picker); [watched] is the subset currently being
     * monitored; [messages] is the merged, time-ordered feed across the watched channels (tag each line by its
     * `channelId`). [actionError] is non-null only when the last add/scrollback failed — surfaced as a transient
     * banner while keeping the feed rendered.
     */
    data class Ready(
        val available: List<ChannelSummary>,
        val watched: List<ChannelSummary>,
        val messages: List<ChatMessage>,
        val actionError: String? = null,
    ) : MultiChatState

    data class Error(val detail: String) : MultiChatState
}
