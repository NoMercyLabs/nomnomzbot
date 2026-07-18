// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
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
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
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
import bot.nomnomz.dashboard.core.network.BuiltinCommand
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.feature.commands.state.CommandsController
import bot.nomnomz.dashboard.feature.commands.state.CommandsState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.commands_action_error
import nomnomzbot.composeapp.generated.resources.commands_builtins_section
import nomnomzbot.composeapp.generated.resources.commands_builtins_toggle
import nomnomzbot.composeapp.generated.resources.commands_delete_action
import nomnomzbot.composeapp.generated.resources.commands_delete_cancel
import nomnomzbot.composeapp.generated.resources.commands_delete_confirm
import nomnomzbot.composeapp.generated.resources.commands_delete_message
import nomnomzbot.composeapp.generated.resources.commands_delete_title
import nomnomzbot.composeapp.generated.resources.commands_dialog_cancel
import nomnomzbot.composeapp.generated.resources.commands_dialog_create
import nomnomzbot.composeapp.generated.resources.commands_default_template
import nomnomzbot.composeapp.generated.resources.commands_dialog_create_title
import nomnomzbot.composeapp.generated.resources.commands_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.commands_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_name_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_pipeline_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_pipeline_none
import nomnomzbot.composeapp.generated.resources.commands_dialog_response_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_response_optional
import nomnomzbot.composeapp.generated.resources.commands_dialog_save
import nomnomzbot.composeapp.generated.resources.commands_edit_action
import nomnomzbot.composeapp.generated.resources.commands_empty
import nomnomzbot.composeapp.generated.resources.commands_error
import nomnomzbot.composeapp.generated.resources.commands_filter_all
import nomnomzbot.composeapp.generated.resources.commands_filter_builtin
import nomnomzbot.composeapp.generated.resources.commands_filter_custom
import nomnomzbot.composeapp.generated.resources.commands_loading
import nomnomzbot.composeapp.generated.resources.commands_new_action
import nomnomzbot.composeapp.generated.resources.commands_no_description
import nomnomzbot.composeapp.generated.resources.commands_retry
import nomnomzbot.composeapp.generated.resources.commands_search_placeholder
import nomnomzbot.composeapp.generated.resources.commands_title
import nomnomzbot.composeapp.generated.resources.commands_toggle_action
import org.jetbrains.compose.resources.stringResource

@Composable
fun CommandsScreen(
    controller: CommandsController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: CommandsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Commands)

    var editor: CommandEditor? by remember { mutableStateOf(null) }
    var pendingDelete: String? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: CommandsState = state) {
            is CommandsState.Loading -> CenteredMessage(stringResource(Res.string.commands_loading))
            is CommandsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is CommandsState.Empty ->
                ManagedContent(
                    commands = emptyList(),
                    builtins = emptyList(),
                    pipelines = current.pipelines,
                    actionError = null,
                    manage = manage,
                    onNew = { editor = CommandEditor.create() },
                    onEdit = { command -> editor = CommandEditor.edit(command) },
                    onToggle = { command, enabled ->
                        scope.launch { controller.toggleCommand(command.name, enabled) }
                    },
                    onDelete = { command -> pendingDelete = command.name },
                    onToggleBuiltin = { builtinKey, enabled ->
                        scope.launch { controller.toggleBuiltin(builtinKey, enabled) }
                    },
                )
            is CommandsState.Ready ->
                ManagedContent(
                    commands = current.commands,
                    builtins = current.builtins,
                    pipelines = current.pipelines,
                    actionError = current.actionError,
                    manage = manage,
                    onNew = { editor = CommandEditor.create() },
                    onEdit = { command -> editor = CommandEditor.edit(command) },
                    onToggle = { command, enabled ->
                        scope.launch { controller.toggleCommand(command.name, enabled) }
                    },
                    onDelete = { command -> pendingDelete = command.name },
                    onToggleBuiltin = { builtinKey, enabled ->
                        scope.launch { controller.toggleBuiltin(builtinKey, enabled) }
                    },
                )
        }
    }

    editor?.let { open ->
        val pipelines: List<PipelineSummary> = when (val s: CommandsState = state) {
            is CommandsState.Ready -> s.pipelines
            is CommandsState.Empty -> s.pipelines
            else -> emptyList()
        }
        CommandFormDialog(
            editor = open,
            pipelines = pipelines,
            onDismiss = { editor = null },
            onSubmit = { name, templateResponse, pipelineId, enabled ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updateCommand(name, templateResponse, pipelineId, enabled)
                    else controller.createCommand(name, templateResponse, pipelineId, enabled)
                }
            },
        )
    }

    pendingDelete?.let { name ->
        ConfirmDialog(
            title = stringResource(Res.string.commands_delete_title),
            message = stringResource(Res.string.commands_delete_message, name),
            confirmLabel = stringResource(Res.string.commands_delete_confirm),
            dismissLabel = stringResource(Res.string.commands_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteCommand(name) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

@Composable
private fun ManagedContent(
    commands: List<CommandSummary>,
    builtins: List<BuiltinCommand>,
    pipelines: List<PipelineSummary>,
    actionError: String?,
    manage: ManageDecision,
    onNew: () -> Unit,
    onEdit: (CommandSummary) -> Unit,
    onToggle: (CommandSummary, Boolean) -> Unit,
    onDelete: (CommandSummary) -> Unit,
    onToggleBuiltin: (builtinKey: String, Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var searchQuery: String by remember { mutableStateOf("") }
    var activeTab: CommandTab by remember { mutableStateOf(CommandTab.All) }

    val showCustom: Boolean = activeTab != CommandTab.Builtin
    val showBuiltin: Boolean = activeTab != CommandTab.Custom

    val filteredCommands: List<CommandSummary> = commands.filter { cmd ->
        showCustom && (
            searchQuery.isBlank() ||
            cmd.name.contains(searchQuery, ignoreCase = true) ||
            cmd.description?.contains(searchQuery, ignoreCase = true) == true
        )
    }

    val filteredBuiltins: List<BuiltinCommand> = builtins.filter { builtin ->
        showBuiltin && (
            searchQuery.isBlank() ||
            builtin.name.contains(searchQuery, ignoreCase = true)
        )
    }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.commands_title)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onNew, enabled = enabled) {
                    Text(text = stringResource(Res.string.commands_new_action))
                }
            }
        }

        // Search bar — filters both custom commands and built-ins by name/description.
        AppTextField(
            value = searchQuery,
            onValueChange = { searchQuery = it },
            label = "",
            placeholder = stringResource(Res.string.commands_search_placeholder),
            modifier = Modifier.fillMaxWidth(),
        )

        // Tab strip — only shown when there are built-in commands to distinguish.
        if (builtins.isNotEmpty()) {
            TabsList {
                CommandTab.entries.forEach { tab ->
                    TabsTrigger(
                        selected = activeTab == tab,
                        onClick = { activeTab = tab },
                    ) {
                        Text(text = tab.label())
                    }
                }
            }
        }

        actionError?.let {
            ActionErrorBanner(message = stringResource(Res.string.commands_action_error, it))
        }

        // Single card wrapping the entire table — rows are divided by hairlines, not individual cards.
        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (filteredCommands.isEmpty() && filteredBuiltins.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = stringResource(Res.string.commands_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    itemsIndexed(
                        items = filteredCommands,
                        key = { _, cmd -> "custom-${cmd.id}" },
                    ) { index, command ->
                        CommandTableRow(
                            command = command,
                            manage = manage,
                            onEdit = { onEdit(command) },
                            onToggle = { enabled -> onToggle(command, enabled) },
                            onDelete = { onDelete(command) },
                        )
                        if (index < filteredCommands.lastIndex || filteredBuiltins.isNotEmpty()) {
                            Separator()
                        }
                    }

                    // Labelled section separator before built-ins (only when both sections are present).
                    if (filteredBuiltins.isNotEmpty() && filteredCommands.isNotEmpty()) {
                        item(key = "builtins-header") {
                            Row(
                                modifier = Modifier
                                    .fillMaxWidth()
                                    .padding(horizontal = spacing.s4, vertical = spacing.s2),
                            ) {
                                Text(
                                    text = stringResource(Res.string.commands_builtins_section),
                                    style = typography.xs,
                                    color = tokens.mutedForeground,
                                )
                            }
                            Separator()
                        }
                    }

                    itemsIndexed(
                        items = filteredBuiltins,
                        key = { _, builtin -> "builtin-${builtin.builtinKey}" },
                    ) { index, builtin ->
                        BuiltinTableRow(
                            builtin = builtin,
                            manage = manage,
                            onToggle = { enabled -> onToggleBuiltin(builtin.builtinKey, enabled) },
                        )
                        if (index < filteredBuiltins.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

// Command row inside the shared card — no per-row background/clip; dividers separate rows.
@Composable
private fun CommandTableRow(
    command: CommandSummary,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val snippet: String =
        command.description?.takeIf { it.isNotBlank() }
            ?: command.templateResponse?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.commands_no_description)

    val toggleLabel: String = stringResource(Res.string.commands_toggle_action, command.name)
    val editLabel: String = stringResource(Res.string.commands_edit_action, command.name)
    val deleteLabel: String = stringResource(Res.string.commands_delete_action, command.name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = "${command.name}. $snippet" },
            verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
        ) {
            Text(
                text = command.name,
                style = typography.sm.copy(fontFamily = FontFamily.Monospace),
                color = tokens.primary,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = snippet,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }

        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = command.isEnabled,
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

// Built-in command row — toggle only; platform commands can't be edited or deleted.
@Composable
private fun BuiltinTableRow(
    builtin: BuiltinCommand,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val toggleLabel: String = stringResource(Res.string.commands_builtins_toggle, builtin.name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = builtin.name,
            style = typography.sm.copy(fontFamily = FontFamily.Monospace),
            color = tokens.primary,
            modifier = Modifier.weight(1f),
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = builtin.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
    }
}

// One composable for both create and edit. Requires name + (response OR pipeline). Name is
// read-only on edit (the backend addresses a command by name).
@Composable
private fun CommandFormDialog(
    editor: CommandEditor,
    pipelines: List<PipelineSummary>,
    onDismiss: () -> Unit,
    onSubmit: (name: String, templateResponse: String?, pipelineId: String?, enabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    // Pre-fill a sensible default template when creating a fresh command (owner ask: no empty template inputs);
    // an edit keeps the stored response verbatim.
    val defaultTemplate: String = stringResource(Res.string.commands_default_template)
    var name: String by remember { mutableStateOf(editor.name) }
    var response: String by remember {
        mutableStateOf(if (!editor.isEdit && editor.response.isBlank()) defaultTemplate else editor.response)
    }
    var selectedPipelineId: String? by remember { mutableStateOf(editor.pipelineId) }
    var enabled: Boolean by remember { mutableStateOf(editor.isEnabled) }
    var pipelineMenuOpen: Boolean by remember { mutableStateOf(false) }

    val hasPipeline: Boolean = selectedPipelineId != null
    val canSubmit: Boolean = name.isNotBlank() && (response.isNotBlank() || hasPipeline)

    val title: String =
        stringResource(
            if (editor.isEdit) Res.string.commands_dialog_edit_title
            else Res.string.commands_dialog_create_title
        )
    val submitLabel: String =
        stringResource(
            if (editor.isEdit) Res.string.commands_dialog_save else Res.string.commands_dialog_create
        )
    val enabledLabel: String = stringResource(Res.string.commands_dialog_enabled_label)
    val pipelineLabel: String = stringResource(Res.string.commands_dialog_pipeline_label)
    val pipelineNoneLabel: String = stringResource(Res.string.commands_dialog_pipeline_none)
    val responseLabel: String =
        if (hasPipeline) stringResource(Res.string.commands_dialog_response_optional)
        else stringResource(Res.string.commands_dialog_response_label)

    val selectedPipelineName: String =
        selectedPipelineId?.let { id -> pipelines.firstOrNull { it.id == id }?.name } ?: pipelineNoneLabel

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    enabled = !editor.isEdit,
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.commands_dialog_name_label),
                )
                AppTextField(
                    value = response,
                    onValueChange = { response = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = responseLabel,
                )
                if (pipelines.isNotEmpty()) {
                    Box {
                        AppTextField(
                            value = selectedPipelineName,
                            onValueChange = {},
                            modifier = Modifier.fillMaxWidth().clickable { pipelineMenuOpen = true },
                            label = pipelineLabel,
                        )
                        DropdownMenu(
                            expanded = pipelineMenuOpen,
                            onDismissRequest = { pipelineMenuOpen = false },
                        ) {
                            DropdownMenuItem(
                                text = { Text(pipelineNoneLabel, color = tokens.mutedForeground) },
                                onClick = {
                                    selectedPipelineId = null
                                    pipelineMenuOpen = false
                                },
                            )
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
                    }
                }
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = enabledLabel, color = tokens.cardForeground)
                    Switch(
                        checked = enabled,
                        onCheckedChange = { enabled = it },
                        modifier = Modifier.semantics { contentDescription = enabledLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSubmit(name, response.takeIf { it.isNotBlank() }, selectedPipelineId, enabled)
                },
                enabled = canSubmit,
            ) {
                Text(
                    text = submitLabel,
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.commands_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
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
                text = stringResource(Res.string.commands_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.commands_retry)) }
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

private data class CommandEditor(
    val isEdit: Boolean,
    val name: String,
    val response: String,
    val isEnabled: Boolean,
    val pipelineId: String? = null,
) {
    companion object {
        fun create(): CommandEditor =
            CommandEditor(isEdit = false, name = "", response = "", isEnabled = true, pipelineId = null)

        fun edit(command: CommandSummary): CommandEditor =
            CommandEditor(
                isEdit = true,
                name = command.name,
                response = command.templateResponse.orEmpty(),
                isEnabled = command.isEnabled,
                pipelineId = command.pipelineId,
            )
    }
}

// Three-way tab: All shows everything, Custom hides built-ins, Builtin hides custom commands.
private enum class CommandTab { All, Custom, Builtin }

@Composable
private fun CommandTab.label(): String =
    stringResource(
        when (this) {
            CommandTab.All -> Res.string.commands_filter_all
            CommandTab.Custom -> Res.string.commands_filter_custom
            CommandTab.Builtin -> Res.string.commands_filter_builtin
        }
    )
