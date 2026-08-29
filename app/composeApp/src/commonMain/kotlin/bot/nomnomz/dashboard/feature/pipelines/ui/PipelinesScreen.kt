// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.pipelines.ui

import kotlinx.coroutines.flow.SharedFlow
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.contentOrNull
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlinx.serialization.json.boolean
import kotlinx.serialization.json.booleanOrNull
import bot.nomnomz.dashboard.core.realtime.HubEvent
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.EntityPickerField
import bot.nomnomz.dashboard.core.designsystem.component.ResourcePickerField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TemplateHelpersLink
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.mutableStateMapOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateList
import androidx.compose.runtime.snapshots.SnapshotStateMap
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.PipelineBlastRadiusSummary
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.AppIcon
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowDownGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowUpGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditLineGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.i18n.resolveSchemaString
import bot.nomnomz.dashboard.core.network.BlockField
import bot.nomnomz.dashboard.core.network.FieldKind
import bot.nomnomz.dashboard.core.network.PaletteBlock
import bot.nomnomz.dashboard.core.network.PickerKind
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.RuntimePalette
import bot.nomnomz.dashboard.core.network.UserRoleOptions
import bot.nomnomz.dashboard.feature.pipelines.state.EditorOptions
import bot.nomnomz.dashboard.feature.pipelines.state.LoopConfigFields
import bot.nomnomz.dashboard.feature.pipelines.state.PickerOption
import bot.nomnomz.dashboard.feature.pipelines.state.decodeLoopConfig
import bot.nomnomz.dashboard.feature.pipelines.state.encodeLoopConfig
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesController
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_action_error
import nomnomzbot.composeapp.generated.resources.pipelines_badge_disabled
import nomnomzbot.composeapp.generated.resources.pipelines_badge_enabled
import nomnomzbot.composeapp.generated.resources.pipelines_block_ban
import nomnomzbot.composeapp.generated.resources.pipelines_block_check_balance
import nomnomzbot.composeapp.generated.resources.pipelines_block_deduct_currency
import nomnomzbot.composeapp.generated.resources.pipelines_block_delete_message
import nomnomzbot.composeapp.generated.resources.pipelines_block_grant_currency
import nomnomzbot.composeapp.generated.resources.pipelines_block_jar_contribute
import nomnomzbot.composeapp.generated.resources.pipelines_block_play_game
import nomnomzbot.composeapp.generated.resources.pipelines_block_post_quote
import nomnomzbot.composeapp.generated.resources.pipelines_block_random
import nomnomzbot.composeapp.generated.resources.pipelines_block_require_tier
import nomnomzbot.composeapp.generated.resources.pipelines_block_run_code
import nomnomzbot.composeapp.generated.resources.pipelines_block_send_discord_notification
import nomnomzbot.composeapp.generated.resources.pipelines_block_send_message
import nomnomzbot.composeapp.generated.resources.pipelines_block_send_reply
import nomnomzbot.composeapp.generated.resources.pipelines_block_set_variable
import nomnomzbot.composeapp.generated.resources.pipelines_block_shoutout
import nomnomzbot.composeapp.generated.resources.pipelines_block_play_sound
import nomnomzbot.composeapp.generated.resources.pipelines_block_play_tts
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_current
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_queue
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_request
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_skip
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_volume
import nomnomzbot.composeapp.generated.resources.pipelines_block_stop
import nomnomzbot.composeapp.generated.resources.pipelines_block_stop_sound
import nomnomzbot.composeapp.generated.resources.pipelines_block_start_live_game
import nomnomzbot.composeapp.generated.resources.pipelines_block_cancel_live_game
import nomnomzbot.composeapp.generated.resources.pipelines_block_pick_from_list
import nomnomzbot.composeapp.generated.resources.pipelines_block_send_webhook
import nomnomzbot.composeapp.generated.resources.pipelines_block_var_compare
import nomnomzbot.composeapp.generated.resources.pipelines_block_timeout
import nomnomzbot.composeapp.generated.resources.pipelines_block_user_role
import nomnomzbot.composeapp.generated.resources.pipelines_block_wait
import nomnomzbot.composeapp.generated.resources.pipelines_chain_empty
import nomnomzbot.composeapp.generated.resources.pipelines_generic_add
import nomnomzbot.composeapp.generated.resources.pipelines_generic_param_key
import nomnomzbot.composeapp.generated.resources.pipelines_generic_param_value
import nomnomzbot.composeapp.generated.resources.pipelines_generic_params_label
import nomnomzbot.composeapp.generated.resources.pipelines_generic_remove
import nomnomzbot.composeapp.generated.resources.pipelines_picker_choose
import nomnomzbot.composeapp.generated.resources.pipelines_condition_label
import nomnomzbot.composeapp.generated.resources.pipelines_condition_label_short
import nomnomzbot.composeapp.generated.resources.pipelines_condition_none
import nomnomzbot.composeapp.generated.resources.pipelines_delete_action_short
import nomnomzbot.composeapp.generated.resources.pipelines_delete_cancel
import nomnomzbot.composeapp.generated.resources.pipelines_delete_confirm
import nomnomzbot.composeapp.generated.resources.pipelines_delete_message
import nomnomzbot.composeapp.generated.resources.pipelines_delete_title
import nomnomzbot.composeapp.generated.resources.pipelines_dialog_cancel
import nomnomzbot.composeapp.generated.resources.pipelines_dialog_create
import nomnomzbot.composeapp.generated.resources.pipelines_dialog_create_title
import nomnomzbot.composeapp.generated.resources.pipelines_dialog_description_label
import nomnomzbot.composeapp.generated.resources.pipelines_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.pipelines_dialog_name_label
import nomnomzbot.composeapp.generated.resources.pipelines_dialog_save
import nomnomzbot.composeapp.generated.resources.pipelines_edit_chain_action
import nomnomzbot.composeapp.generated.resources.pipelines_edit_chain_action_short
import nomnomzbot.composeapp.generated.resources.pipelines_editor_back
import nomnomzbot.composeapp.generated.resources.pipelines_editor_save
import nomnomzbot.composeapp.generated.resources.pipelines_empty
import nomnomzbot.composeapp.generated.resources.pipelines_error
import nomnomzbot.composeapp.generated.resources.pipelines_field_amount
import nomnomzbot.composeapp.generated.resources.pipelines_field_bet_amount
import nomnomzbot.composeapp.generated.resources.pipelines_field_clip
import nomnomzbot.composeapp.generated.resources.pipelines_code_script_create
import nomnomzbot.composeapp.generated.resources.pipelines_code_script_create_new
import nomnomzbot.composeapp.generated.resources.pipelines_code_script_new_name
import nomnomzbot.composeapp.generated.resources.pipelines_code_script_open
import nomnomzbot.composeapp.generated.resources.pipelines_field_code_script_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_cooldown_minutes
import nomnomzbot.composeapp.generated.resources.pipelines_field_dedupe_key
import nomnomzbot.composeapp.generated.resources.pipelines_field_denied_message
import nomnomzbot.composeapp.generated.resources.pipelines_field_compare_left
import nomnomzbot.composeapp.generated.resources.pipelines_field_compare_operator
import nomnomzbot.composeapp.generated.resources.pipelines_field_compare_right
import nomnomzbot.composeapp.generated.resources.pipelines_field_duration_seconds
import nomnomzbot.composeapp.generated.resources.pipelines_field_endpoint
import nomnomzbot.composeapp.generated.resources.pipelines_field_event_type
import nomnomzbot.composeapp.generated.resources.pipelines_field_game_type
import nomnomzbot.composeapp.generated.resources.pipelines_field_list
import nomnomzbot.composeapp.generated.resources.pipelines_field_pick_variable
import nomnomzbot.composeapp.generated.resources.pipelines_field_handle
import nomnomzbot.composeapp.generated.resources.pipelines_field_jar_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_message
import nomnomzbot.composeapp.generated.resources.pipelines_field_message_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_min_balance
import nomnomzbot.composeapp.generated.resources.pipelines_field_min_role
import nomnomzbot.composeapp.generated.resources.pipelines_field_min_tier
import nomnomzbot.composeapp.generated.resources.pipelines_field_percent
import nomnomzbot.composeapp.generated.resources.pipelines_field_query
import nomnomzbot.composeapp.generated.resources.pipelines_field_quote_number
import nomnomzbot.composeapp.generated.resources.pipelines_field_reason
import nomnomzbot.composeapp.generated.resources.pipelines_field_set_var
import nomnomzbot.composeapp.generated.resources.pipelines_field_song_queue_max
import nomnomzbot.composeapp.generated.resources.pipelines_field_text
import nomnomzbot.composeapp.generated.resources.pipelines_field_trigger_type
import nomnomzbot.composeapp.generated.resources.pipelines_field_user_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_variable_name
import nomnomzbot.composeapp.generated.resources.pipelines_field_variable_value
import nomnomzbot.composeapp.generated.resources.pipelines_field_voice
import nomnomzbot.composeapp.generated.resources.pipelines_field_volume
import nomnomzbot.composeapp.generated.resources.pipelines_field_wait_for_finish
import nomnomzbot.composeapp.generated.resources.pipelines_field_wait_seconds
import nomnomzbot.composeapp.generated.resources.pipelines_field_scene
import nomnomzbot.composeapp.generated.resources.pipelines_field_source
import nomnomzbot.composeapp.generated.resources.pipelines_field_visible
import nomnomzbot.composeapp.generated.resources.pipelines_field_filter
import nomnomzbot.composeapp.generated.resources.pipelines_field_enabled
import nomnomzbot.composeapp.generated.resources.pipelines_field_transition
import nomnomzbot.composeapp.generated.resources.pipelines_field_studio
import nomnomzbot.composeapp.generated.resources.pipelines_field_duration_ms
import nomnomzbot.composeapp.generated.resources.pipelines_field_input
import nomnomzbot.composeapp.generated.resources.pipelines_field_muted
import nomnomzbot.composeapp.generated.resources.pipelines_field_toggle
import nomnomzbot.composeapp.generated.resources.pipelines_field_volume_db
import nomnomzbot.composeapp.generated.resources.pipelines_field_volume_mul
import nomnomzbot.composeapp.generated.resources.pipelines_field_action_verb
import nomnomzbot.composeapp.generated.resources.pipelines_field_hotkey_name
import nomnomzbot.composeapp.generated.resources.pipelines_field_image_format
import nomnomzbot.composeapp.generated.resources.pipelines_field_request_type
import nomnomzbot.composeapp.generated.resources.pipelines_field_request_data
import nomnomzbot.composeapp.generated.resources.pipelines_field_vendor
import nomnomzbot.composeapp.generated.resources.pipelines_field_execution
import nomnomzbot.composeapp.generated.resources.pipelines_field_halt_on_failure
import nomnomzbot.composeapp.generated.resources.pipelines_field_requests
import nomnomzbot.composeapp.generated.resources.pipelines_field_model
import nomnomzbot.composeapp.generated.resources.pipelines_field_hotkey
import nomnomzbot.composeapp.generated.resources.pipelines_field_expression
import nomnomzbot.composeapp.generated.resources.pipelines_field_active
import nomnomzbot.composeapp.generated.resources.pipelines_field_move_x
import nomnomzbot.composeapp.generated.resources.pipelines_field_move_y
import nomnomzbot.composeapp.generated.resources.pipelines_field_rotation
import nomnomzbot.composeapp.generated.resources.pipelines_field_size
import nomnomzbot.composeapp.generated.resources.pipelines_field_time_seconds
import nomnomzbot.composeapp.generated.resources.pipelines_field_relative
import nomnomzbot.composeapp.generated.resources.pipelines_field_color_r
import nomnomzbot.composeapp.generated.resources.pipelines_field_color_g
import nomnomzbot.composeapp.generated.resources.pipelines_field_color_b
import nomnomzbot.composeapp.generated.resources.pipelines_field_color_a
import nomnomzbot.composeapp.generated.resources.pipelines_field_art_mesh_tag
import nomnomzbot.composeapp.generated.resources.pipelines_field_payload_json
import nomnomzbot.composeapp.generated.resources.pipelines_field_giveaway_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_key
import nomnomzbot.composeapp.generated.resources.pipelines_field_value
import nomnomzbot.composeapp.generated.resources.pipelines_field_delta
import nomnomzbot.composeapp.generated.resources.pipelines_field_target
import nomnomzbot.composeapp.generated.resources.pipelines_field_pipeline
import nomnomzbot.composeapp.generated.resources.pipelines_field_delay_seconds
import nomnomzbot.composeapp.generated.resources.pipelines_field_role_or_capability
import nomnomzbot.composeapp.generated.resources.pipelines_field_target_variable
import nomnomzbot.composeapp.generated.resources.pipelines_field_duration_minutes
import nomnomzbot.composeapp.generated.resources.pipelines_field_widget_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_data
import nomnomzbot.composeapp.generated.resources.pipelines_block_run_pipeline
import nomnomzbot.composeapp.generated.resources.pipelines_field_mode
import nomnomzbot.composeapp.generated.resources.pipelines_field_wait
import nomnomzbot.composeapp.generated.resources.pipelines_field_args
import nomnomzbot.composeapp.generated.resources.pipelines_field_named_args
import nomnomzbot.composeapp.generated.resources.pipelines_run_pipeline_named_arg_label
import nomnomzbot.composeapp.generated.resources.pipelines_run_pipeline_no_target_hint
import nomnomzbot.composeapp.generated.resources.pipelines_run_pipeline_args_add
import nomnomzbot.composeapp.generated.resources.pipelines_run_pipeline_args_remove
import nomnomzbot.composeapp.generated.resources.pipelines_loading
import nomnomzbot.composeapp.generated.resources.pipelines_new_action
import nomnomzbot.composeapp.generated.resources.pipelines_no_description
import nomnomzbot.composeapp.generated.resources.pipelines_delete_action
import nomnomzbot.composeapp.generated.resources.pipelines_rename_action
import nomnomzbot.composeapp.generated.resources.pipelines_rename_action_short
import nomnomzbot.composeapp.generated.resources.pipelines_retry
import nomnomzbot.composeapp.generated.resources.pipelines_step_action_label
import nomnomzbot.composeapp.generated.resources.pipelines_step_add
import nomnomzbot.composeapp.generated.resources.pipelines_step_add_title
import nomnomzbot.composeapp.generated.resources.pipelines_step_count
import nomnomzbot.composeapp.generated.resources.pipelines_step_delete
import nomnomzbot.composeapp.generated.resources.pipelines_step_delete_short
import nomnomzbot.composeapp.generated.resources.pipelines_step_edit
import nomnomzbot.composeapp.generated.resources.pipelines_step_edit_short
import nomnomzbot.composeapp.generated.resources.pipelines_step_edit_title
import nomnomzbot.composeapp.generated.resources.pipelines_step_move_down
import nomnomzbot.composeapp.generated.resources.pipelines_step_move_down_short
import nomnomzbot.composeapp.generated.resources.pipelines_step_move_up
import nomnomzbot.composeapp.generated.resources.pipelines_step_move_up_short
import nomnomzbot.composeapp.generated.resources.pipelines_step_stop_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_add_if
import nomnomzbot.composeapp.generated.resources.pipelines_block_if_title
import nomnomzbot.composeapp.generated.resources.pipelines_block_if_summary
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_then
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_else
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_empty
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_add
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_step_edit
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_step_delete
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_step_move_up
import nomnomzbot.composeapp.generated.resources.pipelines_block_lane_step_move_down
import nomnomzbot.composeapp.generated.resources.pipelines_block_add_switch
import nomnomzbot.composeapp.generated.resources.pipelines_block_switch_title
import nomnomzbot.composeapp.generated.resources.pipelines_block_switch_edit_title
import nomnomzbot.composeapp.generated.resources.pipelines_block_switch_summary
import nomnomzbot.composeapp.generated.resources.pipelines_block_switch_value_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_add
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_add_title
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_edit_title
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_match_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_operator_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_default_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_summary
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_summary_default
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_lane_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_edit
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_delete
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_move_up
import nomnomzbot.composeapp.generated.resources.pipelines_block_case_move_down
import nomnomzbot.composeapp.generated.resources.pipelines_block_add_loop
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_title
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_edit_title
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_summary_repeat
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_summary_foreach
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_summary_while
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_mode_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_mode_repeat
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_mode_foreach
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_mode_while
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_count_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_list_var_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_max_iterations_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_max_runtime_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_condition_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_loop_lane_label
import nomnomzbot.composeapp.generated.resources.pipelines_block_operator_eq
import nomnomzbot.composeapp.generated.resources.pipelines_block_operator_ne
import nomnomzbot.composeapp.generated.resources.pipelines_block_operator_gt
import nomnomzbot.composeapp.generated.resources.pipelines_block_operator_lt
import nomnomzbot.composeapp.generated.resources.pipelines_block_operator_gte
import nomnomzbot.composeapp.generated.resources.pipelines_block_operator_lte
import nomnomzbot.composeapp.generated.resources.pipelines_block_operator_contains

import nomnomzbot.composeapp.generated.resources.shell_nav_pipelines
import nomnomzbot.composeapp.generated.resources.pipelines_toggle_action
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_action
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_title
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_subtitle
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_vars_label
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_run
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_running
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_close
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_ok
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_failed
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_meta
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_error
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_chat_heading
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_chat_empty
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_effects_heading
import nomnomzbot.composeapp.generated.resources.pipelines_testrun_effects_empty
import nomnomzbot.composeapp.generated.resources.pipelines_effect_row_type
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Pipelines page: the channel's visual automation pipelines (the action-chain engine), all real data from
// [PipelinesController]. The screen is a pure projection of the controller's state — the LIST surface
// (create / rename / enable-disable / delete) and the chain EDITOR surface (add / configure / reorder / remove
// the ordered action blocks with an optional condition + stop flag, then save). It loads on first composition.
@Composable
fun PipelinesScreen(
    controller: PipelinesController,
    role: ManagementRole?,
    templateHelpersApi: TemplateHelpersApi,
    hubEvents: SharedFlow<HubEvent>? = null,
    historyController: bot.nomnomz.dashboard.feature.pipelines.state.PipelineExecutionHistoryController? = null,
    heldActionKeys: Set<String> = emptySet(),
    /**
     * Navigate to the real Code Scripts editor for [scriptId] (S046-code-tier-link). A `run_code` step's script
     * field fires this when the operator opens its bound script, or right after creating a new one — the actual
     * source is authored in the Code Scripts feature, never inline in this dialog. Defaults to a no-op so callers
     * that have not wired the Code Scripts route yet still compile; the shell wires the real navigation.
     */
    onOpenCodeScript: (scriptId: String) -> Unit = {},
) {
    val state: PipelinesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Pipelines gates every write control at its single Editor manage floor
    // (frontend-ia.md §3) — both the list surface (create / rename / toggle / delete) and the chain editor
    // (add / configure / reorder / remove / save). A caller below it sees the list and the chain but each write
    // disabled with "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Pipelines)

    // The run-history debugging surface (S008c-read-b) is a separate read-only screen entered from the list
    // header; hidden entirely below the `pipelines:read` floor (§7 hide-below-read-floor) rather than shown
    // disabled, since there is nothing to disable-with-reason for a pure read.
    val canReadHistory: Boolean =
        historyController != null &&
            bot.nomnomz.dashboard.feature.pipelines.state.PipelineExecutionHistoryAccess.canRead(heldActionKeys)
    var showHistory: Boolean by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { controller.load() }

    // Live config pushes: another operator (or the bot) changing this domain refetches the page

    // instead of leaving stale rows on screen until a manual reload.

    if (hubEvents != null) {

        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }

    }

    if (showHistory && historyController != null) {
        PipelineHistoryScreen(controller = historyController, onBack = { showHistory = false })
        return
    }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: PipelinesState = state) {
            is PipelinesState.Loading -> CenteredMessage(stringResource(Res.string.pipelines_loading))
            is PipelinesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is PipelinesState.Empty ->
                ListContent(
                    pipelines = emptyList(),
                    actionError = null,
                    manage = manage,
                    controller = controller,
                    scope = scope,
                    canReadHistory = canReadHistory,
                    onOpenHistory = { showHistory = true },
                )
            is PipelinesState.Ready ->
                ListContent(
                    pipelines = current.pipelines,
                    actionError = current.actionError,
                    manage = manage,
                    controller = controller,
                    scope = scope,
                    canReadHistory = canReadHistory,
                    onOpenHistory = { showHistory = true },
                )
            is PipelinesState.Editing ->
                ChainEditor(
                    editing = current,
                    manage = manage,
                    controller = controller,
                    scope = scope,
                    templateHelpersApi = templateHelpersApi,
                    onOpenCodeScript = onOpenCodeScript,
                )
        }
    }
}

// ── The list surface ─────────────────────────────────────────────────────────

@Composable
private fun ListContent(
    pipelines: List<PipelineSummary>,
    actionError: String?,
    manage: ManageDecision,
    controller: PipelinesController,
    scope: kotlinx.coroutines.CoroutineScope,
    canReadHistory: Boolean = false,
    onOpenHistory: () -> Unit = {},
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    // null = no dialog; a value = the create/edit dialog seed. A null id is a create, an id an edit.
    var editor: PipelineEditor? by remember { mutableStateOf(null) }
    var pendingDelete: PipelineSummary? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        ListHeader(
            manage = manage,
            onNew = { editor = PipelineEditor.create() },
            canReadHistory = canReadHistory,
            onOpenHistory = onOpenHistory,
        )
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.pipelines_action_error, it)) }

        if (pipelines.isEmpty()) {
            CenteredMessage(stringResource(Res.string.pipelines_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
                LazyColumn(modifier = Modifier.fillMaxWidth()) {
                    itemsIndexed(items = pipelines, key = { _, pipeline -> pipeline.id }) { index, pipeline ->
                        if (index > 0) {
                            Separator()
                        }
                        PipelineRow(
                            pipeline = pipeline,
                            manage = manage,
                            onOpen = { scope.launch { controller.openEditor(pipeline) } },
                            onEdit = { editor = PipelineEditor.edit(pipeline) },
                            onToggle = { enabled -> scope.launch { controller.togglePipeline(pipeline.id, enabled) } },
                            onDelete = { pendingDelete = pipeline },
                        )
                    }
                }
            }
        }
    }

    editor?.let { open ->
        PipelineFormDialog(
            editor = open,
            onDismiss = { editor = null },
            onSubmit = { name, description ->
                val target: PipelineEditor = open
                editor = null
                scope.launch {
                    if (target.id == null) controller.createPipeline(name, description)
                    else controller.renamePipeline(target.id, name, description)
                }
            },
        )
    }

    pendingDelete?.let { pipeline ->
        // Fetched fresh per pipeline (never cached/guessed) — the counted blast radius the delete confirm
        // MUST show before the destructive delete can proceed (S-CONSEQ-b).
        var blastRadius: BlastRadiusLoadState by remember(pipeline.id) { mutableStateOf(BlastRadiusLoadState.Loading) }
        LaunchedEffect(pipeline.id) {
            blastRadius =
                when (val result: ApiResult<PipelineBlastRadiusSummary> = controller.fetchBlastRadius(pipeline.id)) {
                    is ApiResult.Ok -> BlastRadiusLoadState.Loaded(result.value)
                    is ApiResult.Failure -> BlastRadiusLoadState.Failed
                }
        }
        PipelineDeleteConfirmDialog(
            pipelineName = resolveRowLabel(pipeline.name, typeLabel = "Pipeline", discriminatorSource = pipeline.id),
            blastRadius = blastRadius,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deletePipeline(pipeline.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

@Composable
private fun ListHeader(
    manage: ManageDecision,
    onNew: () -> Unit,
    canReadHistory: Boolean = false,
    onOpenHistory: () -> Unit = {},
) {
    val tokens = LocalTokens.current
    val newLabel: String = stringResource(Res.string.pipelines_new_action)
    val historyLabel: String = pipelineHistoryActionLabel()

    PageHeader(title = stringResource(Res.string.shell_nav_pipelines)) {
        Row(horizontalArrangement = Arrangement.spacedBy(LocalSpacing.current.s2)) {
            if (canReadHistory) {
                TextButton(onClick = onOpenHistory) { Text(text = historyLabel) }
            }
            ManageGate(decision = manage) { enabled ->
                GlyphButton(
                    icon = AddGlyph,
                    label = newLabel,
                    onClick = onNew,
                    enabled = enabled,
                )
            }
        }
    }
}

@Composable
private fun PipelineRow(
    pipeline: PipelineSummary,
    manage: ManageDecision,
    onOpen: () -> Unit,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val snippet: String =
        pipeline.description?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.pipelines_no_description)
    val stateLabel: String =
        stringResource(
            if (pipeline.isEnabled) Res.string.pipelines_badge_enabled
            else Res.string.pipelines_badge_disabled
        )
    val displayName: String =
        resolveRowLabel(pipeline.name, typeLabel = "Pipeline", discriminatorSource = pipeline.id)
    val toggleLabel: String = stringResource(Res.string.pipelines_toggle_action, displayName)
    val editChainLabel: String = stringResource(Res.string.pipelines_edit_chain_action, displayName)
    val renameLabel: String = stringResource(Res.string.pipelines_rename_action, displayName)
    val deleteLabel: String = stringResource(Res.string.pipelines_delete_action, displayName)

    Row(
        modifier =
            Modifier.fillMaxWidth()
                .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier =
                Modifier.weight(1f).clearAndSetSemantics {
                    contentDescription = "$displayName, $stateLabel. $snippet"
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = displayName,
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = snippet,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        // Opening the chain editor is navigation/read, not a write — stays enabled for everyone.
        GlyphButton(
            icon = EditLineGlyph,
            label = editChainLabel,
            onClick = onOpen,
            tint = tokens.primary,
        )
        ManageGate(decision = manage) { enabled ->
            GlyphButton(icon = EditGlyph, label = renameLabel, onClick = onEdit, enabled = enabled)
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
            Switch(
                checked = pipeline.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
    }
}

// ── The chain editor surface ──────────────────────────────────────────────────

@Composable
private fun ChainEditor(
    editing: PipelinesState.Editing,
    manage: ManageDecision,
    controller: PipelinesController,
    scope: kotlinx.coroutines.CoroutineScope,
    templateHelpersApi: TemplateHelpersApi,
    onOpenCodeScript: (scriptId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // null = no step dialog; a value = the add/edit step dialog. A null index is an add, an index an edit.
    var stepDialog: StepDialogTarget? by remember { mutableStateOf(null) }
    // Whether the S047 dry-run dialog is open — its own transient state (variables text) lives inside the dialog.
    var showTestRun: Boolean by remember { mutableStateOf(false) }

    // null = no if-block dialog; a value = the add/edit-condition dialog for one "if" block.
    var ifBlockDialog: IfBlockDialogTarget? by remember { mutableStateOf(null) }
    // null = no switch-value dialog; a value = the add/edit-value dialog for one "switch" block.
    var switchBlockDialog: SwitchBlockDialogTarget? by remember { mutableStateOf(null) }
    // null = no case dialog; a value = the add/edit dialog for one "switch_case" child.
    var switchCaseDialog: SwitchCaseDialogTarget? by remember { mutableStateOf(null) }
    // null = no loop-config dialog; a value = the add/edit dialog for one "loop" block.
    var loopBlockDialog: LoopBlockDialogTarget? by remember { mutableStateOf(null) }

    val backLabel: String = stringResource(Res.string.pipelines_editor_back)
    val saveLabel: String = stringResource(Res.string.pipelines_editor_save)
    val testLabel: String = stringResource(Res.string.pipelines_testrun_action)
    val addLabel: String = stringResource(Res.string.pipelines_step_add)
    val addIfLabel: String = stringResource(Res.string.pipelines_block_add_if)
    val addSwitchLabel: String = stringResource(Res.string.pipelines_block_add_switch)
    val addLoopLabel: String = stringResource(Res.string.pipelines_block_add_loop)

    // The root chain, tree-ordered: only the steps with no parent block, in their lane's `order`. Each "if"
    // block's "then"/"else" children are rendered nested inside its own card, never here at the top level.
    val rootSteps: List<PipelineStep> = editing.steps.filter { it.parentStepId == null }.sortedBy { it.order ?: 0 }

    Column(modifier = Modifier.fillMaxSize(), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            TextButton(
                onClick = { scope.launch { controller.closeEditor() } },
                modifier = Modifier.semantics { contentDescription = backLabel },
            ) {
                Text(text = backLabel, color = tokens.primary, maxLines = 1)
            }
            Text(
                text =
                    resolveRowLabel(editing.name, typeLabel = "Pipeline", discriminatorSource = editing.pipelineId),
                style = typography.xl2,
                color = tokens.foreground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            // Opening the dry-run dialog is read-only (nothing the backend runs is persisted or dispatched), so
            // it stays enabled below the manage floor — same rationale as the "edit chain" open action.
            TextButton(
                onClick = { showTestRun = true },
                modifier = Modifier.semantics { contentDescription = testLabel },
            ) {
                Text(text = testLabel, color = tokens.primary, maxLines = 1)
            }
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { scope.launch { controller.saveChain() } },
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = saveLabel },
                ) {
                    Text(text = saveLabel)
                }
            }
        }

        editing.actionError?.let { ActionErrorBanner(message = stringResource(Res.string.pipelines_action_error, it)) }

        Text(
            text = stringResource(Res.string.pipelines_step_count, editing.steps.size),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { stepDialog = StepDialogTarget(parentStepId = null, branch = null, step = null) },
                    enabled = enabled,
                    modifier = Modifier.weight(1f).semantics { contentDescription = addLabel },
                ) {
                    Text(text = addLabel)
                }
            }
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { ifBlockDialog = IfBlockDialogTarget(blockId = null, condition = null) },
                    enabled = enabled,
                    modifier = Modifier.weight(1f).semantics { contentDescription = addIfLabel },
                ) {
                    Text(text = addIfLabel)
                }
            }
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { switchBlockDialog = SwitchBlockDialogTarget(blockId = null, value = null) },
                    enabled = enabled,
                    modifier = Modifier.weight(1f).semantics { contentDescription = addSwitchLabel },
                ) {
                    Text(text = addSwitchLabel)
                }
            }
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { loopBlockDialog = LoopBlockDialogTarget(blockId = null, config = null, condition = null) },
                    enabled = enabled,
                    modifier = Modifier.weight(1f).semantics { contentDescription = addLoopLabel },
                ) {
                    Text(text = addLoopLabel)
                }
            }
        }

        if (editing.steps.isEmpty()) {
            Box(modifier = Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                CenteredMessage(stringResource(Res.string.pipelines_chain_empty))
            }
        } else {
            Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
                LazyColumn(modifier = Modifier.fillMaxWidth()) {
                    itemsIndexed(items = rootSteps) { index, step ->
                        if (index > 0) {
                            Separator()
                        }
                        if (step.blockKind == "if") {
                            IfBlockCard(
                                block = step,
                                allSteps = editing.steps,
                                index = index,
                                total = rootSteps.size,
                                palette = editing.palette,
                                manage = manage,
                                controller = controller,
                                onEditCondition = { ifBlockDialog = IfBlockDialogTarget(blockId = step.id, condition = step.condition) },
                                onMoveUp = { step.id?.let { controller.moveBranchStepUp(it) } },
                                onMoveDown = { step.id?.let { controller.moveBranchStepDown(it) } },
                                onRemove = { step.id?.let { controller.removeBranchStep(it) } },
                                onAddToLane = { branch -> stepDialog = StepDialogTarget(parentStepId = step.id, branch = branch, step = null) },
                                onEditLaneStep = { child -> stepDialog = StepDialogTarget(parentStepId = child.parentStepId, branch = child.branch, step = child) },
                            )
                        } else if (step.blockKind == "switch") {
                            SwitchBlockCard(
                                block = step,
                                allSteps = editing.steps,
                                index = index,
                                total = rootSteps.size,
                                palette = editing.palette,
                                manage = manage,
                                controller = controller,
                                onEditValue = { switchBlockDialog = SwitchBlockDialogTarget(blockId = step.id, value = step.blockConfig) },
                                onMoveUp = { step.id?.let { controller.moveBranchStepUp(it) } },
                                onMoveDown = { step.id?.let { controller.moveBranchStepDown(it) } },
                                onRemove = { step.id?.let { controller.removeBranchStep(it) } },
                                onAddCase = { switchId -> switchCaseDialog = SwitchCaseDialogTarget(switchId = switchId, caseId = null, config = null) },
                                onEditCase = { caseStep -> switchCaseDialog = SwitchCaseDialogTarget(switchId = caseStep.parentStepId, caseId = caseStep.id, config = caseStep.blockConfig) },
                                onAddCaseStep = { caseId -> stepDialog = StepDialogTarget(parentStepId = caseId, branch = null, step = null) },
                                onEditCaseStep = { child -> stepDialog = StepDialogTarget(parentStepId = child.parentStepId, branch = child.branch, step = child) },
                            )
                        } else if (step.blockKind == "loop") {
                            LoopBlockCard(
                                block = step,
                                allSteps = editing.steps,
                                index = index,
                                total = rootSteps.size,
                                palette = editing.palette,
                                manage = manage,
                                controller = controller,
                                onEditConfig = {
                                    loopBlockDialog = LoopBlockDialogTarget(blockId = step.id, config = step.blockConfig, condition = step.condition)
                                },
                                onMoveUp = { step.id?.let { controller.moveBranchStepUp(it) } },
                                onMoveDown = { step.id?.let { controller.moveBranchStepDown(it) } },
                                onRemove = { step.id?.let { controller.removeBranchStep(it) } },
                                onAddToLane = { step.id?.let { stepDialog = StepDialogTarget(parentStepId = it, branch = null, step = null) } },
                                onEditLaneStep = { child -> stepDialog = StepDialogTarget(parentStepId = child.parentStepId, branch = child.branch, step = child) },
                            )
                        } else {
                            StepCard(
                                index = index,
                                total = rootSteps.size,
                                step = step,
                                palette = editing.palette,
                                manage = manage,
                                onEdit = { stepDialog = StepDialogTarget(parentStepId = null, branch = null, step = step) },
                                onRemove = { step.id?.let { controller.removeBranchStep(it) } },
                                onMoveUp = { step.id?.let { controller.moveBranchStepUp(it) } },
                                onMoveDown = { step.id?.let { controller.moveBranchStepDown(it) } },
                            )
                        }
                    }
                }
            }
        }
    }

    stepDialog?.let { target ->
        StepFormDialog(
            initial = target.step,
            palette = editing.palette,
            options = editing.options,
            templateHelpersApi = templateHelpersApi,
            onOpenCodeScript = onOpenCodeScript,
            createCodeScript = controller::createCodeScript,
            onDismiss = { stepDialog = null },
            onSubmit = { step ->
                val existingId: String? = target.step?.id
                stepDialog = null
                when {
                    existingId != null -> controller.updateStepById(existingId, step)
                    // `branch` is null for a lane that is the only lane under its parent (a switch's own
                    // "switch_case" children, or a case's own body steps) — `parentStepId` alone still
                    // addresses it, so only that needs to be non-null to route into addBranchStep.
                    target.parentStepId != null -> controller.addBranchStep(target.parentStepId, target.branch, step)
                    else -> controller.addRootStep(step)
                }
            },
        )
    }

    ifBlockDialog?.let { target ->
        IfBlockFormDialog(
            initial = target.condition,
            palette = editing.palette,
            options = editing.options,
            templateHelpersApi = templateHelpersApi,
            onOpenCodeScript = onOpenCodeScript,
            createCodeScript = controller::createCodeScript,
            onDismiss = { ifBlockDialog = null },
            onSubmit = { condition ->
                val existingBlockId: String? = target.blockId
                ifBlockDialog = null
                if (existingBlockId != null) {
                    controller.updateStepById(
                        existingBlockId,
                        PipelineStep(action = PipelineNode(type = "block"), blockKind = "if", condition = condition),
                    )
                } else {
                    controller.addIfBlock(condition)
                }
            },
        )
    }

    switchBlockDialog?.let { target ->
        SwitchBlockFormDialog(
            initial = decodeSwitchValue(target.value),
            onDismiss = { switchBlockDialog = null },
            onSubmit = { value ->
                val existingBlockId: String? = target.blockId
                switchBlockDialog = null
                if (existingBlockId != null) {
                    controller.updateStepById(
                        existingBlockId,
                        PipelineStep(
                            action = PipelineNode(type = "block"),
                            blockKind = "switch",
                            blockConfig = encodeSwitchValue(value),
                        ),
                    )
                } else {
                    controller.addSwitchBlock(value)
                }
            },
        )
    }

    switchCaseDialog?.let { target ->
        val (initialMatch: String, initialOperator: String, initialIsDefault: Boolean) = decodeSwitchCase(target.config)
        SwitchCaseFormDialog(
            initialMatch = initialMatch,
            initialOperator = initialOperator,
            initialIsDefault = initialIsDefault,
            onDismiss = { switchCaseDialog = null },
            onSubmit = { match, operatorKey, isDefault ->
                val switchId: String? = target.switchId
                val existingCaseId: String? = target.caseId
                switchCaseDialog = null
                val caseStep =
                    PipelineStep(
                        action = PipelineNode(type = "block"),
                        blockKind = "switch_case",
                        blockConfig = encodeSwitchCase(match, operatorKey, isDefault),
                    )
                when {
                    existingCaseId != null -> controller.updateStepById(existingCaseId, caseStep)
                    switchId != null -> controller.addBranchStep(switchId, null, caseStep)
                }
            },
        )
    }

    loopBlockDialog?.let { target ->
        val decoded: LoopConfigFields = decodeLoopConfig(target.config)
        LoopBlockFormDialog(
            initialMode = decoded.mode,
            initialCount = decoded.count,
            initialListVar = decoded.listVar,
            initialMaxIterations = decoded.maxIterations,
            initialMaxLoopRuntimeSeconds = decoded.maxLoopRuntimeSeconds,
            initialCondition = target.condition,
            palette = editing.palette,
            options = editing.options,
            templateHelpersApi = templateHelpersApi,
            onOpenCodeScript = onOpenCodeScript,
            createCodeScript = controller::createCodeScript,
            onDismiss = { loopBlockDialog = null },
            onSubmit = { mode, count, listVar, maxIterations, maxLoopRuntimeSeconds, whileCondition ->
                val existingBlockId: String? = target.blockId
                loopBlockDialog = null
                val loopStep =
                    PipelineStep(
                        action = PipelineNode(type = "block"),
                        blockKind = "loop",
                        condition = whileCondition,
                        blockConfig = encodeLoopConfig(mode, count, listVar, maxIterations, maxLoopRuntimeSeconds),
                    )
                if (existingBlockId != null) {
                    controller.updateStepById(existingBlockId, loopStep)
                } else {
                    controller.addLoopBlock(mode, count, listVar, maxIterations, maxLoopRuntimeSeconds, whileCondition)
                }
            },
        )
    }

    if (showTestRun) {
        // S047-remaining: the dialog itself is now shared (feature/pipelines/ui/PipelineTestRunDialog.kt) so
        // commands/event-responses/timers show the identical dry-run UI over the identical backend call.
        PipelineTestRunDialog(
            running = editing.testRunning,
            result = editing.testResult,
            error = editing.testError,
            onRun = { variables -> scope.launch { controller.testRun(variables) } },
            onDismiss = { showTestRun = false },
        )
    }
}

@Composable
private fun StepCard(
    index: Int,
    total: Int,
    step: PipelineStep,
    palette: RuntimePalette,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onRemove: () -> Unit,
    onMoveUp: () -> Unit,
    onMoveDown: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val actionName: String = blockDisplayName(palette.action(step.action.type), step.action.type)
    val conditionText: String =
        step.condition?.let {
            stringResource(
                Res.string.pipelines_condition_label,
                blockDisplayName(palette.condition(it.type), it.type),
            )
        } ?: stringResource(Res.string.pipelines_condition_none)

    val editLabel: String = stringResource(Res.string.pipelines_step_edit, index + 1)
    val removeLabel: String = stringResource(Res.string.pipelines_step_delete, index + 1)
    val upLabel: String = stringResource(Res.string.pipelines_step_move_up, index + 1)
    val downLabel: String = stringResource(Res.string.pipelines_step_move_down, index + 1)

    Column(
        modifier =
            Modifier.fillMaxWidth()
                .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Text(text = "${index + 1}", style = typography.sm, color = tokens.mutedForeground)
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
                Text(text = actionName, style = typography.lg, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(text = conditionText, style = typography.xs, color = tokens.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
        // Param summary: each configured param as "label: value", so a card shows what the block will do.
        ParamSummary(step.action, palette)

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
            // Reorder is a write AND bounded by position: the gate's `enabled` and the bound both must hold.
            ManageGate(decision = manage) { allowed ->
                val canMoveUp: Boolean = allowed && index > 0
                GlyphButton(
                    icon = ArrowUpGlyph,
                    label = upLabel,
                    onClick = onMoveUp,
                    enabled = canMoveUp,
                    tint = tokens.primary,
                )
            }
            ManageGate(decision = manage) { allowed ->
                val canMoveDown: Boolean = allowed && index < total - 1
                GlyphButton(
                    icon = ArrowDownGlyph,
                    label = downLabel,
                    onClick = onMoveDown,
                    enabled = canMoveDown,
                    tint = tokens.primary,
                )
            }
            Box(modifier = Modifier.weight(1f))
            ManageGate(decision = manage) { enabled ->
                GlyphButton(icon = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
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
}

// A nested "if" block: no action of its own to run — just its gating condition — plus its "then"/"else" lanes,
// each rendered by [LaneSection] with its own independent add/reorder/remove controls.
@Composable
private fun IfBlockCard(
    block: PipelineStep,
    allSteps: List<PipelineStep>,
    index: Int,
    total: Int,
    palette: RuntimePalette,
    manage: ManageDecision,
    controller: PipelinesController,
    onEditCondition: () -> Unit,
    onMoveUp: () -> Unit,
    onMoveDown: () -> Unit,
    onRemove: () -> Unit,
    onAddToLane: (branch: String) -> Unit,
    onEditLaneStep: (PipelineStep) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val conditionNode: PipelineNode? = block.condition
    val conditionSummary: String =
        conditionNode?.let { blockDisplayName(palette.condition(it.type), it.type) }.orEmpty()

    val blockId: String = block.id ?: return

    val editLabel: String = stringResource(Res.string.pipelines_step_edit, index + 1)
    val removeLabel: String = stringResource(Res.string.pipelines_step_delete, index + 1)
    val upLabel: String = stringResource(Res.string.pipelines_step_move_up, index + 1)
    val downLabel: String = stringResource(Res.string.pipelines_step_move_down, index + 1)

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Text(text = "${index + 1}", style = typography.sm, color = tokens.mutedForeground)
            Text(
                text = stringResource(Res.string.pipelines_block_if_summary, conditionSummary),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
        }

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
            ManageGate(decision = manage) { allowed ->
                GlyphButton(icon = ArrowUpGlyph, label = upLabel, onClick = onMoveUp, enabled = allowed && index > 0, tint = tokens.primary)
            }
            ManageGate(decision = manage) { allowed ->
                GlyphButton(icon = ArrowDownGlyph, label = downLabel, onClick = onMoveDown, enabled = allowed && index < total - 1, tint = tokens.primary)
            }
            Box(modifier = Modifier.weight(1f))
            ManageGate(decision = manage) { enabled -> GlyphButton(icon = EditGlyph, label = editLabel, onClick = onEditCondition, enabled = enabled) }
            ManageGate(decision = manage) { enabled ->
                GlyphButton(icon = TrashGlyph, label = removeLabel, onClick = onRemove, enabled = enabled, tint = tokens.destructive)
            }
        }

        LaneSection(
            label = stringResource(Res.string.pipelines_block_lane_then),
            branch = "then",
            steps = allSteps.filter { it.parentStepId == blockId && it.branch == "then" }.sortedBy { it.order ?: 0 },
            palette = palette,
            manage = manage,
            controller = controller,
            onAdd = { onAddToLane("then") },
            onEditStep = onEditLaneStep,
        )
        LaneSection(
            label = stringResource(Res.string.pipelines_block_lane_else),
            branch = "else",
            steps = allSteps.filter { it.parentStepId == blockId && it.branch == "else" }.sortedBy { it.order ?: 0 },
            palette = palette,
            manage = manage,
            controller = controller,
            onAdd = { onAddToLane("else") },
            onEditStep = onEditLaneStep,
        )
    }
}

// One "then"/"else" lane inside an "if" block: an indented, independently-ordered list of [steps], each with
// its own add/reorder/remove — every write here targets ONLY this lane's steps (by id), never the sibling
// lane or the block that owns them.
@Composable
private fun LaneSection(
    label: String,
    branch: String,
    steps: List<PipelineStep>,
    palette: RuntimePalette,
    manage: ManageDecision,
    controller: PipelinesController,
    onAdd: () -> Unit,
    onEditStep: (PipelineStep) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val addLaneLabel: String = stringResource(Res.string.pipelines_block_lane_add, label)

    Column(
        modifier = Modifier.fillMaxWidth().padding(start = spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = label, style = typography.sm, color = tokens.mutedForeground)
            Box(modifier = Modifier.weight(1f))
            ManageGate(decision = manage) { enabled ->
                GlyphButton(icon = AddGlyph, label = addLaneLabel, onClick = onAdd, enabled = enabled, tint = tokens.primary)
            }
        }

        if (steps.isEmpty()) {
            Text(
                text = stringResource(Res.string.pipelines_block_lane_empty),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        } else {
            Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                for ((laneIndex, laneStep) in steps.withIndex()) {
                    val stepId: String = laneStep.id ?: continue
                    val editLabel: String = stringResource(Res.string.pipelines_block_lane_step_edit, label, laneIndex + 1)
                    val removeLabel: String = stringResource(Res.string.pipelines_block_lane_step_delete, label, laneIndex + 1)
                    val upLabel: String = stringResource(Res.string.pipelines_block_lane_step_move_up, label, laneIndex + 1)
                    val downLabel: String = stringResource(Res.string.pipelines_block_lane_step_move_down, label, laneIndex + 1)
                    val actionName: String = blockDisplayName(palette.action(laneStep.action.type), laneStep.action.type)

                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                    ) {
                        Text(
                            text = actionName,
                            style = typography.sm,
                            color = tokens.cardForeground,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.weight(1f),
                        )
                        ManageGate(decision = manage) { allowed ->
                            GlyphButton(
                                icon = ArrowUpGlyph,
                                label = upLabel,
                                onClick = { controller.moveBranchStepUp(stepId) },
                                enabled = allowed && laneIndex > 0,
                                tint = tokens.primary,
                            )
                        }
                        ManageGate(decision = manage) { allowed ->
                            GlyphButton(
                                icon = ArrowDownGlyph,
                                label = downLabel,
                                onClick = { controller.moveBranchStepDown(stepId) },
                                enabled = allowed && laneIndex < steps.size - 1,
                                tint = tokens.primary,
                            )
                        }
                        ManageGate(decision = manage) { enabled ->
                            GlyphButton(icon = EditGlyph, label = editLabel, onClick = { onEditStep(laneStep) }, enabled = enabled)
                        }
                        ManageGate(decision = manage) { enabled ->
                            GlyphButton(
                                icon = TrashGlyph,
                                label = removeLabel,
                                onClick = { controller.removeBranchStep(stepId) },
                                enabled = enabled,
                                tint = tokens.destructive,
                            )
                        }
                    }
                }
            }
        }
    }
}

// The "if" block's condition editor — add a brand-new block, or re-pick an existing one's condition. Reuses
// the same condition picker + param editor the per-step condition uses (S046-branching-if): an "if" block is
// gated the same way a step's optional condition is, just promoted to its own addressable tree node so it can
// own "then"/"else" child lanes.
@Composable
private fun IfBlockFormDialog(
    initial: PipelineNode?,
    palette: RuntimePalette,
    options: EditorOptions,
    templateHelpersApi: TemplateHelpersApi,
    onOpenCodeScript: (scriptId: String) -> Unit,
    createCodeScript: suspend (name: String) -> PickerOption?,
    onDismiss: () -> Unit,
    onSubmit: (PipelineNode) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    val firstConditionType: String? = initial?.type ?: palette.conditions.firstOrNull()?.type
    var conditionType: String? by remember { mutableStateOf(firstConditionType) }
    val conditionBlock: PaletteBlock? = conditionType?.let { palette.condition(it) }
    val conditionParams: SnapshotStateMap<String, String> = remember { mutableStateMapFrom(initial?.params) }
    val conditionGeneric: SnapshotStateList<GenericEntry> =
        remember { genericEntriesFrom(initial?.params.takeIf { conditionBlock?.hasHints == false }) }

    val canSubmit: Boolean = conditionType != null && blockComplete(conditionBlock, conditionParams)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.pipelines_block_if_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                LabeledText(stringResource(Res.string.pipelines_condition_label_short))
                ConditionPicker(
                    conditions = palette.conditions,
                    selected = conditionBlock,
                    onSelect = { type ->
                        conditionType = type
                        conditionParams.clear()
                        conditionGeneric.clear()
                    },
                )
                conditionBlock?.let { block ->
                    BlockParamEditor(
                        block = block,
                        typed = conditionParams,
                        generic = conditionGeneric,
                        options = options,
                        templateHelpersApi = templateHelpersApi,
                        onOpenCodeScript = onOpenCodeScript,
                        createCodeScript = createCodeScript,
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    val type: String = conditionType ?: return@TextButton
                    onSubmit(PipelineNode(type = type, params = paramsFor(conditionBlock, conditionParams, conditionGeneric)))
                },
                enabled = canSubmit,
            ) {
                Text(text = stringResource(Res.string.pipelines_dialog_save), color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.pipelines_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// A nested "switch" block: no action of its own to run — just its switch value — plus its ordered
// "switch_case" children, each rendered as its own case header (match/operator/is_default) followed by a
// [LaneSection] holding that ONE case's own body steps (what runs when it is the matched case).
@Composable
private fun SwitchBlockCard(
    block: PipelineStep,
    allSteps: List<PipelineStep>,
    index: Int,
    total: Int,
    palette: RuntimePalette,
    manage: ManageDecision,
    controller: PipelinesController,
    onEditValue: () -> Unit,
    onMoveUp: () -> Unit,
    onMoveDown: () -> Unit,
    onRemove: () -> Unit,
    onAddCase: (switchId: String) -> Unit,
    onEditCase: (PipelineStep) -> Unit,
    onAddCaseStep: (caseId: String) -> Unit,
    onEditCaseStep: (PipelineStep) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val blockId: String = block.id ?: return
    val switchValue: String = decodeSwitchValue(block.blockConfig)

    val editLabel: String = stringResource(Res.string.pipelines_step_edit, index + 1)
    val removeLabel: String = stringResource(Res.string.pipelines_step_delete, index + 1)
    val upLabel: String = stringResource(Res.string.pipelines_step_move_up, index + 1)
    val downLabel: String = stringResource(Res.string.pipelines_step_move_down, index + 1)
    val addCaseLabel: String = stringResource(Res.string.pipelines_block_case_add)

    val cases: List<PipelineStep> =
        allSteps.filter { it.parentStepId == blockId && it.blockKind == "switch_case" }.sortedBy { it.order ?: 0 }

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Text(text = "${index + 1}", style = typography.sm, color = tokens.mutedForeground)
            Text(
                text = stringResource(Res.string.pipelines_block_switch_summary, switchValue),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
        }

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
            ManageGate(decision = manage) { allowed ->
                GlyphButton(icon = ArrowUpGlyph, label = upLabel, onClick = onMoveUp, enabled = allowed && index > 0, tint = tokens.primary)
            }
            ManageGate(decision = manage) { allowed ->
                GlyphButton(icon = ArrowDownGlyph, label = downLabel, onClick = onMoveDown, enabled = allowed && index < total - 1, tint = tokens.primary)
            }
            Box(modifier = Modifier.weight(1f))
            ManageGate(decision = manage) { enabled -> GlyphButton(icon = EditGlyph, label = editLabel, onClick = onEditValue, enabled = enabled) }
            ManageGate(decision = manage) { enabled ->
                GlyphButton(icon = TrashGlyph, label = removeLabel, onClick = onRemove, enabled = enabled, tint = tokens.destructive)
            }
        }

        for ((caseIndex, case) in cases.withIndex()) {
            val caseId: String = case.id ?: continue
            val (match: String, operatorKey: String, isDefault: Boolean) = decodeSwitchCase(case.blockConfig)
            val caseSummary: String =
                if (isDefault) stringResource(Res.string.pipelines_block_case_summary_default, caseIndex + 1)
                else stringResource(Res.string.pipelines_block_case_summary, caseIndex + 1, operatorDisplayName(operatorKey), match)
            val caseEditLabel: String = stringResource(Res.string.pipelines_block_case_edit, caseIndex + 1)
            val caseRemoveLabel: String = stringResource(Res.string.pipelines_block_case_delete, caseIndex + 1)
            val caseUpLabel: String = stringResource(Res.string.pipelines_block_case_move_up, caseIndex + 1)
            val caseDownLabel: String = stringResource(Res.string.pipelines_block_case_move_down, caseIndex + 1)

            Column(
                modifier = Modifier.fillMaxWidth().padding(start = spacing.s4),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Text(
                        text = caseSummary,
                        style = typography.sm,
                        color = tokens.cardForeground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                        modifier = Modifier.weight(1f),
                    )
                    ManageGate(decision = manage) { allowed ->
                        GlyphButton(
                            icon = ArrowUpGlyph,
                            label = caseUpLabel,
                            onClick = { controller.moveBranchStepUp(caseId) },
                            enabled = allowed && caseIndex > 0,
                            tint = tokens.primary,
                        )
                    }
                    ManageGate(decision = manage) { allowed ->
                        GlyphButton(
                            icon = ArrowDownGlyph,
                            label = caseDownLabel,
                            onClick = { controller.moveBranchStepDown(caseId) },
                            enabled = allowed && caseIndex < cases.size - 1,
                            tint = tokens.primary,
                        )
                    }
                    ManageGate(decision = manage) { enabled ->
                        GlyphButton(icon = EditGlyph, label = caseEditLabel, onClick = { onEditCase(case) }, enabled = enabled)
                    }
                    ManageGate(decision = manage) { enabled ->
                        GlyphButton(
                            icon = TrashGlyph,
                            label = caseRemoveLabel,
                            onClick = { controller.removeBranchStep(caseId) },
                            enabled = enabled,
                            tint = tokens.destructive,
                        )
                    }
                }

                LaneSection(
                    label = stringResource(Res.string.pipelines_block_case_lane_label, caseIndex + 1),
                    branch = "case",
                    steps = allSteps.filter { it.parentStepId == caseId }.sortedBy { it.order ?: 0 },
                    palette = palette,
                    manage = manage,
                    controller = controller,
                    onAdd = { onAddCaseStep(caseId) },
                    onEditStep = onEditCaseStep,
                )
            }
        }

        Row(modifier = Modifier.fillMaxWidth().padding(start = spacing.s4)) {
            ManageGate(decision = manage) { enabled ->
                GlyphButton(icon = AddGlyph, label = addCaseLabel, onClick = { onAddCase(blockId) }, enabled = enabled, tint = tokens.primary)
            }
        }
    }
}

// A nested "loop" block: no action of its own to run — just its iteration config (mode/count/list_var, or a
// while-condition) — plus its ordered body lane. `ExecuteLoopAsync` walks the block's children with no branch
// filter (PipelineEngine.cs:1821), so the body is rendered via [LaneSection] with `branch = null` addressing it
// by `parentStepId` alone, same as a "switch_case"'s own body.
@Composable
private fun LoopBlockCard(
    block: PipelineStep,
    allSteps: List<PipelineStep>,
    index: Int,
    total: Int,
    palette: RuntimePalette,
    manage: ManageDecision,
    controller: PipelinesController,
    onEditConfig: () -> Unit,
    onMoveUp: () -> Unit,
    onMoveDown: () -> Unit,
    onRemove: () -> Unit,
    onAddToLane: () -> Unit,
    onEditLaneStep: (PipelineStep) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val blockId: String = block.id ?: return
    val config: LoopConfigFields = decodeLoopConfig(block.blockConfig)
    val summary: String =
        when (config.mode) {
            "foreach" -> stringResource(Res.string.pipelines_block_loop_summary_foreach, config.listVar.orEmpty())
            "while" ->
                stringResource(
                    Res.string.pipelines_block_loop_summary_while,
                    block.condition?.let { blockDisplayName(palette.condition(it.type), it.type) }.orEmpty(),
                )
            else -> stringResource(Res.string.pipelines_block_loop_summary_repeat, config.count ?: 0)
        }

    val editLabel: String = stringResource(Res.string.pipelines_step_edit, index + 1)
    val removeLabel: String = stringResource(Res.string.pipelines_step_delete, index + 1)
    val upLabel: String = stringResource(Res.string.pipelines_step_move_up, index + 1)
    val downLabel: String = stringResource(Res.string.pipelines_step_move_down, index + 1)

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Text(text = "${index + 1}", style = typography.sm, color = tokens.mutedForeground)
            Text(
                text = summary,
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
        }

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
            ManageGate(decision = manage) { allowed ->
                GlyphButton(icon = ArrowUpGlyph, label = upLabel, onClick = onMoveUp, enabled = allowed && index > 0, tint = tokens.primary)
            }
            ManageGate(decision = manage) { allowed ->
                GlyphButton(icon = ArrowDownGlyph, label = downLabel, onClick = onMoveDown, enabled = allowed && index < total - 1, tint = tokens.primary)
            }
            Box(modifier = Modifier.weight(1f))
            ManageGate(decision = manage) { enabled -> GlyphButton(icon = EditGlyph, label = editLabel, onClick = onEditConfig, enabled = enabled) }
            ManageGate(decision = manage) { enabled ->
                GlyphButton(icon = TrashGlyph, label = removeLabel, onClick = onRemove, enabled = enabled, tint = tokens.destructive)
            }
        }

        LaneSection(
            label = stringResource(Res.string.pipelines_block_loop_lane_label),
            branch = "body",
            steps = allSteps.filter { it.parentStepId == blockId }.sortedBy { it.order ?: 0 },
            palette = palette,
            manage = manage,
            controller = controller,
            onAdd = onAddToLane,
            onEditStep = onEditLaneStep,
        )
    }
}

// The "loop" block's config editor: a mode picker (repeat/foreach/while) plus the fields that mode actually
// needs — read/written exactly as [encodeLoopConfig]/[decodeLoopConfig] shape it. "while" reuses the same
// [ConditionPicker]/[BlockParamEditor] pair the "if" block's condition editor uses, since its condition lands
// on the SAME `condition` field (never `blockConfig`) — see [PipelinesController.addLoopBlock].
@Composable
private fun LoopBlockFormDialog(
    initialMode: String,
    initialCount: Int?,
    initialListVar: String?,
    initialMaxIterations: Int?,
    initialMaxLoopRuntimeSeconds: Int?,
    initialCondition: PipelineNode?,
    palette: RuntimePalette,
    options: EditorOptions,
    templateHelpersApi: TemplateHelpersApi,
    onOpenCodeScript: (scriptId: String) -> Unit,
    createCodeScript: suspend (name: String) -> PickerOption?,
    onDismiss: () -> Unit,
    onSubmit: (
        mode: String,
        count: Int?,
        listVar: String?,
        maxIterations: Int?,
        maxLoopRuntimeSeconds: Int?,
        whileCondition: PipelineNode?,
    ) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var mode: String by remember { mutableStateOf(initialMode) }
    var countText: String by remember { mutableStateOf(initialCount?.toString().orEmpty()) }
    var listVar: String by remember { mutableStateOf(initialListVar.orEmpty()) }
    var maxIterationsText: String by remember { mutableStateOf(initialMaxIterations?.toString().orEmpty()) }
    var maxRuntimeText: String by remember { mutableStateOf(initialMaxLoopRuntimeSeconds?.toString().orEmpty()) }

    var conditionType: String? by remember { mutableStateOf(initialCondition?.type ?: palette.conditions.firstOrNull()?.type) }
    val conditionBlock: PaletteBlock? = conditionType?.let { palette.condition(it) }
    val conditionParams: SnapshotStateMap<String, String> = remember { mutableStateMapFrom(initialCondition?.params) }
    val conditionGeneric: SnapshotStateList<GenericEntry> =
        remember { genericEntriesFrom(initialCondition?.params.takeIf { conditionBlock?.hasHints == false }) }

    val canSubmit: Boolean =
        when (mode) {
            "repeat" -> countText.toIntOrNull() != null
            "foreach" -> listVar.isNotBlank()
            "while" -> conditionType != null && blockComplete(conditionBlock, conditionParams)
            else -> false
        }

    val title: String =
        stringResource(if (initialCount == null && initialListVar == null && initialCondition == null) Res.string.pipelines_block_loop_title else Res.string.pipelines_block_loop_edit_title)
    val modeLabel: String = stringResource(Res.string.pipelines_block_loop_mode_label)
    val repeatModeLabel: String = stringResource(Res.string.pipelines_block_loop_mode_repeat)
    val foreachModeLabel: String = stringResource(Res.string.pipelines_block_loop_mode_foreach)
    val whileModeLabel: String = stringResource(Res.string.pipelines_block_loop_mode_while)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                LabeledText(modeLabel)
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    for ((modeValue, modeText) in listOf("repeat" to repeatModeLabel, "foreach" to foreachModeLabel, "while" to whileModeLabel)) {
                        TextButton(
                            onClick = { mode = modeValue },
                            modifier = Modifier.weight(1f).semantics { contentDescription = modeText },
                        ) {
                            Text(text = modeText, color = if (mode == modeValue) tokens.primary else tokens.mutedForeground)
                        }
                    }
                }

                when (mode) {
                    "repeat" ->
                        AppTextField(
                            value = countText,
                            onValueChange = { countText = it },
                            label = stringResource(Res.string.pipelines_block_loop_count_label),
                        )
                    "foreach" ->
                        AppTextField(
                            value = listVar,
                            onValueChange = { listVar = it },
                            label = stringResource(Res.string.pipelines_block_loop_list_var_label),
                        )
                    "while" -> {
                        LabeledText(stringResource(Res.string.pipelines_block_loop_condition_label))
                        ConditionPicker(
                            conditions = palette.conditions,
                            selected = conditionBlock,
                            onSelect = { type ->
                                conditionType = type
                                conditionParams.clear()
                                conditionGeneric.clear()
                            },
                        )
                        conditionBlock?.let { block ->
                            BlockParamEditor(
                                block = block,
                                typed = conditionParams,
                                generic = conditionGeneric,
                                options = options,
                                templateHelpersApi = templateHelpersApi,
                                onOpenCodeScript = onOpenCodeScript,
                                createCodeScript = createCodeScript,
                            )
                        }
                    }
                }

                AppTextField(
                    value = maxIterationsText,
                    onValueChange = { maxIterationsText = it },
                    label = stringResource(Res.string.pipelines_block_loop_max_iterations_label),
                )
                AppTextField(
                    value = maxRuntimeText,
                    onValueChange = { maxRuntimeText = it },
                    label = stringResource(Res.string.pipelines_block_loop_max_runtime_label),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    val whileCondition: PipelineNode? =
                        if (mode == "while") {
                            val type: String = conditionType ?: return@TextButton
                            PipelineNode(type = type, params = paramsFor(conditionBlock, conditionParams, conditionGeneric))
                        } else {
                            null
                        }
                    onSubmit(
                        mode,
                        countText.toIntOrNull(),
                        listVar.ifBlank { null },
                        maxIterationsText.toIntOrNull(),
                        maxRuntimeText.toIntOrNull(),
                        whileCondition,
                    )
                },
                enabled = canSubmit,
            ) {
                Text(text = stringResource(Res.string.pipelines_dialog_save), color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.pipelines_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// The "switch" block's value editor — a single free-text/template field (e.g. `{{args.1}}`), read/written to
// `blockConfig` (never `condition`) via [decodeSwitchValue]/[encodeSwitchValue].
@Composable
private fun SwitchBlockFormDialog(
    initial: String,
    onDismiss: () -> Unit,
    onSubmit: (value: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    var value: String by remember { mutableStateOf(initial) }
    val label: String = stringResource(Res.string.pipelines_block_switch_value_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(text = stringResource(if (initial.isBlank()) Res.string.pipelines_block_switch_title else Res.string.pipelines_block_switch_edit_title))
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(value = value, onValueChange = { value = it }, label = label)
            }
        },
        confirmButton = {
            TextButton(onClick = { onSubmit(value) }, enabled = value.isNotBlank()) {
                Text(
                    text = stringResource(Res.string.pipelines_dialog_save),
                    color = if (value.isNotBlank()) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.pipelines_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// A "switch_case" child's own editor: its match value, its comparison operator (a closed dropdown of exactly
// the operators the engine's MatchesCase understands), and its is-default/catch-all toggle. A default case
// ignores its own match/operator at match time (PipelineEngine.cs), but this dialog still lets both be typed
// so flipping the toggle back off doesn't lose them.
@Composable
private fun SwitchCaseFormDialog(
    initialMatch: String,
    initialOperator: String,
    initialIsDefault: Boolean,
    onDismiss: () -> Unit,
    onSubmit: (match: String, operator: String, isDefault: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    var match: String by remember { mutableStateOf(initialMatch) }
    var operatorKey: String by remember { mutableStateOf(initialOperator) }
    var isDefault: Boolean by remember { mutableStateOf(initialIsDefault) }
    var operatorMenuExpanded: Boolean by remember { mutableStateOf(false) }
    val matchLabel: String = stringResource(Res.string.pipelines_block_case_match_label)
    val operatorLabel: String = stringResource(Res.string.pipelines_block_case_operator_label)
    val defaultLabel: String = stringResource(Res.string.pipelines_block_case_default_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(text = stringResource(if (initialMatch.isBlank() && !initialIsDefault) Res.string.pipelines_block_case_add_title else Res.string.pipelines_block_case_edit_title))
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(value = match, onValueChange = { match = it }, label = matchLabel, enabled = !isDefault)

                LabeledText(operatorLabel)
                Box(modifier = Modifier.fillMaxWidth()) {
                    TextButton(onClick = { operatorMenuExpanded = true }, modifier = Modifier.fillMaxWidth().semantics { contentDescription = operatorLabel }) {
                        Text(text = operatorDisplayName(operatorKey), color = tokens.foreground, modifier = Modifier.weight(1f))
                    }
                    DropdownMenu(expanded = operatorMenuExpanded, onDismissRequest = { operatorMenuExpanded = false }) {
                        for (option in SwitchCaseOperators) {
                            DropdownMenuItem(
                                text = { Text(operatorDisplayName(option)) },
                                onClick = {
                                    operatorKey = option
                                    operatorMenuExpanded = false
                                },
                            )
                        }
                    }
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = defaultLabel, color = tokens.cardForeground)
                    Switch(
                        checked = isDefault,
                        onCheckedChange = { isDefault = it },
                        modifier = Modifier.semantics { contentDescription = defaultLabel },
                    )
                }
            }
        },
        confirmButton = {
            val canSubmit: Boolean = isDefault || match.isNotBlank()
            TextButton(onClick = { onSubmit(match, operatorKey, isDefault) }, enabled = canSubmit) {
                Text(text = stringResource(Res.string.pipelines_dialog_save), color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.pipelines_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun ParamSummary(node: PipelineNode, palette: RuntimePalette) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val block: PaletteBlock? = palette.action(node.type)

    // Typed blocks render their known fields in field order with friendly labels; a block without local hints
    // (a backend-discovered action we don't model) renders its raw params by key, so its config is still visible.
    val rows: List<Pair<String, String>> =
        if (block != null && block.hasHints) {
            block.fields.mapNotNull { field ->
                val value: String = node.params[field.key].orEmpty()
                if (value.isBlank()) null else fieldDisplayName(field) to value
            }
        } else {
            node.params.entries.mapNotNull { (key, value) ->
                if (value.isBlank()) null else humanize(key) to value
            }
        }

    for ((label, value) in rows) {
        Text(
            text = "$label: $value",
            style = typography.xs,
            color = tokens.mutedForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

// ── The add/edit step dialog ──────────────────────────────────────────────────

// One dialog for both add and edit (DRY): a null [initial] opens a blank add, a seeded one opens an edit. The
// action + condition options come from the backend-sourced [palette] (grouped by category), so every block the
// engine runs is offered. A block with local field hints renders typed fields (with pickers where relevant);
// a hint-less backend block renders a generic key/value editor so it stays configurable. The Save button is
// disabled until every REQUIRED typed field of the chosen action (and condition, if any) is non-blank.
@Composable
private fun StepFormDialog(
    initial: PipelineStep?,
    palette: RuntimePalette,
    options: EditorOptions,
    templateHelpersApi: TemplateHelpersApi,
    onOpenCodeScript: (scriptId: String) -> Unit,
    createCodeScript: suspend (name: String) -> PickerOption?,
    onDismiss: () -> Unit,
    onSubmit: (PipelineStep) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    val firstActionType: String = palette.actions.firstOrNull()?.type ?: ""
    var actionType: String by remember { mutableStateOf(initial?.action?.type ?: firstActionType) }
    val actionBlock: PaletteBlock? = palette.action(actionType)

    // Typed params back the hinted fields; generic entries back a hint-less block's key/value editor. Only the
    // one that matches the current block is read on submit, so switching type between them is clean.
    val actionParams: SnapshotStateMap<String, String> = remember { mutableStateMapFrom(initial?.action?.params) }
    val actionGeneric: SnapshotStateList<GenericEntry> =
        remember { genericEntriesFrom(initial?.action?.params.takeIf { actionBlock?.hasHints == false }) }

    var conditionType: String? by remember { mutableStateOf(initial?.condition?.type) }
    val conditionBlock: PaletteBlock? = conditionType?.let { palette.condition(it) }
    val conditionParams: SnapshotStateMap<String, String> = remember { mutableStateMapFrom(initial?.condition?.params) }
    val conditionGeneric: SnapshotStateList<GenericEntry> =
        remember { genericEntriesFrom(initial?.condition?.params.takeIf { conditionBlock?.hasHints == false }) }

    var stopOnMatch: Boolean by remember { mutableStateOf(initial?.stopOnMatch ?: false) }

    val canSubmit: Boolean =
        blockComplete(actionBlock, actionParams) &&
            (conditionType == null || blockComplete(conditionBlock, conditionParams))

    val title: String =
        stringResource(
            if (initial == null) Res.string.pipelines_step_add_title else Res.string.pipelines_step_edit_title
        )

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                // Action type picker (backend palette, grouped by category).
                LabeledText(stringResource(Res.string.pipelines_step_action_label))
                BlockTypePicker(
                    grouped = palette.actionsByCategory,
                    selected = actionBlock,
                    selectedType = actionType,
                    onSelect = { type ->
                        actionType = type
                        actionParams.clear()
                        actionGeneric.clear()
                    },
                )
                actionBlock?.let { block ->
                    BlockParamEditor(
                        block = block,
                        typed = actionParams,
                        generic = actionGeneric,
                        options = options,
                        templateHelpersApi = templateHelpersApi,
                        onOpenCodeScript = onOpenCodeScript,
                        createCodeScript = createCodeScript,
                    )
                }

                // Optional condition.
                LabeledText(stringResource(Res.string.pipelines_condition_label_short))
                ConditionPicker(
                    conditions = palette.conditions,
                    selected = conditionBlock,
                    onSelect = { type ->
                        conditionType = type
                        conditionParams.clear()
                        conditionGeneric.clear()
                    },
                )
                conditionBlock?.let { block ->
                    BlockParamEditor(
                        block = block,
                        typed = conditionParams,
                        generic = conditionGeneric,
                        options = options,
                        templateHelpersApi = templateHelpersApi,
                        onOpenCodeScript = onOpenCodeScript,
                        createCodeScript = createCodeScript,
                    )
                }

                // Stop-on-match.
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    val stopLabel: String = stringResource(Res.string.pipelines_step_stop_label)
                    Text(text = stopLabel, color = tokens.cardForeground)
                    Switch(
                        checked = stopOnMatch,
                        onCheckedChange = { stopOnMatch = it },
                        modifier = Modifier.semantics { contentDescription = stopLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    val action =
                        PipelineNode(type = actionType, params = paramsFor(actionBlock, actionParams, actionGeneric))
                    val condition: PipelineNode? =
                        conditionType?.let {
                            PipelineNode(type = it, params = paramsFor(conditionBlock, conditionParams, conditionGeneric))
                        }
                    onSubmit(PipelineStep(action = action, condition = condition, stopOnMatch = stopOnMatch))
                },
                enabled = canSubmit,
            ) {
                Text(text = stringResource(Res.string.pipelines_dialog_save), color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.pipelines_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// Renders a block's parameters: typed fields when the block has local hints (with the role / endpoint / list
// pickers where a field maps to a closed set), else a generic key/value editor for a backend-discovered block.
@Composable
private fun BlockParamEditor(
    block: PaletteBlock,
    typed: MutableMap<String, String>,
    generic: SnapshotStateList<GenericEntry>,
    options: EditorOptions,
    templateHelpersApi: TemplateHelpersApi,
    onOpenCodeScript: (scriptId: String) -> Unit,
    createCodeScript: suspend (name: String) -> PickerOption?,
) {
    if (block.description.isNotBlank()) {
        val tokens = LocalTokens.current
        val typography = LocalTypography.current
        Text(text = resolveSchemaString(block.description), style = typography.xs, color = tokens.mutedForeground)
    }
    if (block.hasHints) {
        TypedParamFields(
            block = block,
            params = typed,
            options = options,
            templateHelpersApi = templateHelpersApi,
            onOpenCodeScript = onOpenCodeScript,
            createCodeScript = createCodeScript,
        )
    } else {
        GenericParamFields(entries = generic, templateHelpersApi = templateHelpersApi)
    }
}

@Composable
private fun TypedParamFields(
    block: PaletteBlock,
    params: MutableMap<String, String>,
    options: EditorOptions,
    templateHelpersApi: TemplateHelpersApi,
    onOpenCodeScript: (scriptId: String) -> Unit,
    createCodeScript: suspend (name: String) -> PickerOption?,
) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        for (field in block.fields) {
            when {
                // A boolean param (an OBS `visible`, VTS `active`, …) is a toggle, encoded as a JSON boolean.
                field.kind == FieldKind.Bool ->
                    BoolField(
                        label = fieldDisplayName(field),
                        checked = params[field.key].toBoolean(),
                        onCheckedChange = { params[field.key] = it.toString() },
                    )
                // A backend-tagged resource-picker field (S-RICH-PICKERS: discord_channel/discord_role/
                // twitch_user/reward/widget/voice/sound_clip/asset) renders the rich, backend-sourced picker —
                // label + secondary context + image + disabled-with-reason + source-unavailable — ahead of any
                // hand-matched key below, so a NEW picker kind only needs the backend field tagged with that
                // kind. Falls through to the legacy key-based pickers when the API is unavailable (best-effort,
                // like every other editor picker source).
                options.pipelineOptionsApi != null && PickerKind.fromWireName(field.remoteKind.orEmpty()) != null ->
                    ResourcePickerField(
                        kind = PickerKind.fromWireName(field.remoteKind.orEmpty())!!,
                        api = options.pipelineOptionsApi,
                        selectedId = params[field.key].orEmpty().ifBlank { null },
                        onSelect = { params[field.key] = it.orEmpty() },
                        label = fieldLabelWithRequired(field),
                    )
                // A closed value set (OBS action verbs / batch execution mode) is a dropdown over its options.
                field.options.isNotEmpty() ->
                    OptionPicker(
                        label = fieldLabelWithRequired(field),
                        options = field.options.map { PickerOption(value = it, label = humanize(it)) },
                        selected = params[field.key].orEmpty(),
                        onSelect = { params[field.key] = it },
                    )
                // The role floor is a closed set — a picker, not free text.
                field.key == "min_role" ->
                    RolePicker(
                        selected = params[field.key].orEmpty(),
                        onSelect = { params[field.key] = it },
                    )
                // send_webhook → search one of the channel's outbound endpoints.
                field.key == "endpoint" ->
                    EntityPickerField(
                        items = options.outboundEndpoints,
                        selectedId = params[field.key].orEmpty().ifBlank { null },
                        onSelect = { params[field.key] = it.orEmpty() },
                        idOf = { it.value },
                        labelOf = { it.label },
                        label = fieldLabelWithRequired(field),
                    )
                // pick_from_list → search one of the channel's pick-lists by name.
                field.key == "list" ->
                    EntityPickerField(
                        items = options.pickLists,
                        selectedId = params[field.key].orEmpty().ifBlank { null },
                        onSelect = { params[field.key] = it.orEmpty() },
                        idOf = { it.value },
                        labelOf = { it.label },
                        label = fieldLabelWithRequired(field),
                    )
                // widget_event → search one of the channel's overlay widgets.
                field.key == "widget_id" ->
                    EntityPickerField(
                        items = options.widgets,
                        selectedId = params[field.key].orEmpty().ifBlank { null },
                        onSelect = { params[field.key] = it.orEmpty() },
                        idOf = { it.value },
                        labelOf = { it.label },
                        label = fieldLabelWithRequired(field),
                    )
                // schedule_pipeline → search one of the channel's pipelines by name.
                field.key == "pipeline" ->
                    EntityPickerField(
                        items = options.pipelines,
                        selectedId = params[field.key].orEmpty().ifBlank { null },
                        onSelect = { params[field.key] = it.orEmpty() },
                        idOf = { it.value },
                        labelOf = { it.label },
                        label = fieldLabelWithRequired(field),
                    )
                // run_code's script reference gets its own field (S046-code-tier-link): pick/create a code
                // script AND open it in the real Code Scripts editor — never a bare id picker with nowhere to
                // actually write the script.
                field.key == "code_script_id" ->
                    CodeScriptStepField(
                        scripts = options.codeScripts,
                        selectedId = params[field.key].orEmpty().ifBlank { null },
                        onSelect = { params[field.key] = it.orEmpty() },
                        onOpenScript = onOpenCodeScript,
                        onCreateScript = createCodeScript,
                        label = fieldLabelWithRequired(field),
                    )
                // The remaining entity references (play_sound clip, play_tts voice, jar_contribute jar,
                // giveaway_*, post_quote number) — each a search dropdown over its channel-loaded list.
                field.key in setOf("clip", "voice", "jar_id", "giveaway_id", "quote_number") ->
                    EntityPickerField(
                        items =
                            when (field.key) {
                                "clip" -> options.soundClips
                                "voice" -> options.ttsVoices
                                "jar_id" -> options.jars
                                "giveaway_id" -> options.giveaways
                                else -> options.quotes
                            },
                        selectedId = params[field.key].orEmpty().ifBlank { null },
                        onSelect = { params[field.key] = it.orEmpty() },
                        idOf = { it.value },
                        labelOf = { it.label },
                        label = fieldLabelWithRequired(field),
                    )
                // run_pipeline's dynamic argument editor renders under the "named_args" slot for BOTH the
                // named-args and positional-args cases (whichever applies to the currently-picked target); the
                // catalogue's separate "args" field entry is a placeholder for the encoder only and renders
                // nothing here, so the section appears exactly once.
                block.type == "run_pipeline" && field.key == "named_args" ->
                    RunPipelineArgumentsField(
                        targetPipelineName = params["pipeline"].orEmpty(),
                        declaredNamesByPipeline = options.pipelineParameterNames,
                        namedArgsJson = params["named_args"].orEmpty(),
                        onNamedArgsJsonChange = { params["named_args"] = it },
                        argsJson = params["args"].orEmpty(),
                        onArgsJsonChange = { params["args"] = it },
                    )
                block.type == "run_pipeline" && field.key == "args" -> Unit
                // Every other free-text field is a candidate template body (send_message/send_reply's
                // "message", TTS/Discord text, …) — a pipeline can be bound to a chat command, an EventSub
                // trigger, or a timer, so it gets the broadest helper set (TemplateHelperContext.Pipeline,
                // S042/S043) rather than guessing which trigger this step will end up wired to.
                else ->
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        AppTextField(
                            value = params[field.key].orEmpty(),
                            onValueChange = { params[field.key] = it },
                            label = fieldLabelWithRequired(field),
                            supportingText = fieldHelpText(field),
                            modifier = Modifier.fillMaxWidth(),
                        )
                        TemplateHelpersLink(
                            context = TemplateHelperContext.Pipeline,
                            api = templateHelpersApi,
                            onInsert = { token ->
                                val current: String = params[field.key].orEmpty()
                                params[field.key] = if (current.isBlank()) token else "$current $token"
                            },
                        )
                    }
            }
        }
    }
}

// A boolean param rendered as a labelled Switch (design-system Switch), matching the step dialog's stop-on-match row.
@Composable
private fun BoolField(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(text = label, color = tokens.cardForeground)
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            modifier = Modifier.semantics { contentDescription = label },
        )
    }
}

// The generic key/value editor for a backend block we don't model — every param is a free-form key + value row,
// so any discovered action stays configurable. Keys map to the action's backend param names.
@Composable
private fun GenericParamFields(entries: SnapshotStateList<GenericEntry>, templateHelpersApi: TemplateHelpersApi) {
    val spacing = LocalSpacing.current
    val addLabel: String = stringResource(Res.string.pipelines_generic_add)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        LabeledText(stringResource(Res.string.pipelines_generic_params_label))
        entries.forEachIndexed { index, entry ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                AppTextField(
                    value = entry.key,
                    onValueChange = { entry.key = it },
                    label = stringResource(Res.string.pipelines_generic_param_key),
                    modifier = Modifier.weight(1f),
                )
                AppTextField(
                    value = entry.value,
                    onValueChange = { entry.value = it },
                    label = stringResource(Res.string.pipelines_generic_param_value),
                    modifier = Modifier.weight(1f),
                )
                GlyphButton(
                    icon = TrashGlyph,
                    label = stringResource(Res.string.pipelines_generic_remove),
                    onClick = { entries.removeAt(index) },
                    tint = LocalTokens.current.destructive,
                )
            }
        }
        GlyphButton(
            icon = AddGlyph,
            label = addLabel,
            onClick = { entries.add(GenericEntry("", "")) },
            tint = LocalTokens.current.primary,
        )
        if (entries.isNotEmpty()) {
            // Inserts into the last row's value — a discovered action's param values can be templated the same
            // as a modeled block's, we just don't know which row is the text one, so we default to the most
            // recently added row.
            TemplateHelpersLink(
                context = TemplateHelperContext.Pipeline,
                api = templateHelpersApi,
                onInsert = { token ->
                    val last: GenericEntry = entries.last()
                    last.value = if (last.value.isBlank()) token else "${last.value} $token"
                },
            )
        }
    }
}

// run_code's script field (S046-code-tier-link): pick an existing code script by the shared search dropdown,
// author a new one inline (name only — its actual source is written in the real Code Scripts editor, never
// here), and — once one is bound — jump straight to that script's real editor. Visible at module scope (not
// private) so CodeScriptStepFieldTest can render and assert on it directly, the same way the step dialog
// reaches it — mirrors [RunPipelineArgumentsField]'s testability rationale below.
@Composable
internal fun CodeScriptStepField(
    scripts: List<PickerOption>,
    selectedId: String?,
    onSelect: (String?) -> Unit,
    onOpenScript: (scriptId: String) -> Unit,
    onCreateScript: suspend (name: String) -> PickerOption?,
    label: String,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val scope = rememberCoroutineScope()

    var creatingNew: Boolean by remember { mutableStateOf(false) }
    var newName: String by remember { mutableStateOf("") }
    var isSubmitting: Boolean by remember { mutableStateOf(false) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        if (creatingNew) {
            AppTextField(
                value = newName,
                onValueChange = { newName = it },
                label = stringResource(Res.string.pipelines_code_script_new_name),
                modifier = Modifier.fillMaxWidth(),
                enabled = !isSubmitting,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                TextButton(
                    onClick = {
                        creatingNew = false
                        newName = ""
                    },
                    enabled = !isSubmitting,
                ) {
                    Text(text = stringResource(Res.string.pipelines_dialog_cancel), color = tokens.mutedForeground)
                }
                TextButton(
                    onClick = {
                        val name: String = newName.trim()
                        scope.launch {
                            isSubmitting = true
                            // The new script's source is empty — the operator writes it in the real Code
                            // Scripts editor, which is exactly where this immediately navigates them.
                            val created: PickerOption? = onCreateScript(name)
                            isSubmitting = false
                            if (created != null) {
                                onSelect(created.value)
                                creatingNew = false
                                newName = ""
                                onOpenScript(created.value)
                            }
                        }
                    },
                    enabled = !isSubmitting && newName.isNotBlank(),
                ) {
                    Text(text = stringResource(Res.string.pipelines_code_script_create), color = tokens.primary)
                }
            }
        } else {
            EntityPickerField(
                items = scripts,
                selectedId = selectedId,
                onSelect = onSelect,
                idOf = { it.value },
                labelOf = { it.label },
                label = label,
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                TextButton(onClick = { creatingNew = true }) {
                    AppIcon(AddGlyph, contentDescription = null, tint = tokens.primary, size = spacing.s4)
                    Text(text = stringResource(Res.string.pipelines_code_script_create_new), color = tokens.primary)
                }
                if (selectedId != null) {
                    TextButton(onClick = { onOpenScript(selectedId) }) {
                        Text(text = stringResource(Res.string.pipelines_code_script_open), color = tokens.primary)
                    }
                }
            }
        }
    }
}

// run_pipeline's argument editor (S-PIPE-TREE-d2b-UI): once a target pipeline is picked, renders one labelled
// field per DECLARED parameter name (bound into the "named_args" JSON object param) when the target declares
// any; otherwise falls back to the generic positional argument list (bound into the "args" JSON array param) —
// the same fallback the engine itself applies (RunPipelineAction/PipelineEngine.RunInlineSubPipelineAsync).
// Visible at module scope (not private) so PipelinesScreenRunPipelineArgumentsFieldTest can render and assert
// on it directly, the same way the step dialog reaches it — a bespoke test-only duplicate would drift from the
// real editor instead of proving it.
@Composable
internal fun RunPipelineArgumentsField(
    targetPipelineName: String,
    declaredNamesByPipeline: Map<String, List<String>>,
    namedArgsJson: String,
    onNamedArgsJsonChange: (String) -> Unit,
    argsJson: String,
    onArgsJsonChange: (String) -> Unit,
) {
    val spacing = LocalSpacing.current
    val declaredNames: List<String> = declaredNamesByPipeline[targetPipelineName].orEmpty()

    if (targetPipelineName.isBlank()) {
        Text(
            text = stringResource(Res.string.pipelines_run_pipeline_no_target_hint),
            style = LocalTypography.current.xs,
            color = LocalTokens.current.mutedForeground,
        )
        return
    }

    if (declaredNames.isNotEmpty()) {
        val current: Map<String, String> = remember(namedArgsJson) { decodeNamedArgs(namedArgsJson) }
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            for (name in declaredNames) {
                AppTextField(
                    value = current[name].orEmpty(),
                    onValueChange = { newValue ->
                        onNamedArgsJsonChange(encodeNamedArgs(current + (name to newValue)))
                    },
                    label = stringResource(Res.string.pipelines_run_pipeline_named_arg_label, name),
                    modifier = Modifier.fillMaxWidth().semantics { contentDescription = name },
                )
            }
        }
        return
    }

    val current: List<String> = remember(argsJson) { decodeArgsList(argsJson) }
    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        LabeledText(stringResource(Res.string.pipelines_field_args))
        current.forEachIndexed { index, value ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                AppTextField(
                    value = value,
                    onValueChange = { newValue ->
                        onArgsJsonChange(encodeArgsList(current.toMutableList().also { it[index] = newValue }))
                    },
                    label = stringResource(Res.string.pipelines_run_pipeline_named_arg_label, (index + 1).toString()),
                    modifier = Modifier.weight(1f),
                )
                GlyphButton(
                    icon = TrashGlyph,
                    label = stringResource(Res.string.pipelines_run_pipeline_args_remove),
                    onClick = { onArgsJsonChange(encodeArgsList(current.toMutableList().also { it.removeAt(index) })) },
                    tint = LocalTokens.current.destructive,
                )
            }
        }
        GlyphButton(
            icon = AddGlyph,
            label = stringResource(Res.string.pipelines_run_pipeline_args_add),
            onClick = { onArgsJsonChange(encodeArgsList(current + "")) },
            tint = LocalTokens.current.primary,
        )
    }
}

// A blank/unparsable stored value degrades to an empty map/list rather than throwing — the editor must always
// render, even against a hand-edited or legacy graph.
private fun decodeNamedArgs(json: String): Map<String, String> =
    runCatching {
            Json.parseToJsonElement(json).jsonObject.mapValues { (_, v) ->
                (v as? JsonPrimitive)?.contentOrNull ?: v.toString()
            }
        }
        .getOrDefault(emptyMap())

private fun encodeNamedArgs(map: Map<String, String>): String =
    JsonObject(map.filterValues { it.isNotBlank() }.mapValues { (_, v) -> JsonPrimitive(v) as JsonElement }).toString()

private fun decodeArgsList(json: String): List<String> =
    runCatching {
            Json.parseToJsonElement(json).jsonArray.map { (it as? JsonPrimitive)?.contentOrNull ?: it.toString() }
        }
        .getOrDefault(emptyList())

private fun encodeArgsList(list: List<String>): String = JsonArray(list.map { JsonPrimitive(it) }).toString()

// A closed-set value picker (endpoint / pick-list): a labelled dropdown when options exist, else a free-text
// field so a channel with no endpoints/lists yet can still type an id/name by hand.
@Composable
private fun OptionPicker(
    label: String,
    options: List<PickerOption>,
    selected: String,
    onSelect: (String) -> Unit,
) {
    if (options.isEmpty()) {
        AppTextField(
            value = selected,
            onValueChange = onSelect,
            label = label,
            modifier = Modifier.fillMaxWidth(),
        )
        return
    }

    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val current: PickerOption? = options.firstOrNull { it.value == selected }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
        Text(text = label, style = typography.xs, color = tokens.mutedForeground)
        Box(modifier = Modifier.fillMaxWidth()) {
            Box(
                modifier =
                    Modifier.fillMaxWidth()
                        .clip(RoundedCornerShape(tokens.radius.md))
                        .background(tokens.input)
                        .semantics { contentDescription = label },
            ) {
                TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
                    Text(
                        text = current?.label ?: selected.ifBlank { stringResource(Res.string.pipelines_picker_choose) },
                        color = if (current == null && selected.isBlank()) tokens.mutedForeground else tokens.foreground,
                        modifier = Modifier.weight(1f),
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                for (option in options) {
                    DropdownMenuItem(
                        text = {
                            Text(
                                text =
                                    resolveRowLabel(
                                        option.label,
                                        typeLabel = "Option",
                                        discriminatorSource = option.value,
                                    ),
                                color = tokens.popoverForeground,
                            )
                        },
                        onClick = {
                            onSelect(option.value)
                            expanded = false
                        },
                    )
                }
            }
        }
    }
}

// The action-block picker — a category-grouped dropdown so all ~66 backend blocks are browsable by group.
@Composable
private fun BlockTypePicker(
    grouped: List<Pair<String, List<PaletteBlock>>>,
    selected: PaletteBlock?,
    selectedType: String,
    onSelect: (String) -> Unit,
) {
    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth()) {
        TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
            Text(
                text = blockDisplayName(selected, selectedType),
                color = tokens.foreground,
                modifier = Modifier.weight(1f),
            )
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            for ((category, blocks) in grouped) {
                Text(
                    text = resolveSchemaString(category),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    modifier = Modifier.padding(horizontal = LocalSpacing.current.s3, vertical = LocalSpacing.current.s1),
                )
                for (option in blocks) {
                    DropdownMenuItem(
                        text = { Text(blockDisplayName(option, option.type)) },
                        onClick = {
                            onSelect(option.type)
                            expanded = false
                        },
                    )
                }
            }
        }
    }
}

@Composable
private fun ConditionPicker(
    conditions: List<PaletteBlock>,
    selected: PaletteBlock?,
    onSelect: (String?) -> Unit,
) {
    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current
    val noneLabel: String = stringResource(Res.string.pipelines_condition_none)

    Box(modifier = Modifier.fillMaxWidth()) {
        TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
            Text(
                text = selected?.let { blockDisplayName(it, it.type) } ?: noneLabel,
                color = tokens.foreground,
                modifier = Modifier.weight(1f),
            )
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            DropdownMenuItem(
                text = { Text(noneLabel) },
                onClick = {
                    onSelect(null)
                    expanded = false
                },
            )
            for (option in conditions) {
                DropdownMenuItem(
                    text = { Text(blockDisplayName(option, option.type)) },
                    onClick = {
                        onSelect(option.type)
                        expanded = false
                    },
                )
            }
        }
    }
}

// The role floor is a closed set, so it is a labelled dropdown (not free text): the button shows the chosen
// role (or a prompt), and the menu lists the canonical ladder. The button border reads on-theme via tokens.
@Composable
private fun RolePicker(selected: String, onSelect: (String) -> Unit) {
    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val label: String = stringResource(Res.string.pipelines_field_min_role)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
        Text(text = "$label *", style = typography.xs, color = tokens.mutedForeground)
        Box(modifier = Modifier.fillMaxWidth()) {
            Box(
                modifier =
                    Modifier.fillMaxWidth()
                        .clip(RoundedCornerShape(tokens.radius.md))
                        .background(tokens.input)
                        .semantics { contentDescription = label },
            ) {
                TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
                    Text(
                        text = selected.ifBlank { stringResource(Res.string.pipelines_field_min_role) },
                        color = if (selected.isBlank()) tokens.mutedForeground else tokens.foreground,
                        modifier = Modifier.weight(1f),
                        maxLines = 1,
                    )
                }
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                for (role in UserRoleOptions) {
                    DropdownMenuItem(
                        text = { Text(text = role, color = tokens.popoverForeground) },
                        onClick = {
                            onSelect(role)
                            expanded = false
                        },
                    )
                }
            }
        }
    }
}

// ── The create/rename pipeline dialog ─────────────────────────────────────────

@Composable
private fun PipelineFormDialog(
    editor: PipelineEditor,
    onDismiss: () -> Unit,
    onSubmit: (name: String, description: String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var name: String by remember { mutableStateOf(editor.name) }
    var description: String by remember { mutableStateOf(editor.description) }

    val canSubmit: Boolean = name.isNotBlank()
    val title: String =
        stringResource(if (editor.id == null) Res.string.pipelines_dialog_create_title else Res.string.pipelines_dialog_edit_title)
    val submitLabel: String =
        stringResource(if (editor.id == null) Res.string.pipelines_dialog_create else Res.string.pipelines_dialog_save)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    label = stringResource(Res.string.pipelines_dialog_name_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = description,
                    onValueChange = { description = it },
                    label = stringResource(Res.string.pipelines_dialog_description_label),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { onSubmit(name, description) }, enabled = canSubmit) {
                Text(text = submitLabel, color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.pipelines_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// ── Shared bits ───────────────────────────────────────────────────────────────

@Composable
private fun LabeledText(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Text(text = text, style = typography.sm, color = tokens.mutedForeground)
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
                text = stringResource(Res.string.pipelines_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.pipelines_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

// Resolve a block's display name: its i18n label when the type is locally known (labelKey set), else a
// humanized form of the backend type discriminator so a hint-less backend block still reads well.
@Composable
private fun blockDisplayName(block: PaletteBlock?, rawType: String): String {
    val labelKey: String? = block?.labelKey
    return if (labelKey != null) stringResource(blockLabel(labelKey)) else humanize(block?.type ?: rawType)
}

@Composable
private fun fieldDisplayName(field: BlockField): String = stringResource(fieldLabel(field.labelKey))

@Composable
private fun fieldLabelWithRequired(field: BlockField): String {
    val base: String = fieldDisplayName(field)
    return if (field.required) "$base *" else base
}

// The backend-authored help text for this field (S-SCHEMA-I18N-c), or null when the backend catalogue carries
// no description key for it — an unresolved key falls back to itself in [resolveSchemaString], so a blank
// [BlockField.descriptionKey] is the only case treated as "no help text" rather than shown as a raw key.
@Composable
private fun fieldHelpText(field: BlockField): String? {
    val key: String = field.descriptionKey ?: return null
    return resolveSchemaString(key)
}

// Humanize a raw backend discriminator (type/category/param key) for display: separators to spaces, first
// letter capitalized. Applied only to backend-provided data we don't have a translated label for.
private fun humanize(raw: String): String {
    val spaced: String = raw.replace('_', ' ').replace('.', ' ').replace('-', ' ').trim()
    return spaced.replaceFirstChar { if (it.isLowerCase()) it.titlecase() else it.toString() }
}

// Map the catalogue's labelKey suffix to its declared StringResource (the locally-hinted blocks are a fixed
// set, so this is an exhaustive lookup — a hint-less backend block never reaches here, it is humanized instead).
private fun blockLabel(labelKey: String): StringResource =
    when (labelKey) {
        "send_message" -> Res.string.pipelines_block_send_message
        "send_reply" -> Res.string.pipelines_block_send_reply
        "timeout" -> Res.string.pipelines_block_timeout
        "ban" -> Res.string.pipelines_block_ban
        "delete_message" -> Res.string.pipelines_block_delete_message
        "shoutout" -> Res.string.pipelines_block_shoutout
        "song_request" -> Res.string.pipelines_block_song_request
        "song_skip" -> Res.string.pipelines_block_song_skip
        "song_volume" -> Res.string.pipelines_block_song_volume
        "song_current" -> Res.string.pipelines_block_song_current
        "song_queue" -> Res.string.pipelines_block_song_queue
        "play_sound" -> Res.string.pipelines_block_play_sound
        "play_tts" -> Res.string.pipelines_block_play_tts
        "grant_currency" -> Res.string.pipelines_block_grant_currency
        "deduct_currency" -> Res.string.pipelines_block_deduct_currency
        "check_balance" -> Res.string.pipelines_block_check_balance
        "play_game" -> Res.string.pipelines_block_play_game
        "jar_contribute" -> Res.string.pipelines_block_jar_contribute
        "post_quote" -> Res.string.pipelines_block_post_quote
        "send_discord_notification" -> Res.string.pipelines_block_send_discord_notification
        "require_tier" -> Res.string.pipelines_block_require_tier
        "run_code" -> Res.string.pipelines_block_run_code
        "set_variable" -> Res.string.pipelines_block_set_variable
        "wait" -> Res.string.pipelines_block_wait
        "stop" -> Res.string.pipelines_block_stop
        "send_webhook" -> Res.string.pipelines_block_send_webhook
        "pick_from_list" -> Res.string.pipelines_block_pick_from_list
        "stop_sound" -> Res.string.pipelines_block_stop_sound
        "start_live_game" -> Res.string.pipelines_block_start_live_game
        "cancel_live_game" -> Res.string.pipelines_block_cancel_live_game
        "user_role" -> Res.string.pipelines_block_user_role
        "random" -> Res.string.pipelines_block_random
        "var_compare" -> Res.string.pipelines_block_var_compare
        else -> Res.string.pipelines_block_send_message
    }

private fun fieldLabel(labelKey: String): StringResource =
    when (labelKey) {
        "message" -> Res.string.pipelines_field_message
        "user_id" -> Res.string.pipelines_field_user_id
        "duration_seconds" -> Res.string.pipelines_field_duration_seconds
        "reason" -> Res.string.pipelines_field_reason
        "message_id" -> Res.string.pipelines_field_message_id
        "cooldown_minutes" -> Res.string.pipelines_field_cooldown_minutes
        "query" -> Res.string.pipelines_field_query
        "volume" -> Res.string.pipelines_field_volume
        "clip" -> Res.string.pipelines_field_clip
        "wait_for_finish" -> Res.string.pipelines_field_wait_for_finish
        "handle" -> Res.string.pipelines_field_handle
        "text" -> Res.string.pipelines_field_text
        "voice" -> Res.string.pipelines_field_voice
        "song_queue_max" -> Res.string.pipelines_field_song_queue_max
        "amount" -> Res.string.pipelines_field_amount
        "set_var" -> Res.string.pipelines_field_set_var
        "min_balance" -> Res.string.pipelines_field_min_balance
        "game_type" -> Res.string.pipelines_field_game_type
        "bet_amount" -> Res.string.pipelines_field_bet_amount
        "jar_id" -> Res.string.pipelines_field_jar_id
        "quote_number" -> Res.string.pipelines_field_quote_number
        "trigger_type" -> Res.string.pipelines_field_trigger_type
        "dedupe_key" -> Res.string.pipelines_field_dedupe_key
        "min_tier" -> Res.string.pipelines_field_min_tier
        "denied_message" -> Res.string.pipelines_field_denied_message
        "code_script_id" -> Res.string.pipelines_field_code_script_id
        "variable_name" -> Res.string.pipelines_field_variable_name
        "variable_value" -> Res.string.pipelines_field_variable_value
        "wait_seconds" -> Res.string.pipelines_field_wait_seconds
        "min_role" -> Res.string.pipelines_field_min_role
        "percent" -> Res.string.pipelines_field_percent
        "endpoint" -> Res.string.pipelines_field_endpoint
        "event_type" -> Res.string.pipelines_field_event_type
        "list" -> Res.string.pipelines_field_list
        "pick_variable" -> Res.string.pipelines_field_pick_variable
        "compare_left" -> Res.string.pipelines_field_compare_left
        "compare_operator" -> Res.string.pipelines_field_compare_operator
        "compare_right" -> Res.string.pipelines_field_compare_right
        "scene" -> Res.string.pipelines_field_scene
        "source" -> Res.string.pipelines_field_source
        "visible" -> Res.string.pipelines_field_visible
        "filter" -> Res.string.pipelines_field_filter
        "enabled" -> Res.string.pipelines_field_enabled
        "transition" -> Res.string.pipelines_field_transition
        "studio" -> Res.string.pipelines_field_studio
        "duration_ms" -> Res.string.pipelines_field_duration_ms
        "input" -> Res.string.pipelines_field_input
        "muted" -> Res.string.pipelines_field_muted
        "toggle" -> Res.string.pipelines_field_toggle
        "volume_db" -> Res.string.pipelines_field_volume_db
        "volume_mul" -> Res.string.pipelines_field_volume_mul
        "action_verb" -> Res.string.pipelines_field_action_verb
        "hotkey_name" -> Res.string.pipelines_field_hotkey_name
        "image_format" -> Res.string.pipelines_field_image_format
        "request_type" -> Res.string.pipelines_field_request_type
        "request_data" -> Res.string.pipelines_field_request_data
        "vendor" -> Res.string.pipelines_field_vendor
        "execution" -> Res.string.pipelines_field_execution
        "halt_on_failure" -> Res.string.pipelines_field_halt_on_failure
        "requests" -> Res.string.pipelines_field_requests
        "model" -> Res.string.pipelines_field_model
        "hotkey" -> Res.string.pipelines_field_hotkey
        "expression" -> Res.string.pipelines_field_expression
        "active" -> Res.string.pipelines_field_active
        "move_x" -> Res.string.pipelines_field_move_x
        "move_y" -> Res.string.pipelines_field_move_y
        "rotation" -> Res.string.pipelines_field_rotation
        "size" -> Res.string.pipelines_field_size
        "time_seconds" -> Res.string.pipelines_field_time_seconds
        "relative" -> Res.string.pipelines_field_relative
        "color_r" -> Res.string.pipelines_field_color_r
        "color_g" -> Res.string.pipelines_field_color_g
        "color_b" -> Res.string.pipelines_field_color_b
        "color_a" -> Res.string.pipelines_field_color_a
        "art_mesh_tag" -> Res.string.pipelines_field_art_mesh_tag
        "payload_json" -> Res.string.pipelines_field_payload_json
        "giveaway_id" -> Res.string.pipelines_field_giveaway_id
        "key" -> Res.string.pipelines_field_key
        "value" -> Res.string.pipelines_field_value
        "delta" -> Res.string.pipelines_field_delta
        "target" -> Res.string.pipelines_field_target
        "pipeline" -> Res.string.pipelines_field_pipeline
        "delay_seconds" -> Res.string.pipelines_field_delay_seconds
        "role_or_capability" -> Res.string.pipelines_field_role_or_capability
        "target_variable" -> Res.string.pipelines_field_target_variable
        "duration_minutes" -> Res.string.pipelines_field_duration_minutes
        "widget_id" -> Res.string.pipelines_field_widget_id
        "data" -> Res.string.pipelines_field_data
        else -> Res.string.pipelines_field_message
    }

// A fresh observable string map seeded from existing params (or empty) — backs the dialog's editable fields.
private fun mutableStateMapFrom(source: Map<String, String>?): SnapshotStateMap<String, String> {
    val map = mutableStateMapOf<String, String>()
    source?.let { map.putAll(it) }
    return map
}

// ── Generic (hint-less backend block) param editing ───────────────────────────

/** One editable row in the generic key/value editor — observable so edits recompose the dialog. */
private class GenericEntry(key: String, value: String) {
    var key: String by mutableStateOf(key)
    var value: String by mutableStateOf(value)
}

/** A fresh observable entry list seeded from existing params (or empty). */
private fun genericEntriesFrom(source: Map<String, String>?): SnapshotStateList<GenericEntry> {
    val list: SnapshotStateList<GenericEntry> = mutableStateListOf()
    source?.forEach { (key, value) -> list.add(GenericEntry(key, value)) }
    return list
}

// True when the chosen [block] is fully specified: a hinted block needs every required field non-blank; a
// hint-less (generic) block is always accepted; a null block (none chosen) is never complete.
private fun blockComplete(block: PaletteBlock?, typed: Map<String, String>): Boolean =
    when {
        block == null -> false
        !block.hasHints -> true
        else -> block.fields.filter { it.required }.all { typed[it.key]?.isNotBlank() == true }
    }

// Build the wire params for a node: a hinted block reads its typed field map; a generic block folds its
// non-blank key/value rows (last write wins on a duplicate key).
private fun paramsFor(
    block: PaletteBlock?,
    typed: Map<String, String>,
    generic: List<GenericEntry>,
): Map<String, String> =
    if (block != null && !block.hasHints) {
        generic.filter { it.key.isNotBlank() && it.value.isNotBlank() }.associate { it.key.trim() to it.value }
    } else {
        typed.filterValues { it.isNotBlank() }
    }

// The create/rename dialog seed: a null [id] is a create (blank), an id is a rename of that pipeline.
private data class PipelineEditor(val id: String?, val name: String, val description: String) {
    companion object {
        fun create(): PipelineEditor = PipelineEditor(id = null, name = "", description = "")

        fun edit(pipeline: PipelineSummary): PipelineEditor =
            PipelineEditor(id = pipeline.id, name = pipeline.name, description = pipeline.description.orEmpty())
    }
}

// The step add/edit dialog target: a null [index] is an add, an index edits that step. [step] seeds an edit.
// A null [step] is an add; a non-null one is an edit of that exact step (its id decides the target). A null
// [parentStepId]/[branch] targets the root chain; a non-null pair targets that block's lane — [addLabel]-less
// callers (the "add step" toolbar button, an "if" block's per-lane add button, a StepCard's edit button)
// all resolve through this one shape.
private data class StepDialogTarget(val parentStepId: String?, val branch: String?, val step: PipelineStep?)

// A null [blockId] is adding a brand-new "if" block; a non-null one is re-editing an existing block's
// condition (its [condition] pre-fills the dialog).
private data class IfBlockDialogTarget(val blockId: String?, val condition: PipelineNode?)

// A null [blockId] is adding a brand-new "switch" block; a non-null one is re-editing an existing block's
// value (its raw `blockConfig` [value] pre-fills the dialog, decoded by [decodeSwitchValue]).
private data class SwitchBlockDialogTarget(val blockId: String?, val value: JsonElement?)

// A null [caseId] is adding a brand-new "switch_case" under [switchId]; a non-null [caseId] is re-editing
// that existing case's match/operator/is_default (its raw `blockConfig` [config] pre-fills the dialog,
// decoded by [decodeSwitchCase]).
private data class SwitchCaseDialogTarget(val switchId: String?, val caseId: String?, val config: JsonElement?)

// A null [blockId] is adding a brand-new "loop" block; a non-null one is re-editing an existing block's
// [config]/[condition] (its raw `blockConfig`/`condition` pre-fill the dialog via [decodeLoopConfig]).
private data class LoopBlockDialogTarget(val blockId: String?, val config: JsonElement?, val condition: PipelineNode?)

// The switch operators MatchesCase (PipelineEngine.cs) actually understands — exactly this set, no more, no
// less, so the operator picker can never offer one the engine would silently treat as "no match".
private val SwitchCaseOperators: List<String> = listOf("eq", "ne", "gt", "lt", "gte", "lte", "contains")

@Composable
private fun operatorDisplayName(operator: String): String =
    when (operator) {
        "eq" -> stringResource(Res.string.pipelines_block_operator_eq)
        "ne" -> stringResource(Res.string.pipelines_block_operator_ne)
        "gt" -> stringResource(Res.string.pipelines_block_operator_gt)
        "lt" -> stringResource(Res.string.pipelines_block_operator_lt)
        "gte" -> stringResource(Res.string.pipelines_block_operator_gte)
        "lte" -> stringResource(Res.string.pipelines_block_operator_lte)
        "contains" -> stringResource(Res.string.pipelines_block_operator_contains)
        else -> operator
    }

// Reads a "switch" block's own value back out of its `blockConfig` (`SwitchBlockConfig { value }` on the
// backend) — never `condition`, which this block kind never populates.
private fun decodeSwitchValue(blockConfig: JsonElement?): String =
    (blockConfig as? JsonObject)?.get("value")?.jsonPrimitive?.contentOrNull.orEmpty()

private fun encodeSwitchValue(value: String): JsonElement = JsonObject(mapOf("value" to JsonPrimitive(value)))

// Reads a "switch_case" child's match/operator/is_default back out of its `blockConfig`
// (`SwitchCaseBlockConfig { match, operator, is_default }` on the backend), defaulting the operator to "eq"
// and is_default to false exactly the way the engine's own ParseBlockConfig defaults them.
private fun decodeSwitchCase(blockConfig: JsonElement?): Triple<String, String, Boolean> {
    val obj: JsonObject? = blockConfig as? JsonObject
    val match: String = obj?.get("match")?.jsonPrimitive?.contentOrNull.orEmpty()
    val operator: String = obj?.get("operator")?.jsonPrimitive?.contentOrNull ?: "eq"
    val isDefault: Boolean = obj?.get("is_default")?.jsonPrimitive?.booleanOrNull ?: false
    return Triple(match, operator, isDefault)
}

private fun encodeSwitchCase(match: String, operator: String, isDefault: Boolean): JsonElement =
    JsonObject(
        mapOf(
            "match" to JsonPrimitive(match),
            "operator" to JsonPrimitive(operator),
            "is_default" to JsonPrimitive(isDefault),
        )
    )
