// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import org.jetbrains.compose.resources.StringResource
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.spam_group_bursts
import nomnomzbot.composeapp.generated.resources.spam_group_campaign
import nomnomzbot.composeapp.generated.resources.spam_group_content
import nomnomzbot.composeapp.generated.resources.spam_group_lockdown
import nomnomzbot.composeapp.generated.resources.spam_group_master
import nomnomzbot.composeapp.generated.resources.spam_group_network
import nomnomzbot.composeapp.generated.resources.spam_group_trust
import nomnomzbot.composeapp.generated.resources.spam_invariant_sd0_guarantee
import nomnomzbot.composeapp.generated.resources.spam_invariant_sd11_guarantee
import nomnomzbot.composeapp.generated.resources.spam_invariant_sd12_guarantee
import nomnomzbot.composeapp.generated.resources.spam_invariant_sd8_guarantee
import nomnomzbot.composeapp.generated.resources.spam_invariant_sd9_guarantee
import nomnomzbot.composeapp.generated.resources.spam_setting_action_delay_seconds_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_action_delay_seconds_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_action_delay_seconds_label
import nomnomzbot.composeapp.generated.resources.spam_setting_auto_reverse_on_dequalify_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_auto_reverse_on_dequalify_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_auto_reverse_on_dequalify_label
import nomnomzbot.composeapp.generated.resources.spam_setting_dequalify_below_qualify
import nomnomzbot.composeapp.generated.resources.spam_setting_dequalify_no_standing_share_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_dequalify_no_standing_share_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_dequalify_no_standing_share_label
import nomnomzbot.composeapp.generated.resources.spam_setting_dry_run_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_dry_run_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_dry_run_label
import nomnomzbot.composeapp.generated.resources.spam_setting_follow_spike_factor_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_follow_spike_factor_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_follow_spike_factor_label
import nomnomzbot.composeapp.generated.resources.spam_setting_is_enabled_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_is_enabled_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_is_enabled_label
import nomnomzbot.composeapp.generated.resources.spam_setting_join_burst_factor_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_join_burst_factor_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_join_burst_factor_label
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_auto_extend_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_auto_extend_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_auto_extend_label
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_max_minutes_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_max_minutes_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_max_minutes_label
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_minutes_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_minutes_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_lockdown_minutes_label
import nomnomzbot.composeapp.generated.resources.spam_setting_max_window_seconds_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_max_window_seconds_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_max_window_seconds_label
import nomnomzbot.composeapp.generated.resources.spam_setting_minimum_cohort_size_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_minimum_cohort_size_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_minimum_cohort_size_label
import nomnomzbot.composeapp.generated.resources.spam_setting_minimum_skeleton_length_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_minimum_skeleton_length_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_minimum_skeleton_length_label
import nomnomzbot.composeapp.generated.resources.spam_setting_near_duplicate_similarity_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_near_duplicate_similarity_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_near_duplicate_similarity_label
import nomnomzbot.composeapp.generated.resources.spam_setting_network_contribute_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_network_contribute_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_network_contribute_label
import nomnomzbot.composeapp.generated.resources.spam_setting_network_subscribe_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_network_subscribe_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_network_subscribe_label
import nomnomzbot.composeapp.generated.resources.spam_setting_non_latin_script_gate_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_non_latin_script_gate_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_non_latin_script_gate_label
import nomnomzbot.composeapp.generated.resources.spam_setting_qualify_no_standing_share_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_qualify_no_standing_share_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_qualify_no_standing_share_label
import nomnomzbot.composeapp.generated.resources.spam_setting_required_corroborations_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_required_corroborations_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_required_corroborations_label
import nomnomzbot.composeapp.generated.resources.spam_setting_semi_trusted_watch_hours_here_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_semi_trusted_watch_hours_here_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_semi_trusted_watch_hours_here_label
import nomnomzbot.composeapp.generated.resources.spam_setting_semi_trusted_watch_hours_instance_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_semi_trusted_watch_hours_instance_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_semi_trusted_watch_hours_instance_label
import nomnomzbot.composeapp.generated.resources.spam_setting_trust_thresholds_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_trust_thresholds_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_trust_thresholds_label
import nomnomzbot.composeapp.generated.resources.spam_setting_window_seconds_cost
import nomnomzbot.composeapp.generated.resources.spam_setting_window_seconds_explanation
import nomnomzbot.composeapp.generated.resources.spam_setting_window_seconds_label

// The backend sends resource KEYS, never words, because the product ships in English and Dutch and a
// server that returned sentences would show a Dutch streamer English. Compose Resources resolves
// strings STATICALLY, though — there is no lookup-by-name — so the two have to meet in an explicit map.
//
// That constraint turns out to be useful: this map is the single place where a key the server can send
// either has copy or does not, and SpamDefenseCopyTest asserts it matches the string resources in both
// directions. A knob added to the backend with no copy here fails the build rather than rendering a raw
// key like "spam_setting_action_delay_seconds_label" at somebody mid-raid.
object SpamDefenseCopy {

    val byKey: Map<String, StringResource> =
        mapOf(
            "spam_group_bursts" to Res.string.spam_group_bursts,
            "spam_group_campaign" to Res.string.spam_group_campaign,
            "spam_group_content" to Res.string.spam_group_content,
            "spam_group_lockdown" to Res.string.spam_group_lockdown,
            "spam_group_master" to Res.string.spam_group_master,
            "spam_group_network" to Res.string.spam_group_network,
            "spam_group_trust" to Res.string.spam_group_trust,
            "spam_invariant_sd0_guarantee" to Res.string.spam_invariant_sd0_guarantee,
            "spam_invariant_sd11_guarantee" to Res.string.spam_invariant_sd11_guarantee,
            "spam_invariant_sd12_guarantee" to Res.string.spam_invariant_sd12_guarantee,
            "spam_invariant_sd8_guarantee" to Res.string.spam_invariant_sd8_guarantee,
            "spam_invariant_sd9_guarantee" to Res.string.spam_invariant_sd9_guarantee,
            "spam_setting_action_delay_seconds_cost" to Res.string.spam_setting_action_delay_seconds_cost,
            "spam_setting_action_delay_seconds_explanation" to Res.string.spam_setting_action_delay_seconds_explanation,
            "spam_setting_action_delay_seconds_label" to Res.string.spam_setting_action_delay_seconds_label,
            "spam_setting_auto_reverse_on_dequalify_cost" to Res.string.spam_setting_auto_reverse_on_dequalify_cost,
            "spam_setting_auto_reverse_on_dequalify_explanation" to Res.string.spam_setting_auto_reverse_on_dequalify_explanation,
            "spam_setting_auto_reverse_on_dequalify_label" to Res.string.spam_setting_auto_reverse_on_dequalify_label,
            "spam_setting_dequalify_below_qualify" to Res.string.spam_setting_dequalify_below_qualify,
            "spam_setting_dequalify_no_standing_share_cost" to Res.string.spam_setting_dequalify_no_standing_share_cost,
            "spam_setting_dequalify_no_standing_share_explanation" to Res.string.spam_setting_dequalify_no_standing_share_explanation,
            "spam_setting_dequalify_no_standing_share_label" to Res.string.spam_setting_dequalify_no_standing_share_label,
            "spam_setting_dry_run_cost" to Res.string.spam_setting_dry_run_cost,
            "spam_setting_dry_run_explanation" to Res.string.spam_setting_dry_run_explanation,
            "spam_setting_dry_run_label" to Res.string.spam_setting_dry_run_label,
            "spam_setting_follow_spike_factor_cost" to Res.string.spam_setting_follow_spike_factor_cost,
            "spam_setting_follow_spike_factor_explanation" to Res.string.spam_setting_follow_spike_factor_explanation,
            "spam_setting_follow_spike_factor_label" to Res.string.spam_setting_follow_spike_factor_label,
            "spam_setting_is_enabled_cost" to Res.string.spam_setting_is_enabled_cost,
            "spam_setting_is_enabled_explanation" to Res.string.spam_setting_is_enabled_explanation,
            "spam_setting_is_enabled_label" to Res.string.spam_setting_is_enabled_label,
            "spam_setting_join_burst_factor_cost" to Res.string.spam_setting_join_burst_factor_cost,
            "spam_setting_join_burst_factor_explanation" to Res.string.spam_setting_join_burst_factor_explanation,
            "spam_setting_join_burst_factor_label" to Res.string.spam_setting_join_burst_factor_label,
            "spam_setting_lockdown_auto_extend_cost" to Res.string.spam_setting_lockdown_auto_extend_cost,
            "spam_setting_lockdown_auto_extend_explanation" to Res.string.spam_setting_lockdown_auto_extend_explanation,
            "spam_setting_lockdown_auto_extend_label" to Res.string.spam_setting_lockdown_auto_extend_label,
            "spam_setting_lockdown_max_minutes_cost" to Res.string.spam_setting_lockdown_max_minutes_cost,
            "spam_setting_lockdown_max_minutes_explanation" to Res.string.spam_setting_lockdown_max_minutes_explanation,
            "spam_setting_lockdown_max_minutes_label" to Res.string.spam_setting_lockdown_max_minutes_label,
            "spam_setting_lockdown_minutes_cost" to Res.string.spam_setting_lockdown_minutes_cost,
            "spam_setting_lockdown_minutes_explanation" to Res.string.spam_setting_lockdown_minutes_explanation,
            "spam_setting_lockdown_minutes_label" to Res.string.spam_setting_lockdown_minutes_label,
            "spam_setting_max_window_seconds_cost" to Res.string.spam_setting_max_window_seconds_cost,
            "spam_setting_max_window_seconds_explanation" to Res.string.spam_setting_max_window_seconds_explanation,
            "spam_setting_max_window_seconds_label" to Res.string.spam_setting_max_window_seconds_label,
            "spam_setting_minimum_cohort_size_cost" to Res.string.spam_setting_minimum_cohort_size_cost,
            "spam_setting_minimum_cohort_size_explanation" to Res.string.spam_setting_minimum_cohort_size_explanation,
            "spam_setting_minimum_cohort_size_label" to Res.string.spam_setting_minimum_cohort_size_label,
            "spam_setting_minimum_skeleton_length_cost" to Res.string.spam_setting_minimum_skeleton_length_cost,
            "spam_setting_minimum_skeleton_length_explanation" to Res.string.spam_setting_minimum_skeleton_length_explanation,
            "spam_setting_minimum_skeleton_length_label" to Res.string.spam_setting_minimum_skeleton_length_label,
            "spam_setting_near_duplicate_similarity_cost" to Res.string.spam_setting_near_duplicate_similarity_cost,
            "spam_setting_near_duplicate_similarity_explanation" to Res.string.spam_setting_near_duplicate_similarity_explanation,
            "spam_setting_near_duplicate_similarity_label" to Res.string.spam_setting_near_duplicate_similarity_label,
            "spam_setting_network_contribute_cost" to Res.string.spam_setting_network_contribute_cost,
            "spam_setting_network_contribute_explanation" to Res.string.spam_setting_network_contribute_explanation,
            "spam_setting_network_contribute_label" to Res.string.spam_setting_network_contribute_label,
            "spam_setting_network_subscribe_cost" to Res.string.spam_setting_network_subscribe_cost,
            "spam_setting_network_subscribe_explanation" to Res.string.spam_setting_network_subscribe_explanation,
            "spam_setting_network_subscribe_label" to Res.string.spam_setting_network_subscribe_label,
            "spam_setting_non_latin_script_gate_cost" to Res.string.spam_setting_non_latin_script_gate_cost,
            "spam_setting_non_latin_script_gate_explanation" to Res.string.spam_setting_non_latin_script_gate_explanation,
            "spam_setting_non_latin_script_gate_label" to Res.string.spam_setting_non_latin_script_gate_label,
            "spam_setting_qualify_no_standing_share_cost" to Res.string.spam_setting_qualify_no_standing_share_cost,
            "spam_setting_qualify_no_standing_share_explanation" to Res.string.spam_setting_qualify_no_standing_share_explanation,
            "spam_setting_qualify_no_standing_share_label" to Res.string.spam_setting_qualify_no_standing_share_label,
            "spam_setting_required_corroborations_cost" to Res.string.spam_setting_required_corroborations_cost,
            "spam_setting_required_corroborations_explanation" to Res.string.spam_setting_required_corroborations_explanation,
            "spam_setting_required_corroborations_label" to Res.string.spam_setting_required_corroborations_label,
            "spam_setting_semi_trusted_watch_hours_here_cost" to Res.string.spam_setting_semi_trusted_watch_hours_here_cost,
            "spam_setting_semi_trusted_watch_hours_here_explanation" to Res.string.spam_setting_semi_trusted_watch_hours_here_explanation,
            "spam_setting_semi_trusted_watch_hours_here_label" to Res.string.spam_setting_semi_trusted_watch_hours_here_label,
            "spam_setting_semi_trusted_watch_hours_instance_cost" to Res.string.spam_setting_semi_trusted_watch_hours_instance_cost,
            "spam_setting_semi_trusted_watch_hours_instance_explanation" to Res.string.spam_setting_semi_trusted_watch_hours_instance_explanation,
            "spam_setting_semi_trusted_watch_hours_instance_label" to Res.string.spam_setting_semi_trusted_watch_hours_instance_label,
            "spam_setting_trust_thresholds_cost" to Res.string.spam_setting_trust_thresholds_cost,
            "spam_setting_trust_thresholds_explanation" to Res.string.spam_setting_trust_thresholds_explanation,
            "spam_setting_trust_thresholds_label" to Res.string.spam_setting_trust_thresholds_label,
            "spam_setting_window_seconds_cost" to Res.string.spam_setting_window_seconds_cost,
            "spam_setting_window_seconds_explanation" to Res.string.spam_setting_window_seconds_explanation,
            "spam_setting_window_seconds_label" to Res.string.spam_setting_window_seconds_label,
        )

    /** The resource for a key, or null when the backend knows a setting this build has no copy for. */
    fun resource(key: String): StringResource? = byKey[key]
}
