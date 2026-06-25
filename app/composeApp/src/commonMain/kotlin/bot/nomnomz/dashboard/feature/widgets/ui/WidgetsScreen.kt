// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.FilterChip
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
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
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.widgets.state.WidgetsController
import bot.nomnomz.dashboard.feature.widgets.state.WidgetsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.widgets_action_error
import nomnomzbot.composeapp.generated.resources.widgets_badge_disabled
import nomnomzbot.composeapp.generated.resources.widgets_badge_enabled
import nomnomzbot.composeapp.generated.resources.widgets_delete_action
import nomnomzbot.composeapp.generated.resources.widgets_delete_action_short
import nomnomzbot.composeapp.generated.resources.widgets_delete_cancel
import nomnomzbot.composeapp.generated.resources.widgets_delete_confirm
import nomnomzbot.composeapp.generated.resources.widgets_delete_message
import nomnomzbot.composeapp.generated.resources.widgets_delete_title
import nomnomzbot.composeapp.generated.resources.widgets_empty
import nomnomzbot.composeapp.generated.resources.widgets_error
import nomnomzbot.composeapp.generated.resources.widgets_loading
import nomnomzbot.composeapp.generated.resources.widgets_retry
import nomnomzbot.composeapp.generated.resources.widgets_subtitle
import nomnomzbot.composeapp.generated.resources.widgets_title
import nomnomzbot.composeapp.generated.resources.widgets_toggle_action
import nomnomzbot.composeapp.generated.resources.widgets_url_copied
import nomnomzbot.composeapp.generated.resources.widgets_url_copy
import nomnomzbot.composeapp.generated.resources.widgets_url_label
import nomnomzbot.composeapp.generated.resources.widgets_clone_action
import nomnomzbot.composeapp.generated.resources.widgets_clone_action_short
import nomnomzbot.composeapp.generated.resources.widgets_create_action
import nomnomzbot.composeapp.generated.resources.widgets_create_confirm
import nomnomzbot.composeapp.generated.resources.widgets_create_dismiss
import nomnomzbot.composeapp.generated.resources.widgets_create_name
import nomnomzbot.composeapp.generated.resources.widgets_create_name_required
import nomnomzbot.composeapp.generated.resources.widgets_create_title
import nomnomzbot.composeapp.generated.resources.widgets_create_type
import nomnomzbot.composeapp.generated.resources.widgets_rename_action
import nomnomzbot.composeapp.generated.resources.widgets_rename_action_short
import nomnomzbot.composeapp.generated.resources.widgets_rename_confirm
import nomnomzbot.composeapp.generated.resources.widgets_rename_dismiss
import nomnomzbot.composeapp.generated.resources.widgets_rename_name
import nomnomzbot.composeapp.generated.resources.widgets_rename_title
import nomnomzbot.composeapp.generated.resources.widgets_url_missing
import org.jetbrains.compose.resources.stringResource

// The Overlays page (frontend-ia.md §3, Stream group): the channel's OBS browser-source overlay widgets, all
// real data from [WidgetsController]. The screen is a pure projection of the controller's state; it loads on
// first composition. The core value of the page is each overlay's browser-source URL, shown in a copyable chip
// the operator pastes into an OBS browser source. Each overlay can be enabled/disabled inline, or deleted —
// deletion is destructive (its browser-source URL stops resolving once gone), so it confirms first.
@Composable
fun WidgetsScreen(controller: WidgetsController, role: ManagementRole?) {
    val state: WidgetsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // One decision for the whole page: Overlays gates every write control at its single Editor manage floor
    // (frontend-ia.md §3, Stream group). A caller below it still sees each overlay and can copy its
    // browser-source URL, but every enable/disable and delete control renders disabled with "Requires Editor"
    // (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Widgets)

    var pendingDelete: PendingDelete? by remember { mutableStateOf(null) }
    var pendingRename: WidgetSummary? by remember { mutableStateOf(null) }
    var pendingClone: WidgetSummary? by remember { mutableStateOf(null) }
    var showCreateDialog: Boolean by remember { mutableStateOf(false) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.SpaceBetween,
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column {
                Text(
                    text = stringResource(Res.string.widgets_title),
                    style = typography.xl2,
                    color = tokens.foreground,
                )
                Text(
                    text = stringResource(Res.string.widgets_subtitle),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
            ManageGate(manage) {
                Button(onClick = { showCreateDialog = true }) {
                    Text(stringResource(Res.string.widgets_create_action))
                }
            }
        }

        when (val current: WidgetsState = state) {
            is WidgetsState.Loading -> CenteredMessage(stringResource(Res.string.widgets_loading))
            is WidgetsState.Empty -> CenteredMessage(stringResource(Res.string.widgets_empty))
            is WidgetsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is WidgetsState.Ready ->
                ReadyContent(
                    widgets = current.widgets,
                    actionError = current.actionError,
                    manage = manage,
                    onToggle = { widget, enabled ->
                        scope.launch { controller.toggleWidget(widget.id, enabled) }
                    },
                    onDelete = { widget -> pendingDelete = PendingDelete(widget.id, widget.name) },
                    onRename = { widget -> pendingRename = widget },
                    onClone = { widget -> pendingClone = widget },
                )
        }
    }

    pendingDelete?.let { target ->
        ConfirmDialog(
            title = stringResource(Res.string.widgets_delete_title),
            message = stringResource(Res.string.widgets_delete_message, target.name),
            confirmLabel = stringResource(Res.string.widgets_delete_confirm),
            dismissLabel = stringResource(Res.string.widgets_delete_cancel),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                scope.launch { controller.deleteWidget(target.id) }
            },
            onDismiss = { pendingDelete = null },
        )
    }

    pendingRename?.let { widget ->
        RenameWidgetDialog(
            currentName = widget.name,
            onConfirm = { newName ->
                pendingRename = null
                scope.launch { controller.renameWidget(widget.id, newName) }
            },
            onDismiss = { pendingRename = null },
        )
    }

    pendingClone?.let { widget ->
        ConfirmDialog(
            title = stringResource(Res.string.widgets_clone_action, widget.name),
            message = "Create \"Copy of ${widget.name}\" with the same type?",
            confirmLabel = stringResource(Res.string.widgets_clone_action_short),
            dismissLabel = stringResource(Res.string.widgets_delete_cancel),
            destructive = false,
            onConfirm = {
                pendingClone = null
                scope.launch { controller.cloneWidget(widget.type, widget.name) }
            },
            onDismiss = { pendingClone = null },
        )
    }

    if (showCreateDialog) {
        CreateWidgetDialog(
            onConfirm = { name, type ->
                showCreateDialog = false
                scope.launch { controller.createWidget(name, type) }
            },
            onDismiss = { showCreateDialog = false },
        )
    }
}

// The list-bearing content: an optional write-failure banner over the overlay rows. The header (title +
// subtitle) lives on the screen so it shows in every state, matching the integrations page.
@Composable
private fun ReadyContent(
    widgets: List<WidgetSummary>,
    actionError: String?,
    manage: ManageDecision,
    onToggle: (WidgetSummary, Boolean) -> Unit,
    onDelete: (WidgetSummary) -> Unit,
    onRename: (WidgetSummary) -> Unit,
    onClone: (WidgetSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        actionError?.let { ActionErrorBanner(detail = it) }
        WidgetList(
            widgets = widgets,
            manage = manage,
            onToggle = onToggle,
            onDelete = onDelete,
            onRename = onRename,
            onClone = onClone,
        )
    }
}

@Composable
private fun ActionErrorBanner(detail: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = stringResource(Res.string.widgets_action_error, detail),
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
private fun WidgetList(
    widgets: List<WidgetSummary>,
    manage: ManageDecision,
    onToggle: (WidgetSummary, Boolean) -> Unit,
    onDelete: (WidgetSummary) -> Unit,
    onRename: (WidgetSummary) -> Unit,
    onClone: (WidgetSummary) -> Unit,
) {
    val spacing = LocalSpacing.current

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(vertical = spacing.s1),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        items(items = widgets, key = { widget -> widget.id }) { widget ->
            WidgetRow(
                widget = widget,
                manage = manage,
                onToggle = { enabled -> onToggle(widget, enabled) },
                onDelete = { onDelete(widget) },
                onRename = { onRename(widget) },
                onClone = { onClone(widget) },
            )
        }
    }
}

// One overlay card: its name + state on the left, an enable/disable switch + delete on the right, and below
// them the browser-source URL in a copyable chip (the page's core value — paste into OBS). The header row uses
// the hardened layout: the text block takes weight(1f) and ellipsizes, the trailing controls stay single-line.
@Composable
private fun WidgetRow(
    widget: WidgetSummary,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
    onDelete: () -> Unit,
    onRename: () -> Unit,
    onClone: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val stateLabel: String =
        stringResource(
            if (widget.isEnabled) Res.string.widgets_badge_enabled
            else Res.string.widgets_badge_disabled
        )
    val toggleLabel: String = stringResource(Res.string.widgets_toggle_action, widget.name)
    val deleteLabel: String = stringResource(Res.string.widgets_delete_action, widget.name)
    val renameLabel: String = stringResource(Res.string.widgets_rename_action, widget.name)
    val cloneLabel: String = stringResource(Res.string.widgets_clone_action, widget.name)
    val urlLabel: String = stringResource(Res.string.widgets_url_label)

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(
                modifier = Modifier
                    .weight(1f)
                    // One node for the text block: "Alerts, overlay, enabled.".
                    .clearAndSetSemantics { contentDescription = "${widget.name}, ${widget.type}, $stateLabel." },
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = widget.name,
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                Text(
                    text = "$stateLabel · ${widget.type}",
                    style = typography.sm,
                    color = if (widget.isEnabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }

            ManageGate(decision = manage) { enabled ->
                Switch(
                    checked = widget.isEnabled,
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
                TextButton(
                    onClick = onRename,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = renameLabel },
                ) {
                    Text(
                        text = stringResource(Res.string.widgets_rename_action_short),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(
                    onClick = onClone,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = cloneLabel },
                ) {
                    Text(
                        text = stringResource(Res.string.widgets_clone_action_short),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(
                    onClick = onDelete,
                    enabled = enabled,
                    modifier = Modifier.semantics { contentDescription = deleteLabel },
                ) {
                    Text(
                        text = stringResource(Res.string.widgets_delete_action_short),
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                        maxLines = 1,
                    )
                }
            }
        }

        // The browser-source URL — the operator pastes this into an OBS browser source. Shown labelled in a
        // copyable chip. Missing only if the backend has no overlay token yet for the channel.
        Text(text = urlLabel, style = typography.xs, color = tokens.mutedForeground, maxLines = 1)
        val overlayUrl: String? = widget.overlayUrl?.takeIf { it.isNotBlank() }
        if (overlayUrl != null) {
            CopyValue(
                value = overlayUrl,
                copyLabel = stringResource(Res.string.widgets_url_copy),
                copiedLabel = stringResource(Res.string.widgets_url_copied),
            )
        } else {
            Text(
                text = stringResource(Res.string.widgets_url_missing),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
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
                text = stringResource(Res.string.widgets_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.widgets_retry)) }
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

// The delete-confirm target: the widget's id (the backend address) plus its name (for the confirm message).
private data class PendingDelete(val id: String, val name: String)

// Dialog to create a new widget. The operator enters a name and picks a type from the closed set the backend
// supports. The caller owns open/closed state — it opens when the user clicks "Create Overlay".
@Composable
private fun CreateWidgetDialog(
    onConfirm: (name: String, type: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val types: List<String> = listOf("alerts", "nowplaying", "chat", "goals", "countdown", "custom")

    var name: String by remember { mutableStateOf("") }
    var selectedType: String by remember { mutableStateOf(types.first()) }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.widgets_create_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.widgets_create_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.widgets_create_name_required) else null,
                )
                Text(
                    text = stringResource(Res.string.widgets_create_type),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                    types.chunked(3).forEach { row ->
                        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            row.forEach { t ->
                                FilterChip(
                                    selected = selectedType == t,
                                    onClick = { selectedType = t },
                                    label = { Text(t, style = typography.xs) },
                                )
                            }
                        }
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (name.isBlank()) { nameError = true; return@Button }
                    onConfirm(name.trim(), selectedType)
                },
            ) {
                Text(stringResource(Res.string.widgets_create_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.widgets_create_dismiss)) }
        },
        containerColor = tokens.card,
    )
}

// Dialog to rename an existing widget. Pre-filled with the current name; the operator edits it and confirms.
@Composable
private fun RenameWidgetDialog(
    currentName: String,
    onConfirm: (newName: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf(currentName) }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.widgets_rename_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            AppTextField(
                value = name,
                onValueChange = { name = it; nameError = false },
                label = stringResource(Res.string.widgets_rename_name),
                isError = nameError,
                errorText = if (nameError) stringResource(Res.string.widgets_create_name_required) else null,
            )
        },
        confirmButton = {
            Button(
                onClick = {
                    if (name.isBlank()) { nameError = true; return@Button }
                    onConfirm(name.trim())
                },
            ) {
                Text(stringResource(Res.string.widgets_rename_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.widgets_rename_dismiss)) }
        },
        containerColor = tokens.card,
    )
}
