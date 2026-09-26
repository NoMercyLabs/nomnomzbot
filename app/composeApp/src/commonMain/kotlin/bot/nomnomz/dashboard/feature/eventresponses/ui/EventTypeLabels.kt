// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.ui

import androidx.compose.runtime.Composable
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.event_type_channel_cheer
import nomnomzbot.composeapp.generated.resources.event_type_channel_follow
import nomnomzbot.composeapp.generated.resources.event_type_channel_points_redemption
import nomnomzbot.composeapp.generated.resources.event_type_channel_poll_begin
import nomnomzbot.composeapp.generated.resources.event_type_channel_prediction_begin
import nomnomzbot.composeapp.generated.resources.event_type_channel_raid
import nomnomzbot.composeapp.generated.resources.event_type_channel_raid_out
import nomnomzbot.composeapp.generated.resources.event_type_channel_raid_start
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscribe
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_gift
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_message
import nomnomzbot.composeapp.generated.resources.event_type_engagement_first_time_chatter
import nomnomzbot.composeapp.generated.resources.event_type_engagement_session_first_message
import nomnomzbot.composeapp.generated.resources.event_type_stream_offline
import nomnomzbot.composeapp.generated.resources.event_type_stream_online
import nomnomzbot.composeapp.generated.resources.event_type_unknown
import org.jetbrains.compose.resources.stringResource

/** The display name of a channel event type — shared by the Event Responses page and the admin template form. */
@Composable
internal fun String.toEventLabel(): String =
    when (this) {
        "channel.follow" -> stringResource(Res.string.event_type_channel_follow)
        "channel.subscribe" -> stringResource(Res.string.event_type_channel_subscribe)
        "channel.subscription.gift" -> stringResource(Res.string.event_type_channel_subscription_gift)
        "channel.subscription.message" -> stringResource(Res.string.event_type_channel_subscription_message)
        "channel.cheer" -> stringResource(Res.string.event_type_channel_cheer)
        "channel.raid" -> stringResource(Res.string.event_type_channel_raid)
        "channel.raid.start" -> stringResource(Res.string.event_type_channel_raid_start)
        "channel.raid.out" -> stringResource(Res.string.event_type_channel_raid_out)
        "stream.online" -> stringResource(Res.string.event_type_stream_online)
        "stream.offline" -> stringResource(Res.string.event_type_stream_offline)
        "channel.poll.begin" -> stringResource(Res.string.event_type_channel_poll_begin)
        "channel.prediction.begin" -> stringResource(Res.string.event_type_channel_prediction_begin)
        "channel.channel_points_custom_reward_redemption.add" ->
            stringResource(Res.string.event_type_channel_points_redemption)
        "engagement.session_first_message" ->
            stringResource(Res.string.event_type_engagement_session_first_message)
        "engagement.first_time_chatter" ->
            stringResource(Res.string.event_type_engagement_first_time_chatter)
        else -> stringResource(Res.string.event_type_unknown, this)
    }
