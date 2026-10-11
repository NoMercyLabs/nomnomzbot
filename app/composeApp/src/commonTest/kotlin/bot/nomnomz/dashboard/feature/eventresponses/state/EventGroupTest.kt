// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.state

import bot.nomnomz.dashboard.core.network.EventResponseSummary
import bot.nomnomz.dashboard.feature.eventresponses.ui.EventTypeLabels
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue

class EventGroupTest {
    @Test
    fun everyLabelledEventTypeBelongsToAGroup() {
        val ungrouped: List<String> = EventTypeLabels.keys.filter { eventGroupOf(it) == null }
        assertTrue(ungrouped.isEmpty(), "event types without a group: $ungrouped")
    }

    @Test
    fun everyGroupHoldsAtLeastOneEventType() {
        val used: Set<EventGroup> = EventTypeLabels.keys.mapNotNull { eventGroupOf(it) }.toSet()
        assertEquals(EventGroup.entries.toSet(), used)
    }

    @Test
    fun relatedTypesShareAGroup() {
        assertEquals(EventGroup.FollowsAndSubs, eventGroupOf("channel.follow"))
        assertEquals(EventGroup.FollowsAndSubs, eventGroupOf("channel.subscription.gift.received.anonymous"))
        assertEquals(EventGroup.CheersAndBits, eventGroupOf("channel.cheer"))
        assertEquals(EventGroup.RaidsAndShoutouts, eventGroupOf("channel.raid.out"))
        assertEquals(EventGroup.StreamStatus, eventGroupOf("stream.offline"))
        assertEquals(EventGroup.PollsAndPredictions, eventGroupOf("channel.prediction.begin"))
        assertEquals(EventGroup.ChannelPoints, eventGroupOf("reward.paused"))
        assertEquals(EventGroup.Ads, eventGroupOf("channel.ad_break.upcoming"))
        assertEquals(EventGroup.Moderation, eventGroupOf("channel.moderator.add"))
        assertEquals(EventGroup.Engagement, eventGroupOf("engagement.watch_streak"))
        assertEquals(EventGroup.Supporters, eventGroupOf("supporter.tip"))
        assertEquals(EventGroup.ObsAndVtubeStudio, eventGroupOf("vts.ModelClickedEvent"))
    }

    @Test
    fun anUnknownTypeHasNoGroup() {
        assertNull(eventGroupOf("channel.something_new"))
    }

    private fun row(type: String): EventResponseSummary = EventResponseSummary(id = "id-$type", eventType = type)

    @Test
    fun allViewSortsRowsIntoGroupSectionsInGroupOrderWithUnknownLast() {
        val rows: List<EventResponseSummary> =
            listOf(row("channel.something_new"), row("stream.online"), row("channel.follow"), row("channel.cheer"))
        val sections: List<EventGroupSection> = sectionsOf(rows, filter = null)
        assertEquals(
            listOf(EventGroup.FollowsAndSubs, EventGroup.CheersAndBits, EventGroup.StreamStatus, null),
            sections.map { it.group },
        )
        assertEquals(listOf("channel.something_new"), sections.last().responses.map { it.eventType })
    }

    @Test
    fun filterKeepsOnlyTheChosenGroupsRows() {
        val rows: List<EventResponseSummary> =
            listOf(row("channel.follow"), row("channel.subscribe"), row("stream.online"), row("channel.something_new"))
        val sections: List<EventGroupSection> = sectionsOf(rows, filter = EventGroup.FollowsAndSubs)
        assertEquals(1, sections.size)
        assertEquals(listOf("channel.follow", "channel.subscribe"), sections.single().responses.map { it.eventType })
    }

    @Test
    fun filterOnAGroupWithNoRowsGivesNoSections() {
        assertTrue(sectionsOf(listOf(row("channel.follow")), filter = EventGroup.Ads).isEmpty())
    }
}
