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

// The typed chat facade — the channel's recent chat the Chat page renders, plus the live actions a moderator
// takes from it: send a message as the bot, delete a single message, and timeout a chatter. Real data only:
// the backend persists chat from EventSub `channel.chat.message` (no fabricated lines) and every action goes
// straight to Twitch via Helix. The state holder depends on this interface and fakes it in tests without HTTP.
//
// Backend routes:
//   GET    /api/v1/channels/{channelId}/chat/messages?limit=N        → StatusResponseDto<List<ChatMessageDto>>
//   POST   /api/v1/channels/{channelId}/chat/messages                → StatusResponseDto<bool>  (send as bot)
//   DELETE /api/v1/channels/{channelId}/chat/messages/{messageId}    → StatusResponseDto<bool>  (delete one)
//   POST   /api/v1/channels/{channelId}/moderation/actions           → StatusResponseDto<ModerationActionResult>
//                                                                       (action="timeout", targetUserId, durationSeconds)
// The GET messages payload is a `StatusResponseDto<List<…>>` (the single-value envelope wrapping a list), so it
// is read with getEnvelope's `data: T` unwrap — NOT the flat PaginatedResponse shape. Send/delete return a
// `StatusResponseDto<bool>` whose body the page ignores (any 2xx is success), so they go through postUnit /
// deleteUnit. Timeout likewise: the action result body is irrelevant once it succeeds.
interface ChatApi {
    /** The channel's most recent chat, oldest-first (backend already orders chronologically). */
    suspend fun messages(channelId: String, limit: Int = DEFAULT_LIMIT): ApiResult<List<ChatMessage>>

    /** Send [message] to the channel's chat as the bot. */
    suspend fun send(channelId: String, message: String): ApiResult<Unit>

    /** Delete the single chat message [messageId] (moderation quick-action). */
    suspend fun deleteMessage(channelId: String, messageId: String): ApiResult<Unit>

    /** Timeout [userId] for [durationSeconds] (moderation quick-action). */
    suspend fun timeout(channelId: String, userId: String, durationSeconds: Int): ApiResult<Unit>

    companion object {
        /** The default page size — matches the backend's default `limit` (clamped to 1..200 server-side). */
        const val DEFAULT_LIMIT: Int = 50

        /** The default timeout length a chat quick-action applies (10 minutes), the backend's own default. */
        const val DEFAULT_TIMEOUT_SECONDS: Int = 600
    }
}

class RestChatApi(private val client: ApiClient) : ChatApi {
    override suspend fun messages(channelId: String, limit: Int): ApiResult<List<ChatMessage>> =
        // A `StatusResponseDto<List<ChatMessageDto>>` (single-value envelope wrapping the list), so getEnvelope
        // unwraps `data` to the list — not the flat PaginatedResponse shape.
        client.getEnvelope("api/v1/channels/$channelId/chat/messages?limit=$limit")

    override suspend fun send(channelId: String, message: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/chat/messages", SendChatMessageBody(message))

    override suspend fun deleteMessage(channelId: String, messageId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/chat/messages/$messageId")

    override suspend fun timeout(
        channelId: String,
        userId: String,
        durationSeconds: Int,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/moderation/actions",
            ModerationActionBody(
                action = "timeout",
                targetUserId = userId,
                durationSeconds = durationSeconds,
            ),
        )
}

/**
 * The send-message request body (backend `SendChatMessageRequest`). camelCase JSON; [message] is the line to
 * post as the bot (the backend trims it and rejects empty / >500-char messages).
 */
@Serializable
data class SendChatMessageBody(val message: String)

/**
 * The moderation-action request body (backend `PerformModerationActionRequest`). camelCase JSON. The chat page
 * only issues `timeout`, so [reason] stays null; [durationSeconds] carries the timeout length.
 */
@Serializable
data class ModerationActionBody(
    val action: String,
    val targetUserId: String,
    val durationSeconds: Int? = null,
    val reason: String? = null,
)

/**
 * One chat line (backend `ChatMessageDto`). Fields mirror the backend record's camelCase JSON exactly (the
 * contract test guards this): the message id, the channel, the chatter (id / login / display name / role), an
 * optional name color, the text, the role / cheer flags, an optional reply target, and the ISO timestamp.
 * [badges] and [fragments] are carried through verbatim for rich rendering; the list view reads [message].
 */
@Serializable
data class ChatMessage(
    val id: String,
    val channelId: String = "",
    val userId: String = "",
    val username: String = "",
    val displayName: String = "",
    val userType: String = "",
    val color: String? = null,
    val message: String = "",
    val messageType: String = "",
    val isCommand: Boolean = false,
    val isCheer: Boolean = false,
    val bitsAmount: Int? = null,
    val replyToMessageId: String? = null,
    val timestamp: String = "",
)
