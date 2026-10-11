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

/** The event groups the Responses tab sorts its rows into, in the order their sections appear. */
enum class EventGroup {
    FollowsAndSubs,
    CheersAndBits,
    RaidsAndShoutouts,
    StreamStatus,
    PollsAndPredictions,
    ChannelPoints,
    Ads,
    Moderation,
    Engagement,
    Supporters,
    ObsAndVtubeStudio,
}

/** The group of a wire [eventType], or null for a type this build does not know yet. */
fun eventGroupOf(eventType: String): EventGroup? =
    when {
        eventType == "channel.follow" || eventType.startsWith("channel.subscri") -> EventGroup.FollowsAndSubs
        eventType == "channel.cheer" -> EventGroup.CheersAndBits
        eventType.startsWith("channel.raid") -> EventGroup.RaidsAndShoutouts
        eventType.startsWith("stream.") -> EventGroup.StreamStatus
        eventType.startsWith("channel.poll.") || eventType.startsWith("channel.prediction.") ->
            EventGroup.PollsAndPredictions
        eventType.startsWith("channel.channel_points_") || eventType.startsWith("reward.") ->
            EventGroup.ChannelPoints
        eventType.startsWith("channel.ad_break.") -> EventGroup.Ads
        eventType == "channel.ban" || eventType == "channel.unban" || eventType.startsWith("channel.moderator.") ->
            EventGroup.Moderation
        eventType.startsWith("engagement.") -> EventGroup.Engagement
        eventType.startsWith("supporter.") -> EventGroup.Supporters
        eventType.startsWith("obs.") || eventType.startsWith("vts.") -> EventGroup.ObsAndVtubeStudio
        else -> null
    }

/** One heading and its rows; [group] is null for the "Other events" bucket of types this build does not know. */
data class EventGroupSection(
    val group: EventGroup?,
    val responses: List<EventResponseSummary>,
)

/** Sorts [responses] into sections in group order, unknown types last; a non-null [filter] keeps only that group. */
fun sectionsOf(
    responses: List<EventResponseSummary>,
    filter: EventGroup?,
): List<EventGroupSection> {
    val byGroup: Map<EventGroup?, List<EventResponseSummary>> = responses.groupBy { eventGroupOf(it.eventType) }
    val order: List<EventGroup?> = if (filter != null) listOf(filter) else EventGroup.entries + listOf(null)
    return order.mapNotNull { group ->
        byGroup[group]?.let { rows -> EventGroupSection(group = group, responses = rows) }
    }
}
