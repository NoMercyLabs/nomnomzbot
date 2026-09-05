// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AdminEventSubTenantHealth
import bot.nomnomz.dashboard.core.network.AdminEventSubTopicHealth
import bot.nomnomz.dashboard.core.network.AdminWebhookDelivery
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_eventsub_health_empty
import nomnomzbot.composeapp.generated.resources.admin_eventsub_health_last_confirmed
import nomnomzbot.composeapp.generated.resources.admin_eventsub_health_topic_label
import nomnomzbot.composeapp.generated.resources.admin_webhook_delivery_attempt
import nomnomzbot.composeapp.generated.resources.admin_webhook_delivery_label
import nomnomzbot.composeapp.generated.resources.admin_webhook_delivery_response_code
import nomnomzbot.composeapp.generated.resources.admin_webhook_deliveries_empty
import nomnomzbot.composeapp.generated.resources.admin_webhook_replay
import nomnomzbot.composeapp.generated.resources.admin_webhook_replay_confirm_action
import nomnomzbot.composeapp.generated.resources.admin_webhook_replay_confirm_body
import nomnomzbot.composeapp.generated.resources.admin_webhook_replay_confirm_title
import nomnomzbot.composeapp.generated.resources.admin_webhook_replay_disabled_reason
import org.jetbrains.compose.resources.stringResource

private const val STATUS_ENABLED: String = "enabled"
private const val STATUS_DELIVERED: String = "Delivered"

/**
 * Per-tenant EventSub subscription health (S-ADMIN-6a): which topics are subscribed for which broadcaster,
 * their REAL registry state, and when each was last confirmed — read straight off
 * `TwitchEventSubHostedService`'s own registry rows, never a fabricated "all healthy" list.
 */
@Composable
internal fun EventSubHealthTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.eventSubHealthError?.let { ActionErrorBanner(message = it) }

        if (state.eventSubHealthLoading) {
            Spinner(color = tokens.primary)
        } else if (state.eventSubHealth.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_eventsub_health_empty))
        } else {
            state.eventSubHealth.forEach { tenant -> EventSubTenantCard(tenant) }
        }
    }
}

@Composable
private fun EventSubTenantCard(tenant: AdminEventSubTenantHealth) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column {
            Text(
                text = resolveRowLabel(
                    primary = tenant.channelDisplayName,
                    typeLabel = "Channel",
                    discriminatorSource = tenant.broadcasterId,
                ),
                style = typography.sm,
                color = tokens.cardForeground,
                modifier = Modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
            )
            Separator()
            tenant.topics.forEachIndexed { index, topic ->
                EventSubTopicRow(topic)
                if (index < tenant.topics.lastIndex) Separator()
            }
        }
    }
}

@Composable
private fun EventSubTopicRow(topic: AdminEventSubTopicHealth) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val healthy: Boolean = topic.status.equals(STATUS_ENABLED, ignoreCase = true) && topic.enabled

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(
                text = "${topic.eventType} (v${topic.version})",
                style = typography.sm,
                color = tokens.cardForeground,
                modifier = Modifier.weight(1f),
            )
            Badge(variant = if (healthy) BadgeVariant.Secondary else BadgeVariant.Destructive) {
                Text(text = topic.status, style = typography.xs)
            }
        }
        Text(
            text = stringResource(Res.string.admin_eventsub_health_last_confirmed, topic.lastConfirmedAt),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        topic.lastError?.takeIf { it.isNotBlank() }?.let {
            Text(text = it, style = typography.xs, color = tokens.destructive)
        }
    }
}

/**
 * Cross-tenant outbound webhook delivery log with replay (S-ADMIN-6a): status, response code and timestamp
 * per attempt, and a replay trigger that shows exactly what it will re-send BEFORE the send commits — the
 * commit itself sends a genuinely NEW attempt, never mutates the row being replayed.
 */
@Composable
internal fun WebhookDeliveriesTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.webhookDeliveriesError?.let { ActionErrorBanner(message = it) }
        state.replayError?.let { ActionErrorBanner(message = it) }

        if (state.webhookDeliveriesLoading) {
            Spinner(color = tokens.primary)
        } else if (state.webhookDeliveries.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_webhook_deliveries_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.webhookDeliveries.forEachIndexed { index, delivery ->
                        WebhookDeliveryRow(
                            delivery = delivery,
                            onReplay = { controller.stageWebhookReplay(delivery.id) },
                        )
                        if (index < state.webhookDeliveries.lastIndex) Separator()
                    }
                }
            }
        }
    }

    val pending: AdminWebhookDelivery? =
        state.webhookDeliveries.firstOrNull { it.id == state.replayPendingDeliveryId }
    if (pending != null) {
        WebhookReplayConfirmDialog(
            delivery = pending,
            onDismiss = { controller.dismissWebhookReplay() },
            onConfirm = { scope.launch { controller.confirmWebhookReplay() } },
        )
    }
}

@Composable
private fun WebhookDeliveryRow(delivery: AdminWebhookDelivery, onReplay: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val delivered: Boolean = delivery.status.equals(STATUS_DELIVERED, ignoreCase = true)

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = resolveRowLabel(
                        primary = delivery.endpointName,
                        secondary = delivery.eventType,
                        typeLabel = stringResource(Res.string.admin_webhook_delivery_label),
                        discriminatorSource = delivery.id.toString(),
                    ),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                Badge(variant = if (delivered) BadgeVariant.Secondary else BadgeVariant.Destructive) {
                    Text(text = delivery.status, style = typography.xs)
                }
            }
            Text(text = delivery.eventType, style = typography.xs, color = tokens.mutedForeground)
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(
                    text = stringResource(Res.string.admin_webhook_delivery_attempt, delivery.attempt),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                delivery.responseCode?.let { code ->
                    Text(
                        text = stringResource(Res.string.admin_webhook_delivery_response_code, code),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
            Text(text = delivery.createdAt, style = typography.xs, color = tokens.mutedForeground)
            delivery.error?.takeIf { it.isNotBlank() }?.let {
                Text(text = it, style = typography.xs, color = tokens.destructive)
            }
        }

        // Replay is a side-effecting outbound send, never level with the row's own text — a quiet outline
        // trigger here, disabled when the endpoint can no longer receive it; the loud commit lives in the
        // confirm dialog, which shows exactly what will be re-sent before it sends.
        Column(horizontalAlignment = Alignment.End) {
            Button(variant = ButtonVariant.Outline, onClick = onReplay, enabled = delivery.endpointCanReplay) {
                Text(text = stringResource(Res.string.admin_webhook_replay))
            }
            if (!delivery.endpointCanReplay) {
                Text(
                    text = stringResource(Res.string.admin_webhook_replay_disabled_reason),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
    }
}

/** Shows exactly what a replay will re-send — the event type and the target endpoint — BEFORE the send
 * commits. Confirming sends a genuinely NEW delivery attempt; it never mutates the row being replayed. */
@Composable
private fun WebhookReplayConfirmDialog(delivery: AdminWebhookDelivery, onDismiss: () -> Unit, onConfirm: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_webhook_replay_confirm_title))
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(
            text = stringResource(
                Res.string.admin_webhook_replay_confirm_body,
                delivery.eventType,
                delivery.endpointName,
            ),
            style = typography.sm,
            color = tokens.foreground,
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(variant = ButtonVariant.Outline, onClick = onConfirm) {
                Text(text = stringResource(Res.string.admin_webhook_replay_confirm_action))
            }
        }
    }
}
