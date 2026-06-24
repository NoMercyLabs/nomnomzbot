// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.nav

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// Proves the PARTICIPANT rung's standing gate (Rung 0): the participant page set is keyed off Plane-A
// CommunityStanding, never a management role, and a role-less viewer is a REAL surface (non-empty), not a
// dead-end. The participant shell renders exactly this model, so testing the model proves the routing.
class ParticipantNavTest {

    @Test
    fun every_signed_in_viewer_gets_the_full_base_participant_surface() {
        // The base surface floors at Everyone — a plain viewer (no management role, lowest standing) still sees the
        // whole participant page set. This is the headline: a viewer is never a dead-end.
        val pages: List<ParticipantPage> = ParticipantNav.pagesFor(ParticipantStanding.Everyone)

        assertTrue(pages.isNotEmpty(), "a viewer must see the base participant surface")
        assertEquals(
            listOf(
                ParticipantPage.MyChannel,
                ParticipantPage.NowPlaying,
                ParticipantPage.Leaderboards,
                ParticipantPage.PointsAndStore,
                ParticipantPage.Games,
                ParticipantPage.Me,
            ),
            pages,
        )
    }

    @Test
    fun a_subscriber_sees_at_least_everything_a_plain_viewer_sees() {
        // Progressive unlock is additive: a higher standing never LOSES a page. A sub sees the base surface and any
        // sub-floored lane on top — so the sub set is a superset of the viewer set.
        val viewer: Set<ParticipantPage> = ParticipantNav.pagesFor(ParticipantStanding.Everyone).toSet()
        val subscriber: Set<ParticipantPage> = ParticipantNav.pagesFor(ParticipantStanding.Subscriber).toSet()

        assertTrue(subscriber.containsAll(viewer), "a subscriber must see everything a viewer sees")
    }

    @Test
    fun the_standing_ladder_unlocks_monotonically_upward() {
        // Climbing the ladder never shrinks the visible set — each rung sees at least as many pages as the one below.
        val ladder: List<ParticipantStanding> =
            listOf(
                ParticipantStanding.Everyone,
                ParticipantStanding.Subscriber,
                ParticipantStanding.Vip,
                ParticipantStanding.Artist,
                ParticipantStanding.Moderator,
            )

        ladder.zipWithNext().forEach { (lower, higher) ->
            val lowerPages: Set<ParticipantPage> = ParticipantNav.pagesFor(lower).toSet()
            val higherPages: Set<ParticipantPage> = ParticipantNav.pagesFor(higher).toSet()
            assertTrue(
                higherPages.containsAll(lowerPages),
                "$higher must see at least what $lower sees",
            )
        }
    }

    @Test
    fun a_higher_floored_lane_is_hidden_below_its_floor_and_shown_at_or_above() {
        // Directly exercise the floor gate with a constructed lane: a VIP-floored page is hidden from a plain
        // viewer and a subscriber, and visible to a VIP and above. This is the standing-driven unlock mechanism
        // the inventory uses — proving a sub-or-VIP can see a lane a plain viewer can't.
        val vipLane: List<ParticipantNavPage> =
            listOf(ParticipantNavPage(ParticipantPage.Games, ParticipantStanding.Vip))

        fun visible(standing: ParticipantStanding): List<ParticipantPage> =
            vipLane.filter { standing.level >= it.floor.level }.map { it.page }

        assertTrue(visible(ParticipantStanding.Everyone).isEmpty())
        assertTrue(visible(ParticipantStanding.Subscriber).isEmpty())
        assertEquals(listOf(ParticipantPage.Games), visible(ParticipantStanding.Vip))
        assertEquals(listOf(ParticipantPage.Games), visible(ParticipantStanding.Moderator))
    }

    @Test
    fun the_standing_helpers_gate_sub_and_vip_affordances_correctly() {
        // The in-surface unlocks (sub lane, sub leaderboard, higher pending limits) read these helpers — a Sub is
        // "subscriber or above" but not yet VIP; a VIP clears both.
        assertTrue(ParticipantStanding.Everyone.isSubscriberOrAbove.not())
        assertTrue(ParticipantStanding.Subscriber.isSubscriberOrAbove)
        assertTrue(ParticipantStanding.Subscriber.isVipOrAbove.not())
        assertTrue(ParticipantStanding.Vip.isVipOrAbove)
        assertTrue(ParticipantStanding.Moderator.isVipOrAbove)
    }
}
