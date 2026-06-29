// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.alerts.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
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
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.network.AlertSummary
import bot.nomnomz.dashboard.feature.alerts.state.AlertsController
import bot.nomnomz.dashboard.feature.alerts.state.AlertsState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.alerts_action_error
import nomnomzbot.composeapp.generated.resources.alerts_badge_disabled
import nomnomzbot.composeapp.generated.resources.alerts_badge_enabled
import nomnomzbot.composeapp.generated.resources.alerts_delete_action
import nomnomzbot.composeapp.generated.resources.alerts_delete_cancel
import nomnomzbot.composeapp.generated.resources.alerts_delete_confirm
import nomnomzbot.composeapp.generated.resources.alerts_delete_message
import nomnomzbot.composeapp.generated.resources.alerts_delete_title
import nomnomzbot.composeapp.generated.resources.alerts_dialog_cancel
import nomnomzbot.composeapp.generated.resources.alerts_dialog_create
import nomnomzbot.composeapp.generated.resources.alerts_dialog_create_title
import nomnomzbot.composeapp.generated.resources.alerts_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.alerts_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.alerts_dialog_event_label
import nomnomzbot.composeapp.generated.resources.alerts_dialog_message_label
import nomnomzbot.composeapp.generated.resources.alerts_dialog_save
import nomnomzbot.composeapp.generated.resources.alerts_edit_action
import nomnomzbot.composeapp.generated.resources.alerts_empty
import nomnomzbot.composeapp.generated.resources.alerts_error
import nomnomzbot.composeapp.generated.resources.alerts_loading
import nomnomzbot.composeapp.generated.resources.alerts_new_action
import nomnomzbot.composeapp.generated.resources.alerts_no_message
import nomnomzbot.composeapp.generated.resources.alerts_retry
import nomnomzbot.composeapp.generated.resources.alerts_title
import nomnomzbot.composeapp.generated.resources.alerts_toggle_action
import org.jetbrains.compose.resources.stringResource

// The Alerts page (frontend-ia.md, Community group): the channel's event responses — what the bot says when a
// follow / sub / raid / cheer fires — all real data from [AlertsController]. The screen is a pure projection
// of the controller's state; it loads on first composition. This is the full management surface — create,
// edit, enable/disable, and delete — each routed back through the controller, which re-lists after every
// successful write so the page reflects the backend.
@Composable
fun AlertsScreen(controller: AlertsController, role: ManagementRole?) {
    val state: AlertsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the page: Alerts gates every write control at its single Editor manage floor
    // (frontend-ia.md §3 Stream row). Below it the list stays readable but create/edit/toggle/delete disable
    // with "Requires Editor" (§7); the backend re-checks every write.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Alerts)

    // The create/edit dialog target: null = closed, a value = open (an empty editor = create, a pre-filled one
    // = edit). The delete-confirm target is the event type pending confirmation, or null when none.
    var editor: AlertEditor? by remember { mutableStateOf(null) }
    var pendingDelete: String? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: AlertsState = state) {
            is AlertsState.Loading -> CenteredMessage(stringResource(Res.string.alerts_loading))
            is AlertsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is AlertsState.Empty ->
                ManagedContent(
                    alerts = emptyList(),
                    actionError = null,
                    manage = manage,
                    onNew = { editor = AlertEditor.create() },
                    onEdit = { alert ->
                        // The message lives on the detail, not the list item — fetch it, then open a pre-filled
                        // editor. A failed fetch leaves the page put with the error surfaced (detail returns null).
                        scope.launch {
                            controller.detail(alert.eventType)?.let { found ->
                                editor =
                                    AlertEditor.edit(
                                        eventType = found.eventType,
                                        message = found.message.orEmpty(),
                                        isEnabled = found.isEnabled,
                                    )
                            }
                        }
                    },
                    onToggle = { alert, enabled ->
                        scope.launch { controller.toggleAlert(alert.eventType, enabled) }
                    },
                    onDelete = { alert -> pendingDelete = alert.eventType },
                )
            is AlertsState.Ready ->
                ManagedContent(
                    alerts = current.alerts,
                    actionError = current.actionError,
                    manage = manage,
                    onNew = { editor = AlertEditor.create() },
                    onEdit = { alert ->
                        scope.launch {
                            controller.detail(alert.eventType)?.let { found ->
                                editor =
                                    AlertEditor.edit(
                                        eventType = found.eventType,
                                        message = found.message.orEmpty(),
                                        isEnabled = found.isEnabled,
                                    )
                            }
                        }
                    },
                    onToggle = { alert, enabled ->
                        scope.launch { controller.toggleAlert(alert.eventType, enabled) }
                    },
                    onDelete = { alert -> pendingDelete = alert.eventType },
                )
        }
    }

    editor?.let { open ->
        AlertFormDialog(
            editor = open,
            onDismiss = { editor = null },
            onSubmit = { eventType, message, enabled ->
                editor = null
                scope.launch {
                    if (open.isEdit) controller.updateAlert(eventType, message, enabled)
                    else controller.createAlert(eventType, message, enabled)
                }
            },
        )
    }

    pendingDelete?.let { eventType ->
        ConfirmDialog(
            title = stringResource(Res.string.alerts_delete_title),
            message = stringResource(Res.string.alerts_delete_message, eventType),
            confirmLabel = stringResource(Res.string.alerts_delete_confirm),
            dismissLabel = stringResource(Res.string.alerts_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteAlert(eventType) }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// The list-bearing content: the header with the "+ New alert" action, an optional write-failure banner, and
// either the rows or the empty hint. Shared by the Ready and Empty states so a fresh channel can still create
// its first alert from the same header.
@Composable
private fun ManagedContent(
    alerts: List<AlertSummary>,
    actionError: String?,
    manage: ManageDecision,
    onNew: () -> Unit,
    onEdit: (AlertSummary) -> Unit,
    onToggle: (AlertSummary, Boolean) -> Unit,
    onDelete: (AlertSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Header(manage = manage, onNew = onNew)
        actionError?.let { ActionErrorBanner(message = stringResource(Res.string.alerts_action_error, it)) }

        if (alerts.isEmpty()) {
            CenteredMessage(stringResource(Res.string.alerts_empty))
        } else {
            AlertList(
                alerts = alerts,
                manage = manage,
                onEdit = onEdit,
                onToggle = onToggle,
                onDelete = onDelete,
            )
        }
    }
}

@Composable
private fun Header(manage: ManageDecision, onNew: () -> Unit) {
    val tokens = LocalTokens.current
    val newLabel: String = stringResource(Res.string.alerts_new_action)

    PageHeader(title = stringResource(Res.string.alerts_title)) {
        ManageGate(decision = manage) { enabled ->
            Button(
                onClick = onNew,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = newLabel },
            ) {
                Text(text = newLabel)
            }
        }
    }
}

@Composable
private fun AlertList(
    alerts: List<AlertSummary>,
    manage: ManageDecision,
    onEdit: (AlertSummary) -> Unit,
    onToggle: (AlertSummary, Boolean) -> Unit,
    onDelete: (AlertSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        items(items = alerts, key = { alert -> alert.id }) { alert ->
            AlertRow(
                alert = alert,
                manage = manage,
                onEdit = { onEdit(alert) },
                onToggle = { enabled -> onToggle(alert, enabled) },
                onDelete = { onDelete(alert) },
            )
        }
    }
}

@Composable
private fun AlertRow(
    alert: AlertSummary,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The list item carries the response type as the secondary line (the message body is only on the detail),
    // so the row shows "how the bot responds" — falling back to a hint when the type is blank.
    val snippet: String =
        alert.responseType.takeIf { it.isNotBlank() }
            ?: stringResource(Res.string.alerts_no_message)
    val stateLabel: String =
        stringResource(
            if (alert.isEnabled) Res.string.alerts_badge_enabled else Res.string.alerts_badge_disabled
        )
    val toggleLabel: String = stringResource(Res.string.alerts_toggle_action, alert.eventType)
    val editLabel: String = stringResource(Res.string.alerts_edit_action, alert.eventType)
    val deleteLabel: String = stringResource(Res.string.alerts_delete_action, alert.eventType)

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
                // One node for the text block: "channel.follow, enabled. chat_message".
                .clearAndSetSemantics {
                    contentDescription = "${alert.eventType}, $stateLabel. $snippet"
                },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = alert.eventType,
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

        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = alert.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                colors = SwitchDefaults.colors(
                    checkedThumbColor = tokens.primaryForeground,
                    checkedTrackColor = tokens.primary,
                    uncheckedThumbColor = tokens.mutedForeground,
                    uncheckedTrackColor = tokens.muted,
                    uncheckedBorderColor = tokens.border,
                ),
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

// One composable for both create and edit (DRY): an empty [editor] = create, a pre-filled one = edit. The
// affirmative button is disabled until both the event type and message are non-blank, so an empty alert can
// never be submitted. On edit the event type is read-only — it is the backend's address for the row.
@Composable
private fun AlertFormDialog(
    editor: AlertEditor,
    onDismiss: () -> Unit,
    onSubmit: (eventType: String, message: String, enabled: Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    var eventType: String by remember { mutableStateOf(editor.eventType) }
    var message: String by remember { mutableStateOf(editor.message) }
    var enabled: Boolean by remember { mutableStateOf(editor.isEnabled) }

    val canSubmit: Boolean = eventType.isNotBlank() && message.isNotBlank()
    val title: String =
        stringResource(
            if (editor.isEdit) Res.string.alerts_dialog_edit_title
            else Res.string.alerts_dialog_create_title
        )
    val submitLabel: String =
        stringResource(
            if (editor.isEdit) Res.string.alerts_dialog_save else Res.string.alerts_dialog_create
        )
    val enabledLabel: String = stringResource(Res.string.alerts_dialog_enabled_label)

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                OutlinedTextField(
                    value = eventType,
                    onValueChange = { eventType = it },
                    enabled = !editor.isEdit,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.alerts_dialog_event_label)) },
                    colors = fieldColors(),
                )
                OutlinedTextField(
                    value = message,
                    onValueChange = { message = it },
                    modifier = Modifier.fillMaxWidth(),
                    label = { Text(stringResource(Res.string.alerts_dialog_message_label)) },
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
            TextButton(onClick = { onSubmit(eventType, message, enabled) }, enabled = canSubmit) {
                Text(
                    text = submitLabel,
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    text = stringResource(Res.string.alerts_dialog_cancel),
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
                text = stringResource(Res.string.alerts_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.alerts_retry)) }
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

// The create/edit dialog's seed: an empty editor opens a blank create form; one seeded from a fetched detail
// opens a pre-filled edit form. [isEdit] decides create-vs-update on submit and locks the event-type field
// (the backend addresses a response by its event type).
private data class AlertEditor(
    val isEdit: Boolean,
    val eventType: String,
    val message: String,
    val isEnabled: Boolean,
) {
    companion object {
        fun create(): AlertEditor =
            AlertEditor(isEdit = false, eventType = "", message = "", isEnabled = true)

        fun edit(eventType: String, message: String, isEnabled: Boolean): AlertEditor =
            AlertEditor(isEdit = true, eventType = eventType, message = message, isEnabled = isEnabled)
    }
}
