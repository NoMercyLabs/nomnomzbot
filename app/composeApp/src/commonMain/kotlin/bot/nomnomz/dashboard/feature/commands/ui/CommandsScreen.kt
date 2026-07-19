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
import androidx.compose.runtime.mutableStateListOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.runtime.snapshots.SnapshotStateList
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
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
import bot.nomnomz.dashboard.feature.picklists.ui.PickListInsertMenu
import bot.nomnomz.dashboard.feature.commands.state.CommandInput
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
import nomnomzbot.composeapp.generated.resources.commands_dialog_alias_add
import nomnomzbot.composeapp.generated.resources.commands_dialog_alias_placeholder
import nomnomzbot.composeapp.generated.resources.commands_dialog_alias_remove
import nomnomzbot.composeapp.generated.resources.commands_dialog_aliases_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_cooldown_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_cooldown_per_user_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_custom_prefix_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_description_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_match_mode_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_match_pattern_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_permission_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_prefix_mode_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_random_mode_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_response_add
import nomnomzbot.composeapp.generated.resources.commands_dialog_response_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_response_remove
import nomnomzbot.composeapp.generated.resources.commands_dialog_responses_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_save
import nomnomzbot.composeapp.generated.resources.commands_dialog_tier_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_user_cooldown_label
import nomnomzbot.composeapp.generated.resources.commands_edit_action
import nomnomzbot.composeapp.generated.resources.commands_empty
import nomnomzbot.composeapp.generated.resources.commands_error
import nomnomzbot.composeapp.generated.resources.commands_filter_all
import nomnomzbot.composeapp.generated.resources.commands_filter_builtin
import nomnomzbot.composeapp.generated.resources.commands_filter_custom
import nomnomzbot.composeapp.generated.resources.commands_loading
import nomnomzbot.composeapp.generated.resources.commands_match_contains
import nomnomzbot.composeapp.generated.resources.commands_match_exact
import nomnomzbot.composeapp.generated.resources.commands_match_regex
import nomnomzbot.composeapp.generated.resources.commands_match_starts_with
import nomnomzbot.composeapp.generated.resources.commands_new_action
import nomnomzbot.composeapp.generated.resources.commands_perm_broadcaster
import nomnomzbot.composeapp.generated.resources.commands_perm_everyone
import nomnomzbot.composeapp.generated.resources.commands_perm_moderator
import nomnomzbot.composeapp.generated.resources.commands_perm_subscriber
import nomnomzbot.composeapp.generated.resources.commands_perm_vip
import nomnomzbot.composeapp.generated.resources.commands_prefix_custom
import nomnomzbot.composeapp.generated.resources.commands_prefix_default
import nomnomzbot.composeapp.generated.resources.commands_prefix_none
import nomnomzbot.composeapp.generated.resources.commands_tier_code
import nomnomzbot.composeapp.generated.resources.commands_tier_code_hint
import nomnomzbot.composeapp.generated.resources.commands_tier_pipeline
import nomnomzbot.composeapp.generated.resources.commands_tier_template
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
        val pickListNames: List<String> = when (val s: CommandsState = state) {
            is CommandsState.Ready -> s.pickListNames
            is CommandsState.Empty -> s.pickListNames
            else -> emptyList()
        }
        CommandFormDialog(
            editor = open,
            pipelines = pipelines,
            pickListNames = pickListNames,
            onDismiss = { editor = null },
            onSubmit = { input ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updateCommand(open.name, input)
                    else controller.createCommand(input)
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

// One composable for both create and edit — the FULL command surface, at parity with the backend command DTO
// (frontend-ia.md §3, Chat group). Name is read-only on edit (the backend addresses a command by name). The
// reaction is driven by the tier picker: a text response (single or a random-picked list), a bound pipeline, or
// a code script (authored elsewhere). Recognition (prefix + match), the permission floor, cooldowns (global +
// optional per-user), aliases, description, and the live flag are all editable. Server-side validation still
// re-checks on submit; a bad value surfaces as the page's action-error banner.
@Composable
private fun CommandFormDialog(
    editor: CommandEditor,
    pipelines: List<PipelineSummary>,
    pickListNames: List<String>,
    onDismiss: () -> Unit,
    onSubmit: (CommandInput) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    // Pre-fill a sensible default template when creating a fresh single-response command (owner ask: no empty
    // template inputs); an edit keeps the stored response verbatim.
    val defaultTemplate: String = stringResource(Res.string.commands_default_template)
    var name: String by remember { mutableStateOf(editor.name) }
    var description: String by remember { mutableStateOf(editor.description) }
    var tier: String by remember { mutableStateOf(editor.tier) }
    var minLevel: Int by remember { mutableStateOf(editor.minPermissionLevel) }
    var prefixMode: String by remember { mutableStateOf(editor.prefixMode) }
    var customPrefix: String by remember { mutableStateOf(editor.customPrefix) }
    var matchMode: String by remember { mutableStateOf(editor.matchMode) }
    var matchPattern: String by remember { mutableStateOf(editor.matchPattern) }
    var randomMode: Boolean by remember { mutableStateOf(editor.templateResponses.isNotEmpty()) }
    var response: String by remember {
        mutableStateOf(if (!editor.isEdit && editor.response.isBlank()) defaultTemplate else editor.response)
    }
    val responses: SnapshotStateList<String> =
        remember { mutableStateListOf<String>().apply { addAll(editor.templateResponses) } }
    val aliases: SnapshotStateList<String> =
        remember { mutableStateListOf<String>().apply { addAll(editor.aliases) } }
    var selectedPipelineId: String? by remember { mutableStateOf(editor.pipelineId) }
    var cooldown: String by remember { mutableStateOf(editor.cooldownSeconds.toString()) }
    var cooldownPerUser: Boolean by remember { mutableStateOf(editor.cooldownPerUser) }
    var userCooldown: String by remember { mutableStateOf(editor.userCooldownSeconds.toString()) }
    var enabled: Boolean by remember { mutableStateOf(editor.isEnabled) }

    var tierMenuOpen: Boolean by remember { mutableStateOf(false) }
    var permMenuOpen: Boolean by remember { mutableStateOf(false) }
    var prefixMenuOpen: Boolean by remember { mutableStateOf(false) }
    var matchMenuOpen: Boolean by remember { mutableStateOf(false) }
    var pipelineMenuOpen: Boolean by remember { mutableStateOf(false) }

    val cooldownValue: Int? = cooldown.ifBlank { "0" }.toIntOrNull()
    val userCooldownValue: Int? = userCooldown.ifBlank { "0" }.toIntOrNull()
    val cooldownValid: Boolean = cooldownValue != null && cooldownValue >= 0
    val userCooldownValid: Boolean = userCooldownValue != null && userCooldownValue >= 0
    val patternValid: Boolean = matchMode != "Regex" || matchPattern.isNotBlank()
    val prefixValid: Boolean = prefixMode != "Custom" || customPrefix.isNotBlank()
    val hasReaction: Boolean =
        when (tier) {
            "pipeline" -> selectedPipelineId != null
            "code" -> true
            else -> if (randomMode) responses.any { it.isNotBlank() } else response.isNotBlank()
        }
    val canSubmit: Boolean =
        name.isNotBlank() &&
            hasReaction &&
            cooldownValid &&
            userCooldownValid &&
            patternValid &&
            prefixValid

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
    val randomModeLabel: String = stringResource(Res.string.commands_dialog_random_mode_label)
    val perUserLabel: String = stringResource(Res.string.commands_dialog_cooldown_per_user_label)
    val pipelineNoneLabel: String = stringResource(Res.string.commands_dialog_pipeline_none)
    val selectedPipelineName: String =
        selectedPipelineId?.let { id -> pipelines.firstOrNull { it.id == id }?.name } ?: pipelineNoneLabel

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(
                modifier = Modifier.heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    enabled = !editor.isEdit,
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.commands_dialog_name_label),
                )
                AppTextField(
                    value = description,
                    onValueChange = { description = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.commands_dialog_description_label),
                )

                // Command type — the authoring tier. Drives which reaction editor shows below.
                PickerField(
                    label = stringResource(Res.string.commands_dialog_tier_label),
                    value = tierLabel(tier),
                    expanded = tierMenuOpen,
                    onExpandedChange = { tierMenuOpen = it },
                ) {
                    Tiers.forEach { key ->
                        DropdownMenuItem(
                            text = { Text(tierLabel(key), color = tokens.cardForeground) },
                            onClick = {
                                tier = key
                                tierMenuOpen = false
                            },
                        )
                    }
                }

                // Reaction editor per tier.
                when (tier) {
                    "pipeline" ->
                        PickerField(
                            label = stringResource(Res.string.commands_dialog_pipeline_label),
                            value = selectedPipelineName,
                            expanded = pipelineMenuOpen,
                            onExpandedChange = { pipelineMenuOpen = it },
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
                    "code" ->
                        Text(
                            text = stringResource(Res.string.commands_tier_code_hint),
                            color = tokens.mutedForeground,
                        )
                    else -> {
                        // Single response ↔ random-response list.
                        SwitchRow(
                            label = randomModeLabel,
                            checked = randomMode,
                            onCheckedChange = { randomMode = it },
                        )
                        if (randomMode) {
                            ListEditor(
                                title = stringResource(Res.string.commands_dialog_responses_label),
                                values = responses,
                                addLabel = stringResource(Res.string.commands_dialog_response_add),
                                removeLabelFor = { index ->
                                    stringResource(Res.string.commands_dialog_response_remove, "${index + 1}")
                                },
                            )
                        } else {
                            AppTextField(
                                value = response,
                                onValueChange = { response = it },
                                modifier = Modifier.fillMaxWidth(),
                                label = stringResource(Res.string.commands_dialog_response_label),
                            )
                            // Insert a `{list.pick.<name>}` token — renders only when the channel has such lists.
                            PickListInsertMenu(names = pickListNames, onInsert = { response += it })
                        }
                    }
                }

                // Minimum role that can use it — role NAMES only, never the numeric ladder value (house rule).
                PickerField(
                    label = stringResource(Res.string.commands_dialog_permission_label),
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

                // Prefix mode (+ custom prefix when Custom).
                PickerField(
                    label = stringResource(Res.string.commands_dialog_prefix_mode_label),
                    value = prefixModeLabel(prefixMode),
                    expanded = prefixMenuOpen,
                    onExpandedChange = { prefixMenuOpen = it },
                ) {
                    PrefixModes.forEach { key ->
                        DropdownMenuItem(
                            text = { Text(prefixModeLabel(key), color = tokens.cardForeground) },
                            onClick = {
                                prefixMode = key
                                prefixMenuOpen = false
                            },
                        )
                    }
                }
                if (prefixMode == "Custom") {
                    AppTextField(
                        value = customPrefix,
                        onValueChange = { customPrefix = it },
                        isError = !prefixValid,
                        modifier = Modifier.fillMaxWidth(),
                        label = stringResource(Res.string.commands_dialog_custom_prefix_label),
                    )
                }

                // Match mode (+ regex pattern when Regex).
                PickerField(
                    label = stringResource(Res.string.commands_dialog_match_mode_label),
                    value = matchModeLabel(matchMode),
                    expanded = matchMenuOpen,
                    onExpandedChange = { matchMenuOpen = it },
                ) {
                    MatchModes.forEach { key ->
                        DropdownMenuItem(
                            text = { Text(matchModeLabel(key), color = tokens.cardForeground) },
                            onClick = {
                                matchMode = key
                                matchMenuOpen = false
                            },
                        )
                    }
                }
                if (matchMode == "Regex") {
                    AppTextField(
                        value = matchPattern,
                        onValueChange = { matchPattern = it },
                        isError = !patternValid,
                        modifier = Modifier.fillMaxWidth(),
                        label = stringResource(Res.string.commands_dialog_match_pattern_label),
                    )
                }

                // Aliases — alternate trigger names.
                ListEditor(
                    title = stringResource(Res.string.commands_dialog_aliases_label),
                    values = aliases,
                    addLabel = stringResource(Res.string.commands_dialog_alias_add),
                    placeholder = stringResource(Res.string.commands_dialog_alias_placeholder),
                    removeLabelFor = { index ->
                        stringResource(Res.string.commands_dialog_alias_remove, "${index + 1}")
                    },
                )

                // Cooldowns — global, plus an optional separate per-user window.
                AppTextField(
                    value = cooldown,
                    onValueChange = { input -> cooldown = input.filter { it.isDigit() } },
                    isError = !cooldownValid,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.commands_dialog_cooldown_label),
                )
                SwitchRow(
                    label = perUserLabel,
                    checked = cooldownPerUser,
                    onCheckedChange = { cooldownPerUser = it },
                )
                if (cooldownPerUser) {
                    AppTextField(
                        value = userCooldown,
                        onValueChange = { input -> userCooldown = input.filter { it.isDigit() } },
                        isError = !userCooldownValid,
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        modifier = Modifier.fillMaxWidth(),
                        label = stringResource(Res.string.commands_dialog_user_cooldown_label),
                    )
                }

                SwitchRow(
                    label = enabledLabel,
                    checked = enabled,
                    onCheckedChange = { enabled = it },
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSubmit(
                        CommandInput(
                            name = name,
                            tier = tier,
                            minPermissionLevel = minLevel,
                            prefixMode = prefixMode,
                            customPrefix = if (prefixMode == "Custom") customPrefix else null,
                            matchMode = matchMode,
                            matchPattern = if (matchMode == "Regex") matchPattern else null,
                            templateResponse =
                                if (tier == "template" && !randomMode) response else null,
                            templateResponses =
                                if (tier == "template" && randomMode) responses.toList() else emptyList(),
                            pipelineId = if (tier == "pipeline") selectedPipelineId else null,
                            cooldownSeconds = cooldownValue ?: 0,
                            userCooldownSeconds = userCooldownValue ?: 0,
                            cooldownPerUser = cooldownPerUser,
                            description = description,
                            aliases = aliases.toList(),
                            isEnabled = enabled,
                        )
                    )
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

// A labelled left / Switch right row — the shared toggle layout used by every boolean in the dialog.
@Composable
private fun SwitchRow(label: String, checked: Boolean, onCheckedChange: (Boolean) -> Unit) {
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

// A repeatable list-of-strings editor (aliases, random responses): one text field per row with a trash button,
// and an "+ Add" affordance that appends a blank row. Mutates the passed-in [SnapshotStateList] in place.
@Composable
private fun ListEditor(
    title: String,
    values: SnapshotStateList<String>,
    addLabel: String,
    removeLabelFor: @Composable (Int) -> String,
    placeholder: String? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = title, style = typography.sm, color = tokens.mutedForeground)
        values.forEachIndexed { index, value ->
            val removeLabel: String = removeLabelFor(index)
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                AppTextField(
                    value = value,
                    onValueChange = { values[index] = it },
                    modifier = Modifier.weight(1f),
                    label = "",
                    placeholder = placeholder,
                )
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = removeLabel,
                    onClick = { values.removeAt(index) },
                    tint = tokens.destructive,
                )
            }
        }
        TextButton(onClick = { values.add("") }) {
            Text(text = addLabel, color = tokens.primary)
        }
    }
}

// A read-only field that opens a themed dropdown when clicked (the shared select pattern — an AppTextField shows
// the current value, its Box anchors the DropdownMenu). Keeps the pickers consistent without repeating plumbing.
@Composable
private fun PickerField(
    label: String,
    value: String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    items: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    AppSelectField(
        label = label,
        value = value,
        expanded = expanded,
        onExpandedChange = onExpandedChange,
        modifier = Modifier.fillMaxWidth(),
        menu = items,
    )
}

@Composable
private fun tierLabel(tier: String): String =
    stringResource(
        when (tier) {
            "pipeline" -> Res.string.commands_tier_pipeline
            "code" -> Res.string.commands_tier_code
            else -> Res.string.commands_tier_template
        }
    )

@Composable
private fun prefixModeLabel(mode: String): String =
    stringResource(
        when (mode) {
            "Custom" -> Res.string.commands_prefix_custom
            "None" -> Res.string.commands_prefix_none
            else -> Res.string.commands_prefix_default
        }
    )

@Composable
private fun matchModeLabel(mode: String): String =
    stringResource(
        when (mode) {
            "Exact" -> Res.string.commands_match_exact
            "Contains" -> Res.string.commands_match_contains
            "Regex" -> Res.string.commands_match_regex
            else -> Res.string.commands_match_starts_with
        }
    )

@Composable
private fun permissionLabel(level: Int): String =
    stringResource(PermissionRungs.lastOrNull { level >= it.first }?.second ?: Res.string.commands_perm_everyone)

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

// The create/edit dialog's seed: an empty editor opens a blank create form (with backend defaults); one seeded
// from a command opens a pre-filled edit form carrying every field. [isEdit] decides create-vs-update on submit;
// the name addresses the row the update targets (read-only on edit).
private data class CommandEditor(
    val isEdit: Boolean,
    val name: String,
    val tier: String,
    val minPermissionLevel: Int,
    val prefixMode: String,
    val customPrefix: String,
    val matchMode: String,
    val matchPattern: String,
    val response: String,
    val templateResponses: List<String>,
    val pipelineId: String?,
    val cooldownSeconds: Int,
    val userCooldownSeconds: Int,
    val cooldownPerUser: Boolean,
    val description: String,
    val aliases: List<String>,
    val isEnabled: Boolean,
) {
    companion object {
        fun create(): CommandEditor =
            CommandEditor(
                isEdit = false,
                name = "",
                tier = "template",
                minPermissionLevel = 0,
                prefixMode = "Default",
                customPrefix = "",
                matchMode = "StartsWith",
                matchPattern = "",
                response = "",
                templateResponses = emptyList(),
                pipelineId = null,
                cooldownSeconds = 0,
                userCooldownSeconds = 0,
                cooldownPerUser = false,
                description = "",
                aliases = emptyList(),
                isEnabled = true,
            )

        fun edit(command: CommandSummary): CommandEditor =
            CommandEditor(
                isEdit = true,
                name = command.name,
                tier = command.tier.ifBlank { "template" },
                minPermissionLevel = command.minPermissionLevel,
                prefixMode = command.prefixMode.ifBlank { "Default" },
                customPrefix = command.customPrefix.orEmpty(),
                matchMode = command.matchMode.ifBlank { "StartsWith" },
                matchPattern = command.matchPattern.orEmpty(),
                response = command.templateResponse.orEmpty(),
                templateResponses = command.templateResponses.orEmpty(),
                pipelineId = command.pipelineId,
                cooldownSeconds = command.cooldownSeconds,
                userCooldownSeconds = command.userCooldownSeconds,
                cooldownPerUser = command.cooldownPerUser,
                description = command.description.orEmpty(),
                aliases = command.aliases,
                isEnabled = command.isEnabled,
            )
    }
}

// The tier keys the picker offers (template is the default). Code is selectable but authored in Code Scripts.
private val Tiers: List<String> = listOf("template", "pipeline", "code")

// The prefix-mode keys (Default is the channel prefix).
private val PrefixModes: List<String> = listOf("Default", "Custom", "None")

// The match-mode keys, in the backend's canonical order (StartsWith is the default).
private val MatchModes: List<String> = listOf("StartsWith", "Exact", "Contains", "Regex")

// The permission rungs the picker offers as ROLE NAMES mapped to their unified-ladder value (roles-permissions
// §0). Ascending so [permissionLabel] resolves a stored level to the highest rung it clears.
private val PermissionRungs: List<Pair<Int, org.jetbrains.compose.resources.StringResource>> =
    listOf(
        0 to Res.string.commands_perm_everyone,
        2 to Res.string.commands_perm_subscriber,
        4 to Res.string.commands_perm_vip,
        10 to Res.string.commands_perm_moderator,
        40 to Res.string.commands_perm_broadcaster,
    )

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
