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
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
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
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.TimerDetail
import bot.nomnomz.dashboard.core.network.TimerSummary
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.picklists.ui.PickListInsertMenu
import bot.nomnomz.dashboard.feature.timers.state.TimersController
import bot.nomnomz.dashboard.feature.timers.state.TimersState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_timers
import nomnomzbot.composeapp.generated.resources.timers_badge_once
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
import nomnomzbot.composeapp.generated.resources.timers_dialog_fire_once
import nomnomzbot.composeapp.generated.resources.timers_dialog_fire_once_hint
import nomnomzbot.composeapp.generated.resources.timers_dialog_interval
import nomnomzbot.composeapp.generated.resources.timers_dialog_add_message
import nomnomzbot.composeapp.generated.resources.timers_dialog_message
import nomnomzbot.composeapp.generated.resources.timers_dialog_message_remove
import nomnomzbot.composeapp.generated.resources.timers_dialog_min_chat_activity
import nomnomzbot.composeapp.generated.resources.timers_dialog_min_chat_activity_hint
import nomnomzbot.composeapp.generated.resources.timers_dialog_messages
import nomnomzbot.composeapp.generated.resources.timers_dialog_pipeline
import nomnomzbot.composeapp.generated.resources.timers_dialog_pipeline_none
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
import nomnomzbot.composeapp.generated.resources.timers_retry
import nomnomzbot.composeapp.generated.resources.timers_search_placeholder
import nomnomzbot.composeapp.generated.resources.timers_toggle
import nomnomzbot.composeapp.generated.resources.timers_write_error
import org.jetbrains.compose.resources.stringResource

@Composable
fun TimersScreen(controller: TimersController, role: ManagementRole?) {
    val state: TimersState by controller.state.collectAsStateWithLifecycle()
    val writeError: String? by controller.writeError.collectAsStateWithLifecycle()
    val pipelines: List<PipelineSummary> by controller.pipelines.collectAsStateWithLifecycle()
    val pickListNames: List<String> by controller.pickListNames.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Timers)

    LaunchedEffect(Unit) { controller.load() }

    var editTarget: TimerEditTarget? by remember { mutableStateOf(null) }
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
        // Editing pulls the full timer (its pipeline binding + the whole message rotation) — the list row
        // carries neither. New timers start blank. `detail` is null while loading / for a new timer.
        var editDetail: TimerDetail? by remember(target) { mutableStateOf(null) }
        LaunchedEffect(target) {
            if (target is TimerEditTarget.Edit) editDetail = controller.timerDetail(target.timer.id)
        }
        TimerEditDialog(
            target = target,
            detail = editDetail,
            pipelines = pipelines,
            pickListNames = pickListNames,
            onDismiss = { editTarget = null },
            onConfirm = { name, messages, interval, minChatActivity, enabled, fireOnce, pipelineId ->
                editTarget = null
                scope.launch {
                    when (target) {
                        is TimerEditTarget.New ->
                            controller.createTimer(
                                name,
                                messages,
                                interval,
                                minChatActivity,
                                enabled,
                                fireOnce,
                                pipelineId,
                            )
                        is TimerEditTarget.Edit ->
                            controller.updateTimer(
                                target.timer.id,
                                name,
                                messages,
                                interval,
                                minChatActivity,
                                enabled,
                                fireOnce,
                                pipelineId,
                            )
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
    val typography = LocalTypography.current

    var searchQuery: String by remember { mutableStateOf("") }

    val filteredTimers: List<TimerSummary> = timers.filter { timer ->
        searchQuery.isBlank() || timer.name.contains(searchQuery, ignoreCase = true)
    }

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_timers)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onNew, enabled = enabled) {
                    Text(text = stringResource(Res.string.timers_new))
                }
            }
        }

        // Search bar — filters timers by name.
        AppTextField(
            value = searchQuery,
            onValueChange = { searchQuery = it },
            label = "",
            placeholder = stringResource(Res.string.timers_search_placeholder),
            modifier = Modifier.fillMaxWidth(),
        )

        writeError?.let { detail -> WriteErrorBanner(detail = detail, onDismiss = onDismissError) }

        // Single card wrapping the entire table — rows are separated by dividers.
        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (filteredTimers.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = stringResource(Res.string.timers_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    itemsIndexed(items = filteredTimers, key = { _, timer -> timer.id }) { index, timer ->
                        TimerTableRow(
                            timer = timer,
                            manage = manage,
                            onToggle = { onToggle(timer) },
                            onEdit = { onEdit(timer) },
                            onDelete = { onDelete(timer) },
                        )
                        if (index < filteredTimers.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

// Timer row inside the shared card — no per-row background; dividers separate entries.
@Composable
private fun TimerTableRow(
    timer: TimerSummary,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val intervalText: String = stringResource(Res.string.timers_interval, timer.intervalMinutes)
    val messagesText: String = stringResource(Res.string.timers_message_count, timer.messageCount)
    val statusLabel: String =
        stringResource(if (timer.isEnabled) Res.string.timers_enabled else Res.string.timers_disabled)
    // Announced only for one-shot timers, so the row's a11y node conveys the fire-once state the badge shows.
    val onceLabel: String = if (timer.fireOnce) ", ${stringResource(Res.string.timers_badge_once)}" else ""
    val toggleLabel: String = stringResource(Res.string.timers_toggle, timer.name)
    val editLabel: String = stringResource(Res.string.timers_edit, timer.name)
    val deleteLabel: String = stringResource(Res.string.timers_delete, timer.name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // Name + interval/message-count badges, folded into one a11y node.
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics {
                    contentDescription =
                        "${timer.name}, $intervalText, $messagesText, $statusLabel$onceLabel"
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = timer.name,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                // Interval badge — muted chip showing the repeat cadence.
                Box(
                    modifier = Modifier
                        .clip(RoundedCornerShape(tokens.radius.sm))
                        .background(tokens.muted)
                        .padding(horizontal = spacing.s2, vertical = spacing.s0_5),
                ) {
                    Text(text = intervalText, style = typography.xs, color = tokens.mutedForeground)
                }
                // One-shot marker — a primary-tinted chip so a fire-once timer is distinguishable at a glance.
                if (timer.fireOnce) {
                    Box(
                        modifier = Modifier
                            .clip(RoundedCornerShape(tokens.radius.sm))
                            .background(tokens.primary)
                            .padding(horizontal = spacing.s2, vertical = spacing.s0_5),
                    ) {
                        Text(
                            text = stringResource(Res.string.timers_badge_once),
                            style = typography.xs,
                            color = tokens.primaryForeground,
                        )
                    }
                }
                Text(text = messagesText, style = typography.xs, color = tokens.mutedForeground)
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

@Composable
private fun TimerEditDialog(
    target: TimerEditTarget,
    detail: TimerDetail?,
    pipelines: List<PipelineSummary>,
    pickListNames: List<String>,
    onDismiss: () -> Unit,
    onConfirm: (
        name: String,
        messages: List<String>,
        intervalMinutes: Int,
        minChatActivity: Int,
        enabled: Boolean,
        fireOnce: Boolean,
        pipelineId: String?,
    ) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val existing: TimerSummary? = (target as? TimerEditTarget.Edit)?.timer
    // Seed the form from the loaded detail the moment it arrives (Edit) — keyed on `detail` so the fields
    // re-seed once the full timer (its pipeline + whole message rotation) loads. A New timer / the loading
    // window starts from the summary / defaults.
    var name: String by remember(detail) { mutableStateOf(detail?.name ?: existing?.name ?: "") }
    var messages: List<String> by remember(detail) {
        mutableStateOf(detail?.messages?.takeIf { it.isNotEmpty() } ?: listOf(""))
    }
    var interval: String by remember(detail) {
        mutableStateOf(
            (detail?.intervalMinutes ?: existing?.intervalMinutes ?: DEFAULT_INTERVAL_MINUTES).toString()
        )
    }
    // Anti-spam floor: how many chat messages must pass between fires. Blank = 0 (fire regardless of activity).
    var minChatActivity: String by remember(detail) {
        mutableStateOf(detail?.minChatActivity?.takeIf { it > 0 }?.toString().orEmpty())
    }
    var enabled: Boolean by remember(detail) { mutableStateOf(detail?.isEnabled ?: existing?.isEnabled ?: true) }
    var fireOnce: Boolean by remember(detail) { mutableStateOf(detail?.fireOnce ?: existing?.fireOnce ?: false) }
    var pipelineId: String? by remember(detail) { mutableStateOf(detail?.pipelineId) }
    var pipelineMenuOpen: Boolean by remember { mutableStateOf(false) }

    val intervalMinutes: Int? = interval.toIntOrNull()?.takeIf { it in 1..MAX_INTERVAL_MINUTES }
    // Blank rows are dropped; at least one real message is required.
    val cleanedMessages: List<String> = messages.map { it.trim() }.filter { it.isNotEmpty() }
    val canSubmit: Boolean = name.isNotBlank() && cleanedMessages.isNotEmpty() && intervalMinutes != null

    val isCreate: Boolean = target is TimerEditTarget.New
    val titleRes =
        if (isCreate) Res.string.timers_dialog_create_title else Res.string.timers_dialog_edit_title
    val confirmRes = if (isCreate) Res.string.timers_dialog_create else Res.string.timers_dialog_save
    val pipelineNoneLabel: String = stringResource(Res.string.timers_dialog_pipeline_none)
    val selectedPipelineName: String =
        pipelineId?.let { id -> pipelines.firstOrNull { it.id == id }?.name } ?: pipelineNoneLabel
    val messageLabel: String = stringResource(Res.string.timers_dialog_message)
    val removeLabel: String = stringResource(Res.string.timers_dialog_message_remove)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(titleRes)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.timers_dialog_name),
                )

                // Rotation list — each message fires in turn on successive intervals. Add / remove rows.
                Text(
                    text = stringResource(Res.string.timers_dialog_messages),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                messages.forEachIndexed { index, msg ->
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        AppTextField(
                            value = msg,
                            onValueChange = { updated ->
                                messages = messages.toMutableList().also { it[index] = updated }
                            },
                            modifier = Modifier.weight(1f),
                            label = messageLabel,
                        )
                        if (messages.size > 1) {
                            GlyphButton(
                                imageVector = TrashGlyph,
                                label = removeLabel,
                                onClick = { messages = messages.filterIndexed { i, _ -> i != index } },
                                tint = tokens.destructive,
                            )
                        }
                    }
                }
                TextButton(onClick = { messages = messages + "" }) {
                    Text(
                        text = stringResource(Res.string.timers_dialog_add_message),
                        color = tokens.primary,
                    )
                }
                // Insert a random-response token (`{list.pick.<name>}`) into the last message row — renders only
                // when the channel has random-response lists.
                PickListInsertMenu(
                    names = pickListNames,
                    onInsert = { token ->
                        messages =
                            messages.toMutableList().also { list ->
                                val last: Int = list.lastIndex
                                val current: String = list[last]
                                list[last] = if (current.isBlank()) token else "$current $token"
                            }
                    },
                )

                // Optional pipeline to run every interval (e.g. a shoutout using {timer.message}). Reuses the
                // Commands dialog's picker shape. Only shown when the channel has pipelines to bind.
                if (pipelines.isNotEmpty()) {
                    Box {
                        AppTextField(
                            value = selectedPipelineName,
                            onValueChange = {},
                            modifier = Modifier.fillMaxWidth().clickable { pipelineMenuOpen = true },
                            label = stringResource(Res.string.timers_dialog_pipeline),
                        )
                        DropdownMenu(
                            expanded = pipelineMenuOpen,
                            onDismissRequest = { pipelineMenuOpen = false },
                        ) {
                            DropdownMenuItem(
                                text = { Text(pipelineNoneLabel, color = tokens.mutedForeground) },
                                onClick = {
                                    pipelineId = null
                                    pipelineMenuOpen = false
                                },
                            )
                            pipelines.forEach { pipeline ->
                                DropdownMenuItem(
                                    text = { Text(pipeline.name, color = tokens.cardForeground) },
                                    onClick = {
                                        pipelineId = pipeline.id
                                        pipelineMenuOpen = false
                                    },
                                )
                            }
                        }
                    }
                }

                AppTextField(
                    value = interval,
                    onValueChange = { interval = it.filter { ch -> ch.isDigit() } },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.timers_dialog_interval),
                )
                // Anti-spam guard — the timer only fires once at least this many chat messages have arrived
                // since the last fire. Blank/0 = fire regardless of chat activity. Digits only.
                AppTextField(
                    value = minChatActivity,
                    onValueChange = { minChatActivity = it.filter { ch -> ch.isDigit() } },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    supportingText = stringResource(Res.string.timers_dialog_min_chat_activity_hint),
                    modifier = Modifier.fillMaxWidth(),
                    label = stringResource(Res.string.timers_dialog_min_chat_activity),
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
                    Switch(
                        checked = enabled,
                        onCheckedChange = { enabled = it },
                    )
                }
                // One-shot: fire once at the interval, then the timer disables itself instead of looping.
                Row(
                    modifier = Modifier.fillMaxWidth(),
                    horizontalArrangement = Arrangement.SpaceBetween,
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(
                            text = stringResource(Res.string.timers_dialog_fire_once),
                            color = tokens.cardForeground,
                        )
                        Text(
                            text = stringResource(Res.string.timers_dialog_fire_once_hint),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    }
                    Switch(
                        checked = fireOnce,
                        onCheckedChange = { fireOnce = it },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    intervalMinutes?.let {
                        onConfirm(
                            name.trim(),
                            cleanedMessages,
                            it,
                            minChatActivity.toIntOrNull() ?: 0,
                            enabled,
                            fireOnce,
                            pipelineId,
                        )
                    }
                },
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

// Dismissible inline error banner — shown above the list so the rows the user was looking at stay put.
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
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

private const val DEFAULT_INTERVAL_MINUTES: Int = 30
private const val MAX_INTERVAL_MINUTES: Int = 1440
