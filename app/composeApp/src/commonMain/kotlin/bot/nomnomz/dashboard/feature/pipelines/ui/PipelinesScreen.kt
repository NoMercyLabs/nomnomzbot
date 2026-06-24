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
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextFieldColors
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.network.BlockField
import bot.nomnomz.dashboard.core.network.BlockType
import bot.nomnomz.dashboard.core.network.FieldKind
import bot.nomnomz.dashboard.core.network.PipelineCatalogue
import bot.nomnomz.dashboard.core.network.PipelineNode
import bot.nomnomz.dashboard.core.network.PipelineStep
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.UserRoleOptions
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesController
import bot.nomnomz.dashboard.feature.pipelines.state.PipelinesState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_action_error
import nomnomzbot.composeapp.generated.resources.pipelines_badge_disabled
import nomnomzbot.composeapp.generated.resources.pipelines_badge_enabled
import nomnomzbot.composeapp.generated.resources.pipelines_block_ban
import nomnomzbot.composeapp.generated.resources.pipelines_block_delete_message
import nomnomzbot.composeapp.generated.resources.pipelines_block_random
import nomnomzbot.composeapp.generated.resources.pipelines_block_send_message
import nomnomzbot.composeapp.generated.resources.pipelines_block_send_reply
import nomnomzbot.composeapp.generated.resources.pipelines_block_set_variable
import nomnomzbot.composeapp.generated.resources.pipelines_block_shoutout
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_request
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_skip
import nomnomzbot.composeapp.generated.resources.pipelines_block_song_volume
import nomnomzbot.composeapp.generated.resources.pipelines_block_stop
import nomnomzbot.composeapp.generated.resources.pipelines_block_timeout
import nomnomzbot.composeapp.generated.resources.pipelines_block_user_role
import nomnomzbot.composeapp.generated.resources.pipelines_block_wait
import nomnomzbot.composeapp.generated.resources.pipelines_chain_empty
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
import nomnomzbot.composeapp.generated.resources.pipelines_field_cooldown_minutes
import nomnomzbot.composeapp.generated.resources.pipelines_field_duration_seconds
import nomnomzbot.composeapp.generated.resources.pipelines_field_message
import nomnomzbot.composeapp.generated.resources.pipelines_field_message_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_min_role
import nomnomzbot.composeapp.generated.resources.pipelines_field_percent
import nomnomzbot.composeapp.generated.resources.pipelines_field_query
import nomnomzbot.composeapp.generated.resources.pipelines_field_reason
import nomnomzbot.composeapp.generated.resources.pipelines_field_user_id
import nomnomzbot.composeapp.generated.resources.pipelines_field_variable_name
import nomnomzbot.composeapp.generated.resources.pipelines_field_variable_value
import nomnomzbot.composeapp.generated.resources.pipelines_field_volume
import nomnomzbot.composeapp.generated.resources.pipelines_field_wait_seconds
import nomnomzbot.composeapp.generated.resources.pipelines_loading
import nomnomzbot.composeapp.generated.resources.pipelines_new_action
import nomnomzbot.composeapp.generated.resources.pipelines_no_description
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
import nomnomzbot.composeapp.generated.resources.pipelines_title
import nomnomzbot.composeapp.generated.resources.pipelines_toggle_action
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Pipelines page: the channel's visual automation pipelines (the action-chain engine), all real data from
// [PipelinesController]. The screen is a pure projection of the controller's state — the LIST surface
// (create / rename / enable-disable / delete) and the chain EDITOR surface (add / configure / reorder / remove
// the ordered action blocks with an optional condition + stop flag, then save). It loads on first composition.
@Composable
fun PipelinesScreen(controller: PipelinesController) {
    val state: PipelinesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

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
                    controller = controller,
                    scope = scope,
                )
            is PipelinesState.Ready ->
                ListContent(
                    pipelines = current.pipelines,
                    actionError = current.actionError,
                    controller = controller,
                    scope = scope,
                )
            is PipelinesState.Editing ->
                ChainEditor(editing = current, controller = controller, scope = scope)
        }
    }
}

// ── The list surface ─────────────────────────────────────────────────────────

@Composable
private fun ListContent(
    pipelines: List<PipelineSummary>,
    actionError: String?,
    controller: PipelinesController,
    scope: kotlinx.coroutines.CoroutineScope,
) {
    val spacing = LocalSpacing.current

    // null = no dialog; a value = the create/edit dialog seed. A null id is a create, an id an edit.
    var editor: PipelineEditor? by remember { mutableStateOf(null) }
    var pendingDelete: PipelineSummary? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        ListHeader(onNew = { editor = PipelineEditor.create() })
        actionError?.let { ActionErrorBanner(detail = it) }

        if (pipelines.isEmpty()) {
            CenteredMessage(stringResource(Res.string.pipelines_empty))
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                contentPadding = PaddingValues(vertical = spacing.s1),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                items(items = pipelines, key = { it.id }) { pipeline ->
                    PipelineRow(
                        pipeline = pipeline,
                        onOpen = { scope.launch { controller.openEditor(pipeline) } },
                        onEdit = { editor = PipelineEditor.edit(pipeline) },
                        onToggle = { enabled -> scope.launch { controller.togglePipeline(pipeline.id, enabled) } },
                        onDelete = { pendingDelete = pipeline },
                    )
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
private fun ListHeader(onNew: () -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val newLabel: String = stringResource(Res.string.pipelines_new_action)

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(text = stringResource(Res.string.pipelines_title), style = typography.xl2, color = tokens.foreground)
        Button(
            onClick = onNew,
            colors =
                ButtonDefaults.buttonColors(
                    containerColor = tokens.primary,
                    contentColor = tokens.primaryForeground,
                ),
            modifier = Modifier.semantics { contentDescription = newLabel },
        ) {
            Text(text = newLabel)
        }
    }
}

@Composable
private fun PipelineRow(
    pipeline: PipelineSummary,
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

    Row(
        modifier =
            Modifier.fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.lg))
                .background(tokens.card)
                .padding(spacing.s4),
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

        Switch(
            checked = pipeline.isEnabled,
            onCheckedChange = onToggle,
            colors = switchColors(),
            modifier = Modifier.semantics { contentDescription = toggleLabel },
        )
        TextButton(
            onClick = onOpen,
            modifier = Modifier.semantics { contentDescription = editChainLabel },
        ) {
            Text(text = stringResource(Res.string.pipelines_edit_chain_action_short), color = tokens.primary, maxLines = 1)
        }
        TextButton(onClick = onEdit) {
            Text(text = stringResource(Res.string.pipelines_rename_action_short), color = tokens.primary, maxLines = 1)
        }
        TextButton(onClick = onDelete) {
            Text(text = stringResource(Res.string.pipelines_delete_action_short), color = tokens.destructive, maxLines = 1)
        }
    }
}

// ── The chain editor surface ──────────────────────────────────────────────────

@Composable
private fun ChainEditor(
    editing: PipelinesState.Editing,
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
            Button(
                onClick = { scope.launch { controller.saveChain() } },
                colors =
                    ButtonDefaults.buttonColors(
                        containerColor = tokens.primary,
                        contentColor = tokens.primaryForeground,
                    ),
                modifier = Modifier.semantics { contentDescription = saveLabel },
            ) {
                Text(text = saveLabel)
            }
        }

        editing.actionError?.let { ActionErrorBanner(detail = it) }

        Text(
            text = stringResource(Res.string.pipelines_step_count, editing.steps.size),
            style = typography.sm,
            color = tokens.mutedForeground,
        )

        Button(
            onClick = { stepDialog = StepDialogTarget(index = null, step = null) },
            colors =
                ButtonDefaults.buttonColors(
                    containerColor = tokens.secondary,
                    contentColor = tokens.secondaryForeground,
                ),
            modifier = Modifier.fillMaxWidth().semantics { contentDescription = addLabel },
        ) {
            Text(text = addLabel)
        }

        if (editing.steps.isEmpty()) {
            CenteredMessage(stringResource(Res.string.pipelines_chain_empty))
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                contentPadding = PaddingValues(vertical = spacing.s1),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                itemsIndexed(items = editing.steps) { index, step ->
                    StepCard(
                        index = index,
                        total = editing.steps.size,
                        step = step,
                        onEdit = { stepDialog = StepDialogTarget(index = index, step = step) },
                        onRemove = { controller.removeStep(index) },
                        onMoveUp = { controller.moveStepUp(index) },
                        onMoveDown = { controller.moveStepDown(index) },
                    )
                }
            }
        }
    }

    stepDialog?.let { target ->
        StepFormDialog(
            initial = target.step,
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
    onEdit: () -> Unit,
    onRemove: () -> Unit,
    onMoveUp: () -> Unit,
    onMoveDown: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val actionName: String = blockDisplayName(PipelineCatalogue.action(step.action.type), step.action.type)
    val conditionText: String =
        step.condition?.let {
            stringResource(
                Res.string.pipelines_condition_label,
                blockDisplayName(PipelineCatalogue.condition(it.type), it.type),
            )
        } ?: stringResource(Res.string.pipelines_condition_none)

    val editLabel: String = stringResource(Res.string.pipelines_step_edit, index + 1)
    val removeLabel: String = stringResource(Res.string.pipelines_step_delete, index + 1)
    val upLabel: String = stringResource(Res.string.pipelines_step_move_up, index + 1)
    val downLabel: String = stringResource(Res.string.pipelines_step_move_down, index + 1)

    Column(
        modifier =
            Modifier.fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.lg))
                .background(tokens.card)
                .padding(spacing.s4),
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
        ParamSummary(step.action)

        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
            TextButton(onClick = onMoveUp, enabled = index > 0, modifier = Modifier.semantics { contentDescription = upLabel }) {
                Text(text = stringResource(Res.string.pipelines_step_move_up_short), color = if (index > 0) tokens.primary else tokens.mutedForeground, maxLines = 1)
            }
            TextButton(
                onClick = onMoveDown,
                enabled = index < total - 1,
                modifier = Modifier.semantics { contentDescription = downLabel },
            ) {
                Text(text = stringResource(Res.string.pipelines_step_move_down_short), color = if (index < total - 1) tokens.primary else tokens.mutedForeground, maxLines = 1)
            }
            Box(modifier = Modifier.weight(1f))
            TextButton(onClick = onEdit, modifier = Modifier.semantics { contentDescription = editLabel }) {
                Text(text = stringResource(Res.string.pipelines_step_edit_short), color = tokens.primary, maxLines = 1)
            }
            TextButton(onClick = onRemove, modifier = Modifier.semantics { contentDescription = removeLabel }) {
                Text(text = stringResource(Res.string.pipelines_step_delete_short), color = tokens.destructive, maxLines = 1)
            }
        }
    }
}

@Composable
private fun ParamSummary(node: PipelineNode) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val block: BlockType = PipelineCatalogue.action(node.type) ?: return

    for (field in block.fields) {
        val value: String = node.params[field.key].orEmpty()
        if (value.isBlank()) continue
        Text(
            text = "${stringResource(fieldLabel(field.labelKey))}: $value",
            style = typography.xs,
            color = tokens.mutedForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
    }
}

// ── The add/edit step dialog ──────────────────────────────────────────────────

// One dialog for both add and edit (DRY): a null [initial] opens a blank add, a seeded one opens an edit. It
// picks the action type, fills the action's params, optionally picks a condition and fills its params, and
// flips the stop-on-match flag. The Save button is disabled until every REQUIRED field of the chosen action
// (and condition, if any) is non-blank, so an invalid block can never be added.
@Composable
private fun StepFormDialog(
    initial: PipelineStep?,
    onDismiss: () -> Unit,
    onSubmit: (PipelineStep) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var actionType: String by remember { mutableStateOf(initial?.action?.type ?: PipelineCatalogue.actions.first().type) }
    val actionParams: MutableMap<String, String> = remember { mutableStateMapFrom(initial?.action?.params) }

    var conditionType: String? by remember { mutableStateOf(initial?.condition?.type) }
    val conditionParams: MutableMap<String, String> = remember { mutableStateMapFrom(initial?.condition?.params) }

    var stopOnMatch: Boolean by remember { mutableStateOf(initial?.stopOnMatch ?: false) }

    val actionBlock: BlockType = PipelineCatalogue.action(actionType) ?: PipelineCatalogue.actions.first()
    val conditionBlock: BlockType? = conditionType?.let { PipelineCatalogue.condition(it) }

    val canSubmit: Boolean =
        actionBlock.fields.filter { it.required }.all { actionParams[it.key]?.isNotBlank() == true } &&
            (conditionBlock == null ||
                conditionBlock.fields.filter { it.required }.all { conditionParams[it.key]?.isNotBlank() == true })

    val title: String =
        stringResource(
            if (initial == null) Res.string.pipelines_step_add_title else Res.string.pipelines_step_edit_title
        )

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                // Action type picker.
                LabeledText(stringResource(Res.string.pipelines_step_action_label))
                BlockTypePicker(
                    options = PipelineCatalogue.actions,
                    selected = actionType,
                    onSelect = { type ->
                        actionType = type
                        actionParams.clear()
                    },
                )
                ParamFields(block = actionBlock, params = actionParams)

                // Optional condition.
                LabeledText(stringResource(Res.string.pipelines_condition_label_short))
                ConditionPicker(
                    selected = conditionType,
                    onSelect = { type ->
                        conditionType = type
                        conditionParams.clear()
                    },
                )
                conditionBlock?.let { ParamFields(block = it, params = conditionParams) }

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
                        colors = switchColors(),
                        modifier = Modifier.semantics { contentDescription = stopLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    val action = PipelineNode(type = actionType, params = actionParams.filterValues { it.isNotBlank() })
                    val condition: PipelineNode? =
                        conditionType?.let { PipelineNode(type = it, params = conditionParams.filterValues { v -> v.isNotBlank() }) }
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

@Composable
private fun ParamFields(block: BlockType, params: MutableMap<String, String>) {
    val spacing = LocalSpacing.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        for (field in block.fields) {
            if (field.key == "min_role") {
                // The role floor is a closed set — a picker, not free text.
                RolePicker(
                    selected = params[field.key].orEmpty(),
                    onSelect = { params[field.key] = it },
                )
            } else {
                OutlinedTextField(
                    value = params[field.key].orEmpty(),
                    onValueChange = { params[field.key] = it },
                    singleLine = field.kind == FieldKind.Number,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(fieldLabelWithRequired(field)) },
                    colors = fieldColors(),
                )
            }
        }
    }
}

@Composable
private fun BlockTypePicker(options: List<BlockType>, selected: String, onSelect: (String) -> Unit) {
    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current

    Box(modifier = Modifier.fillMaxWidth()) {
        TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
            Text(
                text = blockDisplayName(PipelineCatalogue.action(selected) ?: PipelineCatalogue.condition(selected), selected),
                color = tokens.foreground,
                modifier = Modifier.weight(1f),
            )
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            for (option in options) {
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

@Composable
private fun ConditionPicker(selected: String?, onSelect: (String?) -> Unit) {
    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current
    val noneLabel: String = stringResource(Res.string.pipelines_condition_none)

    Box(modifier = Modifier.fillMaxWidth()) {
        TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
            Text(
                text = selected?.let { blockDisplayName(PipelineCatalogue.condition(it), it) } ?: noneLabel,
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
            for (option in PipelineCatalogue.conditions) {
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
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.pipelines_dialog_name_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = description,
                    onValueChange = { description = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.pipelines_dialog_description_label)) },
                    colors = fieldColors(),
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
private fun ActionErrorBanner(detail: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.pipelines_action_error, detail),
        style = typography.sm,
        color = tokens.destructiveForeground,
        modifier =
            Modifier.fillMaxWidth()
                .clip(RoundedCornerShape(tokens.radius.md))
                .background(tokens.destructive)
                .padding(horizontal = spacing.s3, vertical = spacing.s2),
    )
}

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

@Composable
private fun switchColors() =
    SwitchDefaults.colors(
        checkedThumbColor = LocalTokens.current.primaryForeground,
        checkedTrackColor = LocalTokens.current.primary,
        uncheckedThumbColor = LocalTokens.current.mutedForeground,
        uncheckedTrackColor = LocalTokens.current.muted,
        uncheckedBorderColor = LocalTokens.current.border,
    )

@Composable
private fun fieldColors(): TextFieldColors {
    val tokens: Tokens = LocalTokens.current
    return OutlinedTextFieldDefaults.colors(
        focusedTextColor = tokens.cardForeground,
        unfocusedTextColor = tokens.cardForeground,
        disabledTextColor = tokens.mutedForeground,
        focusedBorderColor = tokens.ring,
        unfocusedBorderColor = tokens.border,
        disabledBorderColor = tokens.border,
        focusedLabelColor = tokens.mutedForeground,
        unfocusedLabelColor = tokens.mutedForeground,
        disabledLabelColor = tokens.mutedForeground,
        cursorColor = tokens.primary,
    )
}

// Resolve a block type's display name from its i18n key, falling back to the raw backend type when uncatalogued.
@Composable
private fun blockDisplayName(block: BlockType?, rawType: String): String =
    block?.let { stringResource(blockLabel(it.labelKey)) } ?: rawType

@Composable
private fun fieldLabelWithRequired(field: BlockField): String {
    val base: String = stringResource(fieldLabel(field.labelKey))
    return if (field.required) "$base *" else base
}

// Map the catalogue's labelKey suffix to its declared StringResource (the catalogue is closed, so this is a
// fixed, exhaustive lookup — no dynamic resource resolution).
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
        "set_variable" -> Res.string.pipelines_block_set_variable
        "wait" -> Res.string.pipelines_block_wait
        "stop" -> Res.string.pipelines_block_stop
        "user_role" -> Res.string.pipelines_block_user_role
        "random" -> Res.string.pipelines_block_random
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
        "variable_name" -> Res.string.pipelines_field_variable_name
        "variable_value" -> Res.string.pipelines_field_variable_value
        "wait_seconds" -> Res.string.pipelines_field_wait_seconds
        "min_role" -> Res.string.pipelines_field_min_role
        "percent" -> Res.string.pipelines_field_percent
        else -> Res.string.pipelines_field_message
    }

// A fresh observable string map seeded from existing params (or empty) — backs the dialog's editable fields.
private fun mutableStateMapFrom(source: Map<String, String>?): androidx.compose.runtime.snapshots.SnapshotStateMap<String, String> {
    val map = androidx.compose.runtime.mutableStateMapOf<String, String>()
    source?.let { map.putAll(it) }
    return map
}

// The create/rename dialog seed: a null [id] is a create (blank), an id is a rename of that pipeline.
private data class PipelineEditor(val id: Int?, val name: String, val description: String) {
    companion object {
        fun create(): PipelineEditor = PipelineEditor(id = null, name = "", description = "")

        fun edit(pipeline: PipelineSummary): PipelineEditor =
            PipelineEditor(id = pipeline.id, name = pipeline.name, description = pipeline.description.orEmpty())
    }
}

// The step add/edit dialog target: a null [index] is an add, an index edits that step. [step] seeds an edit.
private data class StepDialogTarget(val index: Int?, val step: PipelineStep?)
