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
// takes from it: send a message as the bot, delete a single message, timeout a chatter, control chat modes,
// and post Twitch announcements. Real data only: the backend persists chat from EventSub
// `channel.chat.message` (no fabricated lines) and every action goes straight to Twitch via Helix. The
// state holder depends on this interface and fakes it in tests without HTTP.
//
// Backend routes:
//   GET    /api/v1/channels/{channelId}/chat/messages?limit=N        → StatusResponseDto<List<ChatMessageDto>>
//   POST   /api/v1/channels/{channelId}/chat/messages                → StatusResponseDto<bool>  (send as bot)
//   DELETE /api/v1/channels/{channelId}/chat/messages/{messageId}    → StatusResponseDto<bool>  (delete one)
//   POST   /api/v1/channels/{channelId}/moderation/actions           → StatusResponseDto<ModerationActionResult>
//   GET    /api/v1/channels/{channelId}/chat/settings                → StatusResponseDto<ChatSettingsDto>
//   PUT    /api/v1/channels/{channelId}/chat/settings                → StatusResponseDto<ChatSettingsDto>
//   POST   /api/v1/channels/{channelId}/chat/announce                → 204
interface ChatApi {
    /** The channel's most recent chat, oldest-first (backend already orders chronologically). */
    suspend fun messages(channelId: String, limit: Int = DEFAULT_LIMIT): ApiResult<List<ChatMessage>>

    /** Send [message] to the channel's chat as the bot. */
    suspend fun send(channelId: String, message: String): ApiResult<Unit>

    /** Delete the single chat message [messageId] (moderation quick-action). */
    suspend fun deleteMessage(channelId: String, messageId: String): ApiResult<Unit>

    /** Timeout [userId] for [durationSeconds] (moderation quick-action). */
    suspend fun timeout(channelId: String, userId: String, durationSeconds: Int): ApiResult<Unit>

    /** Load the channel's current chat mode settings (slow, sub-only, emote-only, followers-only). */
    suspend fun settings(channelId: String): ApiResult<ChatSettings>

    /**
     * Persist [settings] — replaces the whole settings object on the backend.
     * Any mode toggled or delay changed goes through here.
     */
    suspend fun updateSettings(channelId: String, settings: ChatSettings): ApiResult<ChatSettings>

    /** Post a Twitch Announcement in the channel's chat. [color]: "primary" | "blue" | "green" | "orange". */
    suspend fun announce(channelId: String, message: String, color: String): ApiResult<Unit>

    companion object {
        /** The default page size — matches the backend's default `limit` (clamped to 1..200 server-side). */
        const val DEFAULT_LIMIT: Int = 50

        /** The default timeout length a chat quick-action applies (10 minutes), the backend's own default. */
        const val DEFAULT_TIMEOUT_SECONDS: Int = 600
    }
}

class RestChatApi(private val client: ApiClient) : ChatApi {
    override suspend fun messages(channelId: String, limit: Int): ApiResult<List<ChatMessage>> =
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

    override suspend fun settings(channelId: String): ApiResult<ChatSettings> =
        client.getEnvelope("api/v1/channels/$channelId/chat/settings")

    override suspend fun updateSettings(
        channelId: String,
        settings: ChatSettings,
    ): ApiResult<ChatSettings> =
        client.putEnvelope("api/v1/channels/$channelId/chat/settings", settings)

    override suspend fun announce(
        channelId: String,
        message: String,
        color: String,
    ): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/chat/announce",
            AnnounceChatBody(message = message, color = color),
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
 * The channel's chat mode configuration (backend `ChatSettingsDto`). All six fields are settable; the backend
 * persists the whole object and the delay/duration values are irrelevant when the matching mode is disabled.
 * Field names mirror the DTO camelCase exactly — [slowMode] / [slowModeDelay] / [subscriberOnly] /
 * [emotesOnly] / [followersOnly] / [followersOnlyDuration].
 */
@Serializable
data class ChatSettings(
    val slowMode: Boolean = false,
    val slowModeDelay: Int = 30,
    val subscriberOnly: Boolean = false,
    val emotesOnly: Boolean = false,
    val followersOnly: Boolean = false,
    val followersOnlyDuration: Int = 0,
)

/** The announce-to-chat request body (backend `AnnounceRequest`). */
@Serializable
data class AnnounceChatBody(val message: String, val color: String = "primary")

/**
 * One chat line (backend `ChatMessageDto`). Fields mirror the backend record's camelCase JSON exactly (the
 * contract test guards this): the message id, the channel, the chatter, role flags, color, reply target,
 * ISO timestamp, and the decorated fragment + badge lists for rich rendering.
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
    val fragments: List<ChatFragment> = emptyList(),
    val badges: List<ChatBadge> = emptyList(),
    // Hub-enrichment fields (also on REST history since chat-client.md §3.6 unified the shape): the chatter's
    // avatar and resolved pronouns, null when unavailable.
    val avatarUrl: String? = null,
    val pronouns: String? = null,
)

/** One decorated fragment — type "text" | "emote" | "cheermote" | "mention" | "link". */
@Serializable
data class ChatFragment(
    val type: String = "text",
    val text: String = "",
    val emote: ChatEmote? = null,
    val cheermote: ChatCheermote? = null,
    val mention: ChatMention? = null,
    val linkUrl: String? = null,
)

/** Resolved emote. [urls] keyed by scale "1".."4". */
@Serializable
data class ChatEmote(
    val id: String = "",
    val setId: String? = null,
    val format: String = "",
    val provider: String = "",
    val urls: Map<String, String> = emptyMap(),
    val animated: Boolean = false,
    val zeroWidth: Boolean = false,
)

/** Resolved cheermote. [urls] keyed by scale "1".."4". */
@Serializable
data class ChatCheermote(
    val prefix: String = "",
    val bits: Int = 0,
    val tier: Int = 0,
    val urls: Map<String, String>? = null,
    val animated: Boolean = false,
    val colorHex: String? = null,
)

/** @mention fragment — includes the mentioned user's last-seen chat colour. */
@Serializable
data class ChatMention(
    val userId: String = "",
    val username: String = "",
    val displayName: String = "",
    val color: String? = null,
)

/** Resolved badge. [urls] keyed by scale "1" / "2" / "4". */
@Serializable
data class ChatBadge(
    val setId: String = "",
    val id: String = "",
    val info: String? = null,
    val urls: Map<String, String> = emptyMap(),
)
