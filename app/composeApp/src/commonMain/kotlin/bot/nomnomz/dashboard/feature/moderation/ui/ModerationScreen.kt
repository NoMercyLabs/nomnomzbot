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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateList
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PickerRef
import bot.nomnomz.dashboard.core.designsystem.component.SearchPickerField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcon
import bot.nomnomz.dashboard.core.designsystem.icon.HistoryGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.NetworkGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.UnlockGlyph
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.SpamDefensePolicy
import bot.nomnomz.dashboard.core.network.SpamDefenseSettings
import bot.nomnomz.dashboard.core.network.TrustPolicy
import bot.nomnomz.dashboard.core.network.TwitchAutoModSettings
import bot.nomnomz.dashboard.core.network.UpdateTrustPolicyBody
import bot.nomnomz.dashboard.core.network.UpdateTwitchAutoModSettingsBody
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.EscalationLadderStep
import bot.nomnomz.dashboard.core.network.EscalationPolicy
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.NetworkNukeBatch
import bot.nomnomz.dashboard.core.network.UnbanRequest
import bot.nomnomz.dashboard.core.network.UpsertEscalationPolicyBody
import bot.nomnomz.dashboard.core.network.ModerationQueueItem
import bot.nomnomz.dashboard.core.network.ViewerReport
import bot.nomnomz.dashboard.core.network.ChatFilter
import bot.nomnomz.dashboard.core.network.Moderator
import bot.nomnomz.dashboard.core.network.ModerationActionLog
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.ModerationStanding
import bot.nomnomz.dashboard.core.network.ModerationStats
import bot.nomnomz.dashboard.core.network.SharedBanSettings
import bot.nomnomz.dashboard.core.network.SharedBanTrustedChannel
import bot.nomnomz.dashboard.core.network.ShoutoutOverride
import bot.nomnomz.dashboard.core.network.UserModerationContext
import bot.nomnomz.dashboard.core.network.UserModerationHistorySummary
import bot.nomnomz.dashboard.core.network.UserTrustSummary
import bot.nomnomz.dashboard.core.network.UserNote
import kotlin.math.roundToInt
import bot.nomnomz.dashboard.feature.moderation.state.AutomationLine
import bot.nomnomz.dashboard.feature.moderation.state.AutomodFilter
import bot.nomnomz.dashboard.feature.moderation.state.deriveAutomationLines
import bot.nomnomz.dashboard.feature.moderation.state.ModerationController
import bot.nomnomz.dashboard.feature.moderation.state.ModerationState
import bot.nomnomz.dashboard.feature.moderation.state.UserContextState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_action_error
import nomnomzbot.composeapp.generated.resources.moderation_moderators_title
import nomnomzbot.composeapp.generated.resources.moderation_moderators_add_label
import nomnomzbot.composeapp.generated.resources.moderation_moderators_add
import nomnomzbot.composeapp.generated.resources.moderation_moderators_remove_action
import nomnomzbot.composeapp.generated.resources.moderation_moderators_remove_title
import nomnomzbot.composeapp.generated.resources.moderation_moderators_remove_message
import nomnomzbot.composeapp.generated.resources.moderation_moderators_remove_confirm
import nomnomzbot.composeapp.generated.resources.moderation_moderators_remove_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_clear_chat_action
import nomnomzbot.composeapp.generated.resources.moderation_clear_chat_title
import nomnomzbot.composeapp.generated.resources.moderation_clear_chat_message
import nomnomzbot.composeapp.generated.resources.moderation_clear_chat_confirm
import nomnomzbot.composeapp.generated.resources.moderation_clear_chat_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_stats_automod
import nomnomzbot.composeapp.generated.resources.moderation_stats_bans_today
import nomnomzbot.composeapp.generated.resources.moderation_stats_deleted
import nomnomzbot.composeapp.generated.resources.moderation_stats_timeouts
import nomnomzbot.composeapp.generated.resources.moderation_automod_caps
import nomnomzbot.composeapp.generated.resources.moderation_automod_caps_detail
import nomnomzbot.composeapp.generated.resources.moderation_automod_caps_threshold_hint
import nomnomzbot.composeapp.generated.resources.moderation_automod_caps_threshold_label
import nomnomzbot.composeapp.generated.resources.moderation_automod_disable
import nomnomzbot.composeapp.generated.resources.moderation_automod_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_automod_enable
import nomnomzbot.composeapp.generated.resources.moderation_automod_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_automod_emote_detail
import nomnomzbot.composeapp.generated.resources.moderation_automod_emote_max_hint
import nomnomzbot.composeapp.generated.resources.moderation_automod_emote_max_label
import nomnomzbot.composeapp.generated.resources.moderation_automod_emotes
import nomnomzbot.composeapp.generated.resources.moderation_automod_link
import nomnomzbot.composeapp.generated.resources.moderation_automod_off
import nomnomzbot.composeapp.generated.resources.moderation_automod_on
import nomnomzbot.composeapp.generated.resources.moderation_automod_phrases
import nomnomzbot.composeapp.generated.resources.moderation_automod_phrases_add
import nomnomzbot.composeapp.generated.resources.moderation_automod_phrases_add_label
import nomnomzbot.composeapp.generated.resources.moderation_automod_phrases_empty
import nomnomzbot.composeapp.generated.resources.moderation_automod_title
import nomnomzbot.composeapp.generated.resources.moderation_automod_whitelist_add
import nomnomzbot.composeapp.generated.resources.moderation_automod_whitelist_add_label
import nomnomzbot.composeapp.generated.resources.moderation_automod_whitelist_empty
import nomnomzbot.composeapp.generated.resources.moderation_bans_title
import nomnomzbot.composeapp.generated.resources.moderation_bans_unavailable
import nomnomzbot.composeapp.generated.resources.moderation_log_by
import nomnomzbot.composeapp.generated.resources.moderation_log_row_description
import nomnomzbot.composeapp.generated.resources.moderation_log_title
import nomnomzbot.composeapp.generated.resources.moderation_notes_add_action
import nomnomzbot.composeapp.generated.resources.moderation_notes_add_label
import nomnomzbot.composeapp.generated.resources.moderation_notes_cancel
import nomnomzbot.composeapp.generated.resources.moderation_notes_delete
import nomnomzbot.composeapp.generated.resources.moderation_notes_delete_message
import nomnomzbot.composeapp.generated.resources.moderation_notes_delete_title
import nomnomzbot.composeapp.generated.resources.moderation_notes_edit
import nomnomzbot.composeapp.generated.resources.moderation_notes_empty
import nomnomzbot.composeapp.generated.resources.moderation_notes_meta
import nomnomzbot.composeapp.generated.resources.moderation_notes_pin
import nomnomzbot.composeapp.generated.resources.moderation_notes_save
import nomnomzbot.composeapp.generated.resources.moderation_notes_title
import nomnomzbot.composeapp.generated.resources.moderation_notes_unpin
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_title
import nomnomzbot.composeapp.generated.resources.moderation_shield_unavailable
import nomnomzbot.composeapp.generated.resources.moderation_terms_add
import nomnomzbot.composeapp.generated.resources.moderation_terms_add_label
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove_action
import nomnomzbot.composeapp.generated.resources.moderation_terms_title
import nomnomzbot.composeapp.generated.resources.moderation_terms_unavailable
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_title
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_help
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_label
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_save
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_overrides_title
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_override_target_id_label
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_override_target_name_label
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_override_message_label
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_override_add
import nomnomzbot.composeapp.generated.resources.moderation_shoutout_override_remove_action
import nomnomzbot.composeapp.generated.resources.moderation_banned_by
import nomnomzbot.composeapp.generated.resources.moderation_banned_on
import nomnomzbot.composeapp.generated.resources.moderation_context_action_short
import nomnomzbot.composeapp.generated.resources.moderation_context_bans
import nomnomzbot.composeapp.generated.resources.moderation_actions_title
import nomnomzbot.composeapp.generated.resources.moderation_context_close
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_clear
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_label
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_monitor
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_restrict
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_restrict_confirm
import nomnomzbot.composeapp.generated.resources.moderation_warn_action
import nomnomzbot.composeapp.generated.resources.moderation_warn_reason
import nomnomzbot.composeapp.generated.resources.moderation_context_disclaimer
import nomnomzbot.composeapp.generated.resources.moderation_context_error
import nomnomzbot.composeapp.generated.resources.moderation_context_last
import nomnomzbot.composeapp.generated.resources.moderation_context_loading
import nomnomzbot.composeapp.generated.resources.moderation_context_recent
import nomnomzbot.composeapp.generated.resources.moderation_context_recent_empty
import nomnomzbot.composeapp.generated.resources.moderation_context_timeouts
import nomnomzbot.composeapp.generated.resources.moderation_context_title
import nomnomzbot.composeapp.generated.resources.moderation_context_unbans
import nomnomzbot.composeapp.generated.resources.moderation_context_view
import nomnomzbot.composeapp.generated.resources.moderation_context_warns
import nomnomzbot.composeapp.generated.resources.moderation_empty
import nomnomzbot.composeapp.generated.resources.moderation_error
import nomnomzbot.composeapp.generated.resources.moderation_loading
import nomnomzbot.composeapp.generated.resources.moderation_no_reason
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_confirm
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_message
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_title
import nomnomzbot.composeapp.generated.resources.moderation_rules_disable
import nomnomzbot.composeapp.generated.resources.moderation_rules_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_enable
import nomnomzbot.composeapp.generated.resources.moderation_rules_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_add
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_confirm
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_name
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_name_required
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_title
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_type
import nomnomzbot.composeapp.generated.resources.moderation_rules_title
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_title
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_add
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_enable
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_disable
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_delete_action
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_delete_title
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_delete_message
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_delete_confirm
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_delete_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_title
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_name
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_name_required
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_type
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_type_pattern
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_type_blocklist
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_pattern
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_pattern_required
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_terms
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_terms_required
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_action
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_confirm
import nomnomzbot.composeapp.generated.resources.moderation_chat_filters_create_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_action_type_hold
import nomnomzbot.composeapp.generated.resources.moderation_action_type_flag
import nomnomzbot.composeapp.generated.resources.moderation_action_type_escalate
import nomnomzbot.composeapp.generated.resources.moderation_action_apply
import nomnomzbot.composeapp.generated.resources.moderation_action_confirm
import nomnomzbot.composeapp.generated.resources.moderation_action_dialog_title
import nomnomzbot.composeapp.generated.resources.moderation_action_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_action_duration
import nomnomzbot.composeapp.generated.resources.moderation_action_duration_preset_60
import nomnomzbot.composeapp.generated.resources.moderation_action_duration_preset_600
import nomnomzbot.composeapp.generated.resources.moderation_action_duration_preset_3600
import nomnomzbot.composeapp.generated.resources.moderation_action_duration_preset_86400
import nomnomzbot.composeapp.generated.resources.moderation_action_duration_preset_604800
import nomnomzbot.composeapp.generated.resources.moderation_action_duration_preset_custom
import nomnomzbot.composeapp.generated.resources.moderation_action_reason
import nomnomzbot.composeapp.generated.resources.moderation_action_type_ban
import nomnomzbot.composeapp.generated.resources.moderation_action_type_delete
import nomnomzbot.composeapp.generated.resources.moderation_action_type_timeout
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_caps
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_emotes
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_links
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_profanity
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_spam
import nomnomzbot.composeapp.generated.resources.moderation_action_user_id_required
import nomnomzbot.composeapp.generated.resources.moderation_announce_action
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_blue
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_green
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_label
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_orange
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_primary
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_purple
import nomnomzbot.composeapp.generated.resources.moderation_announce_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_announce_message_label
import nomnomzbot.composeapp.generated.resources.moderation_announce_message_required
import nomnomzbot.composeapp.generated.resources.moderation_announce_send
import nomnomzbot.composeapp.generated.resources.moderation_announce_title
import nomnomzbot.composeapp.generated.resources.moderation_automod_queue_approve
import nomnomzbot.composeapp.generated.resources.moderation_automod_queue_category
import nomnomzbot.composeapp.generated.resources.moderation_automod_queue_deny
import nomnomzbot.composeapp.generated.resources.moderation_automod_queue_title
import nomnomzbot.composeapp.generated.resources.moderation_reports_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_reports_escalate
import nomnomzbot.composeapp.generated.resources.moderation_reports_reported_by
import nomnomzbot.composeapp.generated.resources.moderation_reports_title
import nomnomzbot.composeapp.generated.resources.moderation_retry
import nomnomzbot.composeapp.generated.resources.moderation_unban_action
import nomnomzbot.composeapp.generated.resources.moderation_unban_action_short
import nomnomzbot.composeapp.generated.resources.moderation_unban_confirm
import nomnomzbot.composeapp.generated.resources.moderation_unban_approve
import nomnomzbot.composeapp.generated.resources.moderation_unban_deny
import nomnomzbot.composeapp.generated.resources.moderation_unban_deny_message
import nomnomzbot.composeapp.generated.resources.moderation_unban_deny_title
import nomnomzbot.composeapp.generated.resources.moderation_unban_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_confirm
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_message
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_short
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_title
import nomnomzbot.composeapp.generated.resources.moderation_unban_requests_title
import nomnomzbot.composeapp.generated.resources.moderation_unban_message
import nomnomzbot.composeapp.generated.resources.moderation_unban_title
import nomnomzbot.composeapp.generated.resources.shell_nav_moderation
import nomnomzbot.composeapp.generated.resources.moderation_history_title
import nomnomzbot.composeapp.generated.resources.moderation_history_timeouts
import nomnomzbot.composeapp.generated.resources.moderation_history_bans
import nomnomzbot.composeapp.generated.resources.moderation_history_warnings
import nomnomzbot.composeapp.generated.resources.moderation_history_deleted
import nomnomzbot.composeapp.generated.resources.moderation_history_first_seen
import nomnomzbot.composeapp.generated.resources.moderation_trust_badge
import nomnomzbot.composeapp.generated.resources.moderation_heat_badge
import nomnomzbot.composeapp.generated.resources.moderation_standing_title
import nomnomzbot.composeapp.generated.resources.moderation_standing_normal
import nomnomzbot.composeapp.generated.resources.moderation_standing_muted
import nomnomzbot.composeapp.generated.resources.moderation_standing_shadowbanned
import nomnomzbot.composeapp.generated.resources.moderation_standing_blacklisted
import nomnomzbot.composeapp.generated.resources.moderation_standing_restore
import nomnomzbot.composeapp.generated.resources.moderation_standing_reason
import nomnomzbot.composeapp.generated.resources.moderation_standing_badge
import nomnomzbot.composeapp.generated.resources.moderation_standing_hint
import nomnomzbot.composeapp.generated.resources.moderation_escalation_title
import nomnomzbot.composeapp.generated.resources.moderation_escalation_desc
import nomnomzbot.composeapp.generated.resources.moderation_escalation_enabled
import nomnomzbot.composeapp.generated.resources.moderation_escalation_disabled
import nomnomzbot.composeapp.generated.resources.moderation_escalation_edit
import nomnomzbot.composeapp.generated.resources.moderation_escalation_summary
import nomnomzbot.composeapp.generated.resources.moderation_escalation_enable_label
import nomnomzbot.composeapp.generated.resources.moderation_escalation_window
import nomnomzbot.composeapp.generated.resources.moderation_escalation_count_automod
import nomnomzbot.composeapp.generated.resources.moderation_escalation_rung
import nomnomzbot.composeapp.generated.resources.moderation_escalation_action_warn
import nomnomzbot.composeapp.generated.resources.moderation_escalation_action_timeout
import nomnomzbot.composeapp.generated.resources.moderation_escalation_action_ban
import nomnomzbot.composeapp.generated.resources.moderation_escalation_duration
import nomnomzbot.composeapp.generated.resources.moderation_escalation_add_rung
import nomnomzbot.composeapp.generated.resources.moderation_escalation_remove_rung
import nomnomzbot.composeapp.generated.resources.moderation_escalation_save
import nomnomzbot.composeapp.generated.resources.moderation_escalation_cancel
import nomnomzbot.composeapp.generated.resources.moderation_escalation_forgive
import nomnomzbot.composeapp.generated.resources.moderation_heat_threshold_label
import nomnomzbot.composeapp.generated.resources.moderation_heat_auto_timeout_explain
import nomnomzbot.composeapp.generated.resources.moderation_heat_auto_timeout_title
import nomnomzbot.composeapp.generated.resources.moderation_heat_timeout_seconds_hint
import nomnomzbot.composeapp.generated.resources.moderation_heat_timeout_seconds_label
import nomnomzbot.composeapp.generated.resources.moderation_heat_threshold_hint
import nomnomzbot.composeapp.generated.resources.moderation_heat_threshold_save
import nomnomzbot.composeapp.generated.resources.moderation_nuke_action
import nomnomzbot.composeapp.generated.resources.moderation_nuke_confirm_title
import nomnomzbot.composeapp.generated.resources.moderation_nuke_confirm_message
import nomnomzbot.composeapp.generated.resources.moderation_nuke_confirm
import nomnomzbot.composeapp.generated.resources.moderation_nuke_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_nuke_reason_label
import nomnomzbot.composeapp.generated.resources.moderation_nuke_match_term_label
import nomnomzbot.composeapp.generated.resources.moderation_nuke_match_term_hint
import nomnomzbot.composeapp.generated.resources.moderation_nuke_target_fallback
import nomnomzbot.composeapp.generated.resources.moderation_nuke_batches_title
import nomnomzbot.composeapp.generated.resources.moderation_nuke_target
import nomnomzbot.composeapp.generated.resources.moderation_nuke_channels
import nomnomzbot.composeapp.generated.resources.moderation_nuke_status_active
import nomnomzbot.composeapp.generated.resources.moderation_nuke_status_partial
import nomnomzbot.composeapp.generated.resources.moderation_nuke_status_reverted
import nomnomzbot.composeapp.generated.resources.moderation_nuke_revert
import nomnomzbot.composeapp.generated.resources.moderation_nuke_revert_title
import nomnomzbot.composeapp.generated.resources.moderation_nuke_revert_message
import nomnomzbot.composeapp.generated.resources.moderation_shared_title
import nomnomzbot.composeapp.generated.resources.moderation_shared_desc
import nomnomzbot.composeapp.generated.resources.moderation_shared_accept
import nomnomzbot.composeapp.generated.resources.moderation_shared_accept_hint
import nomnomzbot.composeapp.generated.resources.moderation_shared_share
import nomnomzbot.composeapp.generated.resources.moderation_shared_share_hint
import nomnomzbot.composeapp.generated.resources.moderation_shared_trusted_title
import nomnomzbot.composeapp.generated.resources.moderation_shared_trusted_empty
import nomnomzbot.composeapp.generated.resources.moderation_shared_trusted_add_label
import nomnomzbot.composeapp.generated.resources.moderation_shared_trusted_add
import nomnomzbot.composeapp.generated.resources.moderation_shared_trusted_remove
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.SharedFlow
import org.jetbrains.compose.resources.stringResource

// The Moderation page: the channel's currently-banned viewers, all real data from [ModerationController].
// The screen is a pure projection of the controller's state; it loads on first composition and offers a
// retry on failure. Each ban is actionable — an Unban affordance that, only once the moderator confirms it
// in the shared ConfirmDialog, lifts the ban via the controller (which reloads the list on success).
@Composable
fun ModerationScreen(
    controller: ModerationController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: ModerationState by controller.state.collectAsStateWithLifecycle()
    val userContext: UserContextState? by controller.userContext.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Moderation gates its write affordance at its single Moderator manage
    // floor (frontend-ia.md §3). A caller below it sees the ban list but the Unban control is disabled with
    // "Requires Moderator" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Moderation)
    // Marking a viewer suspicious (monitor / restrict) is a Lead-Moderator (SuperMod) action — a higher floor
    // than the page's Moderator gate (warn / bans). The backend re-checks regardless.
    val suspiciousManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.SuperMod)
    // Trust weights and Twitch's own AutoMod levels change what the bot does to EVERY viewer, so both sit at the
    // Broadcaster floor (backend keys trust:policy:manage / moderation:automod:twitch:manage). Below it the
    // controls render disabled with the reason, never hidden.
    val broadcasterManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Broadcaster)

    LaunchedEffect(Unit) { controller.load() }
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: ModerationState = state) {
            is ModerationState.Loading ->
                CenteredMessage(stringResource(Res.string.moderation_loading))
            is ModerationState.Empty ->
                CenteredMessage(stringResource(Res.string.moderation_empty))
            is ModerationState.Error ->
                ErrorContent(
                    detail = current.detail,
                    onRetry = { scope.launch { controller.load() } },
                )
            is ModerationState.Ready ->
                BansList(
                    bans = current.bans,
                    modLog = current.modLog,
                    shieldEnabled = current.shieldEnabled,
                    blockedTerms = current.blockedTerms,
                    automod = current.automod,
                    rules = current.rules,
                    moderators = current.moderators,
                    chatFilters = current.chatFilters,
                    stats = current.stats,
                    actionError = current.actionError,
                    unbanRequests = current.unbanRequests,
                    reports = current.reports,
                    automodQueue = current.automodQueue,
                    bansAvailable = current.bansAvailable,
                    blockedTermsAvailable = current.blockedTermsAvailable,
                    shieldAvailable = current.shieldAvailable,
                    escalationPolicy = current.escalationPolicy,
                    sharedBanSettings = current.sharedBanSettings,
                    nukeBatches = current.nukeBatches,
                    shoutoutTemplate = current.shoutoutTemplate,
                    shoutoutOverrides = current.shoutoutOverrides,
                    // Computed from the very config objects enforcement reads — never a hardcoded claim.
                    automationLines =
                        deriveAutomationLines(
                            automod = current.automod,
                            twitchAutoMod = current.twitchAutoMod,
                            chatFilters = current.chatFilters,
                            rules = current.rules,
                            escalationPolicy = current.escalationPolicy,
                        ),
                    trustPolicy = current.trustPolicy,
                    spamDefense = current.spamDefense,
                    twitchAutoMod = current.twitchAutoMod,
                    trustWeightSumInvalid = current.trustWeightSumInvalid,
                    broadcasterManage = broadcasterManage,
                    onSaveTrustPolicy = { body -> scope.launch { controller.saveTrustPolicy(body) } },
                    onSaveSpamDefense = { settings ->
                        scope.launch { controller.saveSpamDefense(settings) }
                    },
                    onSaveTwitchAutoMod = { body -> scope.launch { controller.saveTwitchAutoMod(body) } },
                    onToggleAutoTimeoutOnHeat = { on ->
                        scope.launch { controller.setAutoTimeoutOnHeat(on) }
                    },
                    onSaveHeatTimeoutSeconds = { seconds ->
                        scope.launch { controller.setHeatTimeoutSeconds(seconds) }
                    },
                    manage = manage,
                    suspiciousManage = suspiciousManage,
                    onSaveEscalation = { policy -> scope.launch { controller.saveEscalationPolicy(policy) } },
                    onSaveHeatThreshold = { v -> scope.launch { controller.setHeatTimeoutThreshold(v) } },
                    onSaveSharedBans = { accept, share ->
                        scope.launch { controller.saveSharedBanSettings(accept, share) }
                    },
                    onAddTrusted = { id -> scope.launch { controller.addTrustedChannel(id) } },
                    onRemoveTrusted = { id -> scope.launch { controller.removeTrustedChannel(id) } },
                    onRevertNuke = { batchId -> scope.launch { controller.revertNuke(batchId) } },
                    onResolveUnban = { requestId, approve, note ->
                        scope.launch { controller.resolveUnbanRequest(requestId, approve, note) }
                    },
                    onResolveReport = { reportId, action ->
                        scope.launch { controller.resolveReport(reportId, action) }
                    },
                    onResolveAutomodQueueItem = { queueItemId, action ->
                        scope.launch { controller.resolveAutomodQueueItem(queueItemId, action) }
                    },
                    onUnban = { userId -> scope.launch { controller.unban(userId) } },
                    onNetworkUnban = { userId ->
                        scope.launch { controller.networkUnban(userId, "all_moderated") }
                    },
                    onViewContext = { userId -> scope.launch { controller.openUserContext(userId) } },
                    searchViewers = { query -> controller.searchViewers(query) },
                    searchChannels = { query -> controller.searchChannels(query) },
                    onPerformAction = { action, userId, duration, reason ->
                        scope.launch { controller.performAction(action, userId, duration, reason) }
                    },
                    onToggleShield = { on -> scope.launch { controller.setShieldMode(on) } },
                    onAddModerator = { id -> scope.launch { controller.addModerator(id) } },
                    onRemoveModerator = { id -> scope.launch { controller.removeModerator(id) } },
                    onClearChat = { scope.launch { controller.clearChat() } },
                    onAddTerm = { term -> scope.launch { controller.addBlockedTerm(term) } },
                    onRemoveTerm = { term -> scope.launch { controller.removeBlockedTerm(term) } },
                    onToggleFilter = { f -> scope.launch { controller.toggleAutomodFilter(f) } },
                    onSaveCapsThreshold = { v -> scope.launch { controller.setCapsThreshold(v) } },
                    onSaveEmoteMaxEmotes = { v -> scope.launch { controller.setEmoteMaxEmotes(v) } },
                    onAddPhrase = { p -> scope.launch { controller.addBannedPhrase(p) } },
                    onRemovePhrase = { p -> scope.launch { controller.removeBannedPhrase(p) } },
                    onAddWhitelist = { d -> scope.launch { controller.addLinkWhitelist(d) } },
                    onRemoveWhitelist = { d -> scope.launch { controller.removeLinkWhitelist(d) } },
                    onToggleRule = { id, on -> scope.launch { controller.toggleRule(id, on) } },
                    onDeleteRule = { id -> scope.launch { controller.deleteRule(id) } },
                    onCreateRule = { name, type, action, duration, reason ->
                        scope.launch { controller.createRule(name, type, action, duration, reason) }
                    },
                    onToggleChatFilter = { id, on -> scope.launch { controller.toggleChatFilter(id, on) } },
                    onDeleteChatFilter = { id -> scope.launch { controller.deleteChatFilter(id) } },
                    onCreateChatFilter = { filterType, name, action, pattern, terms, duration ->
                        scope.launch {
                            controller.createChatFilter(filterType, name, action, pattern, terms, duration)
                        }
                    },
                    onSendAnnouncement = { msg, color ->
                        scope.launch { controller.sendAnnouncement(msg, color) }
                    },
                    onSaveShoutoutTemplate = { t -> scope.launch { controller.setShoutoutTemplate(t) } },
                    onSaveShoutoutOverride = { id, name, template ->
                        scope.launch { controller.setShoutoutOverride(id, name, template) }
                    },
                    onDeleteShoutoutOverride = { id -> scope.launch { controller.deleteShoutoutOverride(id) } },
                )
        }
    }

    userContext?.let { ctx ->
        // The heat auto-timeout threshold lives on the channel's AutoMod config — read it off the Ready state so
        // the panel can color a viewer's heat red once it crosses the line. Defaults to the backend default (80).
        val heatThreshold: Int =
            (state as? ModerationState.Ready)?.automod?.heatTimeoutThreshold ?: DEFAULT_HEAT_THRESHOLD
        UserModerationContextDialog(
            state = ctx,
            heatThreshold = heatThreshold,
            manage = manage,
            suspiciousManage = suspiciousManage,
            onWarn = { userId, reason -> scope.launch { controller.warn(userId, reason) } },
            onSuspicious = { userId, status -> scope.launch { controller.setSuspicious(userId, status) } },
            onClearSuspicious = { userId -> scope.launch { controller.clearSuspicious(userId) } },
            onForgive = { userId -> scope.launch { controller.forgiveUser(userId) } },
            onNetworkNuke = { userId, reason, matchTerm ->
                scope.launch { controller.networkNuke(userId, reason, matchTerm) }
            },
            onSetStanding = { userId, provider, standing, reason ->
                scope.launch { controller.setStanding(userId, provider, standing, reason) }
            },
            onClearStanding = { userId, provider ->
                scope.launch { controller.clearStanding(userId, provider) }
            },
            onAddNote = { userId, content, pinned ->
                scope.launch { controller.addNote(userId, content, pinned) }
            },
            onEditNote = { userId, noteId, content, pinned ->
                scope.launch { controller.editNote(userId, noteId, content, pinned) }
            },
            onDeleteNote = { userId, noteId -> scope.launch { controller.deleteNote(userId, noteId) } },
            onDismiss = { controller.closeUserContext() },
        )
    }
}

// The bot's default heat auto-timeout threshold (backend AutomodConfigDto default) — used when the config
// hasn't loaded yet so the per-user panel can still color heat consistently.
private const val DEFAULT_HEAT_THRESHOLD: Int = 80

// The bot-side standing tiers (J.12), in ascending severity. The write always targets the Twitch identity —
// the panel opens from a Twitch ban and the context id is the viewer's Twitch id.
private const val STANDING_PROVIDER: String = "twitch"

@Composable
private fun BansList(
    bans: List<BannedUser>,
    modLog: List<ModLogEntry>,
    shieldEnabled: Boolean,
    blockedTerms: List<String>,
    automod: AutomodConfig,
    rules: List<ModerationRule>,
    moderators: List<Moderator>,
    chatFilters: List<ChatFilter>,
    stats: ModerationStats,
    actionError: String?,
    unbanRequests: List<UnbanRequest>,
    reports: List<ViewerReport>,
    automodQueue: List<ModerationQueueItem>,
    bansAvailable: Boolean,
    blockedTermsAvailable: Boolean,
    shieldAvailable: Boolean,
    escalationPolicy: EscalationPolicy?,
    sharedBanSettings: SharedBanSettings?,
    nukeBatches: List<NetworkNukeBatch>,
    shoutoutTemplate: String?,
    shoutoutOverrides: List<ShoutoutOverride>,
    // The derived "what happens automatically" account, plus the two broadcaster-gated editors behind it.
    automationLines: List<AutomationLine>,
    trustPolicy: TrustPolicy?,
    spamDefense: SpamDefensePolicy?,
    twitchAutoMod: TwitchAutoModSettings?,
    trustWeightSumInvalid: Boolean,
    broadcasterManage: ManageDecision,
    onSaveTrustPolicy: (UpdateTrustPolicyBody) -> Unit,
    onSaveSpamDefense: (SpamDefenseSettings) -> Unit,
    onSaveTwitchAutoMod: (UpdateTwitchAutoModSettingsBody) -> Unit,
    onToggleAutoTimeoutOnHeat: (Boolean) -> Unit,
    onSaveHeatTimeoutSeconds: (Int) -> Unit,
    manage: ManageDecision,
    suspiciousManage: ManageDecision,
    onSaveEscalation: (UpsertEscalationPolicyBody) -> Unit,
    onSaveHeatThreshold: (Int) -> Unit,
    onSaveSharedBans: (accept: Boolean, share: Boolean) -> Unit,
    onAddTrusted: (trustedChannelId: String) -> Unit,
    onRemoveTrusted: (trustedChannelId: String) -> Unit,
    onRevertNuke: (batchId: String) -> Unit,
    onResolveUnban: (requestId: String, approve: Boolean, note: String?) -> Unit,
    onResolveReport: (reportId: String, action: String) -> Unit,
    onResolveAutomodQueueItem: (queueItemId: String, action: String) -> Unit,
    onUnban: (userId: String) -> Unit,
    onNetworkUnban: (userId: String) -> Unit,
    onViewContext: (userId: String) -> Unit,
    searchViewers: suspend (query: String) -> List<PickerOption>,
    searchChannels: suspend (query: String) -> List<PickerOption>,
    onPerformAction: (action: String, targetUserId: String, durationSeconds: Int?, reason: String?) -> Unit,
    onToggleShield: (Boolean) -> Unit,
    onAddModerator: (targetTwitchUserId: String) -> Unit,
    onRemoveModerator: (userId: String) -> Unit,
    onClearChat: () -> Unit,
    onAddTerm: (String) -> Unit,
    onRemoveTerm: (String) -> Unit,
    onToggleFilter: (AutomodFilter) -> Unit,
    onSaveCapsThreshold: (Int) -> Unit,
    onSaveEmoteMaxEmotes: (Int) -> Unit,
    onAddPhrase: (String) -> Unit,
    onRemovePhrase: (String) -> Unit,
    onAddWhitelist: (String) -> Unit,
    onRemoveWhitelist: (String) -> Unit,
    onToggleRule: (Int, Boolean) -> Unit,
    onDeleteRule: (Int) -> Unit,
    onCreateRule: (name: String, type: String, action: String, durationSeconds: Int?, reason: String?) -> Unit,
    onToggleChatFilter: (filterId: String, enabled: Boolean) -> Unit,
    onDeleteChatFilter: (filterId: String) -> Unit,
    onCreateChatFilter: (
        filterType: String,
        name: String,
        action: String,
        pattern: String?,
        terms: List<String>?,
        timeoutSeconds: Int?,
    ) -> Unit,
    onSendAnnouncement: (message: String, color: String?) -> Unit,
    onSaveShoutoutTemplate: (String) -> Unit,
    onSaveShoutoutOverride: (targetTwitchUserId: String, targetDisplayName: String, messageTemplate: String) -> Unit,
    onDeleteShoutoutOverride: (targetTwitchUserId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // Escalation is edited in a dialog (the whole ladder is replaced on save); this owns its open/closed state.
    var showEscalationDialog: Boolean by remember { mutableStateOf(false) }
    // The nuke batch awaiting a revert confirmation, if any.
    var pendingRevert: NetworkNukeBatch? by remember { mutableStateOf(null) }
    // Writes on the escalation ladder + shared-ban trust web are Lead-Moderator (SuperMod) actions.
    val superManage: ManageDecision = suspiciousManage

    // The ban awaiting confirmation, if any — the screen owns the dialog's open/closed state.
    var pendingUnban: BannedUser? by remember { mutableStateOf(null) }
    // The unban-request appeal awaiting a deny confirmation, if any.
    var pendingDeny: UnbanRequest? by remember { mutableStateOf(null) }
    // The banned viewer awaiting an "unban everywhere" (all-moderated) confirmation, if any.
    var pendingNetworkUnban: BannedUser? by remember { mutableStateOf(null) }
    // The filter rule awaiting delete confirmation, if any.
    var pendingDeleteRule: ModerationRule? by remember { mutableStateOf(null) }
    // Whether the "moderate a viewer" action dialog is open.
    var showActionDialog: Boolean by remember { mutableStateOf(false) }
    // Whether the "add filter rule" dialog is open.
    var showCreateRuleDialog: Boolean by remember { mutableStateOf(false) }
    // The chat filter awaiting delete confirmation, if any.
    var pendingDeleteChatFilter: ChatFilter? by remember { mutableStateOf(null) }
    // Whether the "add chat filter" dialog is open.
    var showCreateChatFilterDialog: Boolean by remember { mutableStateOf(false) }
    // Whether the "send announcement" dialog is open.
    var showAnnounceDialog: Boolean by remember { mutableStateOf(false) }
    // The moderator awaiting a remove confirmation, if any.
    var pendingRemoveModerator: Moderator? by remember { mutableStateOf(null) }
    // Whether the "clear chat" confirmation is open (irreversible — every message is gone).
    var showClearChatConfirm: Boolean by remember { mutableStateOf(false) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        item(key = "page-header") {
            PageHeader(title = stringResource(Res.string.shell_nav_moderation))
        }
        actionError?.let { detail ->
            item(key = "unban-error") {
                ActionErrorBanner(message = stringResource(Res.string.moderation_action_error, detail))
            }
        }
        item(key = "stats") {
            Card(modifier = Modifier.fillMaxWidth()) {
                ModerationStatsBanner(stats = stats)
            }
        }
        item(key = "shield-toggle") {
            Card(modifier = Modifier.fillMaxWidth()) {
                if (shieldAvailable) {
                    ShieldToggle(enabled = shieldEnabled, manage = manage, onToggle = onToggleShield)
                } else {
                    SectionUnavailable(stringResource(Res.string.moderation_shield_unavailable))
                }
            }
        }
        // "Moderate a viewer" and "Send Announcement" quick-action buttons.
        item(key = "action-button") {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ManageGate(decision = manage) { enabled ->
                    TextButton(
                        onClick = { showActionDialog = true },
                        enabled = enabled,
                    ) {
                        Text(
                            text = stringResource(Res.string.moderation_action_apply),
                            color = if (enabled) tokens.primary else tokens.mutedForeground,
                        )
                    }
                }
                ManageGate(decision = manage) { enabled ->
                    TextButton(
                        onClick = { showAnnounceDialog = true },
                        enabled = enabled,
                    ) {
                        Text(
                            text = stringResource(Res.string.moderation_announce_action),
                            color = if (enabled) tokens.primary else tokens.mutedForeground,
                        )
                    }
                }
                // Clear Chat is irreversible (every message in the room is gone, no undo) — gated behind a
                // confirmation like every other destructive action on this page.
                ManageGate(decision = manage) { enabled ->
                    TextButton(
                        onClick = { showClearChatConfirm = true },
                        enabled = enabled,
                    ) {
                        Text(
                            text = stringResource(Res.string.moderation_clear_chat_action),
                            color = if (enabled) tokens.destructive else tokens.mutedForeground,
                        )
                    }
                }
            }
        }
        item(key = "moderators-header") {
            Text(
                text = stringResource(Res.string.moderation_moderators_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        item(key = "moderators-add") { AddModeratorRow(manage = manage, onAdd = onAddModerator) }
        if (moderators.isNotEmpty()) {
            item(key = "moderators-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        moderators.forEachIndexed { index, moderator ->
                            ModeratorRow(
                                moderator = moderator,
                                manage = manage,
                                onRemove = { pendingRemoveModerator = moderator },
                            )
                            if (index < moderators.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        if (!bansAvailable || bans.isNotEmpty()) {
            item(key = "bans-header") {
                Text(
                    text = stringResource(Res.string.moderation_bans_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "bans-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    if (!bansAvailable) {
                        // A read failure is NOT "no bans" — the live Twitch list is unavailable here.
                        SectionUnavailable(stringResource(Res.string.moderation_bans_unavailable))
                    } else {
                        Column {
                            bans.forEachIndexed { index, ban ->
                                BanRow(
                                    ban = ban,
                                    manage = manage,
                                    onUnban = { pendingUnban = ban },
                                    onNetworkUnban = { pendingNetworkUnban = ban },
                                    onViewContext = { onViewContext(ban.id) },
                                )
                                if (index < bans.lastIndex) {
                                    Separator()
                                }
                            }
                        }
                    }
                }
            }
        }
        if (unbanRequests.isNotEmpty()) {
            item(key = "unban-header") {
                Text(
                    text = stringResource(Res.string.moderation_unban_requests_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "unban-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        unbanRequests.forEachIndexed { index, request ->
                            UnbanRequestRow(
                                request = request,
                                manage = suspiciousManage,
                                onApprove = { onResolveUnban(request.id, true, null) },
                                onDeny = { pendingDeny = request },
                            )
                            if (index < unbanRequests.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        if (reports.isNotEmpty()) {
            item(key = "reports-header") {
                Text(
                    text = stringResource(Res.string.moderation_reports_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "reports-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        reports.forEachIndexed { index, report ->
                            ViewerReportRow(
                                report = report,
                                manage = suspiciousManage,
                                onDismiss = { onResolveReport(report.id, "dismiss") },
                                onEscalate = { onResolveReport(report.id, "escalate") },
                            )
                            if (index < reports.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        if (automodQueue.isNotEmpty()) {
            item(key = "automod-queue-header") {
                Text(
                    text = stringResource(Res.string.moderation_automod_queue_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "automod-queue-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        automodQueue.forEachIndexed { index, item ->
                            AutomodQueueRow(
                                item = item,
                                manage = manage,
                                onApprove = { onResolveAutomodQueueItem(item.id, "approve") },
                                onDeny = { onResolveAutomodQueueItem(item.id, "deny") },
                            )
                            if (index < automodQueue.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        if (modLog.isNotEmpty()) {
            item(key = "log-header") {
                Text(
                    text = stringResource(Res.string.moderation_log_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "log-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        modLog.forEachIndexed { index, entry ->
                            ModLogRow(entry = entry)
                            if (index < modLog.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        // Always shown in Ready so the add input is reachable even with no terms yet.
        item(key = "terms-header") {
            Text(
                text = stringResource(Res.string.moderation_terms_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        if (!blockedTermsAvailable) {
            // The live blocked-terms list is unavailable here — don't offer an add input that would just fail.
            item(key = "terms-unavailable") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    SectionUnavailable(stringResource(Res.string.moderation_terms_unavailable))
                }
            }
        } else {
            item(key = "terms-add") { AddTermRow(manage = manage, onAdd = onAddTerm) }
            if (blockedTerms.isNotEmpty()) {
                item(key = "terms-card") {
                    Card(modifier = Modifier.fillMaxWidth()) {
                        Column {
                            blockedTerms.forEachIndexed { index, term ->
                                BlockedTermRow(term = term, manage = manage, onRemove = { onRemoveTerm(term) })
                                if (index < blockedTerms.lastIndex) {
                                    Separator()
                                }
                            }
                        }
                    }
                }
            }
        }
        item(key = "shoutout-header") {
            Text(
                text = stringResource(Res.string.moderation_shoutout_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        item(key = "shoutout-card") {
            Card(modifier = Modifier.fillMaxWidth()) {
                ShoutoutTemplateEditor(
                    template = shoutoutTemplate,
                    manage = manage,
                    onSave = onSaveShoutoutTemplate,
                )
            }
        }
        item(key = "shoutout-overrides-header") {
            Text(
                text = stringResource(Res.string.moderation_shoutout_overrides_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        item(key = "shoutout-overrides-add") {
            AddShoutoutOverrideRow(manage = manage, onAdd = onSaveShoutoutOverride)
        }
        if (shoutoutOverrides.isNotEmpty()) {
            item(key = "shoutout-overrides-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        shoutoutOverrides.forEachIndexed { index, override ->
                            ShoutoutOverrideRow(
                                override = override,
                                manage = manage,
                                onRemove = { onDeleteShoutoutOverride(override.targetTwitchUserId) },
                            )
                            if (index < shoutoutOverrides.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        item(key = "automod-header") {
            Text(
                text = stringResource(Res.string.moderation_automod_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        item(key = "automod-card") {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_link),
                        enabled = automod.linkFilter.enabled,
                        detail = null,
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Link) },
                    )
                    // Link filter allow-list — domains that stay permitted while links are blocked (add/remove).
                    AutomodListEditor(
                        items = automod.linkFilter.whitelist,
                        addLabel = stringResource(Res.string.moderation_automod_whitelist_add_label),
                        addAction = stringResource(Res.string.moderation_automod_whitelist_add),
                        emptyLabel = stringResource(Res.string.moderation_automod_whitelist_empty),
                        manage = manage,
                        onAdd = onAddWhitelist,
                        onRemove = onRemoveWhitelist,
                    )
                    Separator()
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_caps),
                        enabled = automod.capsFilter.enabled,
                        detail =
                            stringResource(
                                Res.string.moderation_automod_caps_detail,
                                automod.capsFilter.threshold,
                            ),
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Caps) },
                    )
                    // Caps threshold — the percent of a message that may be caps before it is flagged.
                    AutomodNumberRow(
                        value = automod.capsFilter.threshold,
                        label = stringResource(Res.string.moderation_automod_caps_threshold_label),
                        hint = stringResource(Res.string.moderation_automod_caps_threshold_hint),
                        manage = manage,
                        onSave = onSaveCapsThreshold,
                    )
                    Separator()
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_phrases),
                        enabled = automod.bannedPhrases.enabled,
                        detail = null,
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Phrases) },
                    )
                    // Banned phrases — the terms this filter blocks (add/remove).
                    AutomodListEditor(
                        items = automod.bannedPhrases.phrases,
                        addLabel = stringResource(Res.string.moderation_automod_phrases_add_label),
                        addAction = stringResource(Res.string.moderation_automod_phrases_add),
                        emptyLabel = stringResource(Res.string.moderation_automod_phrases_empty),
                        manage = manage,
                        onAdd = onAddPhrase,
                        onRemove = onRemovePhrase,
                    )
                    Separator()
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_emotes),
                        enabled = automod.emoteSpam.enabled,
                        detail =
                            stringResource(
                                Res.string.moderation_automod_emote_detail,
                                automod.emoteSpam.maxEmotes,
                            ),
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Emotes) },
                    )
                    // Emote-spam limit — how many emotes a single message may carry before it is flagged.
                    AutomodNumberRow(
                        value = automod.emoteSpam.maxEmotes,
                        label = stringResource(Res.string.moderation_automod_emote_max_label),
                        hint = stringResource(Res.string.moderation_automod_emote_max_hint),
                        manage = manage,
                        onSave = onSaveEmoteMaxEmotes,
                    )
                    Separator()
                    HeatThresholdRow(
                        threshold = automod.heatTimeoutThreshold,
                        manage = manage,
                        onSave = onSaveHeatThreshold,
                    )
                    // The auto-timeout is OPT-IN: off, crossing the line above only flags the viewer.
                    AutomodRow(
                        name = stringResource(Res.string.moderation_heat_auto_timeout_title),
                        enabled = automod.autoTimeoutOnHeat,
                        detail = stringResource(Res.string.moderation_heat_auto_timeout_explain),
                        manage = manage,
                        onToggle = { onToggleAutoTimeoutOnHeat(!automod.autoTimeoutOnHeat) },
                    )
                    AutomodNumberRow(
                        value = automod.heatTimeoutSeconds,
                        label = stringResource(Res.string.moderation_heat_timeout_seconds_label),
                        hint = stringResource(Res.string.moderation_heat_timeout_seconds_hint),
                        manage = manage,
                        onSave = onSaveHeatTimeoutSeconds,
                    )
                }
            }
        }
        // Trust & Automation (S-OWN23 T4): what the bot does on its own, and every number behind it.
        item(key = "trust-automation-header") {
            Text(
                text = trustAutomationSectionTitle(),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        item(key = "automation-panel") {
            Card(modifier = Modifier.fillMaxWidth()) { AutomationPanel(lines = automationLines) }
        }
        item(key = "twitch-automod-card") {
            Card(modifier = Modifier.fillMaxWidth()) {
                TwitchAutoModEditor(
                    settings = twitchAutoMod,
                    manage = broadcasterManage,
                    onSave = onSaveTwitchAutoMod,
                )
            }
        }
        // The trust editor renders only when the policy read succeeded (Moderator+ read floor); the writes inside
        // it stay gated at the Broadcaster floor.
        trustPolicy?.let { policy ->
            item(key = "trust-policy-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    TrustPolicyEditor(
                        policy = policy,
                        manage = broadcasterManage,
                        weightSumInvalid = trustWeightSumInvalid,
                        onSave = onSaveTrustPolicy,
                    )
                }
            }
        }
        // Spam defence. Same posture as the trust editor: the section renders when the read succeeded
        // (Moderator+ floor), and the writes inside it stay gated at the Broadcaster floor, because
        // changing these weights is where enforcement gets switched on at all.
        spamDefense?.let { policy ->
            item(key = "spam-defense-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    SpamDefenseSection(
                        policy = policy,
                        manage = broadcasterManage,
                        onSave = onSaveSpamDefense,
                    )
                }
            }
        }
        // Escalation ladder (J.10). Rendered only when the policy read succeeded (Moderator+ read floor).
        escalationPolicy?.let { policy ->
            item(key = "escalation-header") {
                Text(
                    text = stringResource(Res.string.moderation_escalation_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "escalation-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    EscalationLadderCard(
                        policy = policy,
                        manage = superManage,
                        onEdit = { showEscalationDialog = true },
                    )
                }
            }
        }
        // Shared-chat bans trust web (J.9). Rendered only when the SuperMod-gated read succeeded.
        sharedBanSettings?.let { settings ->
            item(key = "shared-bans-header") {
                Text(
                    text = stringResource(Res.string.moderation_shared_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "shared-bans-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    SharedBansCard(
                        settings = settings,
                        manage = superManage,
                        searchChannels = searchChannels,
                        onSave = onSaveSharedBans,
                        onAddTrusted = onAddTrusted,
                        onRemoveTrusted = onRemoveTrusted,
                    )
                }
            }
        }
        // Network-nuke batch history (J.2a). Shown only when there is a batch to revert or review.
        if (nukeBatches.isNotEmpty()) {
            item(key = "nuke-header") {
                Text(
                    text = stringResource(Res.string.moderation_nuke_batches_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "nuke-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        nukeBatches.forEachIndexed { index, batch ->
                            NukeBatchRow(
                                batch = batch,
                                manage = superManage,
                                onRevert = { pendingRevert = batch },
                            )
                            if (index < nukeBatches.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        item(key = "rules-header") {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_rules_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
                ManageGate(manage) {
                    TextButton(onClick = { showCreateRuleDialog = true }) {
                        Text(
                            text = stringResource(Res.string.moderation_rules_add),
                            style = typography.sm,
                            color = tokens.primary,
                        )
                    }
                }
            }
        }
        if (rules.isNotEmpty()) {
            item(key = "rules-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        rules.forEachIndexed { index, rule ->
                            RuleRow(
                                rule = rule,
                                manage = manage,
                                onToggle = { onToggleRule(rule.id, !rule.isEnabled) },
                                onDelete = { pendingDeleteRule = rule },
                            )
                            if (index < rules.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        item(key = "chat-filters-header") {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_chat_filters_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
                ManageGate(manage) {
                    TextButton(onClick = { showCreateChatFilterDialog = true }) {
                        Text(
                            text = stringResource(Res.string.moderation_chat_filters_add),
                            style = typography.sm,
                            color = tokens.primary,
                        )
                    }
                }
            }
        }
        if (chatFilters.isNotEmpty()) {
            item(key = "chat-filters-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        chatFilters.forEachIndexed { index, filter ->
                            ChatFilterRow(
                                filter = filter,
                                manage = manage,
                                onToggle = { onToggleChatFilter(filter.id, !filter.isEnabled) },
                                onDelete = { pendingDeleteChatFilter = filter },
                            )
                            if (index < chatFilters.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
    }

    pendingUnban?.let { ban ->
        val name: String = ban.displayName.takeIf { it.isNotBlank() } ?: ban.username
        ConfirmDialog(
            title = stringResource(Res.string.moderation_unban_title),
            message = stringResource(Res.string.moderation_unban_message, name),
            confirmLabel = stringResource(Res.string.moderation_unban_confirm),
            dismissLabel = stringResource(Res.string.moderation_unban_dismiss),
            destructive = true,
            onConfirm = {
                onUnban(ban.id)
                pendingUnban = null
            },
            onDismiss = { pendingUnban = null },
        )
    }

    pendingDeny?.let { request ->
        val name: String = request.userName.takeIf { it.isNotBlank() } ?: request.userLogin
        ConfirmDialog(
            title = stringResource(Res.string.moderation_unban_deny_title),
            message = stringResource(Res.string.moderation_unban_deny_message, name),
            confirmLabel = stringResource(Res.string.moderation_unban_deny),
            dismissLabel = stringResource(Res.string.moderation_unban_dismiss),
            destructive = true,
            onConfirm = {
                onResolveUnban(request.id, false, null)
                pendingDeny = null
            },
            onDismiss = { pendingDeny = null },
        )
    }

    pendingNetworkUnban?.let { ban ->
        val name: String = ban.displayName.takeIf { it.isNotBlank() } ?: ban.username
        ConfirmDialog(
            title = stringResource(Res.string.moderation_unban_everywhere_title),
            message = stringResource(Res.string.moderation_unban_everywhere_message, name),
            confirmLabel = stringResource(Res.string.moderation_unban_everywhere_confirm),
            dismissLabel = stringResource(Res.string.moderation_unban_dismiss),
            destructive = true,
            onConfirm = {
                onNetworkUnban(ban.id)
                pendingNetworkUnban = null
            },
            onDismiss = { pendingNetworkUnban = null },
        )
    }

    pendingDeleteRule?.let { rule ->
        ConfirmDialog(
            title = stringResource(Res.string.moderation_rules_delete_title),
            message =
                stringResource(
                    Res.string.moderation_rules_delete_message,
                    resolveRowLabel(
                        rule.name,
                        secondary = rule.type,
                        typeLabel = "Rule",
                        discriminatorSource = rule.id.toString(),
                    ),
                ),
            confirmLabel = stringResource(Res.string.moderation_rules_delete_confirm),
            dismissLabel = stringResource(Res.string.moderation_rules_delete_dismiss),
            destructive = true,
            onConfirm = {
                onDeleteRule(rule.id)
                pendingDeleteRule = null
            },
            onDismiss = { pendingDeleteRule = null },
        )
    }

    pendingDeleteChatFilter?.let { filter ->
        ConfirmDialog(
            title = stringResource(Res.string.moderation_chat_filters_delete_title),
            message = stringResource(Res.string.moderation_chat_filters_delete_message, filter.name),
            confirmLabel = stringResource(Res.string.moderation_chat_filters_delete_confirm),
            dismissLabel = stringResource(Res.string.moderation_chat_filters_delete_dismiss),
            destructive = true,
            onConfirm = {
                onDeleteChatFilter(filter.id)
                pendingDeleteChatFilter = null
            },
            onDismiss = { pendingDeleteChatFilter = null },
        )
    }

    pendingRemoveModerator?.let { moderator ->
        ConfirmDialog(
            title = stringResource(Res.string.moderation_moderators_remove_title),
            message = stringResource(Res.string.moderation_moderators_remove_message, moderator.username),
            confirmLabel = stringResource(Res.string.moderation_moderators_remove_confirm),
            dismissLabel = stringResource(Res.string.moderation_moderators_remove_dismiss),
            destructive = true,
            onConfirm = {
                onRemoveModerator(moderator.userId)
                pendingRemoveModerator = null
            },
            onDismiss = { pendingRemoveModerator = null },
        )
    }

    if (showClearChatConfirm) {
        ConfirmDialog(
            title = stringResource(Res.string.moderation_clear_chat_title),
            message = stringResource(Res.string.moderation_clear_chat_message),
            confirmLabel = stringResource(Res.string.moderation_clear_chat_confirm),
            dismissLabel = stringResource(Res.string.moderation_clear_chat_dismiss),
            destructive = true,
            onConfirm = {
                onClearChat()
                showClearChatConfirm = false
            },
            onDismiss = { showClearChatConfirm = false },
        )
    }

    if (showActionDialog) {
        ModerateViewerDialog(
            searchViewers = searchViewers,
            onConfirm = { action, userId, duration, reason ->
                onPerformAction(action, userId, duration, reason)
                showActionDialog = false
            },
            onDismiss = { showActionDialog = false },
        )
    }

    if (showCreateRuleDialog) {
        CreateRuleDialog(
            onConfirm = { name, type, action, duration, reason ->
                onCreateRule(name, type, action, duration, reason)
                showCreateRuleDialog = false
            },
            onDismiss = { showCreateRuleDialog = false },
        )
    }

    if (showCreateChatFilterDialog) {
        CreateChatFilterDialog(
            onConfirm = { filterType, name, action, pattern, terms, duration ->
                onCreateChatFilter(filterType, name, action, pattern, terms, duration)
                showCreateChatFilterDialog = false
            },
            onDismiss = { showCreateChatFilterDialog = false },
        )
    }

    if (showAnnounceDialog) {
        AnnounceDialog(
            onConfirm = { msg, color ->
                onSendAnnouncement(msg, color)
                showAnnounceDialog = false
            },
            onDismiss = { showAnnounceDialog = false },
        )
    }

    if (showEscalationDialog && escalationPolicy != null) {
        EscalationLadderDialog(
            policy = escalationPolicy,
            onConfirm = { body ->
                onSaveEscalation(body)
                showEscalationDialog = false
            },
            onDismiss = { showEscalationDialog = false },
        )
    }

    pendingRevert?.let { batch ->
        val target: String = batch.targetTwitchUserId ?: batch.targetUserId ?: ""
        ConfirmDialog(
            title = stringResource(Res.string.moderation_nuke_revert_title),
            message = stringResource(Res.string.moderation_nuke_revert_message, target, batch.channelCount),
            confirmLabel = stringResource(Res.string.moderation_nuke_revert),
            dismissLabel = stringResource(Res.string.moderation_nuke_dismiss),
            destructive = true,
            onConfirm = {
                onRevertNuke(batch.id)
                pendingRevert = null
            },
            onDismiss = { pendingRevert = null },
        )
    }
}

// Dialog to apply a ban or timeout to a viewer identified by their Twitch user ID.
// The moderator selects the action type (ban vs timeout), optionally enters a reason and, for timeouts,
// a duration in seconds (default 600 = 10 minutes). The caller owns open/closed state.
//
// S012b — the preset durations the dropdown offers (60s / 10m / 1h / 24h / 7d); "custom" reveals a free-text
// field instead. The backend hard-rejects a zero/negative duration server-side regardless of this list.
private val DURATION_PRESETS_SECONDS: List<Int> = listOf(60, 600, 3_600, 86_400, 604_800)

@Composable
private fun durationPresetLabel(seconds: Int?): String =
    when (seconds) {
        60 -> stringResource(Res.string.moderation_action_duration_preset_60)
        600 -> stringResource(Res.string.moderation_action_duration_preset_600)
        3_600 -> stringResource(Res.string.moderation_action_duration_preset_3600)
        86_400 -> stringResource(Res.string.moderation_action_duration_preset_86400)
        604_800 -> stringResource(Res.string.moderation_action_duration_preset_604800)
        else -> stringResource(Res.string.moderation_action_duration_preset_custom)
    }

@Composable
private fun ModerateViewerDialog(
    searchViewers: suspend (query: String) -> List<PickerOption>,
    onConfirm: (action: String, targetUserId: String, durationSeconds: Int?, reason: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    // The picked viewer — [PickerRef.id] is the Twitch user id the ban / timeout writes key on. Null until a
    // viewer is chosen from the debounced search, so the action can't fire against a hand-typed guess.
    var target: PickerRef? by remember { mutableStateOf(null) }
    var reason: String by remember { mutableStateOf("") }
    // S012b — a preset duration is the default so a moderator never fat-fingers a raw second count; "custom"
    // reveals the free-text field. 600s (10 min) matches the prior hardcoded default.
    var durationPreset: Int? by remember { mutableStateOf(600) }
    var durationMenuOpen: Boolean by remember { mutableStateOf(false) }
    var durationText: String by remember { mutableStateOf("600") }
    var isBan: Boolean by remember { mutableStateOf(true) }
    var showUserIdError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_action_dialog_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                SearchPickerField(
                    search = searchViewers,
                    selected = target,
                    onSelect = { ref -> target = ref; showUserIdError = false },
                    onClear = { target = null },
                    modifier = Modifier.fillMaxWidth(),
                )
                if (showUserIdError) {
                    Text(
                        text = stringResource(Res.string.moderation_action_user_id_required),
                        style = typography.xs,
                        color = tokens.destructive,
                    )
                }
                TabsList {
                    TabsTrigger(
                        selected = isBan,
                        onClick = { isBan = true },
                    ) {
                        Text(stringResource(Res.string.moderation_action_type_ban))
                    }
                    TabsTrigger(
                        selected = !isBan,
                        onClick = { isBan = false },
                    ) {
                        Text(stringResource(Res.string.moderation_action_type_timeout))
                    }
                }
                if (!isBan) {
                    AppSelectField(
                        label = stringResource(Res.string.moderation_action_duration),
                        value = durationPresetLabel(durationPreset),
                        expanded = durationMenuOpen,
                        onExpandedChange = { durationMenuOpen = it },
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        DURATION_PRESETS_SECONDS.forEach { seconds ->
                            DropdownMenuItem(
                                text = { Text(durationPresetLabel(seconds), color = tokens.cardForeground) },
                                onClick = {
                                    durationPreset = seconds
                                    durationText = seconds.toString()
                                    durationMenuOpen = false
                                },
                            )
                        }
                        DropdownMenuItem(
                            text = {
                                Text(
                                    stringResource(Res.string.moderation_action_duration_preset_custom),
                                    color = tokens.cardForeground,
                                )
                            },
                            onClick = {
                                durationPreset = null
                                durationMenuOpen = false
                            },
                        )
                    }
                    if (durationPreset == null) {
                        AppTextField(
                            value = durationText,
                            onValueChange = { durationText = it },
                            label = stringResource(Res.string.moderation_action_duration),
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                }
                AppTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.moderation_action_reason),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val picked: PickerRef? = target
                    if (picked == null) {
                        showUserIdError = true
                        return@Button
                    }
                    val action: String = if (isBan) "ban" else "timeout"
                    val duration: Int? =
                        if (!isBan) durationText.trim().toIntOrNull()?.takeIf { it > 0 } else null
                    val reasonOrNull: String? = reason.trim().takeIf { it.isNotEmpty() }
                    onConfirm(action, picked.id, duration, reasonOrNull)
                },
            ) {
                Text(stringResource(Res.string.moderation_action_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_action_dismiss))
            }
        },
    )
}

@Composable
private fun BanRow(
    ban: BannedUser,
    manage: ManageDecision,
    onUnban: () -> Unit,
    onNetworkUnban: () -> Unit,
    onViewContext: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = ban.displayName.takeIf { it.isNotBlank() } ?: ban.username
    val reason: String =
        ban.reason.takeIf { it.isNotBlank() } ?: stringResource(Res.string.moderation_no_reason)
    val bannedOn: String? = ban.bannedAt.takeIf { it.isNotBlank() }?.let { datePart(it) }
    val unbanLabel: String = stringResource(Res.string.moderation_unban_action, name)
    val networkUnbanLabel: String = stringResource(Res.string.moderation_unban_everywhere_short)
    val viewHistoryLabel: String = stringResource(Res.string.moderation_context_view, name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(spacing.s4),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the row's detail: name, reason, and when (the Unban button stays separate).
                .clearAndSetSemantics {
                    contentDescription =
                        if (bannedOn != null) "$name · $reason · $bannedOn" else "$name · $reason"
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = reason,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            bannedOn?.let { on ->
                Text(
                    text = stringResource(Res.string.moderation_banned_on, on),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
            ban.bannedBy.takeIf { it.isNotBlank() }?.let { by ->
                Text(
                    text = stringResource(Res.string.moderation_banned_by, by),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
        }

        // Action hierarchy (quick-read left→right): History and Unban-everywhere are quiet Ghost buttons,
        // the primary Unban is the emphasised Outline button on the far right — the action a moderator reaches
        // for most on a ban list. All three are the same labelled Sm Button (matching glyph, icon size, height
        // and the concentric Sleak padding), so hierarchy comes from the variant, not from mismatched shapes.
        Button(
            onClick = onViewContext,
            variant = ButtonVariant.Ghost,
            size = ButtonSize.Sm,
            leftIcon = { AppIcon(HistoryGlyph, contentDescription = null) },
            modifier = Modifier.clearAndSetSemantics { contentDescription = viewHistoryLabel },
        ) {
            Text(text = stringResource(Res.string.moderation_context_action_short), maxLines = 1)
        }
        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = onNetworkUnban,
                enabled = enabled,
                variant = ButtonVariant.Ghost,
                size = ButtonSize.Sm,
                leftIcon = { AppIcon(NetworkGlyph, contentDescription = null) },
                modifier = Modifier.clearAndSetSemantics { contentDescription = networkUnbanLabel },
            ) {
                Text(text = stringResource(Res.string.moderation_unban_everywhere_short), maxLines = 1)
            }
        }
        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = onUnban,
                enabled = enabled,
                variant = ButtonVariant.Outline,
                size = ButtonSize.Sm,
                leftIcon = { AppIcon(UnlockGlyph, contentDescription = null) },
                modifier = Modifier.clearAndSetSemantics { contentDescription = unbanLabel },
            ) {
                Text(text = stringResource(Res.string.moderation_unban_action_short), maxLines = 1)
            }
        }
    }
}

// One pending unban-request appeal: the viewer + their appeal text, with Approve (lifts the ban) / Deny buttons
// gated at the Lead-Moderator (SuperMod) floor. Deny confirms first (the ban stays); approve acts immediately.
@Composable
private fun UnbanRequestRow(
    request: UnbanRequest,
    manage: ManageDecision,
    onApprove: () -> Unit,
    onDeny: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = request.userName.takeIf { it.isNotBlank() } ?: request.userLogin

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = name,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (request.text.isNotBlank()) {
            Text(
                text = request.text,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onApprove, enabled = enabled) {
                    Text(stringResource(Res.string.moderation_unban_approve))
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onDeny, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.moderation_unban_deny),
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// One open viewer report awaiting triage — a viewer flagged another chatter. A Lead-Moderator (SuperMod) action.
// Dismiss closes it with no action; Escalate flags it for a moderator to punish separately (it does NOT auto-punish
// — the mod then acts via the existing ban/timeout tools). The reporter name is shown when the backend supplies it.
@Composable
private fun ViewerReportRow(
    report: ViewerReport,
    manage: ManageDecision,
    onDismiss: () -> Unit,
    onEscalate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = report.reportedUsername.takeIf { it.isNotBlank() } ?: report.reportedTwitchUserId,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (report.reason.isNotBlank()) {
            Text(
                text = report.reason,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
            )
        }
        report.reporterName?.takeIf { it.isNotBlank() }?.let { reporter ->
            Text(
                text = stringResource(Res.string.moderation_reports_reported_by, reporter),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onEscalate, enabled = enabled) {
                    Text(stringResource(Res.string.moderation_reports_escalate))
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onDismiss, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.moderation_reports_dismiss),
                        color = tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// One pending AutoMod-held message (J.1, S066) — approve releases it to chat, deny drops it. Both relay through
// Helix on the backend before the row clears from the pending queue (a failed relay leaves it pending to retry).
@Composable
private fun AutomodQueueRow(
    item: ModerationQueueItem,
    manage: ManageDecision,
    onApprove: () -> Unit,
    onDeny: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = item.targetUsernameSnapshot?.takeIf { it.isNotBlank() }
                ?: item.targetTwitchUserId
                ?: "",
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        item.messageContentSnapshot?.takeIf { it.isNotBlank() }?.let { text ->
            Text(
                text = text,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
            )
        }
        item.autoModCategory?.takeIf { it.isNotBlank() }?.let { category ->
            Text(
                text = stringResource(Res.string.moderation_automod_queue_category, category),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onApprove, enabled = enabled) {
                    Text(stringResource(Res.string.moderation_automod_queue_approve))
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onDeny, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.moderation_automod_queue_deny),
                        color = tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// The per-user moderation panel (handoff item: mod-panel read side). A dialog opened from a banned-user row that
// shows the viewer's rap sheet — the bot's OWN recorded ban/timeout/warn/unban history, explicitly NOT the full
// Twitch record (the disclaimer says so). Read-only; the mod then acts via the existing ban/timeout/unban tools.
@Composable
private fun UserModerationContextDialog(
    state: UserContextState,
    heatThreshold: Int,
    manage: ManageDecision,
    suspiciousManage: ManageDecision,
    onWarn: (userId: String, reason: String) -> Unit,
    onSuspicious: (userId: String, status: String) -> Unit,
    onClearSuspicious: (userId: String) -> Unit,
    onForgive: (userId: String) -> Unit,
    onNetworkNuke: (userId: String, reason: String, matchTerm: String) -> Unit,
    onSetStanding: (userId: String, provider: String, standing: String, reason: String?) -> Unit,
    onClearStanding: (userId: String, provider: String) -> Unit,
    onAddNote: (userId: String, content: String, pinned: Boolean) -> Unit,
    onEditNote: (userId: String, noteId: String, content: String?, pinned: Boolean?) -> Unit,
    onDeleteNote: (userId: String, noteId: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_context_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Text(
                    text = stringResource(Res.string.moderation_context_disclaimer),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                when (state) {
                    is UserContextState.Loading ->
                        Text(
                            text = stringResource(Res.string.moderation_context_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is UserContextState.Error ->
                        Text(
                            text = stringResource(Res.string.moderation_context_error, state.detail),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is UserContextState.Ready -> {
                        UserModerationContextBody(state.context, heatThreshold)
                        UserModerationActions(
                            userId = state.context.userId,
                            standings = state.context.standings,
                            manage = manage,
                            suspiciousManage = suspiciousManage,
                            onWarn = onWarn,
                            onSuspicious = onSuspicious,
                            onClearSuspicious = onClearSuspicious,
                            onForgive = onForgive,
                            onNetworkNuke = onNetworkNuke,
                            onSetStanding = onSetStanding,
                            onClearStanding = onClearStanding,
                        )
                        UserModerationNotes(
                            userId = state.context.userId,
                            notes = state.notes,
                            manage = manage,
                            onAddNote = onAddNote,
                            onEditNote = onEditNote,
                            onDeleteNote = onDeleteNote,
                        )
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_context_close))
            }
        },
    )
}

// The write side of the per-user mod panel (gated at the page's Moderator floor): issue a Twitch warning (needs
// a reason) and set/clear a suspicious flag (monitor = watch, restrict = hold their messages — confirmed). Each
// acts immediately and the controller reloads the rap sheet above. Below the manage floor the controls disable
// with the standard reason.
@Composable
private fun UserModerationActions(
    userId: String,
    standings: List<ModerationStanding>,
    manage: ManageDecision,
    suspiciousManage: ManageDecision,
    onWarn: (userId: String, reason: String) -> Unit,
    onSuspicious: (userId: String, status: String) -> Unit,
    onClearSuspicious: (userId: String) -> Unit,
    onForgive: (userId: String) -> Unit,
    onNetworkNuke: (userId: String, reason: String, matchTerm: String) -> Unit,
    onSetStanding: (userId: String, provider: String, standing: String, reason: String?) -> Unit,
    onClearStanding: (userId: String, provider: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var reason: String by remember(userId) { mutableStateOf("") }
    var confirmRestrict: Boolean by remember(userId) { mutableStateOf(false) }
    var confirmNuke: Boolean by remember(userId) { mutableStateOf(false) }
    // S013b — the nuke fan-out bans across every channel the moderator leads; Reason and MatchTerm are both
    // REQUIRED before it can submit, so a batch can always be reviewed and reverted precisely afterward.
    var nukeReason: String by remember(userId) { mutableStateOf("") }
    var nukeMatchTerm: String by remember(userId) { mutableStateOf("") }

    Separator()
    Text(
        text = stringResource(Res.string.moderation_actions_title),
        style = typography.sm,
        color = tokens.mutedForeground,
    )

    AppTextField(
        value = reason,
        onValueChange = { reason = it },
        label = stringResource(Res.string.moderation_warn_reason),
        modifier = Modifier.fillMaxWidth(),
    )
    ManageGate(decision = manage) { enabled ->
        Button(
            onClick = {
                onWarn(userId, reason.trim())
                reason = ""
            },
            enabled = enabled && reason.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(stringResource(Res.string.moderation_warn_action))
        }
    }

    Text(
        text = stringResource(Res.string.moderation_suspicious_label),
        style = typography.xs,
        color = tokens.mutedForeground,
    )
    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { onSuspicious(userId, "active_monitoring") }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_suspicious_monitor),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { confirmRestrict = true }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_suspicious_restrict),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { onClearSuspicious(userId) }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_suspicious_clear),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
    }

    // Bot-side standing tiers (J.12) — distinct from Twitch ban/timeout; targets the viewer's Twitch identity.
    StandingSelector(
        userId = userId,
        standings = standings,
        manage = suspiciousManage,
        onSetStanding = onSetStanding,
        onClearStanding = onClearStanding,
    )

    // Escalation forgiveness (J.11) + the network nuke (J.2a) — both Lead-Moderator (SuperMod) actions.
    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { onForgive(userId) }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_escalation_forgive),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { confirmNuke = true }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_nuke_action),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                )
            }
        }
    }

    if (confirmRestrict) {
        ConfirmDialog(
            title = stringResource(Res.string.moderation_suspicious_restrict),
            message = stringResource(Res.string.moderation_suspicious_restrict_confirm),
            confirmLabel = stringResource(Res.string.moderation_suspicious_restrict),
            dismissLabel = stringResource(Res.string.moderation_context_close),
            destructive = true,
            onConfirm = {
                confirmRestrict = false
                onSuspicious(userId, "restricted")
            },
            onDismiss = { confirmRestrict = false },
        )
    }

    if (confirmNuke) {
        val nukeReasonValid: Boolean = nukeReason.isNotBlank()
        val nukeMatchTermValid: Boolean = nukeMatchTerm.isNotBlank()
        AlertDialog(
            onDismissRequest = { confirmNuke = false },
            title = {
                Text(
                    text = stringResource(Res.string.moderation_nuke_confirm_title),
                    style = typography.lg,
                    color = tokens.popoverForeground,
                )
            },
            text = {
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                    Text(
                        text = stringResource(Res.string.moderation_nuke_confirm_message, userId),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                    AppTextField(
                        value = nukeReason,
                        onValueChange = { nukeReason = it },
                        isError = false,
                        label = stringResource(Res.string.moderation_nuke_reason_label),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    AppTextField(
                        value = nukeMatchTerm,
                        onValueChange = { nukeMatchTerm = it },
                        isError = false,
                        label = stringResource(Res.string.moderation_nuke_match_term_label),
                        supportingText = stringResource(Res.string.moderation_nuke_match_term_hint),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            },
            dismissButton = {
                TextButton(onClick = { confirmNuke = false }) {
                    Text(stringResource(Res.string.moderation_nuke_dismiss))
                }
            },
            confirmButton = {
                Button(
                    onClick = {
                        confirmNuke = false
                        onNetworkNuke(userId, nukeReason.trim(), nukeMatchTerm.trim())
                        nukeReason = ""
                        nukeMatchTerm = ""
                    },
                    enabled = nukeReasonValid && nukeMatchTermValid,
                ) {
                    Text(stringResource(Res.string.moderation_nuke_confirm))
                }
            },
        )
    }
}

// The mod-team notes on this viewer (gated at the page's Moderator floor): a list with pin/edit/delete plus an
// add field. Pinned notes float to the top (the backend orders them). Editing a note swaps its row into an inline
// field; deleting confirms first. Each mutation reloads the panel above via the controller.
@Composable
private fun UserModerationNotes(
    userId: String,
    notes: List<UserNote>,
    manage: ManageDecision,
    onAddNote: (userId: String, content: String, pinned: Boolean) -> Unit,
    onEditNote: (userId: String, noteId: String, content: String?, pinned: Boolean?) -> Unit,
    onDeleteNote: (userId: String, noteId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The note being edited inline (its id), and the working copy of its text; both reset when the panel re-targets.
    var editingNoteId: Int? by remember(userId) { mutableStateOf(null) }
    var editingText: String by remember(userId) { mutableStateOf("") }
    // The note awaiting a delete confirmation, if any.
    var pendingDelete: UserNote? by remember(userId) { mutableStateOf(null) }
    // The add-note draft + whether it should be pinned.
    var newNote: String by remember(userId) { mutableStateOf("") }
    var newPinned: Boolean by remember(userId) { mutableStateOf(false) }

    Separator()
    Text(
        text = stringResource(Res.string.moderation_notes_title),
        style = typography.sm,
        color = tokens.mutedForeground,
    )

    if (notes.isEmpty()) {
        Text(
            text = stringResource(Res.string.moderation_notes_empty),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
    }

    notes.forEach { note ->
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            if (editingNoteId == note.id) {
                AppTextField(
                    value = editingText,
                    onValueChange = { editingText = it },
                    label = stringResource(Res.string.moderation_notes_add_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    ManageGate(decision = manage) { enabled ->
                        Button(
                            onClick = {
                                onEditNote(userId, note.id.toString(), editingText.trim(), null)
                                editingNoteId = null
                            },
                            enabled = enabled && editingText.isNotBlank(),
                        ) {
                            Text(stringResource(Res.string.moderation_notes_save))
                        }
                    }
                    TextButton(onClick = { editingNoteId = null }) {
                        Text(
                            text = stringResource(Res.string.moderation_notes_cancel),
                            color = tokens.mutedForeground,
                        )
                    }
                }
            } else {
                Text(
                    text = note.content,
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                val meta: String =
                    stringResource(
                        Res.string.moderation_notes_meta,
                        note.authorName?.takeIf { it.isNotBlank() } ?: "—",
                        note.createdAt.takeIf { it.isNotBlank() }?.let { datePart(it) } ?: "",
                    )
                Text(
                    text = if (note.pinned) "📌 $meta" else meta,
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    ManageGate(decision = manage) { enabled ->
                        TextButton(
                            onClick = { onEditNote(userId, note.id.toString(), null, !note.pinned) },
                            enabled = enabled,
                        ) {
                            Text(
                                text =
                                    if (note.pinned) {
                                        stringResource(Res.string.moderation_notes_unpin)
                                    } else {
                                        stringResource(Res.string.moderation_notes_pin)
                                    },
                                color = if (enabled) tokens.primary else tokens.mutedForeground,
                            )
                        }
                    }
                    ManageGate(decision = manage) { enabled ->
                        TextButton(
                            onClick = {
                                editingText = note.content
                                editingNoteId = note.id
                            },
                            enabled = enabled,
                        ) {
                            Text(
                                text = stringResource(Res.string.moderation_notes_edit),
                                color = if (enabled) tokens.primary else tokens.mutedForeground,
                            )
                        }
                    }
                    ManageGate(decision = manage) { enabled ->
                        TextButton(onClick = { pendingDelete = note }, enabled = enabled) {
                            Text(
                                text = stringResource(Res.string.moderation_notes_delete),
                                color = if (enabled) tokens.destructive else tokens.mutedForeground,
                            )
                        }
                    }
                }
            }
            Separator()
        }
    }

    // Add a new note: the text field, a pin toggle, and the add button (enabled once non-blank).
    AppTextField(
        value = newNote,
        onValueChange = { newNote = it },
        label = stringResource(Res.string.moderation_notes_add_label),
        modifier = Modifier.fillMaxWidth(),
    )
    Row(
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Badge(selected = newPinned, onClick = { newPinned = !newPinned }) {
            Text(stringResource(Res.string.moderation_notes_pin), style = typography.sm)
        }
        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = {
                    onAddNote(userId, newNote.trim(), newPinned)
                    newNote = ""
                    newPinned = false
                },
                enabled = enabled && newNote.isNotBlank(),
            ) {
                Text(stringResource(Res.string.moderation_notes_add_action))
            }
        }
    }

    pendingDelete?.let { note ->
        ConfirmDialog(
            title = stringResource(Res.string.moderation_notes_delete_title),
            message = stringResource(Res.string.moderation_notes_delete_message),
            confirmLabel = stringResource(Res.string.moderation_notes_delete),
            dismissLabel = stringResource(Res.string.moderation_notes_cancel),
            destructive = true,
            onConfirm = {
                onDeleteNote(userId, note.id.toString())
                pendingDelete = null
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The loaded rap sheet: the viewer's counters + last action + the recent recorded actions, plus the J.4 all-actions
// history rollup and the J.5 trust/heat pair when the projections have computed them.
@Composable
private fun UserModerationContextBody(context: UserModerationContext, heatThreshold: Int) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = context.username?.takeIf { it.isNotBlank() } ?: context.userId

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = name,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        context.trust?.let { trust -> TrustHeatBadges(trust = trust, heatThreshold = heatThreshold) }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            StatChip(label = stringResource(Res.string.moderation_context_bans), value = context.banCount)
            StatChip(label = stringResource(Res.string.moderation_context_timeouts), value = context.timeoutCount)
            StatChip(label = stringResource(Res.string.moderation_context_warns), value = context.warnCount)
            StatChip(label = stringResource(Res.string.moderation_context_unbans), value = context.unbanCount)
        }
        context.history?.let { history -> UserModerationHistoryRow(history = history) }
        context.lastActionType?.takeIf { it.isNotBlank() }?.let { last ->
            val on: String = context.lastActionAt?.takeIf { it.isNotBlank() }?.let { datePart(it) } ?: ""
            Text(
                text = stringResource(Res.string.moderation_context_last, last, on),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
        Text(
            text = stringResource(Res.string.moderation_context_recent),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        if (context.recentActions.isEmpty()) {
            Text(
                text = stringResource(Res.string.moderation_context_recent_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                context.recentActions.forEach { action -> UserContextActionRow(action) }
            }
        }
    }
}

// One recorded action: "<action> · <moderator> · <date>" with the reason beneath it when present.
@Composable
private fun UserContextActionRow(action: ModerationActionLog) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    val on: String = action.timestamp.takeIf { it.isNotBlank() }?.let { datePart(it) } ?: ""
    val by: String = action.moderatorUsername.takeIf { it.isNotBlank() } ?: action.moderatorId
    val head: String = if (on.isNotBlank()) "${action.action} · $by · $on" else "${action.action} · $by"

    Column {
        Text(
            text = head,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        action.reason?.takeIf { it.isNotBlank() }?.let { r ->
            Text(
                text = r,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.moderation_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.moderation_retry)) }
        }
    }
}

// One filter-rule row: the rule name + its type, with Enable/Disable + Delete actions (Editor floor; the
// backend re-checks moderation:filter:write). Delete is confirmed via a dialog. The rule editor (the settings
// form) is a follow-up.
@Composable
private fun RuleRow(
    rule: ModerationRule,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val displayName: String =
        resolveRowLabel(rule.name, secondary = rule.type, typeLabel = "Rule", discriminatorSource = rule.id.toString())
    val toggleLabel: String =
        stringResource(
            if (rule.isEnabled) Res.string.moderation_rules_disable_action
            else Res.string.moderation_rules_enable_action,
            displayName,
        )
    val deleteLabel: String = stringResource(Res.string.moderation_rules_delete_action, displayName)
    val rowDescription: String = "$displayName, ${rule.type}"

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = displayName,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = rule.type,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (rule.isEnabled) Res.string.moderation_rules_disable
                            else Res.string.moderation_rules_enable
                        ),
                    maxLines = 1,
                )
            }
        }
    }
}

// One chat filter row: the filter name + its match type, with Enable/Disable + Delete actions (Editor floor;
// the backend re-checks moderation:filter:write). Delete is confirmed via a dialog.
@Composable
private fun ChatFilterRow(
    filter: ChatFilter,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val displayName: String =
        resolveRowLabel(
            filter.name,
            secondary = filter.filterType,
            typeLabel = "Filter",
            discriminatorSource = filter.id,
        )
    val toggleLabel: String =
        stringResource(
            if (filter.isEnabled) Res.string.moderation_chat_filters_disable_action
            else Res.string.moderation_chat_filters_enable_action,
            displayName,
        )
    val deleteLabel: String = stringResource(Res.string.moderation_chat_filters_delete_action, displayName)
    val rowDescription: String = "$displayName, ${filter.filterType}, ${filter.action}"

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = displayName,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = "${filter.filterType} · ${filter.action}",
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (filter.isEnabled) Res.string.moderation_chat_filters_disable
                            else Res.string.moderation_chat_filters_enable
                        ),
                    maxLines = 1,
                )
            }
        }
    }
}

// One AutoMod filter row: the filter name + an On/Off status (with the caps % / emote max detail when enabled)
// plus an Enable/Disable action; the per-filter threshold / list editing is a follow-up.
@Composable
private fun AutomodRow(
    name: String,
    enabled: Boolean,
    detail: String?,
    manage: ManageDecision,
    onToggle: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusWord: String =
        stringResource(
            if (enabled) Res.string.moderation_automod_on else Res.string.moderation_automod_off
        )
    val rowDescription: String =
        if (enabled && detail != null) "$name, $statusWord, $detail" else "$name, $statusWord"
    val toggleLabel: String =
        stringResource(
            if (enabled) Res.string.moderation_automod_disable_action
            else Res.string.moderation_automod_enable_action,
            name,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // The name + status + detail is one semantics node; the enable/disable button keeps its own.
        Row(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (enabled && detail != null) {
                Text(
                    text = detail,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    modifier = Modifier.wrapContentWidth(),
                )
            }
            Text(
                text = statusWord,
                style = typography.sm,
                color = if (enabled) tokens.primary else tokens.mutedForeground,
                maxLines = 1,
                modifier = Modifier.wrapContentWidth(),
            )
        }
        ManageGate(decision = manage) { canManage ->
            TextButton(
                onClick = onToggle,
                enabled = canManage,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (enabled) Res.string.moderation_automod_disable
                            else Res.string.moderation_automod_enable
                        ),
                    color = if (canManage) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// This channel's own custom shoutout announcement — old-bot parity (S-SHOUTOUT-TARGET-TEMPLATE): what
// OTHER streamers on this platform see when THEY shout this channel out with !so, not a template for the
// shoutouts THIS channel gives. Re-syncs its local draft when the loaded value changes (e.g. after Save).
@Composable
private fun ShoutoutTemplateEditor(template: String?, manage: ManageDecision, onSave: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var draft: String by remember(template) { mutableStateOf(template.orEmpty()) }
    val dirty: Boolean = draft != template.orEmpty()

    Column(
        modifier = Modifier.fillMaxWidth().padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.moderation_shoutout_help),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        ManageGate(decision = manage) { enabled ->
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                AppTextField(
                    value = draft,
                    onValueChange = { draft = it },
                    label = stringResource(Res.string.moderation_shoutout_label),
                    enabled = enabled,
                    modifier = Modifier.fillMaxWidth(),
                )
                val canSave: Boolean = enabled && dirty
                TextButton(onClick = { onSave(draft) }, enabled = canSave) {
                    Text(
                        text = stringResource(Res.string.moderation_shoutout_save),
                        color = if (canSave) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
        }
    }
}

// This broadcaster's own personal shoutout line for one target — old-bot parity. Three fields: the
// target's Twitch id (what !so actually matches on), a display name shown in the list, and the message.
@Composable
private fun AddShoutoutOverrideRow(
    manage: ManageDecision,
    onAdd: (targetTwitchUserId: String, targetDisplayName: String, messageTemplate: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    var targetId: String by remember { mutableStateOf("") }
    var targetName: String by remember { mutableStateOf("") }
    var template: String by remember { mutableStateOf("") }

    ManageGate(decision = manage) { enabled ->
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                AppTextField(
                    value = targetId,
                    onValueChange = { targetId = it },
                    label = stringResource(Res.string.moderation_shoutout_override_target_id_label),
                    enabled = enabled,
                    modifier = Modifier.weight(1f),
                )
                AppTextField(
                    value = targetName,
                    onValueChange = { targetName = it },
                    label = stringResource(Res.string.moderation_shoutout_override_target_name_label),
                    enabled = enabled,
                    modifier = Modifier.weight(1f),
                )
            }
            AppTextField(
                value = template,
                onValueChange = { template = it },
                label = stringResource(Res.string.moderation_shoutout_override_message_label),
                enabled = enabled,
                modifier = Modifier.fillMaxWidth(),
            )
            val canSubmit: Boolean =
                enabled && targetId.isNotBlank() && targetName.isNotBlank() && template.isNotBlank()
            val tokens = LocalTokens.current
            TextButton(
                onClick = {
                    if (canSubmit) {
                        onAdd(targetId.trim(), targetName.trim(), template.trim())
                        targetId = ""
                        targetName = ""
                        template = ""
                    }
                },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_shoutout_override_add),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// One saved override row: target name + message + a Remove action (Editor floor).
@Composable
private fun ShoutoutOverrideRow(override: ShoutoutOverride, manage: ManageDecision, onRemove: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val removeLabel: String =
        stringResource(Res.string.moderation_shoutout_override_remove_action, override.targetDisplayName)

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f)) {
            Text(
                text = override.targetDisplayName,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = override.messageTemplate,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
                label = removeLabel,
                onClick = onRemove,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

@Composable
private fun AddTermRow(manage: ManageDecision, onAdd: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    var term: String by remember { mutableStateOf("") }

    ManageGate(decision = manage) { enabled ->
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            AppTextField(
                value = term,
                onValueChange = { term = it },
                label = stringResource(Res.string.moderation_terms_add_label),
                enabled = enabled,
                modifier = Modifier.weight(1f),
            )
            val canSubmit: Boolean = enabled && term.isNotBlank()
            TextButton(
                onClick = {
                    val trimmed: String = term.trim()
                    if (trimmed.isNotEmpty()) {
                        onAdd(trimmed)
                        term = ""
                    }
                },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_terms_add),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// Add-moderator row: a Twitch user id + a Grant action. Add Channel Moderator requires the CHANNEL's own
// broadcaster token (Twitch does not allow a moderator to grant moderator status on their own token), so
// this always targets the channel — same manage floor as the rest of this page.
@Composable
private fun AddModeratorRow(manage: ManageDecision, onAdd: (targetTwitchUserId: String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    var targetTwitchUserId: String by remember { mutableStateOf("") }

    ManageGate(decision = manage) { enabled ->
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            AppTextField(
                value = targetTwitchUserId,
                onValueChange = { targetTwitchUserId = it },
                label = stringResource(Res.string.moderation_moderators_add_label),
                enabled = enabled,
                modifier = Modifier.weight(1f),
            )
            val canSubmit: Boolean = enabled && targetTwitchUserId.isNotBlank()
            TextButton(
                onClick = {
                    val trimmed: String = targetTwitchUserId.trim()
                    if (trimmed.isNotEmpty()) {
                        onAdd(trimmed)
                        targetTwitchUserId = ""
                    }
                },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_moderators_add),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// One moderator row: their username + a Remove action (revokes on Twitch; the backend re-checks
// moderation:moderator:write). A Broadcaster-tier action — the backend gates it at the same floor
// regardless of what [manage] shows here.
@Composable
private fun ModeratorRow(moderator: Moderator, manage: ManageDecision, onRemove: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val removeLabel: String =
        stringResource(Res.string.moderation_moderators_remove_action, moderator.username)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = moderator.username,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
                label = removeLabel,
                onClick = onRemove,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

// One blocked-term row: the term + a Remove action (Editor floor; the backend re-checks moderation:blocklist).
// Removing a term un-blocks it, so the action reads in the neutral primary colour, not destructive.
@Composable
private fun BlockedTermRow(term: String, manage: ManageDecision, onRemove: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val removeLabel: String = stringResource(Res.string.moderation_terms_remove_action, term)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = term,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        // Destructive removal uses the same trash glyph as every other remove/delete on the page (rules,
        // automod lists) — a consistent, quick-read icon rather than a plain text button.
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                icon = TrashGlyph,
                label = removeLabel,
                onClick = onRemove,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

// The page-level emergency Shield Mode toggle: a prominent row that turns Twitch's lockdown on/off. The title
// reads destructive (red) when active. Editor floor (ManageGate); the backend re-checks moderation:shieldmode.
@Composable
private fun ModerationStatsBanner(stats: ModerationStats) {
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(spacing.s3),
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        StatChip(label = stringResource(Res.string.moderation_stats_bans_today), value = stats.bansToday)
        StatChip(label = stringResource(Res.string.moderation_stats_timeouts), value = stats.timeouts)
        StatChip(label = stringResource(Res.string.moderation_stats_deleted), value = stats.deletedMessages)
        StatChip(label = stringResource(Res.string.moderation_stats_automod), value = stats.automodActions)
    }
}

@Composable
private fun StatChip(label: String, value: Int) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(text = value.toString(), style = typography.xl, color = tokens.primary)
        Text(text = label, style = typography.xs, color = tokens.mutedForeground)
    }
}

// A live-Twitch section that couldn't be read here (missing scope / bot not installed). Shown INSTEAD of the
// section's empty/off state so the page never phantom-lies "no bans / no terms / shield off" when the truth is
// "you can't see or control this here". The global scope banner is where the streamer re-grants.
@Composable
private fun SectionUnavailable(message: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = message,
        style = typography.sm,
        color = tokens.mutedForeground,
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
    )
}

@Composable
private fun ShieldToggle(enabled: Boolean, manage: ManageDecision, onToggle: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val actionLabel: String =
        stringResource(
            if (enabled) Res.string.moderation_shield_disable_action
            else Res.string.moderation_shield_enable_action
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.moderation_shield_title),
            style = typography.base,
            color = if (enabled) tokens.destructive else tokens.cardForeground,
            maxLines = 1,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { canManage ->
            TextButton(
                onClick = { onToggle(!enabled) },
                enabled = canManage,
                modifier = Modifier.semantics { contentDescription = actionLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (enabled) Res.string.moderation_shield_disable
                            else Res.string.moderation_shield_enable
                        ),
                    color = if (canManage) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// One mod-log row: the action + target (e.g. "timeout Baduser") over "by <moderator>". Read-only history.
@Composable
private fun ModLogRow(entry: ModLogEntry) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val primary: String = "${entry.action} ${entry.target ?: ""}".trim()
    val byLabel: String = stringResource(Res.string.moderation_log_by, entry.moderator)
    val rowDescription: String =
        stringResource(
            Res.string.moderation_log_row_description,
            entry.action,
            entry.target ?: "",
            entry.moderator,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = primary,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = byLabel,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

/** The date portion of an ISO-8601 timestamp (`2026-06-24T18:05:00Z` → `2026-06-24`); the whole value
 *  when it carries no time component. Avoids pulling a date library into this read-only slice. */
private fun datePart(timestamp: String): String = timestamp.substringBefore('T')

// Dialog to create a new filter rule. The moderator enters a name, picks a type and action, then fills the
// optional duration (for timeout action) and reason. The caller owns open/closed state.
@Composable
private fun CreateRuleDialog(
    onConfirm: (name: String, type: String, action: String, durationSeconds: Int?, reason: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val types: List<Pair<String, String>> = listOf(
        "profanity" to stringResource(Res.string.moderation_rule_type_profanity),
        "links" to stringResource(Res.string.moderation_rule_type_links),
        "caps" to stringResource(Res.string.moderation_rule_type_caps),
        "emotes" to stringResource(Res.string.moderation_rule_type_emotes),
        "spam" to stringResource(Res.string.moderation_rule_type_spam),
    )
    val actions: List<Pair<String, String>> = listOf(
        "delete" to stringResource(Res.string.moderation_action_type_delete),
        "timeout" to stringResource(Res.string.moderation_action_type_timeout),
        "ban" to stringResource(Res.string.moderation_action_type_ban),
    )

    var name: String by remember { mutableStateOf("") }
    var selectedType: String by remember { mutableStateOf(types.first().first) }
    var selectedAction: String by remember { mutableStateOf(actions.first().first) }
    var durationInput: String by remember { mutableStateOf("600") }
    var reason: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_rules_create_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.moderation_rules_create_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.moderation_rules_create_name_required) else null,
                )
                Text(
                    text = stringResource(Res.string.moderation_rules_create_type),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    types.forEach { (key, label) ->
                        Badge(
                            selected = selectedType == key,
                            onClick = { selectedType = key },
                        ) { Text(label, style = typography.xs) }
                    }
                }
                Text(
                    text = stringResource(Res.string.moderation_rules_create_action),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    actions.forEach { (key, label) ->
                        Badge(
                            selected = selectedAction == key,
                            onClick = { selectedAction = key },
                        ) { Text(label, style = typography.xs) }
                    }
                }
                if (selectedAction == "timeout") {
                    AppTextField(
                        value = durationInput,
                        onValueChange = { durationInput = it },
                        label = stringResource(Res.string.moderation_action_duration),
                        isError = false,
                        errorText = null,
                    )
                }
                AppTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.moderation_action_reason),
                    isError = false,
                    errorText = null,
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (name.isBlank()) { nameError = true; return@Button }
                    val duration: Int? = if (selectedAction == "timeout") durationInput.trim().toIntOrNull() else null
                    onConfirm(name.trim(), selectedType, selectedAction, duration, reason.trim().takeIf { it.isNotBlank() })
                },
            ) {
                Text(stringResource(Res.string.moderation_rules_create_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_rules_create_dismiss))
            }
        },
    )
}

// Dialog to create a new chat filter (J.6, S066): a regex pattern or a literal word list, with an action.
// The wire enum values (ChatFilterType / ChatFilterAction) are sent by their exact C# member name — the
// backend's built-in System.Text.Json enum reader accepts a quoted member-name string on write, even though
// it always serializes reads back as the underlying ordinal (no JsonStringEnumConverter is registered on
// either enum) — the row above renders whatever the backend returns either way.
@Composable
private fun CreateChatFilterDialog(
    onConfirm: (
        filterType: String,
        name: String,
        action: String,
        pattern: String?,
        terms: List<String>?,
        timeoutSeconds: Int?,
    ) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val types: List<Pair<String, String>> = listOf(
        "Regex" to stringResource(Res.string.moderation_chat_filters_create_type_pattern),
        "Blocklist" to stringResource(Res.string.moderation_chat_filters_create_type_blocklist),
    )
    val actions: List<Pair<String, String>> = listOf(
        "Delete" to stringResource(Res.string.moderation_action_type_delete),
        "Timeout" to stringResource(Res.string.moderation_action_type_timeout),
        "Hold" to stringResource(Res.string.moderation_action_type_hold),
        "Flag" to stringResource(Res.string.moderation_action_type_flag),
        "Escalate" to stringResource(Res.string.moderation_action_type_escalate),
    )

    var name: String by remember { mutableStateOf("") }
    var selectedType: String by remember { mutableStateOf(types.first().first) }
    var selectedAction: String by remember { mutableStateOf(actions.first().first) }
    var pattern: String by remember { mutableStateOf("") }
    var termsInput: String by remember { mutableStateOf("") }
    var durationInput: String by remember { mutableStateOf("600") }
    var nameError: Boolean by remember { mutableStateOf(false) }
    var patternError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_chat_filters_create_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.moderation_chat_filters_create_name),
                    isError = nameError,
                    errorText =
                        if (nameError) stringResource(Res.string.moderation_chat_filters_create_name_required)
                        else null,
                )
                Text(
                    text = stringResource(Res.string.moderation_chat_filters_create_type),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    types.forEach { (key, label) ->
                        Badge(
                            selected = selectedType == key,
                            onClick = { selectedType = key },
                        ) { Text(label, style = typography.xs) }
                    }
                }
                if (selectedType == "Regex") {
                    AppTextField(
                        value = pattern,
                        onValueChange = { pattern = it; patternError = false },
                        label = stringResource(Res.string.moderation_chat_filters_create_pattern),
                        isError = patternError,
                        errorText =
                            if (patternError)
                                stringResource(Res.string.moderation_chat_filters_create_pattern_required)
                            else null,
                    )
                } else {
                    AppTextField(
                        value = termsInput,
                        onValueChange = { termsInput = it; patternError = false },
                        label = stringResource(Res.string.moderation_chat_filters_create_terms),
                        isError = patternError,
                        errorText =
                            if (patternError)
                                stringResource(Res.string.moderation_chat_filters_create_terms_required)
                            else null,
                    )
                }
                Text(
                    text = stringResource(Res.string.moderation_chat_filters_create_action),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    actions.forEach { (key, label) ->
                        Badge(
                            selected = selectedAction == key,
                            onClick = { selectedAction = key },
                        ) { Text(label, style = typography.xs) }
                    }
                }
                if (selectedAction == "Timeout") {
                    AppTextField(
                        value = durationInput,
                        onValueChange = { durationInput = it },
                        label = stringResource(Res.string.moderation_action_duration),
                        isError = false,
                        errorText = null,
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (name.isBlank()) { nameError = true; return@Button }
                    val terms: List<String> =
                        termsInput.split(",").map { it.trim() }.filter { it.isNotEmpty() }
                    if (selectedType == "Regex" && pattern.isBlank()) { patternError = true; return@Button }
                    if (selectedType == "Blocklist" && terms.isEmpty()) { patternError = true; return@Button }
                    val duration: Int? =
                        if (selectedAction == "Timeout") durationInput.trim().toIntOrNull() else null
                    onConfirm(
                        selectedType,
                        name.trim(),
                        selectedAction,
                        pattern.trim().takeIf { selectedType == "Regex" },
                        terms.takeIf { selectedType == "Blocklist" },
                        duration,
                    )
                },
            ) {
                Text(stringResource(Res.string.moderation_chat_filters_create_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_chat_filters_create_dismiss))
            }
        },
    )
}

// Dialog to send a Twitch chat announcement with an optional color tint. The message is required; color
// defaults to the channel's primary accent when null. The caller owns open/closed state.
@Composable
private fun AnnounceDialog(
    onConfirm: (message: String, color: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var message: String by remember { mutableStateOf("") }
    val messageError: Boolean = message.isBlank()
    // null = "primary" (the Twitch default)
    var selectedColor: String? by remember { mutableStateOf(null) }

    val colorOptions: List<Pair<String?, String>> = listOf(
        null to stringResource(Res.string.moderation_announce_color_primary),
        "blue" to stringResource(Res.string.moderation_announce_color_blue),
        "green" to stringResource(Res.string.moderation_announce_color_green),
        "orange" to stringResource(Res.string.moderation_announce_color_orange),
        "purple" to stringResource(Res.string.moderation_announce_color_purple),
    )

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_announce_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = message,
                    onValueChange = { message = it },
                    label = stringResource(Res.string.moderation_announce_message_label),
                    isError = messageError && message.isNotEmpty(),
                    errorText = if (messageError && message.isNotEmpty())
                        stringResource(Res.string.moderation_announce_message_required) else null,
                )
                Text(
                    text = stringResource(Res.string.moderation_announce_color_label),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(
                    horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    colorOptions.forEach { (colorKey, label) ->
                        Badge(
                            selected = selectedColor == colorKey,
                            onClick = { selectedColor = colorKey },
                        ) { Text(text = label, style = typography.xs) }
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (message.isNotBlank()) onConfirm(message.trim(), selectedColor)
                },
                enabled = message.isNotBlank(),
            ) {
                Text(stringResource(Res.string.moderation_announce_send))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_announce_dismiss))
            }
        },
    )
}

// The J.5 trust + heat pair. Heat at or above the channel's auto-timeout threshold reads as a warning (destructive).
// Internal (not private): the Home attention inbox's held-message dialog renders the SAME badges — reuse, not a fork.
@Composable
internal fun TrustHeatBadges(trust: UserTrustSummary, heatThreshold: Int) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val heatHot: Boolean = trust.heatScore.roundToInt() >= heatThreshold
    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Badge(variant = BadgeVariant.Secondary) {
            Text(
                text = stringResource(Res.string.moderation_trust_badge, trust.trustScore.roundToInt().toString()),
                style = typography.xs,
            )
        }
        Badge(variant = if (heatHot) BadgeVariant.Destructive else BadgeVariant.Secondary) {
            Text(
                text = stringResource(Res.string.moderation_heat_badge, trust.heatScore.roundToInt().toString()),
                style = typography.xs,
            )
        }
    }
}

// The J.4 all-actions rollup — counts EVERY Twitch-side action against the viewer, not only the bot's own record.
@Composable
private fun UserModerationHistoryRow(history: UserModerationHistorySummary) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.moderation_history_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            StatChip(label = stringResource(Res.string.moderation_history_timeouts), value = history.timeoutCount)
            StatChip(label = stringResource(Res.string.moderation_history_bans), value = history.banCount)
            StatChip(label = stringResource(Res.string.moderation_history_warnings), value = history.warningCount)
            StatChip(
                label = stringResource(Res.string.moderation_history_deleted),
                value = history.messagesDeletedCount,
            )
        }
        history.firstSeenAt?.takeIf { it.isNotBlank() }?.let { first ->
            Text(
                text = stringResource(Res.string.moderation_history_first_seen, datePart(first)),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }
}

// The bot-side standing tiers (J.12): current per-provider standing badges, the three apply buttons + Restore, and
// an optional reason. Writes target the viewer's Twitch identity ([STANDING_PROVIDER]); every change is reversible.
@Composable
private fun StandingSelector(
    userId: String,
    standings: List<ModerationStanding>,
    manage: ManageDecision,
    onSetStanding: (userId: String, provider: String, standing: String, reason: String?) -> Unit,
    onClearStanding: (userId: String, provider: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var reason: String by remember(userId) { mutableStateOf("") }

    Separator()
    Text(
        text = stringResource(Res.string.moderation_standing_title),
        style = typography.sm,
        color = tokens.mutedForeground,
    )
    if (standings.isEmpty()) {
        Text(
            text = stringResource(Res.string.moderation_standing_normal),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
    } else {
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            standings.forEach { standing ->
                Badge(variant = standingBadgeVariant(standing.standing)) {
                    Text(
                        text =
                            stringResource(
                                Res.string.moderation_standing_badge,
                                standing.provider,
                                standingLabel(standing.standing),
                            ),
                        style = typography.xs,
                    )
                }
            }
        }
    }
    AppTextField(
        value = reason,
        onValueChange = { reason = it },
        label = stringResource(Res.string.moderation_standing_reason),
        modifier = Modifier.fillMaxWidth(),
    )
    val trimmedReason: String? = reason.trim().takeIf { it.isNotBlank() }
    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { onSetStanding(userId, STANDING_PROVIDER, "muted", trimmedReason) },
                enabled = enabled,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_standing_muted),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { onSetStanding(userId, STANDING_PROVIDER, "shadowbanned", trimmedReason) },
                enabled = enabled,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_standing_shadowbanned),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { onSetStanding(userId, STANDING_PROVIDER, "blacklisted", trimmedReason) },
                enabled = enabled,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_standing_blacklisted),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { onClearStanding(userId, STANDING_PROVIDER) }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_standing_restore),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
    }
    Text(
        text = stringResource(Res.string.moderation_standing_hint),
        style = typography.xs,
        color = tokens.mutedForeground,
    )
}

// The localized label for a bot-side standing tier value.
@Composable
private fun standingLabel(standing: String): String =
    when (standing) {
        "muted" -> stringResource(Res.string.moderation_standing_muted)
        "shadowbanned" -> stringResource(Res.string.moderation_standing_shadowbanned)
        "blacklisted" -> stringResource(Res.string.moderation_standing_blacklisted)
        else -> standing
    }

// Muted is the mildest tier (secondary); shadowban / blacklist read as warnings (destructive).
private fun standingBadgeVariant(standing: String): BadgeVariant =
    when (standing) {
        "muted" -> BadgeVariant.Secondary
        else -> BadgeVariant.Destructive
    }

// The heat auto-timeout threshold (J.5) — a compact number input on the AutoMod card; saving re-sends the config.
@Composable
private fun HeatThresholdRow(threshold: Int, manage: ManageDecision, onSave: (Int) -> Unit) {
    val spacing = LocalSpacing.current

    var input: String by remember(threshold) { mutableStateOf(threshold.toString()) }
    val parsed: Int? = input.trim().toIntOrNull()
    val dirty: Boolean = parsed != null && parsed != threshold

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        AppTextField(
            value = input,
            onValueChange = { input = it },
            label = stringResource(Res.string.moderation_heat_threshold_label),
            supportingText = stringResource(Res.string.moderation_heat_threshold_hint),
            modifier = Modifier.fillMaxWidth(),
        )
        ManageGate(decision = manage) { enabled ->
            Button(onClick = { parsed?.let(onSave) }, enabled = enabled && dirty) {
                Text(stringResource(Res.string.moderation_heat_threshold_save))
            }
        }
    }
}

// An AutoMod numeric-setting row (caps threshold, emote-spam limit): a compact number field + Save that persists
// the whole config. Save enables only when the parsed value is a non-negative int that differs from the stored
// one. Mirrors [HeatThresholdRow].
@Composable
private fun AutomodNumberRow(
    value: Int,
    label: String,
    hint: String,
    manage: ManageDecision,
    onSave: (Int) -> Unit,
) {
    val spacing = LocalSpacing.current

    var input: String by remember(value) { mutableStateOf(value.toString()) }
    val parsed: Int? = input.trim().toIntOrNull()
    val valid: Boolean = parsed != null && parsed >= 0
    val dirty: Boolean = valid && parsed != value

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        AppTextField(
            value = input,
            onValueChange = { input = it.filter { ch -> ch.isDigit() } },
            isError = !valid,
            label = label,
            supportingText = hint,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth(),
        )
        ManageGate(decision = manage) { enabled ->
            Button(onClick = { parsed?.let(onSave) }, enabled = enabled && dirty) {
                Text(stringResource(Res.string.moderation_heat_threshold_save))
            }
        }
    }
}

// An AutoMod string-list editor (banned phrases, link allow-list): an add input + Add button, and one removable
// chip-row per stored item. Each add/remove persists the whole config immediately, like the blocked-terms editor.
@Composable
private fun AutomodListEditor(
    items: List<String>,
    addLabel: String,
    addAction: String,
    emptyLabel: String,
    manage: ManageDecision,
    onAdd: (String) -> Unit,
    onRemove: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var draft: String by remember { mutableStateOf("") }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        if (items.isEmpty()) {
            Text(text = emptyLabel, style = typography.xs, color = tokens.mutedForeground)
        } else {
            items.forEach { item ->
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                ) {
                    Text(
                        text = item,
                        style = typography.sm,
                        color = tokens.cardForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    ManageGate(decision = manage) { enabled ->
                        GlyphButton(
                            icon = TrashGlyph,
                            label = "$addAction: $item",
                            onClick = { onRemove(item) },
                            enabled = enabled,
                            tint = tokens.destructive,
                        )
                    }
                }
            }
        }
        ManageGate(decision = manage) { enabled ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                AppTextField(
                    value = draft,
                    onValueChange = { draft = it },
                    label = addLabel,
                    enabled = enabled,
                    modifier = Modifier.weight(1f),
                )
                val canSubmit: Boolean = enabled && draft.isNotBlank()
                TextButton(
                    onClick = {
                        val trimmed: String = draft.trim()
                        if (trimmed.isNotEmpty()) {
                            onAdd(trimmed)
                            draft = ""
                        }
                    },
                    enabled = canSubmit,
                ) {
                    Text(
                        text = addAction,
                        color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
        }
    }
}

// The escalation ladder summary card (J.10): enabled/disabled state + rung count/window, with an Edit affordance.
@Composable
private fun EscalationLadderCard(policy: EscalationPolicy, manage: ManageDecision, onEdit: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.moderation_escalation_desc),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Row(
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Badge(variant = if (policy.isEnabled) BadgeVariant.Default else BadgeVariant.Secondary) {
                Text(
                    text =
                        stringResource(
                            if (policy.isEnabled) Res.string.moderation_escalation_enabled
                            else Res.string.moderation_escalation_disabled
                        ),
                    style = typography.xs,
                )
            }
            Text(
                text =
                    stringResource(
                        Res.string.moderation_escalation_summary,
                        policy.ladder.size,
                        policy.offenseWindowHours,
                    ),
                style = typography.sm,
                color = tokens.cardForeground,
                modifier = Modifier.weight(1f),
            )
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onEdit, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.moderation_escalation_edit),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// One editable rung's working state in the escalation dialog (Compose-observable so badge/duration edits recompose).
private class EditableRung(action: String, timeoutSeconds: String) {
    var action: String by mutableStateOf(action)
    var timeoutSeconds: String by mutableStateOf(timeoutSeconds)
}

// Edit the whole escalation policy (J.10) — enable flag, window, AutoMod counting, and an ordered rung list (the
// offense number is the position, so rungs are always strictly ascending). Saving replaces the ladder whole.
@Composable
private fun EscalationLadderDialog(
    policy: EscalationPolicy,
    onConfirm: (UpsertEscalationPolicyBody) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var enabled: Boolean by remember { mutableStateOf(policy.isEnabled) }
    var countAutomod: Boolean by remember { mutableStateOf(policy.countAutoModViolations) }
    var windowInput: String by remember { mutableStateOf(policy.offenseWindowHours.toString()) }
    val rungs: SnapshotStateList<EditableRung> =
        remember {
            mutableStateListOf<EditableRung>().apply {
                policy.ladder.forEach {
                    add(EditableRung(it.action.ifBlank { "warn" }, it.timeoutSeconds?.toString() ?: "600"))
                }
            }
        }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_escalation_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                ) {
                    Switch(checked = enabled, onCheckedChange = { enabled = it })
                    Text(
                        text = stringResource(Res.string.moderation_escalation_enable_label),
                        style = typography.sm,
                        color = tokens.cardForeground,
                    )
                }
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                ) {
                    Switch(checked = countAutomod, onCheckedChange = { countAutomod = it })
                    Text(
                        text = stringResource(Res.string.moderation_escalation_count_automod),
                        style = typography.sm,
                        color = tokens.cardForeground,
                    )
                }
                AppTextField(
                    value = windowInput,
                    onValueChange = { windowInput = it },
                    label = stringResource(Res.string.moderation_escalation_window),
                    modifier = Modifier.fillMaxWidth(),
                )
                rungs.forEachIndexed { index, rung ->
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        Text(
                            text = stringResource(Res.string.moderation_escalation_rung, index + 1),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            TabsList(modifier = Modifier.weight(1f)) {
                                TabsTrigger(selected = rung.action == "warn", onClick = { rung.action = "warn" }) {
                                    Text(stringResource(Res.string.moderation_escalation_action_warn), style = typography.xs)
                                }
                                TabsTrigger(selected = rung.action == "timeout", onClick = { rung.action = "timeout" }) {
                                    Text(stringResource(Res.string.moderation_escalation_action_timeout), style = typography.xs)
                                }
                                TabsTrigger(selected = rung.action == "ban", onClick = { rung.action = "ban" }) {
                                    Text(stringResource(Res.string.moderation_escalation_action_ban), style = typography.xs)
                                }
                            }
                            TextButton(onClick = { rungs.removeAt(index) }) {
                                Text(
                                    text = stringResource(Res.string.moderation_escalation_remove_rung),
                                    color = tokens.destructive,
                                )
                            }
                        }
                        if (rung.action == "timeout") {
                            AppTextField(
                                value = rung.timeoutSeconds,
                                onValueChange = { rung.timeoutSeconds = it },
                                label = stringResource(Res.string.moderation_escalation_duration),
                                modifier = Modifier.fillMaxWidth(),
                            )
                        }
                    }
                }
                TextButton(onClick = { rungs.add(EditableRung("warn", "600")) }) {
                    Text(
                        text = stringResource(Res.string.moderation_escalation_add_rung),
                        color = tokens.primary,
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    val ladder: List<EscalationLadderStep> =
                        rungs.mapIndexed { index, rung ->
                            EscalationLadderStep(
                                atOffense = index + 1,
                                action = rung.action,
                                timeoutSeconds =
                                    if (rung.action == "timeout") rung.timeoutSeconds.trim().toIntOrNull()
                                    else null,
                            )
                        }
                    onConfirm(
                        UpsertEscalationPolicyBody(
                            isEnabled = enabled,
                            ladder = ladder,
                            offenseWindowHours = windowInput.trim().toIntOrNull() ?: 0,
                            countAutoModViolations = countAutomod,
                        )
                    )
                },
            ) {
                Text(stringResource(Res.string.moderation_escalation_save))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_escalation_cancel))
            }
        },
    )
}

// The shared-ban trust web (J.9): two opt-in switches (accept inbound / share outbound) that persist on toggle,
// plus the trusted-partner list with add-by-channel-id and per-row delete. Writes are Lead-Moderator (SuperMod).
@Composable
private fun SharedBansCard(
    settings: SharedBanSettings,
    manage: ManageDecision,
    searchChannels: suspend (query: String) -> List<PickerOption>,
    onSave: (accept: Boolean, share: Boolean) -> Unit,
    onAddTrusted: (String) -> Unit,
    onRemoveTrusted: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var picked: PickerRef? by remember(settings) { mutableStateOf(null) }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.moderation_shared_desc),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        SharedBanSwitchRow(
            label = stringResource(Res.string.moderation_shared_accept),
            hint = stringResource(Res.string.moderation_shared_accept_hint),
            checked = settings.acceptSharedChatBans,
            manage = manage,
            onToggle = { onSave(it, settings.shareOutgoingBans) },
        )
        SharedBanSwitchRow(
            label = stringResource(Res.string.moderation_shared_share),
            hint = stringResource(Res.string.moderation_shared_share_hint),
            checked = settings.shareOutgoingBans,
            manage = manage,
            onToggle = { onSave(settings.acceptSharedChatBans, it) },
        )
        Separator()
        Text(
            text = stringResource(Res.string.moderation_shared_trusted_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        if (settings.trustedChannels.isEmpty()) {
            Text(
                text = stringResource(Res.string.moderation_shared_trusted_empty),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        } else {
            settings.trustedChannels.forEach { channel -> TrustedChannelRow(channel, manage, onRemoveTrusted) }
        }
        // Pick the trusted channel by broadcaster search, not a raw id/name.
        SearchPickerField(
            search = searchChannels,
            selected = picked,
            onSelect = { picked = it },
            onClear = { picked = null },
            label = stringResource(Res.string.moderation_shared_trusted_add_label),
            modifier = Modifier.fillMaxWidth(),
        )
        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = {
                    picked?.let { onAddTrusted(it.id) }
                    picked = null
                },
                enabled = enabled && picked != null,
            ) {
                Text(stringResource(Res.string.moderation_shared_trusted_add))
            }
        }
    }
}

// One shared-ban switch row: label + hint on the left, the toggle (manage-gated) on the right.
@Composable
private fun SharedBanSwitchRow(
    label: String,
    hint: String,
    checked: Boolean,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(text = label, style = typography.sm, color = tokens.cardForeground)
            Text(text = hint, style = typography.xs, color = tokens.mutedForeground)
        }
        ManageGate(decision = manage) { enabled ->
            Switch(checked = checked, onCheckedChange = { onToggle(it) }, enabled = enabled)
        }
    }
}

// One trusted-partner row: its display name (or id) + a manage-gated Remove.
@Composable
private fun TrustedChannelRow(
    channel: SharedBanTrustedChannel,
    manage: ManageDecision,
    onRemove: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = channel.trustedChannelName.takeIf { it.isNotBlank() } ?: channel.trustedChannelId,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = { onRemove(channel.trustedChannelId) }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_shared_trusted_remove),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                )
            }
        }
    }
}

// One network-nuke batch row (J.2a): target + reach + status, with a Revert affordance while active/partial.
@Composable
private fun NukeBatchRow(batch: NetworkNukeBatch, manage: ManageDecision, onRevert: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // S013b — prefer the human-readable display name over the raw Twitch id; the id is the fallback only for
    // an older batch recorded before the backend started resolving it.
    val target: String =
        batch.targetDisplayName
            ?: batch.targetTwitchUserId?.let { stringResource(Res.string.moderation_nuke_target_fallback, it) }
            ?: batch.targetUserId
            ?: ""
    val revertable: Boolean = batch.status == "active" || batch.status == "partial"

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = stringResource(Res.string.moderation_nuke_target, target),
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = stringResource(Res.string.moderation_nuke_channels, batch.channelCount),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        Badge(variant = nukeStatusVariant(batch.status)) {
            Text(text = nukeStatusLabel(batch.status), style = typography.xs)
        }
        if (revertable) {
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onRevert, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.moderation_nuke_revert),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// The localized label for a nuke batch status value.
@Composable
private fun nukeStatusLabel(status: String): String =
    when (status) {
        "active" -> stringResource(Res.string.moderation_nuke_status_active)
        "partial" -> stringResource(Res.string.moderation_nuke_status_partial)
        "reverted" -> stringResource(Res.string.moderation_nuke_status_reverted)
        else -> status
    }

// Partial legs read as a warning (destructive); reverted is muted (secondary); active is the default accent.
private fun nukeStatusVariant(status: String): BadgeVariant =
    when (status) {
        "partial" -> BadgeVariant.Destructive
        "reverted" -> BadgeVariant.Secondary
        else -> BadgeVariant.Default
    }
