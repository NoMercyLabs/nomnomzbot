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

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

// The payload shapes of HubChannelEvent.data for the three chat-moderation event types ("chat_cleared" carries
// no data ChatController/MultiChatController need beyond the type itself, so it has no payload class here) —
// mirrors the backend's MessageDeletedDto / UserMessagesClearedDto (ChatModerationBroadcastHandlers.cs). Shared
// by both chat controllers so the single-channel and multi-channel feeds decode identically.

@Serializable
data class MessageDeletedPayload(
    val messageId: String,
    val deletedByUserId: String = "",
    val targetUserId: String = "",
)

@Serializable
data class UserMessagesClearedPayload(
    val targetUserId: String,
    val targetUserDisplayName: String = "",
    val targetUserLogin: String = "",
)

internal val ChatModerationJson: Json = Json { ignoreUnknownKeys = true; isLenient = true }
