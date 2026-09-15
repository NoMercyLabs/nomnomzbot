// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mediashare.ui

import kotlin.test.Test
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * S-OBS-09: a "playing" queue row (the overlay/player has dequeued it but not yet reported it finished)
 * used to be non-actionable — no skip/mark-played button — so once anything dequeued it via GetNext it
 * had no way back out of the queue and lingered forever. These pure gates back [QueueRow]'s buttons.
 */
class MediaShareRowActionsTest {
    @Test
    fun playing_rows_are_actionable_so_they_are_never_a_dead_end() {
        assertTrue(mediaShareRowIsActionable("playing"))
    }

    @Test
    fun playing_rows_show_the_mark_played_button() {
        assertTrue(mediaShareRowShowsMarkPlayed("playing"))
    }

    @Test
    fun approved_rows_remain_actionable_with_mark_played() {
        assertTrue(mediaShareRowIsActionable("approved"))
        assertTrue(mediaShareRowShowsMarkPlayed("approved"))
    }

    @Test
    fun pending_rows_are_actionable_but_have_no_mark_played_button_yet() {
        assertTrue(mediaShareRowIsActionable("pending"))
        assertFalse(mediaShareRowShowsMarkPlayed("pending"))
    }

    @Test
    fun resolved_rows_are_not_actionable() {
        for (status in listOf("played", "rejected", "skipped")) {
            assertFalse(mediaShareRowIsActionable(status), "status=$status should not be actionable")
            assertFalse(mediaShareRowShowsMarkPlayed(status), "status=$status should not show mark-played")
        }
    }
}
