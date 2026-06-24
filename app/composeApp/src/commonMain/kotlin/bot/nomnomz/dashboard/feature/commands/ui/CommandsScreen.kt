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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
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
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.feature.commands.state.CommandsController
import bot.nomnomz.dashboard.feature.commands.state.CommandsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.commands_action_error
import nomnomzbot.composeapp.generated.resources.commands_badge_disabled
import nomnomzbot.composeapp.generated.resources.commands_badge_enabled
import nomnomzbot.composeapp.generated.resources.commands_delete_action
import nomnomzbot.composeapp.generated.resources.commands_delete_action_short
import nomnomzbot.composeapp.generated.resources.commands_delete_cancel
import nomnomzbot.composeapp.generated.resources.commands_delete_confirm
import nomnomzbot.composeapp.generated.resources.commands_delete_message
import nomnomzbot.composeapp.generated.resources.commands_delete_title
import nomnomzbot.composeapp.generated.resources.commands_dialog_cancel
import nomnomzbot.composeapp.generated.resources.commands_dialog_create
import nomnomzbot.composeapp.generated.resources.commands_dialog_create_title
import nomnomzbot.composeapp.generated.resources.commands_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_name_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_response_label
import nomnomzbot.composeapp.generated.resources.commands_dialog_save
import nomnomzbot.composeapp.generated.resources.commands_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.commands_edit_action
import nomnomzbot.composeapp.generated.resources.commands_edit_action_short
import nomnomzbot.composeapp.generated.resources.commands_empty
import nomnomzbot.composeapp.generated.resources.commands_error
import nomnomzbot.composeapp.generated.resources.commands_loading
import nomnomzbot.composeapp.generated.resources.commands_new_action
import nomnomzbot.composeapp.generated.resources.commands_no_description
import nomnomzbot.composeapp.generated.resources.commands_retry
import nomnomzbot.composeapp.generated.resources.commands_title
import nomnomzbot.composeapp.generated.resources.commands_toggle_action
import org.jetbrains.compose.resources.stringResource

// The Commands page (frontend-ia.md §3, Chat group): the channel's custom chat commands, all real data from
// [CommandsController]. The screen is a pure projection of the controller's state; it loads on first
// composition. This is the full management surface — create, edit, enable/disable, and delete — each routed
// back through the controller, which re-lists after every successful write so the page reflects the backend.
@Composable
fun CommandsScreen(controller: CommandsController) {
    val state: CommandsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // The create/edit dialog target: null = closed, a value = open (an empty editor = create, a pre-filled one
    // = edit). The delete-confirm target is the command name pending confirmation, or null when none.
    var editor: CommandEditor? by remember { mutableStateOf(null) }
    var pendingDelete: String? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: CommandsState = state) {
            is CommandsState.Loading -> CenteredMessage(stringResource(Res.string.commands_loading))
            is CommandsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is CommandsState.Empty ->
                ManagedContent(
                    commands = emptyList(),
                    actionError = null,
                    onNew = { editor = CommandEditor.create() },
                    onEdit = { command -> editor = CommandEditor.edit(command) },
                    onToggle = { command, enabled ->
                        scope.launch { controller.toggleCommand(command.name, enabled) }
                    },
                    onDelete = { command -> pendingDelete = command.name },
                )
            is CommandsState.Ready ->
                ManagedContent(
                    commands = current.commands,
                    actionError = current.actionError,
                    onNew = { editor = CommandEditor.create() },
                    onEdit = { command -> editor = CommandEditor.edit(command) },
                    onToggle = { command, enabled ->
                        scope.launch { controller.toggleCommand(command.name, enabled) }
                    },
                    onDelete = { command -> pendingDelete = command.name },
                )
        }
    }

    editor?.let { open ->
        CommandFormDialog(
            editor = open,
            onDismiss = { editor = null },
            onSubmit = { name, response, enabled ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updateCommand(name, response, enabled)
                    else controller.createCommand(name, response, enabled)
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

// The list-bearing content: the header with the "+ New command" action, an optional write-failure banner, and
// either the rows or the empty hint. Shared by the Ready and Empty states so a fresh channel can still create
// its first command from the same header.
@Composable
private fun ManagedContent(
    commands: List<CommandSummary>,
    actionError: String?,
    onNew: () -> Unit,
    onEdit: (CommandSummary) -> Unit,
    onToggle: (CommandSummary, Boolean) -> Unit,
    onDelete: (CommandSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Header(onNew = onNew)
        actionError?.let { ActionErrorBanner(detail = it) }

        if (commands.isEmpty()) {
            CenteredMessage(stringResource(Res.string.commands_empty))
        } else {
            CommandList(
                commands = commands,
                onEdit = onEdit,
                onToggle = onToggle,
                onDelete = onDelete,
            )
        }
    }
}

@Composable
private fun Header(onNew: () -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val newLabel: String = stringResource(Res.string.commands_new_action)

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = stringResource(Res.string.commands_title),
            style = typography.xl2,
            color = tokens.foreground,
        )
        Button(
            onClick = onNew,
            colors = ButtonDefaults.buttonColors(
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
private fun ActionErrorBanner(detail: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.commands_action_error, detail),
        style = typography.sm,
        color = tokens.destructiveForeground,
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.destructive)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
    )
}

@Composable
private fun CommandList(
    commands: List<CommandSummary>,
    onEdit: (CommandSummary) -> Unit,
    onToggle: (CommandSummary, Boolean) -> Unit,
    onDelete: (CommandSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        items(items = commands, key = { command -> command.id }) { command ->
            CommandRow(
                command = command,
                onEdit = { onEdit(command) },
                onToggle = { enabled -> onToggle(command, enabled) },
                onDelete = { onDelete(command) },
            )
        }
    }
}

@Composable
private fun CommandRow(
    command: CommandSummary,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val snippet: String =
        command.description?.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.commands_no_description)
    val stateLabel: String =
        stringResource(
            if (command.isEnabled) Res.string.commands_badge_enabled
            else Res.string.commands_badge_disabled
        )
    val toggleLabel: String = stringResource(Res.string.commands_toggle_action, command.name)
    val editLabel: String = stringResource(Res.string.commands_edit_action, command.name)
    val deleteLabel: String = stringResource(Res.string.commands_delete_action, command.name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the text block: "!hello, enabled. <description>".
                .clearAndSetSemantics { contentDescription = "${command.name}, $stateLabel. $snippet" },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = command.name,
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
            checked = command.isEnabled,
            onCheckedChange = onToggle,
            colors = SwitchDefaults.colors(
                checkedThumbColor = tokens.primaryForeground,
                checkedTrackColor = tokens.primary,
                uncheckedThumbColor = tokens.mutedForeground,
                uncheckedTrackColor = tokens.muted,
                uncheckedBorderColor = tokens.border,
            ),
            modifier = Modifier.semantics { contentDescription = toggleLabel },
        )
        TextButton(
            onClick = onEdit,
            modifier = Modifier.semantics { contentDescription = editLabel },
        ) {
            Text(
                text = stringResource(Res.string.commands_edit_action_short),
                color = tokens.primary,
                maxLines = 1,
            )
        }
        TextButton(
            onClick = onDelete,
            modifier = Modifier.semantics { contentDescription = deleteLabel },
        ) {
            Text(
                text = stringResource(Res.string.commands_delete_action_short),
                color = tokens.destructive,
                maxLines = 1,
            )
        }
    }
}

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until both name and response are non-blank, so an empty command can never be
// submitted. On edit the name is read-only — it is the backend's address for the row.
@Composable
private fun CommandFormDialog(
    editor: CommandEditor,
    onDismiss: () -> Unit,
    onSubmit: (name: String, response: String, enabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var name: String by remember { mutableStateOf(editor.name) }
    var response: String by remember { mutableStateOf(editor.response) }
    var enabled: Boolean by remember { mutableStateOf(editor.isEnabled) }

    val canSubmit: Boolean = name.isNotBlank() && response.isNotBlank()
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
                    enabled = !editor.isEdit,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.commands_dialog_name_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = response,
                    onValueChange = { response = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.commands_dialog_response_label)) },
                    colors = fieldColors(),
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.SpaceBetween,
                ) {
                    Text(text = enabledLabel, color = tokens.cardForeground)
                    Switch(
                        checked = enabled,
                        onCheckedChange = { enabled = it },
                        colors = SwitchDefaults.colors(
                            checkedThumbColor = tokens.primaryForeground,
                            checkedTrackColor = tokens.primary,
                            uncheckedThumbColor = tokens.mutedForeground,
                            uncheckedTrackColor = tokens.muted,
                            uncheckedBorderColor = tokens.border,
                        ),
                        modifier = Modifier.semantics { contentDescription = enabledLabel },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onSubmit(name, response, enabled) }, enabled = canSubmit) {
                Text(
                    text = submitLabel,
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.commands_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

// The shared text-field color set: every slot driven by a token so the field reads on-theme in light + dark.
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

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a command opens a
// pre-filled edit form. [isEdit] decides create-vs-update on submit and locks the name field (the backend
// addresses a command by name).
private data class CommandEditor(
    val isEdit: Boolean,
    val name: String,
    val response: String,
    val isEnabled: Boolean,
) {
    companion object {
        fun create(): CommandEditor =
            CommandEditor(isEdit = false, name = "", response = "", isEnabled = true)

        fun edit(command: CommandSummary): CommandEditor =
            CommandEditor(
                isEdit = true,
                name = command.name,
                // The list item carries the description as the editable snippet; the full response text lives on
                // the detail DTO. The edit form seeds the response field from the description so the operator
                // sees the current text, and the update sends it back as the response.
                response = command.description.orEmpty(),
                isEnabled = command.isEnabled,
            )
    }
}
