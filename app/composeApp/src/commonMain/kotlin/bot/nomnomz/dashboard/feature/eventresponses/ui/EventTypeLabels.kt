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
import nomnomzbot.composeapp.generated.resources.event_type_channel_ad_break_begin
import nomnomzbot.composeapp.generated.resources.event_type_channel_ad_break_end
import nomnomzbot.composeapp.generated.resources.event_type_channel_ban
import nomnomzbot.composeapp.generated.resources.event_type_channel_cheer
import nomnomzbot.composeapp.generated.resources.event_type_channel_follow
import nomnomzbot.composeapp.generated.resources.event_type_channel_points_redemption
import nomnomzbot.composeapp.generated.resources.event_type_channel_poll_begin
import nomnomzbot.composeapp.generated.resources.event_type_channel_poll_end
import nomnomzbot.composeapp.generated.resources.event_type_channel_prediction_begin
import nomnomzbot.composeapp.generated.resources.event_type_channel_raid
import nomnomzbot.composeapp.generated.resources.event_type_channel_raid_out
import nomnomzbot.composeapp.generated.resources.event_type_channel_raid_start
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscribe
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_gift
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_gift_anonymous
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_gift_received
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_gift_received_anonymous
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_message
import nomnomzbot.composeapp.generated.resources.event_type_channel_moderator_add
import nomnomzbot.composeapp.generated.resources.event_type_channel_moderator_remove
import nomnomzbot.composeapp.generated.resources.event_type_channel_unban
import nomnomzbot.composeapp.generated.resources.event_type_engagement_first_time_chatter
import nomnomzbot.composeapp.generated.resources.event_type_engagement_modiversary
import nomnomzbot.composeapp.generated.resources.event_type_engagement_returning_chatter
import nomnomzbot.composeapp.generated.resources.event_type_engagement_session_first_message
import nomnomzbot.composeapp.generated.resources.event_type_engagement_watch_streak
import nomnomzbot.composeapp.generated.resources.event_type_obs_record_state_changed
import nomnomzbot.composeapp.generated.resources.event_type_obs_replay_buffer_saved
import nomnomzbot.composeapp.generated.resources.event_type_obs_scene_changed
import nomnomzbot.composeapp.generated.resources.event_type_obs_stream_state_changed
import nomnomzbot.composeapp.generated.resources.event_type_obs_vendor_event
import nomnomzbot.composeapp.generated.resources.event_type_reward_disabled
import nomnomzbot.composeapp.generated.resources.event_type_reward_enabled
import nomnomzbot.composeapp.generated.resources.event_type_reward_paused
import nomnomzbot.composeapp.generated.resources.event_type_reward_resumed
import nomnomzbot.composeapp.generated.resources.event_type_stream_offline
import nomnomzbot.composeapp.generated.resources.event_type_stream_online
import nomnomzbot.composeapp.generated.resources.event_type_supporter_any
import nomnomzbot.composeapp.generated.resources.event_type_supporter_charity
import nomnomzbot.composeapp.generated.resources.event_type_supporter_membership
import nomnomzbot.composeapp.generated.resources.event_type_supporter_merch
import nomnomzbot.composeapp.generated.resources.event_type_supporter_tip
import nomnomzbot.composeapp.generated.resources.event_type_unknown
import nomnomzbot.composeapp.generated.resources.event_type_vts_hotkey_triggered
import nomnomzbot.composeapp.generated.resources.event_type_vts_model_clicked
import nomnomzbot.composeapp.generated.resources.event_type_vts_model_loaded
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

/**
 * The friendly name of every event type the backend catalogue serves (`EventResponsePresetCatalog.EventTypes`),
 * keyed by the wire event type. A test reads the backend catalogue and fails when a type is missing here, so a
 * new backend event can never reach the list as a raw key.
 */
internal val EventTypeLabels: Map<String, StringResource> =
    mapOf(
        "channel.follow" to Res.string.event_type_channel_follow,
        "channel.subscribe" to Res.string.event_type_channel_subscribe,
        "channel.subscription.gift" to Res.string.event_type_channel_subscription_gift,
        "channel.subscription.gift.received" to Res.string.event_type_channel_subscription_gift_received,
        "channel.subscription.gift.anonymous" to Res.string.event_type_channel_subscription_gift_anonymous,
        "channel.subscription.gift.received.anonymous" to
            Res.string.event_type_channel_subscription_gift_received_anonymous,
        "channel.subscription.message" to Res.string.event_type_channel_subscription_message,
        "channel.cheer" to Res.string.event_type_channel_cheer,
        "channel.raid" to Res.string.event_type_channel_raid,
        "channel.raid.start" to Res.string.event_type_channel_raid_start,
        "channel.raid.out" to Res.string.event_type_channel_raid_out,
        "stream.online" to Res.string.event_type_stream_online,
        "stream.offline" to Res.string.event_type_stream_offline,
        "channel.poll.begin" to Res.string.event_type_channel_poll_begin,
        "channel.poll.end" to Res.string.event_type_channel_poll_end,
        "channel.prediction.begin" to Res.string.event_type_channel_prediction_begin,
        "channel.channel_points_custom_reward_redemption.add" to Res.string.event_type_channel_points_redemption,
        "reward.paused" to Res.string.event_type_reward_paused,
        "reward.resumed" to Res.string.event_type_reward_resumed,
        "reward.enabled" to Res.string.event_type_reward_enabled,
        "reward.disabled" to Res.string.event_type_reward_disabled,
        "channel.ad_break.begin" to Res.string.event_type_channel_ad_break_begin,
        "channel.ad_break.end" to Res.string.event_type_channel_ad_break_end,
        "channel.ban" to Res.string.event_type_channel_ban,
        "channel.unban" to Res.string.event_type_channel_unban,
        "channel.moderator.add" to Res.string.event_type_channel_moderator_add,
        "channel.moderator.remove" to Res.string.event_type_channel_moderator_remove,
        "engagement.first_time_chatter" to Res.string.event_type_engagement_first_time_chatter,
        "engagement.returning_chatter" to Res.string.event_type_engagement_returning_chatter,
        "engagement.watch_streak" to Res.string.event_type_engagement_watch_streak,
        "engagement.session_first_message" to Res.string.event_type_engagement_session_first_message,
        "engagement.modiversary" to Res.string.event_type_engagement_modiversary,
        "supporter.tip" to Res.string.event_type_supporter_tip,
        "supporter.membership" to Res.string.event_type_supporter_membership,
        "supporter.merch" to Res.string.event_type_supporter_merch,
        "supporter.charity" to Res.string.event_type_supporter_charity,
        "supporter.any" to Res.string.event_type_supporter_any,
        "obs.CurrentProgramSceneChanged" to Res.string.event_type_obs_scene_changed,
        "obs.StreamStateChanged" to Res.string.event_type_obs_stream_state_changed,
        "obs.RecordStateChanged" to Res.string.event_type_obs_record_state_changed,
        "obs.ReplayBufferSaved" to Res.string.event_type_obs_replay_buffer_saved,
        "obs.VendorEvent" to Res.string.event_type_obs_vendor_event,
        "vts.ModelLoadedEvent" to Res.string.event_type_vts_model_loaded,
        "vts.HotkeyTriggeredEvent" to Res.string.event_type_vts_hotkey_triggered,
        "vts.ModelClickedEvent" to Res.string.event_type_vts_model_clicked,
    )

/** The display name of a channel event type — shared by the Event Responses page and the admin template form. */
@Composable
internal fun String.toEventLabel(): String =
    EventTypeLabels[this]?.let { label: StringResource -> stringResource(label) }
        ?: stringResource(Res.string.event_type_unknown, this)
