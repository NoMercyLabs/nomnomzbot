// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.codescripts.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.TextButton
import androidx.compose.foundation.layout.size
import bot.nomnomz.dashboard.core.designsystem.icon.EditLineGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import androidx.compose.material3.TextFieldDefaults
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
import androidx.compose.ui.text.font.FontFamily
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CodeScriptDetail
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.feature.codescripts.state.CodeScriptsController
import bot.nomnomz.dashboard.feature.codescripts.state.CodeScriptsState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.scripts_action_error
import nomnomzbot.composeapp.generated.resources.scripts_close_editor
import nomnomzbot.composeapp.generated.resources.scripts_create_confirm
import nomnomzbot.composeapp.generated.resources.scripts_create_description
import nomnomzbot.composeapp.generated.resources.scripts_create_dismiss
import nomnomzbot.composeapp.generated.resources.scripts_create_name
import nomnomzbot.composeapp.generated.resources.scripts_create_name_required
import nomnomzbot.composeapp.generated.resources.scripts_create_source
import nomnomzbot.composeapp.generated.resources.scripts_create_title
import nomnomzbot.composeapp.generated.resources.scripts_delete_cancel
import nomnomzbot.composeapp.generated.resources.scripts_delete_confirm
import nomnomzbot.composeapp.generated.resources.scripts_delete_message
import nomnomzbot.composeapp.generated.resources.scripts_delete_title
import nomnomzbot.composeapp.generated.resources.scripts_editor_publish
import nomnomzbot.composeapp.generated.resources.scripts_editor_save
import nomnomzbot.composeapp.generated.resources.scripts_editor_source_label
import nomnomzbot.composeapp.generated.resources.scripts_empty
import nomnomzbot.composeapp.generated.resources.scripts_error
import nomnomzbot.composeapp.generated.resources.scripts_list_add
import nomnomzbot.composeapp.generated.resources.scripts_loading
import nomnomzbot.composeapp.generated.resources.scripts_retry
import nomnomzbot.composeapp.generated.resources.scripts_status_label
import nomnomzbot.composeapp.generated.resources.scripts_subtitle

import nomnomzbot.composeapp.generated.resources.shell_nav_code_scripts
import nomnomzbot.composeapp.generated.resources.scripts_version_label
import org.jetbrains.compose.resources.stringResource

// The Code Scripts page: a list of versioned Lua scripts on the left (or in the main column) and an inline
// editor when one is open. The editor is a plain multi-line OutlinedTextField — no syntax highlighting
// dependency; the monospace font gives enough visual structure. All backend ops go through
// [CodeScriptsController]; the page reacts to [CodeScriptsState] only.
@Composable
fun CodeScriptsScreen(controller: CodeScriptsController, role: ManagementRole?) {
    val state: CodeScriptsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.CodeScripts)

    var showCreate: Boolean by remember { mutableStateOf(false) }
    var pendingDelete: CodeScriptSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        // Page header is shared between list and editor — always visible.
        when (val current: CodeScriptsState = state) {
            is CodeScriptsState.Editing -> {
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                        Text(text = current.detail.name, style = typography.xl2, color = tokens.foreground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        current.detail.description?.let {
                            Text(text = it, style = typography.sm, color = tokens.mutedForeground, maxLines = 2, overflow = TextOverflow.Ellipsis)
                        }
                    }
                    TextButton(onClick = { controller.close() }) {
                        Text(stringResource(Res.string.scripts_close_editor), color = tokens.primary)
                    }
                }
                current.actionError?.let { detail ->
                    ActionErrorBanner(message = stringResource(Res.string.scripts_action_error, detail))
                }
                EditorContent(
                    detail = current.detail,
                    source = current.editorSource,
                    manage = manage,
                    onSourceChange = { controller.updateEditorSource(it) },
                    onSave = { scope.launch { controller.saveVersion(current.detail.id, current.editorSource, false) } },
                    onPublish = { scope.launch { controller.saveVersion(current.detail.id, current.editorSource, true) } },
                )
            }
            else -> {
                PageHeader(title = stringResource(Res.string.shell_nav_code_scripts), subtitle = stringResource(Res.string.scripts_subtitle)) {
                    ManageGate(manage) {
                        Button(onClick = { showCreate = true }) { Text(stringResource(Res.string.scripts_list_add)) }
                    }
                }

                when (current) {
                    is CodeScriptsState.Loading -> CenteredMessage(stringResource(Res.string.scripts_loading))
                    is CodeScriptsState.Empty -> CenteredMessage(stringResource(Res.string.scripts_empty))
                    is CodeScriptsState.Error ->
                        ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
                    is CodeScriptsState.Ready -> {
                        current.actionError?.let { detail ->
                            ActionErrorBanner(message = stringResource(Res.string.scripts_action_error, detail))
                        }
                        LazyColumn(
                            modifier = Modifier.fillMaxSize(),
                            verticalArrangement = Arrangement.spacedBy(spacing.s3),
                        ) {
                            items(items = current.scripts, key = { it.id }) { script ->
                                ScriptRow(
                                    script = script,
                                    manage = manage,
                                    onOpen = { scope.launch { controller.open(script.id) } },
                                    onToggle = { scope.launch { controller.setEnabled(script.id, !script.isEnabled) } },
                                    onDelete = { pendingDelete = script },
                                )
                            }
                        }
                    }
                    is CodeScriptsState.Editing -> Unit // handled above
                }
            }
        }
    }

    pendingDelete?.let { script ->
        ConfirmDialog(
            title = stringResource(Res.string.scripts_delete_title),
            message = stringResource(Res.string.scripts_delete_message, script.name),
            confirmLabel = stringResource(Res.string.scripts_delete_confirm),
            dismissLabel = stringResource(Res.string.scripts_delete_cancel),
            destructive = true,
            onConfirm = { pendingDelete = null; scope.launch { controller.delete(script.id) } },
            onDismiss = { pendingDelete = null },
        )
    }

    if (showCreate) {
        CreateScriptDialog(
            onConfirm = { name, description, source ->
                showCreate = false
                scope.launch { controller.create(name, description, source) }
            },
            onDismiss = { showCreate = false },
        )
    }
}

@Composable
private fun ScriptRow(
    script: CodeScriptSummary,
    manage: ManageDecision,
    onOpen: () -> Unit,
    onToggle: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = script.name, style = typography.base, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    script.currentVersion?.let {
                        Text(
                            text = stringResource(Res.string.scripts_version_label, it),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                    }
                    Text(
                        text = stringResource(Res.string.scripts_status_label, script.currentValidationStatus),
                        style = typography.xs,
                        color = when (script.currentValidationStatus.lowercase()) {
                            "valid" -> tokens.primary
                            "invalid", "error" -> tokens.destructive
                            else -> tokens.mutedForeground
                        },
                    )
                }
            }
            ManageGate(manage) { enabled ->
                Switch(
                    checked = script.isEnabled,
                    onCheckedChange = { onToggle() },
                    enabled = enabled,
                    colors = SwitchDefaults.colors(
                        checkedThumbColor = tokens.primaryForeground,
                        checkedTrackColor = tokens.primary,
                        uncheckedThumbColor = tokens.mutedForeground,
                        uncheckedTrackColor = tokens.muted,
                        uncheckedBorderColor = tokens.border,
                    ),
                )
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(manage) { enabled ->
                IconButton(onClick = onOpen, enabled = enabled) {
                    Icon(
                        imageVector = EditLineGlyph,
                        contentDescription = stringResource(Res.string.scripts_editor_source_label),
                        tint = if (enabled) tokens.primary else tokens.muted,
                        modifier = Modifier.size(spacing.s4),
                    )
                }
            }
            ManageGate(manage) { enabled ->
                IconButton(onClick = onDelete, enabled = enabled) {
                    Icon(
                        imageVector = TrashGlyph,
                        contentDescription = stringResource(Res.string.scripts_delete_confirm),
                        tint = if (enabled) tokens.destructive else tokens.muted,
                        modifier = Modifier.size(spacing.s4),
                    )
                }
            }
        }
    }
}

@Composable
private fun EditorContent(
    detail: CodeScriptDetail,
    source: String,
    manage: ManageDecision,
    onSourceChange: (String) -> Unit,
    onSave: () -> Unit,
    onPublish: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // The editor — monospace textarea, no external syntax-highlighting dep.
        OutlinedTextField(
            value = source,
            onValueChange = onSourceChange,
            modifier = Modifier.fillMaxWidth().weight(1f),
            label = { Text(stringResource(Res.string.scripts_editor_source_label)) },
            textStyle = typography.base.copy(fontFamily = FontFamily.Monospace),
            colors = TextFieldDefaults.colors(
                focusedContainerColor = tokens.card,
                unfocusedContainerColor = tokens.card,
                focusedTextColor = tokens.cardForeground,
                unfocusedTextColor = tokens.cardForeground,
                focusedLabelColor = tokens.primary,
                unfocusedLabelColor = tokens.mutedForeground,
                focusedIndicatorColor = tokens.primary,
                unfocusedIndicatorColor = tokens.border,
                cursorColor = tokens.primary,
            ),
        )

        // Validation errors from the current version, if any.
        detail.currentVersion?.validationErrors?.takeIf { it.isNotEmpty() }?.let { errors ->
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                errors.take(5).forEach { error ->
                    Text(
                        text = "[${error.line}:${error.column}] ${error.message}",
                        style = typography.xs,
                        color = tokens.destructive,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
            }
        }

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            ManageGate(manage) {
                TextButton(onClick = onSave) {
                    Text(stringResource(Res.string.scripts_editor_save), color = tokens.primary)
                }
            }
            ManageGate(manage) {
                Button(onClick = onPublish) {
                    Text(stringResource(Res.string.scripts_editor_publish))
                }
            }
        }
    }
}

@Composable
private fun CreateScriptDialog(
    onConfirm: (name: String, description: String?, source: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    var description: String by remember { mutableStateOf("") }
    var source: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.scripts_create_title), style = typography.lg, color = tokens.cardForeground) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name, onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.scripts_create_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.scripts_create_name_required) else null,
                )
                AppTextField(
                    value = description, onValueChange = { description = it },
                    label = stringResource(Res.string.scripts_create_description),
                    isError = false, errorText = null,
                )
                OutlinedTextField(
                    value = source,
                    onValueChange = { source = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.scripts_create_source)) },
                    textStyle = typography.sm.copy(fontFamily = FontFamily.Monospace),
                    minLines = 5,
                    colors = TextFieldDefaults.colors(
                        focusedContainerColor = tokens.card,
                        unfocusedContainerColor = tokens.card,
                        focusedTextColor = tokens.cardForeground,
                        unfocusedTextColor = tokens.cardForeground,
                        focusedLabelColor = tokens.primary,
                        unfocusedLabelColor = tokens.mutedForeground,
                        focusedIndicatorColor = tokens.primary,
                        unfocusedIndicatorColor = tokens.border,
                    ),
                )
            }
        },
        confirmButton = {
            Button(onClick = {
                if (name.isBlank()) { nameError = true; return@Button }
                onConfirm(name.trim(), description.trim().takeIf { it.isNotBlank() }, source)
            }) { Text(stringResource(Res.string.scripts_create_confirm)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.scripts_create_dismiss)) } },
        containerColor = tokens.card,
    )
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = stringResource(Res.string.scripts_error, detail), style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            TextButton(onClick = onRetry) { Text(stringResource(Res.string.scripts_retry)) }
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
