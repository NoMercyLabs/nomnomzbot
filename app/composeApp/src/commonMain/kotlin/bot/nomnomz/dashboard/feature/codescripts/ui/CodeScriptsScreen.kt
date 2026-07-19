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
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.FileTree
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.ResizableSplit
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.component.Textarea
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CloseGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CodeGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditLineGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CodeScriptDetail
import bot.nomnomz.dashboard.core.network.CodeScriptSummary
import bot.nomnomz.dashboard.core.network.CodeScriptVersion
import bot.nomnomz.dashboard.core.network.ProjectDto
import bot.nomnomz.dashboard.core.network.TestRunResult
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
import nomnomzbot.composeapp.generated.resources.scripts_editor_compiled
import nomnomzbot.composeapp.generated.resources.scripts_editor_edit_code
import nomnomzbot.composeapp.generated.resources.scripts_editor_source_label
import nomnomzbot.composeapp.generated.resources.scripts_empty
import nomnomzbot.composeapp.generated.resources.scripts_error
import nomnomzbot.composeapp.generated.resources.scripts_list_add
import nomnomzbot.composeapp.generated.resources.scripts_loading
import nomnomzbot.composeapp.generated.resources.scripts_retry
import nomnomzbot.composeapp.generated.resources.scripts_status_label
import nomnomzbot.composeapp.generated.resources.scripts_subtitle
import nomnomzbot.composeapp.generated.resources.scripts_testrun_args_label
import nomnomzbot.composeapp.generated.resources.scripts_testrun_chat_empty
import nomnomzbot.composeapp.generated.resources.scripts_testrun_chat_heading
import nomnomzbot.composeapp.generated.resources.scripts_testrun_effects_empty
import nomnomzbot.composeapp.generated.resources.scripts_testrun_effects_heading
import nomnomzbot.composeapp.generated.resources.scripts_testrun_error
import nomnomzbot.composeapp.generated.resources.scripts_testrun_failed
import nomnomzbot.composeapp.generated.resources.scripts_testrun_meta
import nomnomzbot.composeapp.generated.resources.scripts_testrun_ok
import nomnomzbot.composeapp.generated.resources.scripts_testrun_run
import nomnomzbot.composeapp.generated.resources.scripts_testrun_running
import nomnomzbot.composeapp.generated.resources.scripts_testrun_subtitle
import nomnomzbot.composeapp.generated.resources.scripts_testrun_title
import nomnomzbot.composeapp.generated.resources.scripts_testrun_vars_label
import nomnomzbot.composeapp.generated.resources.scripts_version_label
import nomnomzbot.composeapp.generated.resources.scripts_versions_current
import nomnomzbot.composeapp.generated.resources.scripts_versions_empty
import nomnomzbot.composeapp.generated.resources.scripts_versions_publish
import nomnomzbot.composeapp.generated.resources.scripts_versions_rollback_cancel
import nomnomzbot.composeapp.generated.resources.scripts_versions_rollback_confirm
import nomnomzbot.composeapp.generated.resources.scripts_versions_rollback_message
import nomnomzbot.composeapp.generated.resources.scripts_versions_rollback_title
import nomnomzbot.composeapp.generated.resources.scripts_versions_subtitle
import nomnomzbot.composeapp.generated.resources.scripts_versions_title
import nomnomzbot.composeapp.generated.resources.shell_nav_code_scripts
import org.jetbrains.compose.resources.stringResource

// The Code Scripts page: a list of versioned Lua scripts on the left (or in the main column) and an inline
// editor when one is open. The editor is a plain multi-line Textarea — no syntax highlighting
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

    // The inline success message the project editor shows on a clean save — resolved here (a Composable) and
    // threaded into the controller's compile callback (the controller has no access to Compose resources).
    val compiledMessage: String = stringResource(Res.string.scripts_editor_compiled)

    var showCreate: Boolean by remember { mutableStateOf(false) }
    var pendingDelete: CodeScriptSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().padding(spacing.s6),
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
                    GlyphButton(
                        imageVector = CloseGlyph,
                        label = stringResource(Res.string.scripts_close_editor),
                        onClick = { controller.close() },
                    )
                }
                current.actionError?.let { detail ->
                    ActionErrorBanner(message = stringResource(Res.string.scripts_action_error, detail))
                }
                ProjectView(
                    project = current.project,
                    selectedPath = current.selectedPath,
                    detail = current.detail,
                    versions = current.versions,
                    manage = manage,
                    testRunning = current.testRunning,
                    testResult = current.testResult,
                    testError = current.testError,
                    onSelectFile = { controller.selectFile(it) },
                    onEditCode = { scope.launch { controller.editCode(current.detail.id, compiledMessage) } },
                    onTestRun = { variables, args -> scope.launch { controller.testRun(current.detail.id, variables, args) } },
                    onRollback = { versionId -> scope.launch { controller.rollback(current.detail.id, versionId) } },
                )
            }
            else -> {
                PageHeader(title = stringResource(Res.string.shell_nav_code_scripts), subtitle = stringResource(Res.string.scripts_subtitle)) {
                    ManageGate(manage) { enabled ->
                        GlyphButton(
                            imageVector = AddGlyph,
                            label = stringResource(Res.string.scripts_list_add),
                            onClick = { showCreate = true },
                            enabled = enabled,
                        )
                    }
                }
                (current as? CodeScriptsState.Ready)?.actionError?.let { detail ->
                    ActionErrorBanner(message = stringResource(Res.string.scripts_action_error, detail))
                }
                Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
                    when (current) {
                        is CodeScriptsState.Loading -> CenteredMessage(stringResource(Res.string.scripts_loading))
                        is CodeScriptsState.Empty -> CenteredMessage(stringResource(Res.string.scripts_empty))
                        is CodeScriptsState.Error ->
                            ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
                        is CodeScriptsState.Ready ->
                            LazyColumn(modifier = Modifier.fillMaxSize()) {
                                itemsIndexed(current.scripts, key = { _, script -> script.id }) { index, script ->
                                    ScriptRow(
                                        script = script,
                                        manage = manage,
                                        onOpen = { scope.launch { controller.open(script.id) } },
                                        onToggle = { scope.launch { controller.setEnabled(script.id, !script.isEnabled) } },
                                        onDelete = { pendingDelete = script },
                                    )
                                    if (index < current.scripts.lastIndex) {
                                        Separator()
                                    }
                                }
                            }
                        is CodeScriptsState.Editing -> Unit // handled above
                    }
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
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
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
                )
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = EditLineGlyph,
                    label = stringResource(Res.string.scripts_editor_source_label),
                    onClick = onOpen,
                    enabled = enabled,
                    tint = tokens.primary,
                )
            }
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = stringResource(Res.string.scripts_delete_confirm),
                    onClick = onDelete,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
}

// The in-page project view: the script's `src/` tree on the left (design-system FileTree) and a read-only
// preview of the selected file on the right, in a draggable ResizableSplit. Actual editing happens in the shared
// multi-file project editor, launched with "Edit & compile" — which round-trips the whole project to the backend
// (validate + compile + publish).
@Composable
private fun ProjectView(
    project: ProjectDto,
    selectedPath: String,
    detail: CodeScriptDetail,
    versions: List<CodeScriptVersion>,
    manage: ManageDecision,
    testRunning: Boolean,
    testResult: TestRunResult?,
    testError: String?,
    onSelectFile: (String) -> Unit,
    onEditCode: () -> Unit,
    onTestRun: (variables: Map<String, String>, args: List<String>) -> Unit,
    onRollback: (versionId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = CodeGlyph,
                    label = stringResource(Res.string.scripts_editor_edit_code),
                    onClick = onEditCode,
                    enabled = enabled,
                )
            }
        }

        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            ResizableSplit(
                modifier = Modifier.fillMaxSize(),
                left = {
                    FileTree(
                        paths = project.files.keys,
                        selectedPath = selectedPath,
                        onSelect = onSelectFile,
                        modifier = Modifier.fillMaxSize().padding(spacing.s2),
                    )
                },
                right = {
                    // A read-only preview of the selected file — the full editor is where changes are made.
                    Textarea(
                        value = project.files[selectedPath] ?: "",
                        onValueChange = {},
                        label = selectedPath,
                        modifier = Modifier.fillMaxSize().padding(spacing.s3),
                        enabled = false,
                        monospace = true,
                        fillHeight = true,
                    )
                },
            )
        }

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

        VersionHistorySection(
            versions = versions,
            currentVersionId = detail.currentVersionId,
            manage = manage,
            onRollback = onRollback,
        )

        TestRunSection(
            manage = manage,
            running = testRunning,
            result = testResult,
            error = testError,
            onRun = onTestRun,
        )
    }
}

// The append-only version history + rollback list: every past version newest-first, each with its number,
// validation status, and timestamp. The version currently served is badged and its rollback control is inert;
// every other row offers "Publish this version" (a rollback re-publishes it as active), gated behind the page's
// manage floor. This is the safety net for a bad save — a Save & Compile republishes live, and this is the way back.
@Composable
private fun VersionHistorySection(
    versions: List<CodeScriptVersion>,
    currentVersionId: String?,
    manage: ManageDecision,
    onRollback: (versionId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var pendingRollback: CodeScriptVersion? by remember { mutableStateOf(null) }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = stringResource(Res.string.scripts_versions_title), style = typography.lg, color = tokens.cardForeground)
                Text(text = stringResource(Res.string.scripts_versions_subtitle), style = typography.sm, color = tokens.mutedForeground)
            }

            if (versions.isEmpty()) {
                Text(text = stringResource(Res.string.scripts_versions_empty), style = typography.xs, color = tokens.mutedForeground)
            } else {
                versions.forEachIndexed { index, version ->
                    if (index > 0) {
                        Separator()
                    }
                    val isCurrent: Boolean = currentVersionId != null && version.id == currentVersionId
                    val publishLabel: String = stringResource(Res.string.scripts_versions_publish, version.version)
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                    ) {
                        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                                Text(
                                    text = stringResource(Res.string.scripts_version_label, version.version),
                                    style = typography.sm,
                                    color = tokens.cardForeground,
                                )
                                if (isCurrent) {
                                    Text(
                                        text = stringResource(Res.string.scripts_versions_current),
                                        style = typography.xs,
                                        color = tokens.primary,
                                    )
                                }
                            }
                            Text(
                                text = stringResource(Res.string.scripts_status_label, version.validationStatus),
                                style = typography.xs,
                                color = when (version.validationStatus.lowercase()) {
                                    "valid" -> tokens.primary
                                    "invalid", "error" -> tokens.destructive
                                    else -> tokens.mutedForeground
                                },
                            )
                        }
                        if (!isCurrent) {
                            ManageGate(manage) { enabled ->
                                TextButton(onClick = { pendingRollback = version }, enabled = enabled) {
                                    Text(
                                        text = publishLabel,
                                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    pendingRollback?.let { version ->
        ConfirmDialog(
            title = stringResource(Res.string.scripts_versions_rollback_title),
            message = stringResource(Res.string.scripts_versions_rollback_message, version.version),
            confirmLabel = stringResource(Res.string.scripts_versions_rollback_confirm),
            dismissLabel = stringResource(Res.string.scripts_versions_rollback_cancel),
            onConfirm = {
                val target: CodeScriptVersion = version
                pendingRollback = null
                onRollback(target.id)
            },
            onDismiss = { pendingRollback = null },
        )
    }
}

// The dry-run panel: a few sample inputs (variables as key=value lines, space-separated args) + a Run button that
// calls the backend test-run in CAPTURE mode, then shows the captured chat output + captured effects (or the
// failure reason). Nothing the script does here reaches a real surface.
@Composable
private fun TestRunSection(
    manage: ManageDecision,
    running: Boolean,
    result: TestRunResult?,
    error: String?,
    onRun: (variables: Map<String, String>, args: List<String>) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var varsText: String by remember { mutableStateOf("") }
    var argsText: String by remember { mutableStateOf("") }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = stringResource(Res.string.scripts_testrun_title), style = typography.lg, color = tokens.cardForeground)
                Text(text = stringResource(Res.string.scripts_testrun_subtitle), style = typography.sm, color = tokens.mutedForeground)
            }

            Textarea(
                value = varsText,
                onValueChange = { varsText = it },
                label = stringResource(Res.string.scripts_testrun_vars_label),
                modifier = Modifier.fillMaxWidth(),
                monospace = true,
                minLines = 3,
            )
            AppTextField(
                value = argsText,
                onValueChange = { argsText = it },
                label = stringResource(Res.string.scripts_testrun_args_label),
                isError = false,
                errorText = null,
            )

            ManageGate(manage) { enabled ->
                Button(
                    onClick = { onRun(parseVariables(varsText), parseArgs(argsText)) },
                    enabled = enabled && !running,
                ) {
                    Text(
                        if (running) stringResource(Res.string.scripts_testrun_running)
                        else stringResource(Res.string.scripts_testrun_run)
                    )
                }
            }

            error?.let { ActionErrorBanner(message = stringResource(Res.string.scripts_testrun_error, it)) }

            result?.let { TestRunResultView(it) }
        }
    }
}

@Composable
private fun TestRunResultView(result: TestRunResult) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            Text(
                text =
                    if (result.success) stringResource(Res.string.scripts_testrun_ok)
                    else stringResource(Res.string.scripts_testrun_failed),
                style = typography.sm,
                color = if (result.success) tokens.primary else tokens.destructive,
            )
            Text(
                text = stringResource(Res.string.scripts_testrun_meta, result.durationMs, result.hostCallCount),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
        result.error?.takeIf { it.isNotBlank() }?.let {
            Text(text = it, style = typography.xs, color = tokens.destructive)
        }

        Separator()

        Text(text = stringResource(Res.string.scripts_testrun_chat_heading), style = typography.sm, color = tokens.cardForeground)
        if (result.chatOutput.isEmpty()) {
            Text(text = stringResource(Res.string.scripts_testrun_chat_empty), style = typography.xs, color = tokens.mutedForeground)
        } else {
            result.chatOutput.forEach { line ->
                Text(text = line, style = typography.sm, color = tokens.foreground)
            }
        }

        Separator()

        Text(text = stringResource(Res.string.scripts_testrun_effects_heading), style = typography.sm, color = tokens.cardForeground)
        if (result.capturedEffects.isEmpty()) {
            Text(text = stringResource(Res.string.scripts_testrun_effects_empty), style = typography.xs, color = tokens.mutedForeground)
        } else {
            result.capturedEffects.forEach { effect ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                    Text(text = effect.name, style = typography.sm, color = tokens.foreground)
                    if (effect.argsPreview.isNotBlank()) {
                        Text(
                            text = effect.argsPreview,
                            style = typography.xs,
                            color = tokens.mutedForeground,
                            maxLines = 3,
                            overflow = TextOverflow.Ellipsis,
                        )
                    }
                }
            }
        }
    }
}

// Parse the variables textarea (one `key=value` per line) into a map; blank lines and lines without `=` are skipped.
private fun parseVariables(text: String): Map<String, String> =
    text.lineSequence()
        .mapNotNull { line ->
            val trimmed: String = line.trim()
            if (trimmed.isEmpty() || !trimmed.contains('=')) return@mapNotNull null
            val key: String = trimmed.substringBefore('=').trim()
            val value: String = trimmed.substringAfter('=').trim()
            if (key.isEmpty()) null else key to value
        }
        .toMap()

// Parse the args field into positional arguments, split on any run of whitespace.
private fun parseArgs(text: String): List<String> =
    text.trim().split(Regex("\\s+")).filter { it.isNotEmpty() }

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
