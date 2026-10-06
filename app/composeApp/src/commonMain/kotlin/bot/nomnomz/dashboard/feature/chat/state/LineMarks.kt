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

import bot.nomnomz.dashboard.core.network.ChatMessage
import bot.nomnomz.dashboard.core.realtime.HubModAction

// A moderated line stays in the feed (a moderator keeps the context) and carries a mark that says what happened
// and who did it. The marks live beside the feed in both Ready states — the ChatMessage wire DTO is unchanged.

enum class LineMarkKind { Deleted, TimedOut, Banned, Cleared }

/** What happened to a line. [byDisplayName] is empty until the server names the moderator. */
data class LineMark(
    val kind: LineMarkKind,
    val byDisplayName: String = "",
    val durationSeconds: Int? = null,
)

/** Marks keyed by message id (one deleted line) and by user id (a timeout, ban or purge hits every line). */
data class LineMarks(
    val byMessageId: Map<String, LineMark> = emptyMap(),
    val byUserId: Map<String, LineMark> = emptyMap(),
) {
    /** The mark for [message]: its own deletion wins over a mark on its author. */
    fun forMessage(message: ChatMessage): LineMark? =
        byMessageId[message.id] ?: message.userId.takeIf { it.isNotBlank() }?.let { byUserId[it] }

    /** A deletion mark; a named push upgrades an unnamed one, and an unnamed redelivery never erases a name. */
    fun withDeleted(messageId: String, byDisplayName: String): LineMarks {
        if (messageId.isBlank()) return this
        val existing: LineMark? = byMessageId[messageId]
        val name: String = byDisplayName.ifBlank { existing?.byDisplayName.orEmpty() }
        return copy(byMessageId = byMessageId + (messageId to LineMark(LineMarkKind.Deleted, name)))
    }

    /** A timeout or ban from a mod-action push. Other actions (unban, ...) leave the lines alone. */
    fun withModAction(action: HubModAction): LineMarks {
        if (action.targetUserId.isBlank()) return this
        val kind: LineMarkKind =
            when (action.action.lowercase()) {
                "timeout" -> LineMarkKind.TimedOut
                "ban" -> LineMarkKind.Banned
                else -> return this
            }
        val mark = LineMark(kind, action.moderatorDisplayName.orEmpty(), action.durationSeconds)
        return copy(byUserId = byUserId + (action.targetUserId to mark))
    }

    /** A per-chatter purge with no detail: never replaces a richer timeout/ban mark. */
    fun withPurge(userId: String): LineMarks {
        if (userId.isBlank() || byUserId.containsKey(userId)) return this
        return copy(byUserId = byUserId + (userId to LineMark(LineMarkKind.Cleared)))
    }
}
