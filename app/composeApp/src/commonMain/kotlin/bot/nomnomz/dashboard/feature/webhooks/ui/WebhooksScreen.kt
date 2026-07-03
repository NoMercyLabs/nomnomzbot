// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.webhooks.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.material3.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
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
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.PlayCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.PowerGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RefreshGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.InboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhookCreated
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.webhooks.state.WebhooksController
import bot.nomnomz.dashboard.feature.webhooks.state.WebhooksState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.setup_copy_action
import nomnomzbot.composeapp.generated.resources.setup_copy_done
import nomnomzbot.composeapp.generated.resources.webhooks_action_error
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_label
import nomnomzbot.composeapp.generated.resources.webhooks_create_inbound_adapter
import nomnomzbot.composeapp.generated.resources.webhooks_create_inbound_confirm
import nomnomzbot.composeapp.generated.resources.webhooks_create_inbound_dismiss
import nomnomzbot.composeapp.generated.resources.webhooks_create_inbound_name
import nomnomzbot.composeapp.generated.resources.webhooks_create_inbound_name_required
import nomnomzbot.composeapp.generated.resources.webhooks_create_inbound_secret
import nomnomzbot.composeapp.generated.resources.webhooks_create_inbound_title
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_confirm
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_dismiss
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_events
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_fqdn
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_fqdn_required
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_name
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_name_required
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_path
import nomnomzbot.composeapp.generated.resources.webhooks_create_outbound_title
import nomnomzbot.composeapp.generated.resources.webhooks_delete_cancel
import nomnomzbot.composeapp.generated.resources.webhooks_delete_confirm
import nomnomzbot.composeapp.generated.resources.webhooks_delete_message
import nomnomzbot.composeapp.generated.resources.webhooks_delete_title
import nomnomzbot.composeapp.generated.resources.webhooks_error
import nomnomzbot.composeapp.generated.resources.webhooks_failures_label
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_add
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_title
import nomnomzbot.composeapp.generated.resources.webhooks_loading
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_add
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_disabled_reason
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_reenable
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_test
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_title
import nomnomzbot.composeapp.generated.resources.webhooks_receive_count
import nomnomzbot.composeapp.generated.resources.webhooks_reenable_cancel
import nomnomzbot.composeapp.generated.resources.webhooks_reenable_confirm
import nomnomzbot.composeapp.generated.resources.webhooks_reenable_message
import nomnomzbot.composeapp.generated.resources.webhooks_reenable_title
import nomnomzbot.composeapp.generated.resources.webhooks_retry
import nomnomzbot.composeapp.generated.resources.webhooks_rotate_inbound_token
import nomnomzbot.composeapp.generated.resources.webhooks_rotate_outbound_secret
import nomnomzbot.composeapp.generated.resources.webhooks_secret_once_title
import nomnomzbot.composeapp.generated.resources.webhooks_secret_once_message
import nomnomzbot.composeapp.generated.resources.webhooks_secret_once_dismiss
import nomnomzbot.composeapp.generated.resources.shell_nav_webhooks
import nomnomzbot.composeapp.generated.resources.webhooks_subtitle
import nomnomzbot.composeapp.generated.resources.webhooks_test_failed
import nomnomzbot.composeapp.generated.resources.webhooks_test_result_title
import nomnomzbot.composeapp.generated.resources.webhooks_test_success

import nomnomzbot.composeapp.generated.resources.webhooks_url_label
import org.jetbrains.compose.resources.stringResource

// The Webhooks page: inbound endpoints (events → pipeline) and outbound endpoints (events → external URL).
// Both lists load in parallel via [WebhooksController]. All mutations go through the controller which reloads
// on success. Signing secrets and ingest-URL tokens are shown ONCE in a modal after creation/rotation.
@Composable
fun WebhooksScreen(controller: WebhooksController, role: ManagementRole?) {
    val state: WebhooksState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Webhooks)

    var pendingDeleteInbound: InboundWebhook? by remember { mutableStateOf(null) }
    var pendingDeleteOutbound: OutboundWebhook? by remember { mutableStateOf(null) }
    var pendingReenable: OutboundWebhook? by remember { mutableStateOf(null) }
    var showCreateInbound: Boolean by remember { mutableStateOf(false) }
    var showCreateOutbound: Boolean by remember { mutableStateOf(false) }
    var shownSecret: String? by remember { mutableStateOf(null) }
    var testResult: String? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_webhooks), subtitle = stringResource(Res.string.webhooks_subtitle))

        when (val current: WebhooksState = state) {
            is WebhooksState.Loading -> CenteredMessage(stringResource(Res.string.webhooks_loading))
            is WebhooksState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is WebhooksState.Ready -> {
                current.actionError?.let { detail ->
                    ActionErrorBanner(message = stringResource(Res.string.webhooks_action_error, detail))
                }
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    // ── Inbound ───────────────────────────────────────────
                    item(key = "inbound-section") {
                        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Text(
                                    text = stringResource(Res.string.webhooks_inbound_title),
                                    style = typography.lg,
                                    color = tokens.cardForeground,
                                )
                                ManageGate(manage) { enabled ->
                                    GlyphButton(
                                        imageVector = AddGlyph,
                                        label = stringResource(Res.string.webhooks_inbound_add),
                                        onClick = { showCreateInbound = true },
                                        enabled = enabled,
                                    )
                                }
                            }
                            Card(modifier = Modifier.fillMaxWidth()) {
                                Column {
                                    current.inbound.forEachIndexed { index, ep ->
                                        InboundRow(
                                            ep = ep,
                                            manage = manage,
                                            onToggle = { scope.launch { controller.toggleInbound(ep.id, !ep.isEnabled) } },
                                            onRotate = { scope.launch { shownSecret = controller.rotateInboundToken(ep.id) } },
                                            onDelete = { pendingDeleteInbound = ep },
                                        )
                                        if (index < current.inbound.lastIndex) {
                                            HorizontalDivider(color = tokens.border.copy(alpha = 0.5f))
                                        }
                                    }
                                }
                            }
                        }
                    }
                    item(key = "outbound-divider") { HorizontalDivider() }
                    // ── Outbound ──────────────────────────────────────────
                    item(key = "outbound-section") {
                        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                horizontalArrangement = Arrangement.SpaceBetween,
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Text(
                                    text = stringResource(Res.string.webhooks_outbound_title),
                                    style = typography.lg,
                                    color = tokens.cardForeground,
                                )
                                ManageGate(manage) { enabled ->
                                    GlyphButton(
                                        imageVector = AddGlyph,
                                        label = stringResource(Res.string.webhooks_outbound_add),
                                        onClick = { showCreateOutbound = true },
                                        enabled = enabled,
                                    )
                                }
                            }
                            Card(modifier = Modifier.fillMaxWidth()) {
                                Column {
                                    current.outbound.forEachIndexed { index, ep ->
                                        OutboundRow(
                                            ep = ep,
                                            manage = manage,
                                            onToggle = { scope.launch { controller.toggleOutbound(ep.id, !ep.isEnabled) } },
                                            onReenable = { pendingReenable = ep },
                                            onRotateSecret = { scope.launch { shownSecret = controller.rotateOutboundSecret(ep.id) } },
                                            onTest = {
                                                scope.launch {
                                                    val result = controller.testOutbound(ep.id)
                                                    if (result != null) {
                                                        testResult = if (result.delivered)
                                                            "✓ ${result.responseCode} (${result.durationMs}ms)"
                                                        else
                                                            "✗ ${result.error ?: "no response"}"
                                                    }
                                                }
                                            },
                                            onDelete = { pendingDeleteOutbound = ep },
                                        )
                                        if (index < current.outbound.lastIndex) {
                                            HorizontalDivider(color = tokens.border.copy(alpha = 0.5f))
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    pendingDeleteInbound?.let { ep ->
        ConfirmDialog(
            title = stringResource(Res.string.webhooks_delete_title),
            message = stringResource(Res.string.webhooks_delete_message, ep.name),
            confirmLabel = stringResource(Res.string.webhooks_delete_confirm),
            dismissLabel = stringResource(Res.string.webhooks_delete_cancel),
            destructive = true,
            onConfirm = { pendingDeleteInbound = null; scope.launch { controller.deleteInbound(ep.id) } },
            onDismiss = { pendingDeleteInbound = null },
        )
    }

    pendingDeleteOutbound?.let { ep ->
        ConfirmDialog(
            title = stringResource(Res.string.webhooks_delete_title),
            message = stringResource(Res.string.webhooks_delete_message, ep.name),
            confirmLabel = stringResource(Res.string.webhooks_delete_confirm),
            dismissLabel = stringResource(Res.string.webhooks_delete_cancel),
            destructive = true,
            onConfirm = { pendingDeleteOutbound = null; scope.launch { controller.deleteOutbound(ep.id) } },
            onDismiss = { pendingDeleteOutbound = null },
        )
    }

    pendingReenable?.let { ep ->
        ConfirmDialog(
            title = stringResource(Res.string.webhooks_reenable_title),
            message = stringResource(Res.string.webhooks_reenable_message, ep.name),
            confirmLabel = stringResource(Res.string.webhooks_reenable_confirm),
            dismissLabel = stringResource(Res.string.webhooks_reenable_cancel),
            destructive = false,
            onConfirm = { pendingReenable = null; scope.launch { controller.reenableOutbound(ep.id) } },
            onDismiss = { pendingReenable = null },
        )
    }

    shownSecret?.let { secret ->
        SecretOnceDialog(secret = secret, onDismiss = { shownSecret = null })
    }

    testResult?.let { result ->
        val isSuccess: Boolean = result.startsWith("✓")
        AlertDialog(
            onDismissRequest = { testResult = null },
            title = {
                Text(stringResource(Res.string.webhooks_test_result_title), style = LocalTypography.current.lg, color = LocalTokens.current.cardForeground)
            },
            text = {
                Text(
                    text = result,
                    style = LocalTypography.current.sm,
                    color = if (isSuccess) LocalTokens.current.primary else LocalTokens.current.destructive,
                )
            },
            confirmButton = {
                TextButton(onClick = { testResult = null }) {
                    Text(stringResource(Res.string.webhooks_secret_once_dismiss))
                }
            },
            containerColor = LocalTokens.current.card,
        )
    }

    if (showCreateInbound) {
        CreateInboundDialog(
            onConfirm = { name, adapter, secret ->
                showCreateInbound = false
                scope.launch { controller.createInbound(name, adapter, secret) }
            },
            onDismiss = { showCreateInbound = false },
        )
    }

    if (showCreateOutbound) {
        CreateOutboundDialog(
            onConfirm = { name, fqdn, path, events ->
                showCreateOutbound = false
                scope.launch {
                    val created: OutboundWebhookCreated? = controller.createOutbound(name, fqdn, path, events)
                    if (created != null) shownSecret = created.signingSecret
                }
            },
            onDismiss = { showCreateOutbound = false },
        )
    }
}

@Composable
private fun InboundRow(
    ep: InboundWebhook,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onRotate: () -> Unit,
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
                Text(text = ep.name, style = typography.base, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    text = "${stringResource(Res.string.webhooks_adapter_label)}: ${ep.adapter} · ${stringResource(Res.string.webhooks_receive_count, ep.receiveCount)}",
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
            ManageGate(manage) { enabled ->
                Switch(
                    checked = ep.isEnabled,
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
        Text(text = "${stringResource(Res.string.webhooks_url_label)}: ${ep.ingestUrl}", style = typography.xs, color = tokens.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = RefreshGlyph,
                    label = stringResource(Res.string.webhooks_rotate_inbound_token),
                    onClick = onRotate,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = stringResource(Res.string.webhooks_delete_confirm),
                    onClick = onDelete,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
}

@Composable
private fun OutboundRow(
    ep: OutboundWebhook,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onReenable: () -> Unit,
    onRotateSecret: () -> Unit,
    onTest: () -> Unit,
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
                Text(text = ep.name, style = typography.base, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(
                    text = "${ep.fqdn}${ep.path ?: ""}",
                    style = typography.xs,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
            }
            ManageGate(manage) { enabled ->
                Switch(
                    checked = ep.isEnabled,
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
        if (ep.consecutiveFailureCount > 0 || ep.disabledReason != null) {
            Text(
                text = "${stringResource(Res.string.webhooks_failures_label, ep.consecutiveFailureCount)}${ep.disabledReason?.let { " — $it" } ?: ""}",
                style = typography.xs,
                color = tokens.destructive,
                maxLines = 2,
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            if (ep.disabledAt != null) {
                ManageGate(manage) { enabled ->
                    GlyphButton(
                        imageVector = PowerGlyph,
                        label = stringResource(Res.string.webhooks_outbound_reenable),
                        onClick = onReenable,
                        enabled = enabled,
                        tint = tokens.primary,
                    )
                }
            }
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = PlayCircleGlyph,
                    label = stringResource(Res.string.webhooks_outbound_test),
                    onClick = onTest,
                    enabled = enabled,
                    tint = tokens.primary,
                )
            }
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = RefreshGlyph,
                    label = stringResource(Res.string.webhooks_rotate_outbound_secret),
                    onClick = onRotateSecret,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = TrashGlyph,
                    label = stringResource(Res.string.webhooks_delete_confirm),
                    onClick = onDelete,
                    enabled = enabled,
                    tint = tokens.destructive,
                )
            }
        }
    }
}

@Composable
private fun SecretOnceDialog(secret: String, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(stringResource(Res.string.webhooks_secret_once_title), style = typography.lg, color = tokens.cardForeground)
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Text(stringResource(Res.string.webhooks_secret_once_message), style = typography.sm, color = tokens.mutedForeground)
                CopyValue(
                    value = secret,
                    copyLabel = stringResource(Res.string.setup_copy_action),
                    copiedLabel = stringResource(Res.string.setup_copy_done),
                )
            }
        },
        confirmButton = {
            Button(onClick = onDismiss) { Text(stringResource(Res.string.webhooks_secret_once_dismiss)) }
        },
        containerColor = tokens.card,
    )
}

@Composable
private fun CreateInboundDialog(
    onConfirm: (name: String, adapter: String, secret: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    var adapter: String by remember { mutableStateOf("generic") }
    var secret: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.webhooks_create_inbound_title), style = typography.lg, color = tokens.cardForeground) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name, onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.webhooks_create_inbound_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.webhooks_create_inbound_name_required) else null,
                )
                AppTextField(
                    value = adapter, onValueChange = { adapter = it },
                    label = stringResource(Res.string.webhooks_create_inbound_adapter),
                    isError = false, errorText = null,
                )
                AppTextField(
                    value = secret, onValueChange = { secret = it },
                    label = stringResource(Res.string.webhooks_create_inbound_secret),
                    isError = false, errorText = null,
                )
            }
        },
        confirmButton = {
            Button(onClick = {
                if (name.isBlank()) { nameError = true; return@Button }
                onConfirm(name.trim(), adapter.trim().ifBlank { "generic" }, secret.trim())
            }) { Text(stringResource(Res.string.webhooks_create_inbound_confirm)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.webhooks_create_inbound_dismiss)) } },
        containerColor = tokens.card,
    )
}

@Composable
private fun CreateOutboundDialog(
    onConfirm: (name: String, fqdn: String, path: String?, events: List<String>) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    var fqdn: String by remember { mutableStateOf("") }
    var path: String by remember { mutableStateOf("") }
    var eventsInput: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }
    var fqdnError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.webhooks_create_outbound_title), style = typography.lg, color = tokens.cardForeground) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name, onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.webhooks_create_outbound_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.webhooks_create_outbound_name_required) else null,
                )
                AppTextField(
                    value = fqdn, onValueChange = { fqdn = it; fqdnError = false },
                    label = stringResource(Res.string.webhooks_create_outbound_fqdn),
                    isError = fqdnError,
                    errorText = if (fqdnError) stringResource(Res.string.webhooks_create_outbound_fqdn_required) else null,
                )
                AppTextField(
                    value = path, onValueChange = { path = it },
                    label = stringResource(Res.string.webhooks_create_outbound_path),
                    isError = false, errorText = null,
                )
                AppTextField(
                    value = eventsInput, onValueChange = { eventsInput = it },
                    label = stringResource(Res.string.webhooks_create_outbound_events),
                    isError = false, errorText = null,
                )
            }
        },
        confirmButton = {
            Button(onClick = {
                var valid: Boolean = true
                if (name.isBlank()) { nameError = true; valid = false }
                if (fqdn.isBlank()) { fqdnError = true; valid = false }
                if (!valid) return@Button
                val events: List<String> = eventsInput.split(",").map { it.trim() }.filter { it.isNotEmpty() }
                onConfirm(name.trim(), fqdn.trim(), path.trim().takeIf { it.isNotBlank() }, events)
            }) { Text(stringResource(Res.string.webhooks_create_outbound_confirm)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.webhooks_create_outbound_dismiss)) } },
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
            Text(text = stringResource(Res.string.webhooks_error, detail), style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            TextButton(onClick = onRetry) { Text(stringResource(Res.string.webhooks_retry)) }
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
