// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chat.ui

import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue

/** Why a live chat feed stops following its newest line. Later slices plug [UserCard] and [KeyboardSelection] in. */
enum class FeedPauseReason {
    /** A line's "..." menu, or a confirm / ban / report dialog opened from it, is open. */
    LineMenu,
    /** The user card is open. */
    UserCard,
    /** Keyboard mode has a selected line. */
    KeyboardSelection,
}

/**
 * The follow-the-newest-line state shared by Chat and Multi-Chat.
 *
 * The feed follows (snaps to its newest line) only when nothing holds it: the pointer is not over the feed (and has
 * been gone for [resumeDelayMs]), the user has not scrolled up, and no [FeedPauseReason] is active. While it is held,
 * [newLineCount] tells how many lines arrived since the feed last showed its tail.
 *
 * Plain state with an injectable [clock] (milliseconds) so the grace period is testable without real time. The
 * flags are Compose state, so a composable reading [isFollowing] recomposes when one changes; the grace period
 * ends by time alone, so the host calls [refresh] once [resumeDelayMs] has passed.
 */
@Stable
class FeedFollowState(
    private val clock: () -> Long,
    val resumeDelayMs: Long = DEFAULT_RESUME_DELAY_MS,
) {
    private var pointerOver: Boolean by mutableStateOf(false)
    private var pointerLeftAt: Long? by mutableStateOf(null)
    private var scrolledUp: Boolean by mutableStateOf(false)
    private var pauses: Map<FeedPauseReason, Int> by mutableStateOf(emptyMap())
    private var seenTailKey: String? by mutableStateOf(null)
    private var graceTick: Int by mutableStateOf(0)

    /** True while the pointer is over the feed. */
    val isPointerOver: Boolean
        get() = pointerOver

    /** True when the feed should snap to its newest line right now. */
    val isFollowing: Boolean
        get() {
            graceTick // read, so the grace period ending recomposes readers
            if (pointerOver || scrolledUp || pauses.isNotEmpty()) return false
            val left: Long = pointerLeftAt ?: return true
            return clock() - left >= resumeDelayMs
        }

    fun onPointerEnter() {
        pointerOver = true
        pointerLeftAt = null
    }

    fun onPointerExit() {
        pointerOver = false
        pointerLeftAt = clock()
    }

    /** The user moved the feed toward older lines (wheel up, drag down). Following stays off until they return. */
    fun onUserScrolledUp() {
        scrolledUp = true
    }

    /** The user scrolled back to the bottom of the feed. */
    fun onUserReachedBottom() {
        scrolledUp = false
    }

    /** Hold the feed for [reason] (call [releasePause] when it ends). Reasons count, so overlapping holders are safe. */
    fun acquirePause(reason: FeedPauseReason) {
        pauses = pauses + (reason to ((pauses[reason] ?: 0) + 1))
    }

    fun releasePause(reason: FeedPauseReason) {
        val remaining: Int = (pauses[reason] ?: 0) - 1
        pauses = if (remaining > 0) pauses + (reason to remaining) else pauses - reason
    }

    /** Wake readers of [isFollowing] after the grace period, which ends by time rather than by a state write. */
    fun refresh() {
        graceTick++
    }

    /** The feed now shows its newest line, whose key is [tailKey]. */
    fun markCaughtUp(tailKey: String?) {
        seenTailKey = tailKey
    }

    /** The pill was clicked: drop the scrolled-up hold so the feed can jump to its tail. */
    fun resume() {
        scrolledUp = false
    }

    /**
     * Lines that arrived after the last line the feed showed. [keys] is the feed oldest-first. When the last-seen
     * line has scrolled out of a capped buffer, every line counts as new.
     */
    fun newLineCount(keys: List<String>): Int {
        val seen: String = seenTailKey ?: return 0
        val index: Int = keys.lastIndexOf(seen)
        return if (index < 0) keys.size else keys.lastIndex - index
    }

    companion object {
        const val DEFAULT_RESUME_DELAY_MS: Long = 3_000L
    }
}
