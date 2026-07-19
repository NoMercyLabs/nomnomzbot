// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.liveops.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.AlertDialog
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PickerRef
import bot.nomnomz.dashboard.core.designsystem.component.SearchPickerField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.LiveOpsScheduleSegment
import bot.nomnomz.dashboard.core.network.LiveOpsScheduleVacation
import bot.nomnomz.dashboard.feature.liveops.state.ScheduleController
import bot.nomnomz.dashboard.feature.liveops.state.ScheduleState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.category_picker_empty
import nomnomzbot.composeapp.generated.resources.category_picker_label
import nomnomzbot.composeapp.generated.resources.category_picker_placeholder
import nomnomzbot.composeapp.generated.resources.moderation_action_error
import nomnomzbot.composeapp.generated.resources.schedule_add
import nomnomzbot.composeapp.generated.resources.schedule_delete
import nomnomzbot.composeapp.generated.resources.schedule_delete_message
import nomnomzbot.composeapp.generated.resources.schedule_delete_title
import nomnomzbot.composeapp.generated.resources.schedule_dialog_cancel
import nomnomzbot.composeapp.generated.resources.schedule_dialog_save
import nomnomzbot.composeapp.generated.resources.schedule_download_ics
import nomnomzbot.composeapp.generated.resources.schedule_duration_label
import nomnomzbot.composeapp.generated.resources.schedule_edit
import nomnomzbot.composeapp.generated.resources.schedule_edit_title
import nomnomzbot.composeapp.generated.resources.schedule_empty
import nomnomzbot.composeapp.generated.resources.schedule_error
import nomnomzbot.composeapp.generated.resources.schedule_loading
import nomnomzbot.composeapp.generated.resources.schedule_new_title
import nomnomzbot.composeapp.generated.resources.schedule_recurring
import nomnomzbot.composeapp.generated.resources.schedule_segment_canceled
import nomnomzbot.composeapp.generated.resources.schedule_segment_recurring
import nomnomzbot.composeapp.generated.resources.schedule_segments_title
import nomnomzbot.composeapp.generated.resources.schedule_start_label
import nomnomzbot.composeapp.generated.resources.schedule_time_hint
import nomnomzbot.composeapp.generated.resources.schedule_timezone_label
import nomnomzbot.composeapp.generated.resources.schedule_title_label
import nomnomzbot.composeapp.generated.resources.schedule_vacation_end
import nomnomzbot.composeapp.generated.resources.schedule_vacation_save
import nomnomzbot.composeapp.generated.resources.schedule_vacation_start
import nomnomzbot.composeapp.generated.resources.schedule_vacation_title
import nomnomzbot.composeapp.generated.resources.shell_nav_schedule
import org.jetbrains.compose.resources.stringResource

// The broadcaster's stream-schedule page (frontend-ia.md — the Stream group): the weekly segments + the vacation
// window, read live from Twitch. Read floor is Moderator (the page); writes (add / edit / cancel / delete a
// segment, set vacation) gate at Editor. Datetimes are ISO-8601 and durations minutes — the wire form Twitch
// expects — entered as text (no native date picker in Compose Multiplatform yet); the backend validates them.
@Composable
fun ScheduleScreen(
    controller: ScheduleController,
    role: ManagementRole?,
) {
    val spacing = LocalSpacing.current
    val scope = rememberCoroutineScope()
    val state: ScheduleState by controller.state.collectAsStateWithLifecycle()

    // Writes gate at Editor; the page itself is visible from Moderator (rememberManageDecision uses the page floor,
    // but schedule writes are an Editor action — the backend re-checks live-ops:schedule:write regardless).
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Schedule)

    // The add/edit dialog target: null = closed, a segment = edit, EMPTY_SEGMENT sentinel = add.
    var editorTarget: LiveOpsScheduleSegment? by remember { mutableStateOf(null) }
    var showAdd: Boolean by remember { mutableStateOf(false) }
    var pendingDelete: LiveOpsScheduleSegment? by remember { mutableStateOf(null) }

    LaunchedEffect(controller) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: ScheduleState = state) {
            is ScheduleState.Loading -> CenteredMessage(stringResource(Res.string.schedule_loading))
            is ScheduleState.Error ->
                CenteredMessage(stringResource(Res.string.schedule_error, current.detail))
            is ScheduleState.Empty ->
                ScheduleContent(
                    segments = emptyList(),
                    vacation = null,
                    actionError = null,
                    manage = manage,
                    onAdd = { showAdd = true },
                    onEdit = { editorTarget = it },
                    onDelete = { pendingDelete = it },
                    onSetVacation = { enabled, start, end, tz ->
                        scope.launch { controller.setVacation(enabled, start, end, tz) }
                    },
                )
            is ScheduleState.Ready ->
                ScheduleContent(
                    segments = current.schedule.segments,
                    vacation = current.schedule.vacation,
                    actionError = current.actionError,
                    manage = manage,
                    onAdd = { showAdd = true },
                    onEdit = { editorTarget = it },
                    onDelete = { pendingDelete = it },
                    onSetVacation = { enabled, start, end, tz ->
                        scope.launch { controller.setVacation(enabled, start, end, tz) }
                    },
                    onDownloadIcs = { scope.launch { controller.downloadIcalendar() } },
                )
        }
    }

    if (showAdd) {
        SegmentDialog(
            existing = null,
            onSearchCategories = controller::searchCategories,
            onDismiss = { showAdd = false },
            onSave = { start, tz, duration, title, category, recurring ->
                showAdd = false
                scope.launch { controller.addSegment(start, tz, duration, recurring, title, category) }
            },
        )
    }

    editorTarget?.let { segment ->
        SegmentDialog(
            existing = segment,
            onSearchCategories = controller::searchCategories,
            onDismiss = { editorTarget = null },
            onSave = { start, tz, duration, title, category, _ ->
                editorTarget = null
                scope.launch { controller.editSegment(segment.id, start, duration, tz, title, category) }
            },
        )
    }

    pendingDelete?.let { segment ->
        ConfirmDialog(
            title = stringResource(Res.string.schedule_delete_title),
            message = stringResource(Res.string.schedule_delete_message, segment.title),
            confirmLabel = stringResource(Res.string.schedule_delete),
            dismissLabel = stringResource(Res.string.schedule_dialog_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteSegment(segment.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

@Composable
private fun ScheduleContent(
    segments: List<LiveOpsScheduleSegment>,
    vacation: LiveOpsScheduleVacation?,
    actionError: String?,
    manage: ManageDecision,
    onAdd: () -> Unit,
    onEdit: (LiveOpsScheduleSegment) -> Unit,
    onDelete: (LiveOpsScheduleSegment) -> Unit,
    onSetVacation: (enabled: Boolean, start: String?, end: String?, timezone: String?) -> Unit,
    onDownloadIcs: (() -> Unit)? = null,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        item(key = "header") {
            PageHeader(title = stringResource(Res.string.shell_nav_schedule))
        }
        actionError?.let { detail ->
            item(key = "error") {
                Text(
                    text = stringResource(Res.string.moderation_action_error, detail),
                    style = typography.sm,
                    color = tokens.destructive,
                )
            }
        }
        item(key = "add") {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                ManageGate(decision = manage) { enabled ->
                    Button(onClick = onAdd, enabled = enabled) {
                        Text(stringResource(Res.string.schedule_add))
                    }
                }
                // A one-time authenticated .ics snapshot download (the endpoint is Bearer-authed, so this is not
                // a live webcal subscription). Shown only when there is a schedule to export.
                onDownloadIcs?.let { download ->
                    TextButton(onClick = download) {
                        Text(stringResource(Res.string.schedule_download_ics), color = tokens.primary)
                    }
                }
            }
        }
        item(key = "vacation") {
            Card(modifier = Modifier.fillMaxWidth()) {
                VacationCard(vacation = vacation, manage = manage, onSetVacation = onSetVacation)
            }
        }
        item(key = "segments-title") {
            Text(
                text = stringResource(Res.string.schedule_segments_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        if (segments.isEmpty()) {
            item(key = "segments-empty") {
                Text(
                    text = stringResource(Res.string.schedule_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
        } else {
            item(key = "segments-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        segments.forEachIndexed { index, segment ->
                            SegmentRow(
                                segment = segment,
                                manage = manage,
                                onEdit = { onEdit(segment) },
                                onDelete = { onDelete(segment) },
                            )
                            if (index < segments.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
    }
}

@Composable
private fun SegmentRow(
    segment: LiveOpsScheduleSegment,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(
            text = segment.title.ifBlank { segment.category?.name ?: segment.id },
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(
            text = "${segment.startTime} → ${segment.endTime}",
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        val tags: String =
            listOfNotNull(
                segment.category?.name?.takeIf { it.isNotBlank() },
                if (segment.isRecurring) stringResource(Res.string.schedule_segment_recurring) else null,
                if (segment.canceledUntil != null) stringResource(Res.string.schedule_segment_canceled) else null,
            ).joinToString(" · ")
        if (tags.isNotBlank()) {
            Text(text = tags, style = typography.xs, color = tokens.mutedForeground, maxLines = 1)
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onEdit, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.schedule_edit),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onDelete, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.schedule_delete),
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// The vacation window: a toggle plus its start/end datetime + timezone. Applying sends the whole settings object.
@Composable
private fun VacationCard(
    vacation: LiveOpsScheduleVacation?,
    manage: ManageDecision,
    onSetVacation: (enabled: Boolean, start: String?, end: String?, timezone: String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var enabled: Boolean by remember(vacation) { mutableStateOf(vacation != null) }
    var start: String by remember(vacation) { mutableStateOf(vacation?.startTime ?: "") }
    var end: String by remember(vacation) { mutableStateOf(vacation?.endTime ?: "") }
    var timezone: String by remember(vacation) { mutableStateOf("") }

    Column(
        modifier = Modifier.fillMaxWidth().padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = stringResource(Res.string.schedule_vacation_title),
                style = typography.base,
                color = tokens.cardForeground,
            )
            ManageGate(decision = manage) { canManage ->
                Switch(checked = enabled, onCheckedChange = { enabled = it }, enabled = canManage)
            }
        }
        if (enabled) {
            AppTextField(
                value = start,
                onValueChange = { start = it },
                label = stringResource(Res.string.schedule_vacation_start),
                supportingText = stringResource(Res.string.schedule_time_hint),
                modifier = Modifier.fillMaxWidth(),
            )
            AppTextField(
                value = end,
                onValueChange = { end = it },
                label = stringResource(Res.string.schedule_vacation_end),
                modifier = Modifier.fillMaxWidth(),
            )
            AppTextField(
                value = timezone,
                onValueChange = { timezone = it },
                label = stringResource(Res.string.schedule_timezone_label),
                modifier = Modifier.fillMaxWidth(),
            )
        }
        ManageGate(decision = manage) { canManage ->
            Button(
                onClick = { onSetVacation(enabled, start, end, timezone) },
                enabled = canManage,
            ) {
                Text(stringResource(Res.string.schedule_vacation_save))
            }
        }
    }
}

// The add / edit segment dialog. All fields are text (ISO-8601 datetime, IANA timezone, minutes) that the backend
// validates. On edit the recurring flag isn't editable via this contract (Update has no isRecurring), so it's
// hidden then; on add it's offered.
@Composable
private fun SegmentDialog(
    existing: LiveOpsScheduleSegment?,
    onSearchCategories: suspend (String) -> List<PickerOption>,
    onDismiss: () -> Unit,
    onSave: (start: String, timezone: String, duration: String, title: String?, categoryId: String?, recurring: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var start: String by remember { mutableStateOf(existing?.startTime ?: "") }
    var timezone: String by remember { mutableStateOf("") }
    var duration: String by remember { mutableStateOf("") }
    var title: String by remember { mutableStateOf(existing?.title ?: "") }
    // The category picker owns a PickerRef selection; the segment WRITE consumes the Twitch category id, so the
    // existing segment's category (id + name) seeds it. onClear reopens the search.
    var selectedCategory: PickerRef? by remember {
        mutableStateOf(existing?.category?.let { PickerRef(it.id, it.name) })
    }
    var recurring: Boolean by remember { mutableStateOf(existing?.isRecurring ?: false) }

    val canSave: Boolean = start.isNotBlank() && timezone.isNotBlank() && duration.isNotBlank()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text =
                    if (existing == null) {
                        stringResource(Res.string.schedule_new_title)
                    } else {
                        stringResource(Res.string.schedule_edit_title)
                    }
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = start,
                    onValueChange = { start = it },
                    label = stringResource(Res.string.schedule_start_label),
                    supportingText = stringResource(Res.string.schedule_time_hint),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = timezone,
                    onValueChange = { timezone = it },
                    label = stringResource(Res.string.schedule_timezone_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = duration,
                    onValueChange = { duration = it.filter { c -> c.isDigit() } },
                    label = stringResource(Res.string.schedule_duration_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = title,
                    onValueChange = { title = it },
                    label = stringResource(Res.string.schedule_title_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                SearchPickerField(
                    search = onSearchCategories,
                    selected = selectedCategory,
                    onSelect = { selectedCategory = it },
                    onClear = { selectedCategory = null },
                    label = stringResource(Res.string.category_picker_label),
                    placeholder = stringResource(Res.string.category_picker_placeholder),
                    emptyText = stringResource(Res.string.category_picker_empty),
                    modifier = Modifier.fillMaxWidth(),
                )
                if (existing == null) {
                    Row(
                        modifier = Modifier.fillMaxWidth(),
                        verticalAlignment = Alignment.CenterVertically,
                        horizontalArrangement = Arrangement.SpaceBetween,
                    ) {
                        Text(stringResource(Res.string.schedule_recurring), color = tokens.cardForeground)
                        Switch(checked = recurring, onCheckedChange = { recurring = it })
                    }
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = { onSave(start.trim(), timezone.trim(), duration.trim(), title.ifBlank { null }, selectedCategory?.id, recurring) },
                enabled = canSave,
            ) {
                Text(
                    text = stringResource(Res.string.schedule_dialog_save),
                    color = if (canSave) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.schedule_dialog_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

@Composable
private fun CenteredMessage(message: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = message, style = typography.base, color = tokens.mutedForeground)
    }
}
