// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.realtime

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray

/**
 * Sealed hierarchy of hub invocations the DashboardHub pushes to connected clients.
 *
 * Each leaf maps 1-to-1 to a method on `IDashboardClient` (server-side). Unknown targets decode to
 * [Unknown] so they can be safely ignored without crashing the receive loop.
 */
sealed interface HubEvent {

    data class ChatMessage(val message: HubChatMessage) : HubEvent

    data class StreamStatusChanged(val status: HubStreamStatus) : HubEvent

    data class StreamInfoChanged(val info: HubStreamInfoChanged) : HubEvent

    data class AlertTriggered(val alert: HubAlert) : HubEvent

    data class ModAction(val action: HubModAction) : HubEvent

    data class CommandExecuted(val event: HubCommandExecuted) : HubEvent

    data class RewardRedeemed(val event: HubRewardRedeemed) : HubEvent

    data class MusicStateChanged(val state: HubMusicState) : HubEvent

    data class ChannelEvent(val event: HubChannelEvent) : HubEvent

    data class PermissionChanged(val change: HubPermissionChanged) : HubEvent

    /** A hub target not yet modelled — carry the raw argument so callers can inspect it. */
    data class Unknown(val target: String, val rawArgs: String) : HubEvent

    companion object {
        private val json: Json = Json { ignoreUnknownKeys = true; isLenient = true }

        internal fun from(target: String, args: JsonArray): HubEvent? {
            if (args.isEmpty()) return Unknown(target, "[]")
            val first: String = args[0].toString()
            return runCatching {
                when (target) {
                    "ChatMessage" -> ChatMessage(json.decodeFromString(first))
                    "StreamStatusChanged" -> StreamStatusChanged(json.decodeFromString(first))
                    "StreamInfoChanged" -> StreamInfoChanged(json.decodeFromString(first))
                    "AlertTriggered" -> AlertTriggered(json.decodeFromString(first))
                    "ModAction" -> ModAction(json.decodeFromString(first))
                    "CommandExecuted" -> CommandExecuted(json.decodeFromString(first))
                    "RewardRedeemed" -> RewardRedeemed(json.decodeFromString(first))
                    "MusicStateChanged" -> MusicStateChanged(json.decodeFromString(first))
                    "ChannelEvent" -> ChannelEvent(json.decodeFromString(first))
                    "PermissionChanged" -> PermissionChanged(json.decodeFromString(first))
                    else -> Unknown(target, first)
                }
            }.getOrNull()
        }
    }
}

// ─── DTO mirrors (backend Hubs/Dtos/*.cs) ────────────────────────────────────

@Serializable
data class HubChatMessage(
    val id: String = "",
    val channelId: String = "",
    val userId: String = "",
    val displayName: String = "",
    val username: String = "",
    val message: String = "",
    val userType: String = "viewer",
    val isSubscriber: Boolean = false,
    val isVip: Boolean = false,
    val isModerator: Boolean = false,
    val isBroadcaster: Boolean = false,
    val isCheer: Boolean = false,
    val isCommand: Boolean = false,
    val bitsAmount: Int = 0,
    val color: String? = null,
    val messageType: String = "text",
    val replyToMessageId: String? = null,
    val replyParentMessageBody: String? = null,
    val replyParentUserName: String? = null,
    val timestamp: String = "",
    /** Structured fragments from the backend chat-decoration pipeline (emotes, mentions, cheermotes, links). */
    val fragments: List<HubChatFragment> = emptyList(),
    /** Resolved badge images keyed by scale ("1"/"2"/"4"). */
    val badges: List<HubChatBadge> = emptyList(),
    /** Chatter avatar + resolved pronouns (hub enrichment via IHubUserEnricher); null when unavailable. */
    val avatarUrl: String? = null,
    val pronouns: String? = null,
    /** Source platform of the message — twitch | kick | youtube. The live feed is accurate; history reports twitch. */
    val provider: String = "twitch",
)

/** One fragment of a hub chat message — type is "text" | "emote" | "cheermote" | "mention" | "link". */
@Serializable
data class HubChatFragment(
    val type: String = "text",
    val text: String = "",
    val emote: HubChatEmote? = null,
    val cheermote: HubChatCheermote? = null,
    val mention: HubChatMention? = null,
    val linkUrl: String? = null,
)

/** Resolved emote (Twitch + third-party unified). Urls keyed by scale "1".."4". */
@Serializable
data class HubChatEmote(
    val id: String = "",
    val setId: String? = null,
    val format: String = "",
    val provider: String = "",
    val urls: Map<String, String> = emptyMap(),
    val animated: Boolean = false,
    val zeroWidth: Boolean = false,
)

/** Resolved cheermote. Urls keyed by scale "1".."4"; colorHex is the tier colour (#RRGGBB). */
@Serializable
data class HubChatCheermote(
    val prefix: String = "",
    val bits: Int = 0,
    val tier: Int = 0,
    val urls: Map<String, String>? = null,
    val animated: Boolean = false,
    val colorHex: String? = null,
)

/** @mention fragment — includes the mentioned user's last-seen chat colour. */
@Serializable
data class HubChatMention(
    val userId: String = "",
    val username: String = "",
    val displayName: String = "",
    val color: String? = null,
)

/** Resolved badge — Urls keyed by scale "1" / "2" / "4". */
@Serializable
data class HubChatBadge(
    val setId: String = "",
    val id: String = "",
    val info: String? = null,
    val urls: Map<String, String> = emptyMap(),
)

@Serializable
data class HubStreamStatus(
    val isLive: Boolean = false,
    val streamId: String? = null,
    val title: String? = null,
    val gameName: String? = null,
    val startedAt: String? = null,
)

/** Pushed when the channel's title/category changes (`channel.update`) — keeps the stream-info banner live. */
@Serializable
data class HubStreamInfoChanged(
    val broadcasterId: String = "",
    val broadcasterDisplayName: String = "",
    val title: String = "",
    val gameName: String = "",
)

@Serializable
data class HubAlert(
    val type: String = "",
    val message: String? = null,
)

@Serializable
data class HubModAction(
    val action: String = "",
    val moderatorId: String = "",
    val targetUserId: String = "",
    val reason: String? = null,
    val durationSeconds: Int? = null,
)

@Serializable
data class HubCommandExecuted(
    val broadcasterId: String = "",
    val commandName: String = "",
    val triggeredByUserId: String = "",
    val succeeded: Boolean = false,
    val timestamp: String = "",
)

@Serializable
data class HubRewardRedeemed(
    val broadcasterId: String = "",
    val rewardId: String = "",
    val rewardTitle: String = "",
    val redemptionId: String = "",
    val userId: String = "",
    val userDisplayName: String = "",
    val cost: Int = 0,
    val userInput: String? = null,
    val timestamp: String = "",
)

@Serializable
data class HubMusicState(
    val isPlaying: Boolean = false,
    val currentTrack: HubMusicTrack? = null,
)

@Serializable
data class HubMusicTrack(
    val trackName: String = "",
    val artist: String = "",
    val album: String = "",
    val albumArtUrl: String? = null,
    val durationMs: Int = 0,
    val provider: String = "",
)

@Serializable
data class HubPermissionChanged(
    val subjectType: String = "",
    val subjectId: String = "",
    val resourceType: String = "",
    val resourceId: String = "",
    val value: Int = 0,
)

@Serializable
data class HubChannelEvent(
    val type: String = "",
    val broadcasterId: String = "",
    val userId: String? = null,
    val userDisplayName: String? = null,
    val timestamp: String = "",
)
