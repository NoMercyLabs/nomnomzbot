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
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditLineGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.feature.codescripts.state.CodeScriptsController
import bot.nomnomz.dashboard.feature.codescripts.state.CodeScriptsState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.scripts_editor_compiled
import nomnomzbot.composeapp.generated.resources.scripts_row_type
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
import nomnomzbot.composeapp.generated.resources.scripts_editor_source_label
import nomnomzbot.composeapp.generated.resources.scripts_empty
import nomnomzbot.composeapp.generated.resources.scripts_error
import nomnomzbot.composeapp.generated.resources.scripts_list_add
import nomnomzbot.composeapp.generated.resources.scripts_loading
import nomnomzbot.composeapp.generated.resources.scripts_opening
import nomnomzbot.composeapp.generated.resources.scripts_retry
import nomnomzbot.composeapp.generated.resources.scripts_status_label
import nomnomzbot.composeapp.generated.resources.scripts_subtitle
import nomnomzbot.composeapp.generated.resources.scripts_version_label
import nomnomzbot.composeapp.generated.resources.shell_nav_code_scripts
import org.jetbrains.compose.resources.stringResource
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.consequences.DeleteBlastRadiusDialog
import bot.nomnomz.dashboard.core.consequences.BlastRadiusLoadState

// The Code Scripts page. Opening a script goes STRAIGHT into the real Monaco editor (S-CODE-COLLAPSE) — there is
// no separate read-only detail page to click through first, and no separate "Edit code" step: the file tree,
// version history, and dry-run panel all live INSIDE that editor now (its own side views), not here. This screen
// therefore only ever renders the list (Loading / Empty / Error / Ready) plus a brief placeholder for the moment
// [CodeScriptsController.openAndEdit] is fetching the script before the editor actually mounts. All backend ops
// go through [CodeScriptsController]; this page reacts to [CodeScriptsState] only.
@Composable
fun CodeScriptsScreen(controller: CodeScriptsController, role: ManagementRole?) {
    val state: CodeScriptsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.CodeScripts)

    // The inline success message the shared project editor shows on a clean save — resolved here (a Composable)
    // and threaded into the controller's compile callback (the controller has no access to Compose resources).
    val compiledMessage: String = stringResource(Res.string.scripts_editor_compiled)
    val rowTypeLabel: String = stringResource(Res.string.scripts_row_type)

    var showCreate: Boolean by remember { mutableStateOf(false) }
    var pendingDelete: CodeScriptSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_code_scripts), subtitle = stringResource(Res.string.scripts_subtitle)) {
            ManageGate(manage) { enabled ->
                GlyphButton(
                    icon = AddGlyph,
                    label = stringResource(Res.string.scripts_list_add),
                    onClick = { showCreate = true },
                    enabled = enabled,
                )
            }
        }
        // Write failures announce on the shell-level feedback toast (CodeScriptsController.failWrite).
        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            when (val current: CodeScriptsState = state) {
                is CodeScriptsState.Loading -> CenteredMessage(stringResource(Res.string.scripts_loading))
                is CodeScriptsState.Empty -> CenteredMessage(stringResource(Res.string.scripts_empty))
                is CodeScriptsState.Error ->
                    ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
                // The real work happens in the editor overlay (web) / its own window (desktop) that
                // [CodeScriptsController.openAndEdit] launches — this is only the brief gap before it mounts.
                is CodeScriptsState.Editing -> CenteredMessage(stringResource(Res.string.scripts_opening))
                is CodeScriptsState.Ready ->
                    LazyColumn(modifier = Modifier.fillMaxSize()) {
                        itemsIndexed(current.scripts, key = { _, script -> script.id }) { index, script ->
                            ScriptRow(
                                script = script,
                                manage = manage,
                                rowTypeLabel = rowTypeLabel,
                                onOpen = { displayName ->
                                    scope.launch { controller.openAndEdit(script.id, compiledMessage, displayName) }
                                },
                                onToggle = { scope.launch { controller.setEnabled(script.id, !script.isEnabled) } },
                                onDelete = { pendingDelete = script },
                            )
                            if (index < current.scripts.lastIndex) {
                                Separator()
                            }
                        }
                    }
            }
        }
    }

    pendingDelete?.let { script ->
        val deleteDisplayName: String =
            resolveRowLabel(primary = script.name, typeLabel = rowTypeLabel, discriminatorSource = script.id)
        // Fetched fresh per row (never cached or guessed) — the counted blast radius the confirm MUST show
        // before the destructive save can proceed (S-CONSEQ).
        var blastRadius: BlastRadiusLoadState by remember(script.id) { mutableStateOf(BlastRadiusLoadState.Loading) }
        LaunchedEffect(script.id) {
            blastRadius =
                when (val result: ApiResult<BlastRadiusSummary> = controller.fetchBlastRadius(script.id)) {
                    is ApiResult.Ok -> BlastRadiusLoadState.Loaded(result.value)
                    is ApiResult.Failure -> BlastRadiusLoadState.Failed
                }
        }
        DeleteBlastRadiusDialog(
            title = stringResource(Res.string.scripts_delete_title),
            message = stringResource(Res.string.scripts_delete_message, deleteDisplayName),
            confirmLabel = stringResource(Res.string.scripts_delete_confirm),
            dismissLabel = stringResource(Res.string.scripts_delete_cancel),
            blastRadius = blastRadius,
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
    rowTypeLabel: String,
    onOpen: (displayName: String) -> Unit,
    onToggle: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val rowDisplayName: String =
        resolveRowLabel(primary = script.name, typeLabel = rowTypeLabel, discriminatorSource = script.id)

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = rowDisplayName, style = typography.base, color = tokens.cardForeground)
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
                GlyphButton(
                    icon = EditLineGlyph,
                    label = stringResource(Res.string.scripts_editor_source_label),
                    onClick = { onOpen(rowDisplayName) },
                    enabled = enabled,
                    tint = tokens.primary,
                )
            }
            ManageGate(manage) { enabled ->
                GlyphButton(
                    icon = TrashGlyph,
                    label = stringResource(Res.string.scripts_delete_confirm),
                    onClick = onDelete,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
            ManageGate(manage) { enabled ->
                Switch(
                    checked = script.isEnabled,
                    onCheckedChange = { onToggle() },
                    enabled = enabled,
                )
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
                Textarea(
                    value = source,
                    onValueChange = { source = it },
                    label = stringResource(Res.string.scripts_create_source),
                    modifier = Modifier.fillMaxWidth(),
                    monospace = true,
                    minLines = 5,
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
