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
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.EntityPickerField
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.FileGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.PlayCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.PowerGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RefreshGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.GenericInboundConfig
import bot.nomnomz.dashboard.core.network.InboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundDelivery
import bot.nomnomz.dashboard.core.network.OutboundEventCatalogueEntry
import bot.nomnomz.dashboard.core.network.OutboundWebhook
import bot.nomnomz.dashboard.core.network.OutboundWebhookCreated
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.webhooks.state.WebhooksController
import bot.nomnomz.dashboard.feature.webhooks.state.WebhooksState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.pipelines_empty
import nomnomzbot.composeapp.generated.resources.setup_copy_action
import nomnomzbot.composeapp.generated.resources.setup_copy_done
import nomnomzbot.composeapp.generated.resources.shell_nav_webhooks
import nomnomzbot.composeapp.generated.resources.webhooks_action_error
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_buymeacoffee
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_fourthwall
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_generic
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_github
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_kofi
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_label
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_patreon
import nomnomzbot.composeapp.generated.resources.webhooks_adapter_shopify
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
import nomnomzbot.composeapp.generated.resources.webhooks_deliveries
import nomnomzbot.composeapp.generated.resources.webhooks_deliveries_attempt
import nomnomzbot.composeapp.generated.resources.webhooks_deliveries_close
import nomnomzbot.composeapp.generated.resources.webhooks_deliveries_code
import nomnomzbot.composeapp.generated.resources.webhooks_deliveries_empty
import nomnomzbot.composeapp.generated.resources.webhooks_deliveries_loading
import nomnomzbot.composeapp.generated.resources.webhooks_deliveries_title
import nomnomzbot.composeapp.generated.resources.webhooks_edit
import nomnomzbot.composeapp.generated.resources.webhooks_edit_confirm
import nomnomzbot.composeapp.generated.resources.webhooks_edit_dismiss
import nomnomzbot.composeapp.generated.resources.webhooks_edit_inbound_title
import nomnomzbot.composeapp.generated.resources.webhooks_edit_outbound_title
import nomnomzbot.composeapp.generated.resources.webhooks_edit_secret_optional
import nomnomzbot.composeapp.generated.resources.webhooks_enabled_label
import nomnomzbot.composeapp.generated.resources.webhooks_error
import nomnomzbot.composeapp.generated.resources.webhooks_events_none_selected
import nomnomzbot.composeapp.generated.resources.webhooks_events_select_all
import nomnomzbot.composeapp.generated.resources.webhooks_events_select_all_hint
import nomnomzbot.composeapp.generated.resources.webhooks_events_selected_count
import nomnomzbot.composeapp.generated.resources.webhooks_events_unavailable
import nomnomzbot.composeapp.generated.resources.webhooks_failures_label
import nomnomzbot.composeapp.generated.resources.webhooks_generic_event_id_hint
import nomnomzbot.composeapp.generated.resources.webhooks_generic_event_id_path
import nomnomzbot.composeapp.generated.resources.webhooks_generic_event_kind_hint
import nomnomzbot.composeapp.generated.resources.webhooks_generic_event_kind_path
import nomnomzbot.composeapp.generated.resources.webhooks_generic_help
import nomnomzbot.composeapp.generated.resources.webhooks_generic_required
import nomnomzbot.composeapp.generated.resources.webhooks_generic_secret_body_field
import nomnomzbot.composeapp.generated.resources.webhooks_generic_section
import nomnomzbot.composeapp.generated.resources.webhooks_generic_signature_header
import nomnomzbot.composeapp.generated.resources.webhooks_generic_signature_prefix
import nomnomzbot.composeapp.generated.resources.webhooks_generic_signing_template
import nomnomzbot.composeapp.generated.resources.webhooks_generic_timestamp_header
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_add
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_choose_pipeline
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_event
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_event_hint
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_event_label
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_help
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_label
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_none
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_pipeline
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_routing_pipeline_label
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_target_event
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_target_none
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_target_pipeline
import nomnomzbot.composeapp.generated.resources.webhooks_inbound_title
import nomnomzbot.composeapp.generated.resources.webhooks_loading
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_add
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_reenable
import nomnomzbot.composeapp.generated.resources.webhooks_outbound_target_readonly
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
import nomnomzbot.composeapp.generated.resources.webhooks_secret_once_dismiss
import nomnomzbot.composeapp.generated.resources.webhooks_secret_once_message
import nomnomzbot.composeapp.generated.resources.webhooks_secret_once_title
import nomnomzbot.composeapp.generated.resources.webhooks_subtitle
import nomnomzbot.composeapp.generated.resources.webhooks_test_result_title
import nomnomzbot.composeapp.generated.resources.webhooks_url_label
import org.jetbrains.compose.resources.stringResource

// The Webhooks page: inbound endpoints (events → pipeline) and outbound endpoints (events → external URL).
// Both lists load in parallel via [WebhooksController]. All mutations go through the controller which reloads
// on success. Signing secrets and ingest-URL tokens are shown ONCE in a modal after creation/rotation.
//
// Outbound events are chosen from the backend catalogue as a CHECKLIST (never a free-text box), the full edit
// dialogs persist the whole endpoint (not just the enabled flag), custom (generic) inbound adapters expose
// their signing config, and each outbound endpoint has a delivery log for debugging.
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
    var pendingEditInbound: InboundWebhook? by remember { mutableStateOf(null) }
    var pendingEditOutbound: OutboundWebhook? by remember { mutableStateOf(null) }
    var deliveriesFor: OutboundWebhook? by remember { mutableStateOf(null) }
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
                                            pipelines = current.pipelines,
                                            manage = manage,
                                            onToggle = { scope.launch { controller.toggleInbound(ep.id, !ep.isEnabled) } },
                                            onEdit = { pendingEditInbound = ep },
                                            onRotate = { scope.launch { shownSecret = controller.rotateInboundToken(ep.id) } },
                                            onDelete = { pendingDeleteInbound = ep },
                                        )
                                        if (index < current.inbound.lastIndex) {
                                            Separator()
                                        }
                                    }
                                }
                            }
                        }
                    }
                    item(key = "outbound-divider") { Separator() }
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
                                            onEdit = { pendingEditOutbound = ep },
                                            onReenable = { pendingReenable = ep },
                                            onRotateSecret = { scope.launch { shownSecret = controller.rotateOutboundSecret(ep.id) } },
                                            onDeliveries = { deliveriesFor = ep },
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
                                            Separator()
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

    val catalogue: List<OutboundEventCatalogueEntry> = (state as? WebhooksState.Ready)?.catalogue ?: emptyList()

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
        )
    }

    if (showCreateInbound) {
        val pipelines: List<PipelineSummary> = (state as? WebhooksState.Ready)?.pipelines ?: emptyList()
        InboundDialog(
            existing = null,
            pipelines = pipelines,
            onConfirmCreate = { name, adapter, secret, targetPipelineId, targetEventType, genericConfig ->
                showCreateInbound = false
                scope.launch { controller.createInbound(name, adapter, secret, targetPipelineId, targetEventType, genericConfig) }
            },
            onConfirmEdit = { _, _, _, _, _, _, _ -> },
            onDismiss = { showCreateInbound = false },
        )
    }

    pendingEditInbound?.let { ep ->
        val pipelines: List<PipelineSummary> = (state as? WebhooksState.Ready)?.pipelines ?: emptyList()
        InboundDialog(
            existing = ep,
            pipelines = pipelines,
            onConfirmCreate = { _, _, _, _, _, _ -> },
            onConfirmEdit = { name, _, secret, targetPipelineId, targetEventType, genericConfig, enabled ->
                pendingEditInbound = null
                scope.launch { controller.updateInbound(ep.id, name, secret, targetPipelineId, targetEventType, genericConfig, enabled) }
            },
            onDismiss = { pendingEditInbound = null },
        )
    }

    if (showCreateOutbound) {
        OutboundDialog(
            existing = null,
            catalogue = catalogue,
            onConfirmCreate = { name, fqdn, path, events ->
                showCreateOutbound = false
                scope.launch {
                    val created: OutboundWebhookCreated? = controller.createOutbound(name, fqdn, path, events)
                    if (created != null) shownSecret = created.signingSecret
                }
            },
            onConfirmEdit = { _, _, _ -> },
            onDismiss = { showCreateOutbound = false },
        )
    }

    pendingEditOutbound?.let { ep ->
        OutboundDialog(
            existing = ep,
            catalogue = catalogue,
            onConfirmCreate = { _, _, _, _ -> },
            onConfirmEdit = { name, events, enabled ->
                pendingEditOutbound = null
                scope.launch { controller.updateOutbound(ep.id, name, events, enabled) }
            },
            onDismiss = { pendingEditOutbound = null },
        )
    }

    deliveriesFor?.let { ep ->
        DeliveriesDialog(
            endpoint = ep,
            load = { controller.outboundDeliveries(ep.id) },
            onDismiss = { deliveriesFor = null },
        )
    }
}

@Composable
private fun InboundRow(
    ep: InboundWebhook,
    pipelines: List<PipelineSummary>,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onEdit: () -> Unit,
    onRotate: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The endpoint's routing target, resolved to a friendly line: a bound pipeline's name, an event type, or a
    // clear "no automation" so a streamer sees whether a verified receive actually does anything.
    val routingText: String =
        when {
            ep.targetPipelineId != null -> {
                val name: String =
                    pipelines.firstOrNull { it.id == ep.targetPipelineId }?.name ?: ep.targetPipelineId
                stringResource(Res.string.webhooks_inbound_target_pipeline, name)
            }
            !ep.targetEventType.isNullOrBlank() ->
                stringResource(Res.string.webhooks_inbound_target_event, ep.targetEventType)
            else -> stringResource(Res.string.webhooks_inbound_target_none)
        }

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
                    text = "${stringResource(Res.string.webhooks_adapter_label)}: ${adapterLabel(ep.adapter)} · ${stringResource(Res.string.webhooks_receive_count, ep.receiveCount)}",
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
                )
            }
        }
        Text(
            text = routingText,
            style = typography.xs,
            color = if (ep.targetPipelineId == null && ep.targetEventType.isNullOrBlank()) tokens.mutedForeground else tokens.primary,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Text(text = "${stringResource(Res.string.webhooks_url_label)}: ${ep.ingestUrl}", style = typography.xs, color = tokens.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = EditGlyph,
                    label = stringResource(Res.string.webhooks_edit),
                    onClick = onEdit,
                    enabled = enabled,
                    tint = tokens.primary,
                )
            }
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
    onEdit: () -> Unit,
    onReenable: () -> Unit,
    onRotateSecret: () -> Unit,
    onDeliveries: () -> Unit,
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
                Text(
                    text = ep.subscribedEventTypes.joinToString(", ").ifBlank { "—" },
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
            ManageGate(manage) { enabled ->
                GlyphButton(
                    imageVector = EditGlyph,
                    label = stringResource(Res.string.webhooks_edit),
                    onClick = onEdit,
                    enabled = enabled,
                    tint = tokens.primary,
                )
            }
            GlyphButton(
                imageVector = FileGlyph,
                label = stringResource(Res.string.webhooks_deliveries),
                onClick = onDeliveries,
                tint = tokens.mutedForeground,
            )
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
    )
}

// The three routing choices an inbound endpoint offers on receive: nothing, run a pipeline, or trigger an event.
private enum class InboundRouting { None, Pipeline, Event }

// The inbound adapter kinds, keyed by their backend enum name (the exact wire value the API expects).
private val InboundAdapters: List<String> =
    listOf("Kofi", "Github", "Generic", "Fourthwall", "Shopify", "Patreon", "Buymeacoffee")

private fun isGenericAdapter(value: String): Boolean = value.equals("Generic", ignoreCase = true)

@Composable
private fun adapterLabel(value: String): String =
    when (value.lowercase()) {
        "kofi" -> stringResource(Res.string.webhooks_adapter_kofi)
        "github" -> stringResource(Res.string.webhooks_adapter_github)
        "generic" -> stringResource(Res.string.webhooks_adapter_generic)
        "fourthwall" -> stringResource(Res.string.webhooks_adapter_fourthwall)
        "shopify" -> stringResource(Res.string.webhooks_adapter_shopify)
        "patreon" -> stringResource(Res.string.webhooks_adapter_patreon)
        "buymeacoffee" -> stringResource(Res.string.webhooks_adapter_buymeacoffee)
        else -> value
    }

// Mutable holder for the 7 generic-adapter signing fields, so the create/edit dialogs can hoist one object
// instead of 7 pairs of state. Valid when both required JSON paths are filled.
private class GenericConfigInput(initial: GenericInboundConfig?) {
    var signatureHeaderName: String by mutableStateOf(initial?.signatureHeaderName ?: "")
    var signaturePrefix: String by mutableStateOf(initial?.signaturePrefix ?: "")
    var signingStringTemplate: String by mutableStateOf(initial?.signingStringTemplate ?: "")
    var timestampHeaderName: String by mutableStateOf(initial?.timestampHeaderName ?: "")
    var sharedSecretBodyField: String by mutableStateOf(initial?.sharedSecretBodyField ?: "")
    var eventKindJsonPath: String by mutableStateOf(initial?.eventKindJsonPath ?: "")
    var providerEventIdJsonPath: String by mutableStateOf(initial?.providerEventIdJsonPath ?: "")

    val isValid: Boolean
        get() = eventKindJsonPath.isNotBlank() && providerEventIdJsonPath.isNotBlank()

    fun toConfig(): GenericInboundConfig =
        GenericInboundConfig(
            signatureHeaderName = signatureHeaderName.trim().ifBlank { null },
            signaturePrefix = signaturePrefix.trim().ifBlank { null },
            signingStringTemplate = signingStringTemplate.trim().ifBlank { null },
            timestampHeaderName = timestampHeaderName.trim().ifBlank { null },
            sharedSecretBodyField = sharedSecretBodyField.trim().ifBlank { null },
            eventKindJsonPath = eventKindJsonPath.trim(),
            providerEventIdJsonPath = providerEventIdJsonPath.trim(),
        )
}

@Composable
private fun GenericConfigFields(input: GenericConfigInput, showError: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = stringResource(Res.string.webhooks_generic_section), style = typography.sm, color = tokens.foreground)
        Text(text = stringResource(Res.string.webhooks_generic_help), style = typography.xs, color = tokens.mutedForeground)
        AppTextField(
            value = input.eventKindJsonPath,
            onValueChange = { input.eventKindJsonPath = it },
            label = stringResource(Res.string.webhooks_generic_event_kind_path),
            placeholder = stringResource(Res.string.webhooks_generic_event_kind_hint),
            isError = showError && input.eventKindJsonPath.isBlank(),
            errorText = if (showError && input.eventKindJsonPath.isBlank()) stringResource(Res.string.webhooks_generic_required) else null,
        )
        AppTextField(
            value = input.providerEventIdJsonPath,
            onValueChange = { input.providerEventIdJsonPath = it },
            label = stringResource(Res.string.webhooks_generic_event_id_path),
            placeholder = stringResource(Res.string.webhooks_generic_event_id_hint),
            isError = showError && input.providerEventIdJsonPath.isBlank(),
            errorText = if (showError && input.providerEventIdJsonPath.isBlank()) stringResource(Res.string.webhooks_generic_required) else null,
        )
        AppTextField(
            value = input.signatureHeaderName,
            onValueChange = { input.signatureHeaderName = it },
            label = stringResource(Res.string.webhooks_generic_signature_header),
        )
        AppTextField(
            value = input.signaturePrefix,
            onValueChange = { input.signaturePrefix = it },
            label = stringResource(Res.string.webhooks_generic_signature_prefix),
        )
        AppTextField(
            value = input.signingStringTemplate,
            onValueChange = { input.signingStringTemplate = it },
            label = stringResource(Res.string.webhooks_generic_signing_template),
        )
        AppTextField(
            value = input.timestampHeaderName,
            onValueChange = { input.timestampHeaderName = it },
            label = stringResource(Res.string.webhooks_generic_timestamp_header),
        )
        AppTextField(
            value = input.sharedSecretBodyField,
            onValueChange = { input.sharedSecretBodyField = it },
            label = stringResource(Res.string.webhooks_generic_secret_body_field),
        )
    }
}

// Create + edit for an inbound endpoint. [existing] null = create; non-null = edit (the adapter is fixed at
// create so it is shown read-only, and the secret field becomes an optional rotation).
@Composable
private fun InboundDialog(
    existing: InboundWebhook?,
    pipelines: List<PipelineSummary>,
    onConfirmCreate: (name: String, adapter: String, secret: String, targetPipelineId: String?, targetEventType: String?, genericConfig: GenericInboundConfig?) -> Unit,
    onConfirmEdit: (name: String, adapter: String, secret: String?, targetPipelineId: String?, targetEventType: String?, genericConfig: GenericInboundConfig?, enabled: Boolean) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val isEdit: Boolean = existing != null

    var name: String by remember { mutableStateOf(existing?.name ?: "") }
    var adapter: String by remember { mutableStateOf(existing?.adapter?.replaceFirstChar { it.uppercase() } ?: "Generic") }
    var secret: String by remember { mutableStateOf("") }
    var enabled: Boolean by remember { mutableStateOf(existing?.isEnabled ?: true) }
    var nameError: Boolean by remember { mutableStateOf(false) }
    var showGenericError: Boolean by remember { mutableStateOf(false) }

    val genericInput: GenericConfigInput = remember(existing?.id) { GenericConfigInput(existing?.genericConfig) }

    val initialRouting: InboundRouting =
        when {
            !existing?.targetPipelineId.isNullOrBlank() -> InboundRouting.Pipeline
            !existing?.targetEventType.isNullOrBlank() -> InboundRouting.Event
            else -> InboundRouting.None
        }
    var routing: InboundRouting by remember { mutableStateOf(initialRouting) }
    var pipelineId: String by remember { mutableStateOf(existing?.targetPipelineId ?: "") }
    var eventType: String by remember { mutableStateOf(existing?.targetEventType ?: "") }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = if (isEdit) stringResource(Res.string.webhooks_edit_inbound_title) else stringResource(Res.string.webhooks_create_inbound_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                AppTextField(
                    value = name, onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.webhooks_create_inbound_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.webhooks_create_inbound_name_required) else null,
                )

                // The adapter is chosen once, at create. On edit it is fixed (the verification model is bound to it).
                if (isEdit) {
                    Text(
                        text = "${stringResource(Res.string.webhooks_adapter_label)}: ${adapterLabel(adapter)}",
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                } else {
                    AdapterPicker(selected = adapter, onSelect = { adapter = it })
                }

                AppTextField(
                    value = secret, onValueChange = { secret = it },
                    label = if (isEdit) stringResource(Res.string.webhooks_edit_secret_optional) else stringResource(Res.string.webhooks_create_inbound_secret),
                )

                if (isGenericAdapter(adapter)) {
                    GenericConfigFields(input = genericInput, showError = showGenericError)
                }

                // Routing choice: on a verified receive, run a pipeline OR trigger an event (or nothing yet).
                RoutingPicker(selected = routing, onSelect = { routing = it })
                when (routing) {
                    InboundRouting.Pipeline ->
                        // A reference to another table (the channel's pipelines) → the shared search dropdown;
                        // its own empty state replaces the old paste-an-id fallback.
                        EntityPickerField(
                            items = pipelines,
                            selectedId = pipelineId.ifBlank { null },
                            onSelect = { pipelineId = it ?: "" },
                            idOf = { it.id },
                            labelOf = { it.name },
                            label = stringResource(Res.string.webhooks_inbound_routing_pipeline_label),
                            placeholder = stringResource(Res.string.webhooks_inbound_routing_choose_pipeline),
                            emptyText = stringResource(Res.string.pipelines_empty),
                        )
                    InboundRouting.Event ->
                        AppTextField(
                            value = eventType, onValueChange = { eventType = it },
                            label = stringResource(Res.string.webhooks_inbound_routing_event_label),
                            placeholder = stringResource(Res.string.webhooks_inbound_routing_event_hint),
                        )
                    InboundRouting.None -> Unit
                }
                if (routing != InboundRouting.None) {
                    Text(
                        text = stringResource(Res.string.webhooks_inbound_routing_help),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }

                if (isEdit) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
                        Switch(checked = enabled, onCheckedChange = { enabled = it })
                        Text(text = stringResource(Res.string.webhooks_enabled_label), style = typography.sm, color = tokens.foreground)
                    }
                }
            }
        },
        confirmButton = {
            Button(onClick = {
                if (name.isBlank()) { nameError = true; return@Button }
                if (isGenericAdapter(adapter) && !genericInput.isValid) { showGenericError = true; return@Button }
                val targetPipeline: String? = pipelineId.trim().takeIf { routing == InboundRouting.Pipeline && it.isNotBlank() }
                val targetEvent: String? = eventType.trim().takeIf { routing == InboundRouting.Event && it.isNotBlank() }
                val config: GenericInboundConfig? = if (isGenericAdapter(adapter)) genericInput.toConfig() else null
                if (isEdit) {
                    onConfirmEdit(name.trim(), adapter, secret.trim().ifBlank { null }, targetPipeline, targetEvent, config, enabled)
                } else {
                    onConfirmCreate(name.trim(), adapter, secret.trim(), targetPipeline, targetEvent, config)
                }
            }) {
                Text(if (isEdit) stringResource(Res.string.webhooks_edit_confirm) else stringResource(Res.string.webhooks_create_inbound_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(if (isEdit) stringResource(Res.string.webhooks_edit_dismiss) else stringResource(Res.string.webhooks_create_inbound_dismiss))
            }
        },
    )
}

// Labelled dropdown for the inbound adapter kind.
@Composable
private fun AdapterPicker(selected: String, onSelect: (String) -> Unit) {
    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
        Text(text = stringResource(Res.string.webhooks_adapter_label), style = typography.sm, color = tokens.foreground)
        Box(modifier = Modifier.fillMaxWidth()) {
            TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
                Text(text = adapterLabel(selected), color = tokens.foreground, modifier = Modifier.weight(1f), maxLines = 1)
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                InboundAdapters.forEach { option ->
                    DropdownMenuItem(
                        text = { Text(text = adapterLabel(option), color = tokens.popoverForeground) },
                        onClick = { onSelect(option); expanded = false },
                    )
                }
            }
        }
    }
}

// Labelled dropdown for the inbound routing mode.
@Composable
private fun RoutingPicker(selected: InboundRouting, onSelect: (InboundRouting) -> Unit) {
    var expanded: Boolean by remember { mutableStateOf(false) }
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = stringResource(Res.string.webhooks_inbound_routing_label)
    Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
        Text(text = label, style = typography.sm, color = tokens.foreground)
        Box(modifier = Modifier.fillMaxWidth()) {
            TextButton(onClick = { expanded = true }, modifier = Modifier.fillMaxWidth()) {
                Text(text = routingLabel(selected), color = tokens.foreground, modifier = Modifier.weight(1f), maxLines = 1)
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                InboundRouting.entries.forEach { option ->
                    DropdownMenuItem(
                        text = { Text(text = routingLabel(option), color = tokens.popoverForeground) },
                        onClick = { onSelect(option); expanded = false },
                    )
                }
            }
        }
    }
}

@Composable
private fun routingLabel(routing: InboundRouting): String =
    when (routing) {
        InboundRouting.None -> stringResource(Res.string.webhooks_inbound_routing_none)
        InboundRouting.Pipeline -> stringResource(Res.string.webhooks_inbound_routing_pipeline)
        InboundRouting.Event -> stringResource(Res.string.webhooks_inbound_routing_event)
    }

// Create + edit for an outbound endpoint. The event subscription is a CHECKLIST built from the backend
// catalogue (grouped by category) plus a "subscribe to all" wildcard — never a typo-prone free-text box. The
// FQDN/path are create-only (egress-allowlist bound); edit persists name, events and the enabled flag.
@Composable
private fun OutboundDialog(
    existing: OutboundWebhook?,
    catalogue: List<OutboundEventCatalogueEntry>,
    onConfirmCreate: (name: String, fqdn: String, path: String?, events: List<String>) -> Unit,
    onConfirmEdit: (name: String, events: List<String>, enabled: Boolean) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val isEdit: Boolean = existing != null

    var name: String by remember { mutableStateOf(existing?.name ?: "") }
    var fqdn: String by remember { mutableStateOf(existing?.fqdn ?: "") }
    var path: String by remember { mutableStateOf(existing?.path ?: "") }
    var enabled: Boolean by remember { mutableStateOf(existing?.isEnabled ?: true) }
    var nameError: Boolean by remember { mutableStateOf(false) }
    var fqdnError: Boolean by remember { mutableStateOf(false) }
    var eventsError: Boolean by remember { mutableStateOf(false) }

    var allEvents: Boolean by remember { mutableStateOf(existing?.subscribedEventTypes?.contains("*") ?: false) }
    var selected: Set<String> by remember {
        mutableStateOf(existing?.subscribedEventTypes?.filter { it != "*" }?.toSet() ?: emptySet())
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = if (isEdit) stringResource(Res.string.webhooks_edit_outbound_title) else stringResource(Res.string.webhooks_create_outbound_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s3),
            ) {
                AppTextField(
                    value = name, onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.webhooks_create_outbound_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.webhooks_create_outbound_name_required) else null,
                )
                if (isEdit) {
                    // FQDN/path are fixed at create (egress-allowlist bound) — shown read-only for context.
                    Text(
                        text = stringResource(Res.string.webhooks_outbound_target_readonly, "${fqdn}${path}"),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                } else {
                    AppTextField(
                        value = fqdn, onValueChange = { fqdn = it; fqdnError = false },
                        label = stringResource(Res.string.webhooks_create_outbound_fqdn),
                        isError = fqdnError,
                        errorText = if (fqdnError) stringResource(Res.string.webhooks_create_outbound_fqdn_required) else null,
                    )
                    AppTextField(
                        value = path, onValueChange = { path = it },
                        label = stringResource(Res.string.webhooks_create_outbound_path),
                    )
                }

                EventChecklist(
                    catalogue = catalogue,
                    allEvents = allEvents,
                    selected = selected,
                    showError = eventsError,
                    onToggleAll = { allEvents = it; eventsError = false },
                    onToggleEvent = { type, on ->
                        selected = if (on) selected + type else selected - type
                        eventsError = false
                    },
                )

                if (isEdit) {
                    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
                        Switch(checked = enabled, onCheckedChange = { enabled = it })
                        Text(text = stringResource(Res.string.webhooks_enabled_label), style = typography.sm, color = tokens.foreground)
                    }
                }
            }
        },
        confirmButton = {
            Button(onClick = {
                var valid: Boolean = true
                if (name.isBlank()) { nameError = true; valid = false }
                if (!isEdit && fqdn.isBlank()) { fqdnError = true; valid = false }
                if (!allEvents && selected.isEmpty()) { eventsError = true; valid = false }
                if (!valid) return@Button
                val events: List<String> = if (allEvents) listOf("*") else selected.toList()
                if (isEdit) {
                    onConfirmEdit(name.trim(), events, enabled)
                } else {
                    onConfirmCreate(name.trim(), fqdn.trim(), path.trim().takeIf { it.isNotBlank() }, events)
                }
            }) {
                Text(if (isEdit) stringResource(Res.string.webhooks_edit_confirm) else stringResource(Res.string.webhooks_create_outbound_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(if (isEdit) stringResource(Res.string.webhooks_edit_dismiss) else stringResource(Res.string.webhooks_create_outbound_dismiss))
            }
        },
    )
}

// The outbound event subscription checklist: a "subscribe to all" wildcard toggle, then the catalogue grouped
// by category with a switch per event. Replaces the old comma-separated free-text field (typo-silent-fail).
@Composable
private fun EventChecklist(
    catalogue: List<OutboundEventCatalogueEntry>,
    allEvents: Boolean,
    selected: Set<String>,
    showError: Boolean,
    onToggleAll: (Boolean) -> Unit,
    onToggleEvent: (String, Boolean) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(text = stringResource(Res.string.webhooks_create_outbound_events), style = typography.sm, color = tokens.foreground)
            if (!allEvents && selected.isNotEmpty()) {
                Text(
                    text = stringResource(Res.string.webhooks_events_selected_count, selected.size),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }

        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Switch(checked = allEvents, onCheckedChange = { onToggleAll(it) })
            Column(modifier = Modifier.weight(1f)) {
                Text(text = stringResource(Res.string.webhooks_events_select_all), style = typography.sm, color = tokens.foreground)
                Text(text = stringResource(Res.string.webhooks_events_select_all_hint), style = typography.xs, color = tokens.mutedForeground)
            }
        }

        if (!allEvents) {
            if (catalogue.isEmpty()) {
                Text(text = stringResource(Res.string.webhooks_events_unavailable), style = typography.xs, color = tokens.mutedForeground)
            } else {
                Column(
                    modifier = Modifier.fillMaxWidth().heightIn(max = spacing.s24 * 3).verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s2),
                ) {
                    catalogue.groupBy { it.category }.forEach { (category, entries) ->
                        Text(text = category, style = typography.xs, color = tokens.mutedForeground)
                        entries.forEach { entry ->
                            Row(
                                modifier = Modifier.fillMaxWidth(),
                                verticalAlignment = Alignment.CenterVertically,
                                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                            ) {
                                Switch(
                                    checked = selected.contains(entry.eventType),
                                    onCheckedChange = { onToggleEvent(entry.eventType, it) },
                                )
                                Text(
                                    text = entry.label,
                                    style = typography.sm,
                                    color = tokens.foreground,
                                    modifier = Modifier.weight(1f),
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                        }
                    }
                }
            }
        }

        if (showError) {
            Text(text = stringResource(Res.string.webhooks_events_none_selected), style = typography.xs, color = tokens.destructive)
        }
    }
}

// The delivery log for one outbound endpoint — recent attempts (event, status, HTTP code, timestamp) so an
// integration is debuggable. Loads on open via the passed suspend fetch.
@Composable
private fun DeliveriesDialog(
    endpoint: OutboundWebhook,
    load: suspend () -> List<OutboundDelivery>?,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var loading: Boolean by remember { mutableStateOf(true) }
    var deliveries: List<OutboundDelivery> by remember { mutableStateOf(emptyList()) }

    LaunchedEffect(endpoint.id) {
        deliveries = load() ?: emptyList()
        loading = false
    }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(stringResource(Res.string.webhooks_deliveries_title, endpoint.name), style = typography.lg, color = tokens.cardForeground)
        },
        text = {
            Column(
                modifier = Modifier.fillMaxWidth().heightIn(max = spacing.s24 * 5).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                when {
                    loading ->
                        Text(stringResource(Res.string.webhooks_deliveries_loading), style = typography.sm, color = tokens.mutedForeground)
                    deliveries.isEmpty() ->
                        Text(stringResource(Res.string.webhooks_deliveries_empty), style = typography.sm, color = tokens.mutedForeground)
                    else ->
                        deliveries.forEachIndexed { index, delivery ->
                            DeliveryRow(delivery)
                            if (index < deliveries.lastIndex) Separator()
                        }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) { Text(stringResource(Res.string.webhooks_deliveries_close)) }
        },
    )
}

@Composable
private fun DeliveryRow(delivery: OutboundDelivery) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusColor = when (delivery.status.lowercase()) {
        "delivered" -> tokens.primary
        "failed", "deadletter" -> tokens.destructive
        else -> tokens.mutedForeground
    }

    val attemptText: String = stringResource(Res.string.webhooks_deliveries_attempt, delivery.attempt)
    val codeText: String? = delivery.responseCode?.let { stringResource(Res.string.webhooks_deliveries_code, it) }
    val meta: String = buildList {
        add(attemptText)
        codeText?.let { add(it) }
        delivery.durationMs?.let { add("${it}ms") }
        delivery.createdAt.takeIf { it.isNotBlank() }?.let { add(it) }
    }.joinToString(" · ")

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
            Text(text = delivery.eventType, style = typography.sm, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis, modifier = Modifier.weight(1f))
            Text(text = delivery.status, style = typography.xs, color = statusColor)
        }
        Text(text = meta, style = typography.xs, color = tokens.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
        delivery.error?.takeIf { it.isNotBlank() }?.let {
            Text(text = it, style = typography.xs, color = tokens.destructive, maxLines = 2, overflow = TextOverflow.Ellipsis)
        }
    }
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
