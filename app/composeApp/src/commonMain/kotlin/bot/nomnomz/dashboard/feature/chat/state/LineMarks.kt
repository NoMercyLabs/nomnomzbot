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

/**
 * Marks keyed by message id. A timeout, ban or purge marks the author's lines that exist in the feed at that
 * moment — never the author's later lines, which the action did not touch.
 */
data class LineMarks(
    val byMessageId: Map<String, LineMark> = emptyMap(),
) {
    fun forMessage(message: ChatMessage): LineMark? = byMessageId[message.id]

    /** A deletion mark; a named push upgrades an unnamed one, and an unnamed redelivery never erases a name. */
    fun withDeleted(messageId: String, byDisplayName: String): LineMarks {
        if (messageId.isBlank()) return this
        val existing: LineMark? = byMessageId[messageId]
        val name: String = byDisplayName.ifBlank { existing?.byDisplayName.orEmpty() }
        return copy(byMessageId = byMessageId + (messageId to LineMark(LineMarkKind.Deleted, name)))
    }

    /**
     * A timeout or ban from a mod-action push, applied to the target's lines in [lines] (the feed now, already
     * limited to the action's channel). Other actions (unban, ...) add nothing: past marks stay as they were.
     * A deleted line keeps its deletion mark.
     */
    fun withModAction(action: HubModAction, lines: List<ChatMessage>): LineMarks {
        val kind: LineMarkKind =
            when (action.action.lowercase()) {
                "timeout" -> LineMarkKind.TimedOut
                "ban" -> LineMarkKind.Banned
                else -> return this
            }
        val mark = LineMark(kind, action.moderatorDisplayName.orEmpty(), action.durationSeconds)
        return markAuthorLines(lines, action.targetUserId, { existing -> existing?.kind != LineMarkKind.Deleted }) { mark }
    }

    /** A per-chatter purge with no detail: only marks lines that carry no mark yet, so it never downgrades. */
    fun withPurge(userId: String, lines: List<ChatMessage>): LineMarks =
        markAuthorLines(lines, userId, { existing -> existing == null }) { LineMark(LineMarkKind.Cleared) }

    private fun markAuthorLines(
        lines: List<ChatMessage>,
        userId: String,
        mayReplace: (LineMark?) -> Boolean,
        mark: () -> LineMark,
    ): LineMarks {
        if (userId.isBlank()) return this
        val added: Map<String, LineMark> =
            lines
                .filter { it.userId == userId && it.id.isNotBlank() && mayReplace(byMessageId[it.id]) }
                .associate { it.id to mark() }
        return if (added.isEmpty()) this else copy(byMessageId = byMessageId + added)
    }
}
