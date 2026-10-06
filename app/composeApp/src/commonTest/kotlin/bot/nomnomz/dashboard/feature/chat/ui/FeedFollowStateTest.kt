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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

class FeedFollowStateTest {
    private var now: Long = 0L
    private val state: FeedFollowState = FeedFollowState(clock = { now })

    @Test
    fun follows_by_default() {
        assertTrue(state.isFollowing)
    }

    @Test
    fun pointer_over_pauses_and_following_resumes_three_seconds_after_it_leaves() {
        state.onPointerEnter()
        assertFalse(state.isFollowing)

        now = 10_000L
        state.onPointerExit()
        assertFalse(state.isFollowing, "still inside the grace period")

        now = 12_999L
        assertFalse(state.isFollowing, "one millisecond short of 3 s")

        now = 13_000L
        assertTrue(state.isFollowing)
    }

    @Test
    fun pointer_returning_inside_the_grace_period_cancels_the_resume() {
        state.onPointerEnter()
        now = 1_000L
        state.onPointerExit()
        now = 2_000L
        state.onPointerEnter()
        now = 60_000L
        assertFalse(state.isFollowing)
    }

    @Test
    fun scrolled_up_holds_following_even_after_the_pointer_left_long_ago() {
        state.onPointerEnter()
        state.onUserScrolledUp()
        now = 1_000L
        state.onPointerExit()
        now = 100_000L
        assertFalse(state.isFollowing)

        state.onUserReachedBottom()
        assertTrue(state.isFollowing)
    }

    @Test
    fun resume_clears_the_scrolled_up_hold() {
        state.onUserScrolledUp()
        assertFalse(state.isFollowing)
        state.resume()
        assertTrue(state.isFollowing)
    }

    @Test
    fun every_pause_reason_holds_the_feed_until_all_holders_release() {
        for (reason: FeedPauseReason in FeedPauseReason.entries) {
            state.acquirePause(reason)
            assertFalse(state.isFollowing, "$reason must pause")
            state.releasePause(reason)
            assertTrue(state.isFollowing, "$reason must release")
        }

        state.acquirePause(FeedPauseReason.LineMenu)
        state.acquirePause(FeedPauseReason.LineMenu)
        state.releasePause(FeedPauseReason.LineMenu)
        assertFalse(state.isFollowing, "a second holder of the same reason is still open")
        state.releasePause(FeedPauseReason.LineMenu)
        assertTrue(state.isFollowing)
    }

    @Test
    fun new_line_count_is_zero_until_the_feed_has_shown_a_tail() {
        assertEquals(0, state.newLineCount(listOf("a", "b")))
    }

    @Test
    fun new_line_count_counts_lines_after_the_last_shown_tail() {
        state.markCaughtUp("c")
        assertEquals(0, state.newLineCount(listOf("a", "b", "c")))
        assertEquals(27, state.newLineCount(listOf("a", "b", "c") + (1..27).map { "n$it" }))
    }

    @Test
    fun new_line_count_treats_everything_as_new_when_the_seen_tail_left_a_capped_buffer() {
        state.markCaughtUp("old")
        assertEquals(3, state.newLineCount(listOf("x", "y", "z")))
    }
}
