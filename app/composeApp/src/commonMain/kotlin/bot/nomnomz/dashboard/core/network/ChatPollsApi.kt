// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed chat-polls facade — bot-run polls where viewers vote by typing an option number in chat on ANY
// platform (last vote wins, one per viewer). Distinct from the Twitch-native live-ops polls (affiliate-gated,
// channel-point voting): both live on the polls surface, labeled "Chat poll" vs "Twitch poll". State holders
// depend on this interface and fake it in tests without HTTP.
//
// Backend routes (ChatPollsController), all under channels/{channelId}/chat-polls:
//   GET    /                    → StatusResponseDto<List<ChatPollDto>>   (open poll first, then history)
//   POST   /                    ← OpenChatPollRequest → StatusResponseDto<ChatPollDto>
//   GET    /{pollId}            → StatusResponseDto<ChatPollDto>
//   POST   /{pollId}/close      → StatusResponseDto<ChatPollDto>
// Only one poll may be open per channel at a time (the backend answers 409 otherwise).
interface ChatPollsApi {
    /** Every poll for the channel — the open one first (with live tallies), then closed history, newest-first. */
    suspend fun list(channelId: String): ApiResult<List<ChatPoll>>

    /** Open a new poll. Fails (409) when one is already open. */
    suspend fun open(channelId: String, request: OpenChatPollRequest): ApiResult<ChatPoll>

    /** Close the poll [pollId] now — the backend announces the winner in chat. */
    suspend fun close(channelId: String, pollId: String): ApiResult<ChatPoll>
}

class RestChatPollsApi(private val client: ApiClient) : ChatPollsApi {
    // StatusResponseDto<List<ChatPollDto>> — the single-value envelope wraps the whole list in `data`.
    override suspend fun list(channelId: String): ApiResult<List<ChatPoll>> =
        client.getEnvelope("api/v1/channels/$channelId/chat-polls")

    override suspend fun open(channelId: String, request: OpenChatPollRequest): ApiResult<ChatPoll> =
        client.postEnvelope("api/v1/channels/$channelId/chat-polls", request)

    override suspend fun close(channelId: String, pollId: String): ApiResult<ChatPoll> =
        client.postEnvelope("api/v1/channels/$channelId/chat-polls/$pollId/close")
}

/**
 * A bot-run chat poll (backend `ChatPollDto`). [status] is `open` | `closed`; [options] carry the live per-option
 * [ChatPollOption.votes] tallies; [totalVotes] is their sum. [closesAt] is the scheduled auto-close (null = no
 * timer); [closedAt] is when it actually closed. Field names mirror the DTO camelCase exactly.
 */
@Serializable
data class ChatPoll(
    val id: String = "",
    val question: String = "",
    val options: List<ChatPollOption> = emptyList(),
    val status: String = "",
    val totalVotes: Int = 0,
    val openedAt: String = "",
    val closesAt: String? = null,
    val closedAt: String? = null,
)

/** One poll option (backend `ChatPollOptionDto`): its [index] (the number viewers type), [label], and [votes]. */
@Serializable
data class ChatPollOption(val index: Int = 0, val label: String = "", val votes: Int = 0)

/**
 * Open-poll request body (backend `OpenChatPollRequest`): the [question], 2–10 [options], an optional
 * [durationSeconds] auto-close, and whether to [announce] the poll in chat when it opens.
 */
@Serializable
data class OpenChatPollRequest(
    val question: String,
    val options: List<String>,
    val durationSeconds: Int? = null,
    val announce: Boolean = true,
)
