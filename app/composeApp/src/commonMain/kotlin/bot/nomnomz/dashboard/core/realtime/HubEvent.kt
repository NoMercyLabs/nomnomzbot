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

    data class AlertTriggered(val alert: HubAlert) : HubEvent

    data class ModAction(val action: HubModAction) : HubEvent

    data class CommandExecuted(val event: HubCommandExecuted) : HubEvent

    data class RewardRedeemed(val event: HubRewardRedeemed) : HubEvent

    data class MusicStateChanged(val state: HubMusicState) : HubEvent

    data class ChannelEvent(val event: HubChannelEvent) : HubEvent

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
                    "AlertTriggered" -> AlertTriggered(json.decodeFromString(first))
                    "ModAction" -> ModAction(json.decodeFromString(first))
                    "CommandExecuted" -> CommandExecuted(json.decodeFromString(first))
                    "RewardRedeemed" -> RewardRedeemed(json.decodeFromString(first))
                    "MusicStateChanged" -> MusicStateChanged(json.decodeFromString(first))
                    "ChannelEvent" -> ChannelEvent(json.decodeFromString(first))
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
)

@Serializable
data class HubStreamStatus(
    val isLive: Boolean = false,
    val streamId: String? = null,
    val title: String? = null,
    val gameName: String? = null,
    val startedAt: String? = null,
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
data class HubChannelEvent(
    val type: String = "",
    val broadcasterId: String = "",
    val userId: String? = null,
    val userDisplayName: String? = null,
    val timestamp: String = "",
)
