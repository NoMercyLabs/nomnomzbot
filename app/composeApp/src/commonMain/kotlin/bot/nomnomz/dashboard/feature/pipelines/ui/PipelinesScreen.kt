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
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import androidx.compose.material3.Text
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
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowDownGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.ArrowUpGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditLineGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.BlockField
import bot.nomnomz.dashboard.core.network.FieldKind
import bot.nomnomz.dashboard.core.network.PaletteBlock
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.RuntimePalette
import bot.nomnomz.dashboard.core.network.UserRoleOptions
import bot.nomnomz.dashboard.feature.pipelines.state.EditorOptions
import bot.nomnomz.dashboard.feature.pipelines.state.PickerOption
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

import nomnomzbot.composeapp.generated.resources.shell_nav_pipelines
import nomnomzbot.composeapp.generated.resources.pipelines_toggle_action
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Pipelines page: the channel's visual automation pipelines (the action-chain engine), all real data from
// [PipelinesController]. The screen is a pure projection of the controller's state — the LIST surface
// (create / rename / enable-disable / delete) and the chain EDITOR surface (add / configure / reorder / remove
// the ordered action blocks with an optional condition + stop flag, then save). It loads on first composition.
@Composable
fun PipelinesScreen(controller: PipelinesController, role: ManagementRole?) {
    val state: PipelinesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Pipelines gates every write control at its single Editor manage floor
    // (frontend-ia.md §3) — both the list surface (create / rename / toggle / delete) and the chain editor
    // (add / configure / reorder / remove / save). A caller below it sees the list and the chain but each write
    // disabled with "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Pipelines)

    LaunchedEffect(Unit) { controller.load() }

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
                )
            is PipelinesState.Ready ->
                ListContent(
                    pipelines = current.pipelines,
                    actionError = current.actionError,
                    manage = manage,
                    controller = controller,
                    scope = scope,
                )
            is PipelinesState.Editing ->
                ChainEditor(editing = current, manage = manage, controller = controller, scope = scope)
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
        ListHeader(manage = manage, onNew = { editor = PipelineEditor.create() })
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
        ConfirmDialog(
            title = stringResource(Res.string.pipelines_delete_title),
            message = stringResource(Res.string.pipelines_delete_message, pipeline.name),
            confirmLabel = stringResource(Res.string.pipelines_delete_confirm),
            dismissLabel = stringResource(Res.string.pipelines_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deletePipeline(pipeline.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

@Composable
private fun ListHeader(manage: ManageDecision, onNew: () -> Unit) {
    val tokens = LocalTokens.current
    val newLabel: String = stringResource(Res.string.pipelines_new_action)

    PageHeader(title = stringResource(Res.string.shell_nav_pipelines)) {
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = AddGlyph,
                label = newLabel,
                onClick = onNew,
                enabled = enabled,
            )
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
    val toggleLabel: String = stringResource(Res.string.pipelines_toggle_action, pipeline.name)
    val editChainLabel: String = stringResource(Res.string.pipelines_edit_chain_action, pipeline.name)
    val renameLabel: String = stringResource(Res.string.pipelines_rename_action, pipeline.name)
    val deleteLabel: String = stringResource(Res.string.pipelines_delete_action, pipeline.name)

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
                    contentDescription = "${pipeline.name}, $stateLabel. $snippet"
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = pipeline.name,
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

        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = pipeline.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
        // Opening the chain editor is navigation/read, not a write — stays enabled for everyone.
        GlyphButton(
            imageVector = EditLineGlyph,
            label = editChainLabel,
            onClick = onOpen,
            tint = tokens.primary,
        )
        ManageGate(decision = manage) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = renameLabel, onClick = onEdit, enabled = enabled)
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
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
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // null = no step dialog; a value = the add/edit step dialog. A null index is an add, an index an edit.
    var stepDialog: StepDialogTarget? by remember { mutableStateOf(null) }

    val backLabel: String = stringResource(Res.string.pipelines_editor_back)
    val saveLabel: String = stringResource(Res.string.pipelines_editor_save)
    val addLabel: String = stringResource(Res.string.pipelines_step_add)

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
                text = editing.name,
                style = typography.xl2,
                color = tokens.foreground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
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

        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = { stepDialog = StepDialogTarget(index = null, step = null) },
                enabled = enabled,
                modifier = Modifier.fillMaxWidth().semantics { contentDescription = addLabel },
            ) {
                Text(text = addLabel)
            }
        }

        if (editing.steps.isEmpty()) {
            Box(modifier = Modifier.weight(1f).fillMaxWidth(), contentAlignment = Alignment.Center) {
                CenteredMessage(stringResource(Res.string.pipelines_chain_empty))
            }
        } else {
            Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
                LazyColumn(modifier = Modifier.fillMaxWidth()) {
                    itemsIndexed(items = editing.steps) { index, step ->
                        if (index > 0) {
                            Separator()
                        }
                        StepCard(
                            index = index,
                            total = editing.steps.size,
                            step = step,
                            palette = editing.palette,
                            manage = manage,
                            onEdit = { stepDialog = StepDialogTarget(index = index, step = step) },
                            onRemove = { controller.removeStep(index) },
                            onMoveUp = { controller.moveStepUp(index) },
                            onMoveDown = { controller.moveStepDown(index) },
                        )
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
            onDismiss = { stepDialog = null },
            onSubmit = { step ->
                val editIndex: Int? = target.index
                stepDialog = null
                if (editIndex == null) controller.addStep(step) else controller.updateStep(editIndex, step)
            },
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
                    imageVector = ArrowUpGlyph,
                    label = upLabel,
                    onClick = onMoveUp,
                    enabled = canMoveUp,
                    tint = tokens.primary,
                )
            }
            ManageGate(decision = manage) { allowed ->
                val canMoveDown: Boolean = allowed && index < total - 1
                GlyphButton(
                    imageVector = ArrowDownGlyph,
                    label = downLabel,
                    onClick = onMoveDown,
                    enabled = canMoveDown,
                    tint = tokens.primary,
                )
            }
            Box(modifier = Modifier.weight(1f))
            ManageGate(decision = manage) { enabled ->
                GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
            }
            ManageGate(decision = manage) { enabled ->
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = removeLabel,
                    onClick = onRemove,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
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
) {
    if (block.description.isNotBlank()) {
        val tokens = LocalTokens.current
        val typography = LocalTypography.current
        Text(text = block.description, style = typography.xs, color = tokens.mutedForeground)
    }
    if (block.hasHints) {
        TypedParamFields(block = block, params = typed, options = options)
    } else {
        GenericParamFields(entries = generic)
    }
}

@Composable
private fun TypedParamFields(block: PaletteBlock, params: MutableMap<String, String>, options: EditorOptions) {
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
                else ->
                    AppTextField(
                        value = params[field.key].orEmpty(),
                        onValueChange = { params[field.key] = it },
                        label = fieldLabelWithRequired(field),
                        modifier = Modifier.fillMaxWidth(),
                    )
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
private fun GenericParamFields(entries: SnapshotStateList<GenericEntry>) {
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
                    imageVector = TrashGlyph,
                    label = stringResource(Res.string.pipelines_generic_remove),
                    onClick = { entries.removeAt(index) },
                    tint = LocalTokens.current.destructive,
                )
            }
        }
        GlyphButton(
            imageVector = AddGlyph,
            label = addLabel,
            onClick = { entries.add(GenericEntry("", "")) },
            tint = LocalTokens.current.primary,
        )
    }
}

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
                        text = { Text(text = option.label, color = tokens.popoverForeground) },
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
                    text = humanize(category),
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
private data class StepDialogTarget(val index: Int?, val step: PipelineStep?)
