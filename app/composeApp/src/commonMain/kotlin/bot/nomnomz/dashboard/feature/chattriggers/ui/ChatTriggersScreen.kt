// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.chattriggers.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChatTrigger
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.feature.chattriggers.state.ChatTriggersController
import bot.nomnomz.dashboard.feature.chattriggers.state.ChatTriggersState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.chattriggers_action_error
import nomnomzbot.composeapp.generated.resources.chattriggers_cooldown
import nomnomzbot.composeapp.generated.resources.chattriggers_delete_action
import nomnomzbot.composeapp.generated.resources.chattriggers_delete_cancel
import nomnomzbot.composeapp.generated.resources.chattriggers_delete_confirm
import nomnomzbot.composeapp.generated.resources.chattriggers_delete_message
import nomnomzbot.composeapp.generated.resources.chattriggers_delete_title
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_cancel
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_case_sensitive_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_cooldown_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_create
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_create_title
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_match_type_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_pattern_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_permission_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_pipeline_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_pipeline_none
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_response_label
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_save
import nomnomzbot.composeapp.generated.resources.chattriggers_dialog_use_pipeline_label
import nomnomzbot.composeapp.generated.resources.chattriggers_disabled
import nomnomzbot.composeapp.generated.resources.chattriggers_edit_action
import nomnomzbot.composeapp.generated.resources.chattriggers_empty
import nomnomzbot.composeapp.generated.resources.chattriggers_enabled
import nomnomzbot.composeapp.generated.resources.chattriggers_error
import nomnomzbot.composeapp.generated.resources.chattriggers_helper
import nomnomzbot.composeapp.generated.resources.chattriggers_loading
import nomnomzbot.composeapp.generated.resources.chattriggers_match_contains
import nomnomzbot.composeapp.generated.resources.chattriggers_match_exact
import nomnomzbot.composeapp.generated.resources.chattriggers_match_regex
import nomnomzbot.composeapp.generated.resources.chattriggers_match_starts_with
import nomnomzbot.composeapp.generated.resources.chattriggers_new_action
import nomnomzbot.composeapp.generated.resources.chattriggers_perm_broadcaster
import nomnomzbot.composeapp.generated.resources.chattriggers_perm_everyone
import nomnomzbot.composeapp.generated.resources.chattriggers_perm_moderator
import nomnomzbot.composeapp.generated.resources.chattriggers_perm_subscriber
import nomnomzbot.composeapp.generated.resources.chattriggers_perm_vip
import nomnomzbot.composeapp.generated.resources.chattriggers_reaction_pipeline
import nomnomzbot.composeapp.generated.resources.chattriggers_reaction_response
import nomnomzbot.composeapp.generated.resources.chattriggers_retry
import nomnomzbot.composeapp.generated.resources.chattriggers_row_description
import nomnomzbot.composeapp.generated.resources.chattriggers_toggle_action
import nomnomzbot.composeapp.generated.resources.shell_nav_chat_triggers
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Chat Triggers page (frontend-ia.md §3, Chat group — beside Commands): the channel's keyword auto-replies
// ("someone says X → the bot reacts"). Every trigger is real data from [ChatTriggersController]. The screen is a
// pure projection of the controller's state; it loads on first composition. Full management surface — create,
// edit, enable/disable, delete — each routed back through the controller, which re-lists after every successful
// write. Server-side validation (a regex must compile; a trigger needs a response or a pipeline) surfaces inline
// as the action-error banner.
@Composable
fun ChatTriggersScreen(controller: ChatTriggersController, role: ManagementRole?) {
    val state: ChatTriggersState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: writes gate at the page's Editor manage floor (frontend-ia.md §3, Chat
    // group). A caller below it sees the list but every write control renders disabled with "Requires Editor"
    // (§7); the backend re-checks `chattriggers:write` on every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.ChatTriggers)

    // The create/edit dialog target: null = closed, a value = open (an empty editor = create, a pre-filled one =
    // edit). The delete-confirm target is the trigger pending confirmation, or null when none.
    var editor: TriggerEditor? by remember { mutableStateOf(null) }
    var pendingDelete: ChatTrigger? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: ChatTriggersState = state) {
            is ChatTriggersState.Loading -> CenteredMessage(stringResource(Res.string.chattriggers_loading))
            is ChatTriggersState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is ChatTriggersState.Empty ->
                ManagedContent(
                    triggers = emptyList(),
                    actionError = current.actionError,
                    manage = manage,
                    onNew = { editor = TriggerEditor.create() },
                    onEdit = { trigger -> editor = TriggerEditor.edit(trigger) },
                    onToggle = { trigger, enabled ->
                        scope.launch { controller.toggleTrigger(trigger.id, enabled) }
                    },
                    onDelete = { trigger -> pendingDelete = trigger },
                )
            is ChatTriggersState.Ready ->
                ManagedContent(
                    triggers = current.triggers,
                    actionError = current.actionError,
                    manage = manage,
                    onNew = { editor = TriggerEditor.create() },
                    onEdit = { trigger -> editor = TriggerEditor.edit(trigger) },
                    onToggle = { trigger, enabled ->
                        scope.launch { controller.toggleTrigger(trigger.id, enabled) }
                    },
                    onDelete = { trigger -> pendingDelete = trigger },
                )
        }
    }

    editor?.let { open ->
        val pipelines: List<PipelineSummary> = when (val s: ChatTriggersState = state) {
            is ChatTriggersState.Ready -> s.pipelines
            is ChatTriggersState.Empty -> s.pipelines
            else -> emptyList()
        }
        TriggerFormDialog(
            editor = open,
            pipelines = pipelines,
            onDismiss = { editor = null },
            onSubmit = { form ->
                editor = null
                scope.launch {
                    if (open.isEdit) {
                        controller.updateTrigger(
                            triggerId = open.id,
                            pattern = form.pattern,
                            matchType = form.matchType,
                            caseSensitive = form.caseSensitive,
                            isEnabled = form.isEnabled,
                            usePipeline = form.usePipeline,
                            response = form.response,
                            pipelineId = if (form.usePipeline) form.pipelineId else null,
                            cooldownSeconds = form.cooldownSeconds,
                            minPermissionLevel = form.minPermissionLevel,
                        )
                    } else {
                        controller.createTrigger(
                            pattern = form.pattern,
                            matchType = form.matchType,
                            caseSensitive = form.caseSensitive,
                            isEnabled = form.isEnabled,
                            response = if (form.usePipeline) null else form.response,
                            pipelineId = if (form.usePipeline) form.pipelineId else null,
                            cooldownSeconds = form.cooldownSeconds,
                            minPermissionLevel = form.minPermissionLevel,
                        )
                    }
                }
            },
        )
    }

    pendingDelete?.let { trigger ->
        ConfirmDialog(
            title = stringResource(Res.string.chattriggers_delete_title),
            message = stringResource(Res.string.chattriggers_delete_message, trigger.pattern),
            confirmLabel = stringResource(Res.string.chattriggers_delete_confirm),
            dismissLabel = stringResource(Res.string.chattriggers_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteTrigger(trigger.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The list-bearing content: the header with the "+ New trigger" action, the purpose one-liner, an optional
// write-failure banner (carrying the server-side reason), and one Card wrapping either the rows or the empty hint.
@Composable
private fun ManagedContent(
    triggers: List<ChatTrigger>,
    actionError: String?,
    manage: ManageDecision,
    onNew: () -> Unit,
    onEdit: (ChatTrigger) -> Unit,
    onToggle: (ChatTrigger, Boolean) -> Unit,
    onDelete: (ChatTrigger) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Header(manage = manage, onNew = onNew)
        Text(
            text = stringResource(Res.string.chattriggers_helper),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.chattriggers_action_error, it)) }

        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (triggers.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = stringResource(Res.string.chattriggers_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    itemsIndexed(items = triggers, key = { _, t -> t.id }) { index, trigger ->
                        TriggerRow(
                            trigger = trigger,
                            manage = manage,
                            onEdit = { onEdit(trigger) },
                            onToggle = { enabled -> onToggle(trigger, enabled) },
                            onDelete = { onDelete(trigger) },
                        )
                        if (index < triggers.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun Header(manage: ManageDecision, onNew: () -> Unit) {
    val newLabel: String = stringResource(Res.string.chattriggers_new_action)
    PageHeader(title = stringResource(Res.string.shell_nav_chat_triggers)) {
        ManageGate(decision = manage) { enabled ->
            Button(onClick = onNew, enabled = enabled) { Text(text = newLabel) }
        }
    }
}

@Composable
private fun TriggerRow(
    trigger: ChatTrigger,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusLabel: String =
        if (trigger.isEnabled) stringResource(Res.string.chattriggers_enabled)
        else stringResource(Res.string.chattriggers_disabled)
    val matchLabel: String = matchTypeLabel(trigger.matchType)
    val reaction: String =
        if (trigger.pipelineId != null)
            stringResource(Res.string.chattriggers_reaction_pipeline)
        else
            stringResource(Res.string.chattriggers_reaction_response, trigger.response.orEmpty())
    val cooldownLabel: String = stringResource(Res.string.chattriggers_cooldown, trigger.cooldownSeconds)
    val rowDescription: String =
        stringResource(Res.string.chattriggers_row_description, trigger.pattern, matchLabel, statusLabel)
    val toggleLabel: String = stringResource(Res.string.chattriggers_toggle_action, trigger.pattern)
    val editLabel: String = stringResource(Res.string.chattriggers_edit_action, trigger.pattern)
    val deleteLabel: String = stringResource(Res.string.chattriggers_delete_action, trigger.pattern)

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
                text = trigger.pattern,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = "$matchLabel · $cooldownLabel",
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = reaction,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = trigger.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
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

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. Requires a
// non-blank pattern and either a response (text mode) or a picked pipeline (pipeline mode), so a trigger with no
// reaction can never be submitted from the UI — the backend re-validates (and 400s a bad regex) regardless.
@Composable
private fun TriggerFormDialog(
    editor: TriggerEditor,
    pipelines: List<PipelineSummary>,
    onDismiss: () -> Unit,
    onSubmit: (TriggerForm) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var pattern: String by remember { mutableStateOf(editor.pattern) }
    var matchType: String by remember { mutableStateOf(editor.matchType) }
    var caseSensitive: Boolean by remember { mutableStateOf(editor.caseSensitive) }
    var isEnabled: Boolean by remember { mutableStateOf(editor.isEnabled) }
    var usePipeline: Boolean by remember { mutableStateOf(editor.usePipeline) }
    var response: String by remember { mutableStateOf(editor.response) }
    var selectedPipelineId: String? by remember { mutableStateOf(editor.pipelineId) }
    var cooldown: String by remember { mutableStateOf(editor.cooldownSeconds.toString()) }
    var minLevel: Int by remember { mutableStateOf(editor.minPermissionLevel) }

    var matchMenuOpen: Boolean by remember { mutableStateOf(false) }
    var permMenuOpen: Boolean by remember { mutableStateOf(false) }
    var pipelineMenuOpen: Boolean by remember { mutableStateOf(false) }

    val cooldownValue: Int? = cooldown.ifBlank { "0" }.toIntOrNull()
    val cooldownValid: Boolean = cooldownValue != null && cooldownValue >= 0
    val hasReaction: Boolean =
        if (usePipeline) selectedPipelineId != null else response.isNotBlank()
    val canSubmit: Boolean = pattern.isNotBlank() && hasReaction && cooldownValid

    val title: String =
        stringResource(
            if (editor.isEdit) Res.string.chattriggers_dialog_edit_title
            else Res.string.chattriggers_dialog_create_title
        )
    val submitLabel: String =
        stringResource(
            if (editor.isEdit) Res.string.chattriggers_dialog_save else Res.string.chattriggers_dialog_create
        )
    val caseLabel: String = stringResource(Res.string.chattriggers_dialog_case_sensitive_label)
    val enabledLabel: String = stringResource(Res.string.chattriggers_dialog_enabled_label)
    val usePipelineLabel: String = stringResource(Res.string.chattriggers_dialog_use_pipeline_label)
    val pipelineNoneLabel: String = stringResource(Res.string.chattriggers_dialog_pipeline_none)
    val selectedPipelineName: String =
        selectedPipelineId?.let { id -> pipelines.firstOrNull { it.id == id }?.name } ?: pipelineNoneLabel

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(
                modifier = Modifier.heightIn(max = spacing.s24 * 4).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                AppTextField(
                    value = pattern,
                    onValueChange = { pattern = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.chattriggers_dialog_pattern_label),
                )

                // Match-type picker: contains / exact / starts_with / regex — role/label-named, never the raw key.
                PickerField(
                    label = stringResource(Res.string.chattriggers_dialog_match_type_label),
                    value = matchTypeLabel(matchType),
                    expanded = matchMenuOpen,
                    onExpandedChange = { matchMenuOpen = it },
                ) {
                    MatchTypes.forEach { key ->
                        DropdownMenuItem(
                            text = { Text(matchTypeLabel(key), color = tokens.cardForeground) },
                            onClick = {
                                matchType = key
                                matchMenuOpen = false
                            },
                        )
                    }
                }

                // Minimum role that can fire it — role NAMES only, never the numeric ladder value (house rule).
                PickerField(
                    label = stringResource(Res.string.chattriggers_dialog_permission_label),
                    value = permissionLabel(minLevel),
                    expanded = permMenuOpen,
                    onExpandedChange = { permMenuOpen = it },
                ) {
                    PermissionRungs.forEach { (level, res) ->
                        DropdownMenuItem(
                            text = { Text(stringResource(res), color = tokens.cardForeground) },
                            onClick = {
                                minLevel = level
                                permMenuOpen = false
                            },
                        )
                    }
                }

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = caseLabel, color = tokens.cardForeground)
                    Switch(
                        checked = caseSensitive,
                        onCheckedChange = { caseSensitive = it },
                        modifier = Modifier.semantics { contentDescription = caseLabel },
                    )
                }

                // Reaction toggle: a text reply (default) OR a bound pipeline. Only pipeline mode offers the picker,
                // and only when the channel actually has pipelines.
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = usePipelineLabel, color = tokens.cardForeground)
                    Switch(
                        checked = usePipeline,
                        onCheckedChange = { usePipeline = it && pipelines.isNotEmpty() },
                        enabled = pipelines.isNotEmpty(),
                        modifier = Modifier.semantics { contentDescription = usePipelineLabel },
                    )
                }
                if (usePipeline && pipelines.isNotEmpty()) {
                    PickerField(
                        label = stringResource(Res.string.chattriggers_dialog_pipeline_label),
                        value = selectedPipelineName,
                        expanded = pipelineMenuOpen,
                        onExpandedChange = { pipelineMenuOpen = it },
                    ) {
                        pipelines.forEach { pipeline ->
                            DropdownMenuItem(
                                text = { Text(pipeline.name, color = tokens.cardForeground) },
                                onClick = {
                                    selectedPipelineId = pipeline.id
                                    pipelineMenuOpen = false
                                },
                            )
                        }
                    }
                } else {
                    AppTextField(
                        value = response,
                        onValueChange = { response = it },
                        modifier = Modifier.fillMaxWidth(),
                        label = stringResource(Res.string.chattriggers_dialog_response_label),
                    )
                }

                AppTextField(
                    value = cooldown,
                    onValueChange = { input -> cooldown = input.filter { it.isDigit() } },
                    isError = !cooldownValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.chattriggers_dialog_cooldown_label),
                )

                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = enabledLabel, color = tokens.cardForeground)
                    Switch(
                        checked = isEnabled,
                        onCheckedChange = { isEnabled = it },
                        modifier = Modifier.semantics { contentDescription = enabledLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSubmit(
                        TriggerForm(
                            pattern = pattern,
                            matchType = matchType,
                            caseSensitive = caseSensitive,
                            isEnabled = isEnabled,
                            usePipeline = usePipeline,
                            response = response,
                            pipelineId = selectedPipelineId,
                            cooldownSeconds = cooldownValue ?: 0,
                            minPermissionLevel = minLevel,
                        )
                    )
                },
                enabled = canSubmit,
            ) {
                Text(text = submitLabel, color = if (canSubmit) tokens.primary else tokens.mutedForeground)
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.chattriggers_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

// A read-only field that opens a themed dropdown when clicked (the shared select pattern: an AppTextField shows
// the current value, its Box anchors the DropdownMenu). Keeps the three pickers (match type, permission, pipeline)
// consistent without repeating the plumbing three times.
@Composable
private fun PickerField(
    label: String,
    value: String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    items: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    Box {
        AppTextField(
            value = value,
            onValueChange = {},
            modifier = Modifier.fillMaxWidth().clickable { onExpandedChange(true) },
            label = label,
        )
        DropdownMenu(expanded = expanded, onDismissRequest = { onExpandedChange(false) }, content = items)
    }
}

@Composable
private fun matchTypeLabel(matchType: String): String =
    stringResource(
        when (matchType) {
            "exact" -> Res.string.chattriggers_match_exact
            "starts_with" -> Res.string.chattriggers_match_starts_with
            "regex" -> Res.string.chattriggers_match_regex
            else -> Res.string.chattriggers_match_contains
        }
    )

@Composable
private fun permissionLabel(level: Int): String =
    stringResource(PermissionRungs.lastOrNull { level >= it.first }?.second ?: Res.string.chattriggers_perm_everyone)

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
                text = stringResource(Res.string.chattriggers_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.chattriggers_retry)) }
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

// The match-type keys the picker offers, in the backend's canonical order (contains is the default).
private val MatchTypes: List<String> = listOf("contains", "exact", "starts_with", "regex")

// The permission rungs the picker offers as ROLE NAMES mapped to their unified-ladder value (roles-permissions
// §0). Ascending so [permissionLabel] can resolve a stored level to the highest rung it clears.
private val PermissionRungs: List<Pair<Int, StringResource>> =
    listOf(
        0 to Res.string.chattriggers_perm_everyone,
        2 to Res.string.chattriggers_perm_subscriber,
        4 to Res.string.chattriggers_perm_vip,
        10 to Res.string.chattriggers_perm_moderator,
        40 to Res.string.chattriggers_perm_broadcaster,
    )

// The dialog's submitted values, bundled so the create/edit callback has one clean parameter.
private data class TriggerForm(
    val pattern: String,
    val matchType: String,
    val caseSensitive: Boolean,
    val isEnabled: Boolean,
    val usePipeline: Boolean,
    val response: String,
    val pipelineId: String?,
    val cooldownSeconds: Int,
    val minPermissionLevel: Int,
)

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a trigger opens a
// pre-filled edit form. [isEdit] decides create-vs-update on submit; [id] addresses the row the update targets.
private data class TriggerEditor(
    val isEdit: Boolean,
    val id: String,
    val pattern: String,
    val matchType: String,
    val caseSensitive: Boolean,
    val isEnabled: Boolean,
    val usePipeline: Boolean,
    val response: String,
    val pipelineId: String?,
    val cooldownSeconds: Int,
    val minPermissionLevel: Int,
) {
    companion object {
        fun create(): TriggerEditor =
            TriggerEditor(
                isEdit = false,
                id = "",
                pattern = "",
                matchType = "contains",
                caseSensitive = false,
                isEnabled = true,
                usePipeline = false,
                response = "",
                pipelineId = null,
                cooldownSeconds = 30,
                minPermissionLevel = 0,
            )

        fun edit(trigger: ChatTrigger): TriggerEditor =
            TriggerEditor(
                isEdit = true,
                id = trigger.id,
                pattern = trigger.pattern,
                matchType = trigger.matchType,
                caseSensitive = trigger.caseSensitive,
                isEnabled = trigger.isEnabled,
                usePipeline = trigger.pipelineId != null,
                response = trigger.response.orEmpty(),
                pipelineId = trigger.pipelineId,
                cooldownSeconds = trigger.cooldownSeconds,
                minPermissionLevel = trigger.minPermissionLevel,
            )
    }
}
