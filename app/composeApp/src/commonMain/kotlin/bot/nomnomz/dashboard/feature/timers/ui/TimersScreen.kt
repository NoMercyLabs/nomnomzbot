// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.timers.ui

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
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.layout.size
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
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
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.timers.state.TimersController
import bot.nomnomz.dashboard.feature.timers.state.TimersState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.timers_delete
import nomnomzbot.composeapp.generated.resources.timers_delete_action
import nomnomzbot.composeapp.generated.resources.timers_delete_confirm
import nomnomzbot.composeapp.generated.resources.timers_delete_message
import nomnomzbot.composeapp.generated.resources.timers_delete_title
import nomnomzbot.composeapp.generated.resources.timers_dialog_cancel
import nomnomzbot.composeapp.generated.resources.timers_dialog_create
import nomnomzbot.composeapp.generated.resources.timers_dialog_create_title
import nomnomzbot.composeapp.generated.resources.timers_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.timers_dialog_enabled
import nomnomzbot.composeapp.generated.resources.timers_dialog_interval
import nomnomzbot.composeapp.generated.resources.timers_dialog_message
import nomnomzbot.composeapp.generated.resources.timers_dialog_name
import nomnomzbot.composeapp.generated.resources.timers_dialog_save
import nomnomzbot.composeapp.generated.resources.timers_disabled
import nomnomzbot.composeapp.generated.resources.timers_edit
import nomnomzbot.composeapp.generated.resources.timers_edit_action
import nomnomzbot.composeapp.generated.resources.timers_empty
import nomnomzbot.composeapp.generated.resources.timers_enabled
import nomnomzbot.composeapp.generated.resources.timers_error
import nomnomzbot.composeapp.generated.resources.timers_interval
import nomnomzbot.composeapp.generated.resources.timers_loading
import nomnomzbot.composeapp.generated.resources.timers_message_count
import nomnomzbot.composeapp.generated.resources.timers_new
import nomnomzbot.composeapp.generated.resources.shell_nav_timers
import nomnomzbot.composeapp.generated.resources.timers_retry
import nomnomzbot.composeapp.generated.resources.timers_toggle
import nomnomzbot.composeapp.generated.resources.timers_write_error
import org.jetbrains.compose.resources.stringResource

// The Timers page: the channel's scheduled chat timers — real rows from [TimersController], with the full
// create / edit / toggle / delete management surface. The screen is a pure projection of the controller's
// state; it loads on first composition, offers a retry on failure, and reloads after every successful write.
@Composable
fun TimersScreen(controller: TimersController, role: ManagementRole?) {
    val state: TimersState by controller.state.collectAsStateWithLifecycle()
    val writeError: String? by controller.writeError.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Timers gates every write control at its single Editor manage floor
    // (frontend-ia.md §3). A caller below it sees the list but every new/toggle/edit/delete control disabled
    // with "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Timers)

    LaunchedEffect(Unit) { controller.load() }

    // The open dialog, if any: null = closed, a [TimerEditTarget] = the create/edit form is showing.
    var editTarget: TimerEditTarget? by remember { mutableStateOf(null) }
    // The timer the user asked to delete (the confirm dialog is showing), or null when none is pending.
    var deleteTarget: TimerSummary? by remember { mutableStateOf(null) }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: TimersState = state) {
            is TimersState.Loading -> CenteredMessage(stringResource(Res.string.timers_loading))
            is TimersState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is TimersState.Empty ->
                ManagedContent(
                    timers = emptyList(),
                    writeError = writeError,
                    manage = manage,
                    onNew = { editTarget = TimerEditTarget.New },
                    onToggle = { timer -> scope.launch { controller.toggleTimer(timer.id, !timer.isEnabled) } },
                    onEdit = { timer -> editTarget = TimerEditTarget.Edit(timer) },
                    onDelete = { timer -> deleteTarget = timer },
                    onDismissError = controller::clearWriteError,
                )
            is TimersState.Ready ->
                ManagedContent(
                    timers = current.timers,
                    writeError = writeError,
                    manage = manage,
                    onNew = { editTarget = TimerEditTarget.New },
                    onToggle = { timer -> scope.launch { controller.toggleTimer(timer.id, !timer.isEnabled) } },
                    onEdit = { timer -> editTarget = TimerEditTarget.Edit(timer) },
                    onDelete = { timer -> deleteTarget = timer },
                    onDismissError = controller::clearWriteError,
                )
        }
    }

    editTarget?.let { target ->
        TimerEditDialog(
            target = target,
            onDismiss = { editTarget = null },
            onConfirm = { name, message, interval, enabled ->
                editTarget = null
                scope.launch {
                    when (target) {
                        is TimerEditTarget.New ->
                            controller.createTimer(name, message, interval, enabled)
                        is TimerEditTarget.Edit ->
                            controller.updateTimer(target.timer.id, name, message, interval, enabled)
                    }
                }
            },
        )
    }

    deleteTarget?.let { timer ->
        ConfirmDialog(
            title = stringResource(Res.string.timers_delete_title),
            message = stringResource(Res.string.timers_delete_message, timer.name),
            confirmLabel = stringResource(Res.string.timers_delete_confirm),
            dismissLabel = stringResource(Res.string.timers_dialog_cancel),
            destructive = true,
            onConfirm = {
                deleteTarget = null
                scope.launch { controller.deleteTimer(timer.id) }
            },
            onDismiss = { deleteTarget = null },
        )
    }
}

// Whether the edit dialog is creating a new timer or editing an existing one — carries the row to pre-fill.
private sealed interface TimerEditTarget {
    data object New : TimerEditTarget

    data class Edit(val timer: TimerSummary) : TimerEditTarget
}

@Composable
private fun ManagedContent(
    timers: List<TimerSummary>,
    writeError: String?,
    manage: ManageDecision,
    onNew: () -> Unit,
    onToggle: (TimerSummary) -> Unit,
    onEdit: (TimerSummary) -> Unit,
    onDelete: (TimerSummary) -> Unit,
    onDismissError: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_timers)) {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = onNew,
                    enabled = enabled,
                    colors = ButtonDefaults.buttonColors(
                        disabledContainerColor = tokens.muted,
                        disabledContentColor = tokens.mutedForeground,
                    ),
                ) {
                    Text(text = stringResource(Res.string.timers_new))
                }
            }
        }

        writeError?.let { detail -> WriteErrorBanner(detail = detail, onDismiss = onDismissError) }

        if (timers.isEmpty()) {
            Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                CenteredText(stringResource(Res.string.timers_empty))
            }
        } else {
            LazyColumn(
                modifier = Modifier.fillMaxSize(),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                items(items = timers, key = { it.id }) { timer ->
                    TimerRow(
                        timer = timer,
                        manage = manage,
                        onToggle = { onToggle(timer) },
                        onEdit = { onEdit(timer) },
                        onDelete = { onDelete(timer) },
                    )
                }
            }
        }
    }
}

@Composable
private fun TimerRow(
    timer: TimerSummary,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val interval: String = stringResource(Res.string.timers_interval, timer.intervalMinutes)
    val messages: String = stringResource(Res.string.timers_message_count, timer.messageCount)
    val statusLabel: String =
        stringResource(if (timer.isEnabled) Res.string.timers_enabled else Res.string.timers_disabled)
    val toggleLabel: String = stringResource(Res.string.timers_toggle, timer.name)
    val editLabel: String = stringResource(Res.string.timers_edit, timer.name)
    val deleteLabel: String = stringResource(Res.string.timers_delete, timer.name)

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
                // Fold the read-only facts into one screen-reader node ("Welcome, every 10m, 3 messages, On");
                // the interactive controls keep their own labels beside it.
                .clearAndSetSemantics {
                    contentDescription = "${timer.name}, $interval, $messages, $statusLabel"
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = timer.name,
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(text = interval, style = typography.sm, color = tokens.mutedForeground)
                Text(text = messages, style = typography.sm, color = tokens.mutedForeground)
            }
        }

        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = timer.isEnabled,
                onCheckedChange = { onToggle() },
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            )
        }
        ManageGate(decision = manage) { enabled ->
            IconButton(
                onClick = onEdit,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = editLabel },
            ) {
                Icon(
                    imageVector = EditGlyph,
                    contentDescription = null,
                    tint = if (enabled) tokens.mutedForeground else tokens.muted,
                    modifier = Modifier.size(spacing.s4),
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            IconButton(
                onClick = onDelete,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = deleteLabel },
            ) {
                Icon(
                    imageVector = TrashGlyph,
                    contentDescription = null,
                    tint = if (enabled) tokens.destructive else tokens.muted,
                    modifier = Modifier.size(spacing.s4),
                )
            }
        }
    }
}

// The one reusable create/edit form. New starts blank-and-enabled; Edit pre-fills from the row. The host owns
// open/closed; this only renders when shown and reports the entered fields up on confirm. Create is disabled
// until name + message are non-blank and the interval parses to a positive minute count.
@Composable
private fun TimerEditDialog(
    target: TimerEditTarget,
    onDismiss: () -> Unit,
    onConfirm: (name: String, message: String, intervalMinutes: Int, enabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    val existing: TimerSummary? = (target as? TimerEditTarget.Edit)?.timer
    var name: String by remember { mutableStateOf(existing?.name ?: "") }
    var message: String by remember { mutableStateOf("") }
    var interval: String by remember {
        mutableStateOf(existing?.intervalMinutes?.toString() ?: DEFAULT_INTERVAL_MINUTES.toString())
    }
    var enabled: Boolean by remember { mutableStateOf(existing?.isEnabled ?: true) }

    val intervalMinutes: Int? = interval.toIntOrNull()?.takeIf { it in 1..MAX_INTERVAL_MINUTES }
    val canSubmit: Boolean =
        name.isNotBlank() && message.isNotBlank() && intervalMinutes != null

    val isCreate: Boolean = target is TimerEditTarget.New
    val titleRes =
        if (isCreate) Res.string.timers_dialog_create_title else Res.string.timers_dialog_edit_title
    val confirmRes = if (isCreate) Res.string.timers_dialog_create else Res.string.timers_dialog_save

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = stringResource(titleRes)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                OutlinedTextField(
                    value = name,
                    onValueChange = { name = it },
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.timers_dialog_name)) },
                )
                OutlinedTextField(
                    value = message,
                    onValueChange = { message = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.timers_dialog_message)) },
                )
                OutlinedTextField(
                    value = interval,
                    onValueChange = { interval = it.filter { ch -> ch.isDigit() } },
                    singleLine = true,
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.timers_dialog_interval)) },
                )
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Text(
                        text = stringResource(Res.string.timers_dialog_enabled),
                        color = tokens.cardForeground,
                    )
                    Switch(checked = enabled, onCheckedChange = { enabled = it })
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { intervalMinutes?.let { onConfirm(name, message, it, enabled) } },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(confirmRes),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.timers_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// A dismissible inline banner for a failed write — surfaced above the list so the rows the user was looking at
// stay put (the mutation left the list unchanged).
@Composable
private fun WriteErrorBanner(detail: String, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.md))
            .background(tokens.muted)
            .padding(horizontal = spacing.s3, vertical = spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.timers_write_error, detail),
            style = typography.sm,
            color = tokens.destructive,
            modifier = Modifier.weight(1f),
        )
        TextButton(onClick = onDismiss) {
            Text(text = stringResource(Res.string.timers_dialog_cancel), color = tokens.mutedForeground)
        }
    }
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
                text = stringResource(Res.string.timers_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.timers_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        CenteredText(text)
    }
}

@Composable
private fun CenteredText(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Text(text = text, style = typography.base, color = tokens.mutedForeground)
}

// shadcn's timer defaults (CreateTimerDto): a 30-minute interval, capped at the backend's 1440-minute ceiling.
private const val DEFAULT_INTERVAL_MINUTES: Int = 30
private const val MAX_INTERVAL_MINUTES: Int = 1440
