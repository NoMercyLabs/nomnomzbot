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
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.Input
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AdminEventReplayPreview
import bot.nomnomz.dashboard.core.network.AdminEventSubTenantHealth
import bot.nomnomz.dashboard.core.network.AdminEventSubTopicHealth
import bot.nomnomz.dashboard.core.network.AdminReplayableProjection
import bot.nomnomz.dashboard.core.network.AdminScheduledJob
import bot.nomnomz.dashboard.core.network.AdminTenantErrorBudget
import bot.nomnomz.dashboard.core.network.AdminTenantUsage
import bot.nomnomz.dashboard.core.network.AdminWebhookDelivery
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlin.math.round
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_error_budget_attempts
import nomnomzbot.composeapp.generated.resources.admin_error_budget_empty
import nomnomzbot.composeapp.generated.resources.admin_error_budget_label
import nomnomzbot.composeapp.generated.resources.admin_error_budget_remaining
import nomnomzbot.composeapp.generated.resources.admin_error_budget_window
import nomnomzbot.composeapp.generated.resources.admin_eventsub_health_empty
import nomnomzbot.composeapp.generated.resources.admin_eventsub_health_last_confirmed
import nomnomzbot.composeapp.generated.resources.admin_eventsub_health_topic_label
import nomnomzbot.composeapp.generated.resources.admin_job_label
import nomnomzbot.composeapp.generated.resources.admin_job_retry
import nomnomzbot.composeapp.generated.resources.admin_job_retry_confirm_action
import nomnomzbot.composeapp.generated.resources.admin_job_retry_confirm_body
import nomnomzbot.composeapp.generated.resources.admin_job_retry_confirm_title
import nomnomzbot.composeapp.generated.resources.admin_job_retry_disabled_reason
import nomnomzbot.composeapp.generated.resources.admin_replay_confirm_action
import nomnomzbot.composeapp.generated.resources.admin_replay_confirm_body
import nomnomzbot.composeapp.generated.resources.admin_replay_confirm_title
import nomnomzbot.composeapp.generated.resources.admin_replay_event_type_label
import nomnomzbot.composeapp.generated.resources.admin_replay_event_type_placeholder
import nomnomzbot.composeapp.generated.resources.admin_replay_execute_action
import nomnomzbot.composeapp.generated.resources.admin_replay_from_label
import nomnomzbot.composeapp.generated.resources.admin_replay_preview_action
import nomnomzbot.composeapp.generated.resources.admin_replay_preview_result
import nomnomzbot.composeapp.generated.resources.admin_replay_projection_label
import nomnomzbot.composeapp.generated.resources.admin_replay_projection_placeholder
import nomnomzbot.composeapp.generated.resources.admin_replay_projections_empty
import nomnomzbot.composeapp.generated.resources.admin_replay_result_applied
import nomnomzbot.composeapp.generated.resources.admin_replay_scope_title
import nomnomzbot.composeapp.generated.resources.admin_replay_subscribed_types
import nomnomzbot.composeapp.generated.resources.admin_replay_subscribed_types_all
import nomnomzbot.composeapp.generated.resources.admin_replay_tenant_label
import nomnomzbot.composeapp.generated.resources.admin_replay_to_label
import nomnomzbot.composeapp.generated.resources.admin_scheduled_jobs_empty
import nomnomzbot.composeapp.generated.resources.admin_tenant_usage_empty
import nomnomzbot.composeapp.generated.resources.admin_tenant_usage_label
import nomnomzbot.composeapp.generated.resources.admin_tenant_usage_period
import nomnomzbot.composeapp.generated.resources.admin_tenant_usage_tts_characters
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
private const val JOB_STATUS_FAILED: String = "failed"

/** Formats a 0..1 fraction as a one-decimal percent string in Kotlin — compose-resources ignores
 * width/flag specifiers on positional format args, so rounding/percent formatting never happens inside
 * the string-resource template itself. */
private fun formatPercent(fraction: Double): String {
    val rounded: Double = round(fraction * 1000) / 10.0
    return "$rounded%"
}

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

/**
 * The REAL background job queue (S-ADMIN-6b): every `ScheduledPipelineTask` row — never a fabricated parallel
 * queue — with its actual queued/running/succeeded/failed/cancelled state, and a retry trigger that shows
 * exactly which pipeline it will re-run BEFORE the retry commits. The commit itself schedules a genuinely NEW
 * deferred run; it never mutates the failed attempt being retried.
 */
@Composable
internal fun ScheduledJobsTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.scheduledJobsError?.let { ActionErrorBanner(message = it) }
        state.retryError?.let { ActionErrorBanner(message = it) }

        if (state.scheduledJobsLoading) {
            Spinner(color = tokens.primary)
        } else if (state.scheduledJobs.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_scheduled_jobs_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.scheduledJobs.forEachIndexed { index, job ->
                        ScheduledJobRow(
                            job = job,
                            onRetry = { controller.stageScheduledJobRetry(job.id) },
                        )
                        if (index < state.scheduledJobs.lastIndex) Separator()
                    }
                }
            }
        }
    }

    val pending: AdminScheduledJob? =
        state.scheduledJobs.firstOrNull { it.id == state.retryPendingJobId }
    if (pending != null) {
        ScheduledJobRetryConfirmDialog(
            job = pending,
            onDismiss = { controller.dismissScheduledJobRetry() },
            onConfirm = { scope.launch { controller.confirmScheduledJobRetry() } },
        )
    }
}

@Composable
private fun ScheduledJobRow(job: AdminScheduledJob, onRetry: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val failed: Boolean = job.displayState.equals(JOB_STATUS_FAILED, ignoreCase = true)

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                Text(
                    text = resolveRowLabel(
                        primary = job.pipelineName ?: job.pipelineId,
                        secondary = job.channelDisplayName,
                        typeLabel = stringResource(Res.string.admin_job_label),
                        discriminatorSource = job.id,
                    ),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                Badge(variant = if (failed) BadgeVariant.Destructive else BadgeVariant.Secondary) {
                    Text(text = job.displayState, style = typography.xs)
                }
            }
            Text(text = job.triggeredByDisplayName, style = typography.xs, color = tokens.mutedForeground)
            Text(text = job.dueAt, style = typography.xs, color = tokens.mutedForeground)
            if (!job.pipelineExists) {
                Text(
                    text = stringResource(Res.string.admin_job_retry_disabled_reason),
                    style = typography.xs,
                    color = tokens.destructive,
                )
            }
        }

        // Retry is a side-effecting re-dispatch, never level with the row's own text — a quiet outline
        // trigger here, disabled unless the job genuinely failed and its pipeline still exists; the loud
        // commit lives in the confirm dialog, which names exactly which pipeline it will re-run.
        Column(horizontalAlignment = Alignment.End) {
            Button(variant = ButtonVariant.Outline, onClick = onRetry, enabled = job.canRetry) {
                Text(text = stringResource(Res.string.admin_job_retry))
            }
            if (!job.canRetry) {
                Text(
                    text = stringResource(Res.string.admin_job_retry_disabled_reason),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
    }
}

/** Shows exactly which pipeline a retry will re-run BEFORE the retry commits. Confirming schedules a
 * genuinely NEW deferred run; it never mutates the failed attempt being retried. */
@Composable
private fun ScheduledJobRetryConfirmDialog(job: AdminScheduledJob, onDismiss: () -> Unit, onConfirm: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_job_retry_confirm_title))
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(
            text = stringResource(
                Res.string.admin_job_retry_confirm_body,
                job.pipelineName ?: job.pipelineId,
                job.channelDisplayName,
            ),
            style = typography.sm,
            color = tokens.foreground,
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(variant = ButtonVariant.Outline, onClick = onConfirm) {
                Text(text = stringResource(Res.string.admin_job_retry_confirm_action))
            }
        }
    }
}

/**
 * Per-tenant usage for each tenant's most recent metering period (S-ADMIN-6b), computed purely from recorded
 * `UsageRecord`/`TtsUsageRecord` rows — never a fabricated currency figure, since no per-unit price table
 * exists in this codebase. The period each row covers is stated explicitly.
 */
@Composable
internal fun TenantUsageTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.tenantUsageError?.let { ActionErrorBanner(message = it) }

        if (state.tenantUsageLoading) {
            Spinner(color = tokens.primary)
        } else if (state.tenantUsage.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_tenant_usage_empty))
        } else {
            state.tenantUsage.forEach { usage -> TenantUsageCard(usage) }
        }
    }
}

@Composable
private fun TenantUsageCard(usage: AdminTenantUsage) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column {
            Text(
                text = resolveRowLabel(
                    primary = usage.channelDisplayName,
                    typeLabel = stringResource(Res.string.admin_tenant_usage_label),
                    discriminatorSource = usage.broadcasterId,
                ),
                style = typography.sm,
                color = tokens.cardForeground,
                modifier = Modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
            )
            Separator()
            Column(
                modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = stringResource(Res.string.admin_tenant_usage_period, usage.periodStart, usage.periodEnd),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                usage.metrics.forEach { metric ->
                    Text(
                        text = "${metric.metricKey}: ${metric.quantity}",
                        style = typography.sm,
                        color = tokens.cardForeground,
                    )
                }
                Text(
                    text = stringResource(Res.string.admin_tenant_usage_tts_characters, usage.ttsCharacterCount),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
            }
        }
    }
}

/**
 * Per-tenant error budget for the trailing 24-hour window (S-ADMIN-6c), computed purely from real
 * `OutboundWebhookDelivery` outcomes — never a fabricated percentage. One tenant's failures never count
 * toward another's; a tenant with nothing resolved in the window is simply absent from the page.
 */
@Composable
internal fun ErrorBudgetTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.errorBudgetError?.let { ActionErrorBanner(message = it) }

        if (state.errorBudgetLoading) {
            Spinner(color = tokens.primary)
        } else if (state.errorBudget.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_error_budget_empty))
        } else {
            state.errorBudget.forEach { budget -> ErrorBudgetCard(budget) }
        }
    }
}

@Composable
private fun ErrorBudgetCard(budget: AdminTenantErrorBudget) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    // Budget fully consumed (actual error rate exceeds the policy's allowed rate) reads as a real risk
    // signal on the row itself, not just a number the operator has to do arithmetic on.
    val overBudget: Boolean = (budget.budgetRemainingFraction ?: 1.0) < 0.0

    Card(modifier = Modifier.fillMaxWidth()) {
        Column {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
                modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
            ) {
                Text(
                    text = resolveRowLabel(
                        primary = budget.channelDisplayName,
                        typeLabel = stringResource(Res.string.admin_error_budget_label),
                        discriminatorSource = budget.broadcasterId,
                    ),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                budget.errorRate?.let { rate ->
                    Badge(variant = if (overBudget) BadgeVariant.Destructive else BadgeVariant.Secondary) {
                        Text(text = formatPercent(rate), style = typography.xs)
                    }
                }
            }
            Separator()
            Column(
                modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
                verticalArrangement = Arrangement.spacedBy(spacing.s1),
            ) {
                Text(
                    text = stringResource(
                        Res.string.admin_error_budget_window,
                        budget.windowStartUtc,
                        budget.windowEndUtc,
                    ),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                Text(
                    text = stringResource(Res.string.admin_error_budget_attempts, budget.attempts, budget.errors),
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                budget.budgetRemainingFraction?.let { remaining ->
                    Text(
                        text = stringResource(Res.string.admin_error_budget_remaining, formatPercent(remaining)),
                        style = typography.sm,
                        color = if (remaining < 0.0) tokens.destructive else tokens.cardForeground,
                    )
                }
            }
        }
    }
}

/**
 * Cross-tenant event-store replay (S-ADMIN-6c): the operator picks a real registered projection, a tenant, a
 * window, and an optional event type, previews the REAL count that scope would replay, and only then can
 * confirm — the confirm control is unreachable until a genuine counted preview exists, and any scope edit
 * clears it so a stale count can never be replayed against inputs the operator has since changed.
 */
@Composable
internal fun EventReplayTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    var projectionMenuOpen: Boolean by remember { mutableStateOf(false) }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.replayProjectionsError?.let { ActionErrorBanner(message = it) }
        state.replayPreviewError?.let { ActionErrorBanner(message = it) }
        state.replayExecuteError?.let { ActionErrorBanner(message = it) }

        if (state.replayProjectionsLoading) {
            Spinner(color = tokens.primary)
        } else if (state.replayProjections.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_replay_projections_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column(
                    modifier = Modifier.padding(spacing.s4),
                    verticalArrangement = Arrangement.spacedBy(spacing.s3),
                ) {
                    Text(
                        text = stringResource(Res.string.admin_replay_scope_title),
                        style = typography.sm,
                        color = tokens.cardForeground,
                    )

                    AppSelectField(
                        label = stringResource(Res.string.admin_replay_projection_label),
                        value = state.replayProjectionName,
                        expanded = projectionMenuOpen,
                        onExpandedChange = { projectionMenuOpen = it },
                        modifier = Modifier.fillMaxWidth(),
                        placeholder = stringResource(Res.string.admin_replay_projection_placeholder),
                        menu = { ReplayProjectionMenuItems(state, controller) { projectionMenuOpen = false } },
                    )

                    val selectedProjection: AdminReplayableProjection? =
                        state.replayProjections.firstOrNull { it.projectionName == state.replayProjectionName }
                    selectedProjection?.let { projection ->
                        Text(
                            text = stringResource(
                                Res.string.admin_replay_subscribed_types,
                                projection.subscribedEventTypes.takeIf { it.isNotEmpty() }
                                    ?.joinToString(", ")
                                    ?: stringResource(Res.string.admin_replay_subscribed_types_all),
                            ),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                    }

                    Input(
                        value = state.replayBroadcasterId,
                        onValueChange = { controller.setReplayScope(broadcasterId = it) },
                        label = stringResource(Res.string.admin_replay_tenant_label),
                        modifier = Modifier.fillMaxWidth(),
                    )

                    Row(
                        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                        modifier = Modifier.fillMaxWidth(),
                    ) {
                        Input(
                            value = state.replayFromUtc,
                            onValueChange = { controller.setReplayScope(fromUtc = it) },
                            label = stringResource(Res.string.admin_replay_from_label),
                            modifier = Modifier.weight(1f),
                        )
                        Input(
                            value = state.replayToUtc,
                            onValueChange = { controller.setReplayScope(toUtc = it) },
                            label = stringResource(Res.string.admin_replay_to_label),
                            modifier = Modifier.weight(1f),
                        )
                    }

                    Input(
                        value = state.replayEventType,
                        onValueChange = { controller.setReplayScope(eventType = it) },
                        label = stringResource(Res.string.admin_replay_event_type_label),
                        modifier = Modifier.fillMaxWidth(),
                        placeholder = stringResource(Res.string.admin_replay_event_type_placeholder),
                    )

                    val scopeComplete: Boolean =
                        state.replayProjectionName.isNotBlank() &&
                            state.replayBroadcasterId.isNotBlank() &&
                            state.replayFromUtc.isNotBlank() &&
                            state.replayToUtc.isNotBlank()

                    // Exactly ONE action is ever visible here: Preview until a real count exists, then the
                    // replay trigger replaces it — never both at once, so there is always a single primary
                    // action in this group. Preview is quiet (Outline, nothing happens yet); the replay
                    // trigger below only ever appears once a genuine counted preview backs it.
                    if (state.replayPreview == null) {
                        Button(
                            variant = ButtonVariant.Outline,
                            onClick = { scope.launch { controller.previewEventReplay() } },
                            enabled = scopeComplete && !state.replayPreviewLoading,
                        ) {
                            Text(text = stringResource(Res.string.admin_replay_preview_action))
                        }
                        if (state.replayPreviewLoading) {
                            Spinner(color = tokens.primary)
                        }
                    } else {
                        val preview: AdminEventReplayPreview = state.replayPreview
                        Separator()
                        Text(
                            text = stringResource(
                                Res.string.admin_replay_preview_result,
                                preview.matchingEventCount,
                                preview.projectionName,
                            ),
                            style = typography.sm,
                            color = tokens.cardForeground,
                        )
                        Button(
                            variant = ButtonVariant.Outline,
                            onClick = { controller.stageEventReplay() },
                            enabled = !state.replayExecuting,
                        ) {
                            Text(
                                text = stringResource(
                                    Res.string.admin_replay_execute_action,
                                    preview.matchingEventCount,
                                ),
                            )
                        }
                    }

                    state.replayResult?.let { result ->
                        Text(
                            text = stringResource(Res.string.admin_replay_result_applied, result.appliedCount),
                            style = typography.xs,
                            color = tokens.mutedForeground,
                        )
                    }
                }
            }
        }
    }

    val pendingPreview: AdminEventReplayPreview? = state.replayPreview
    if (state.replayConfirmOpen && pendingPreview != null) {
        EventReplayConfirmDialog(
            preview = pendingPreview,
            onDismiss = { controller.dismissEventReplay() },
            onConfirm = { scope.launch { controller.confirmEventReplay() } },
        )
    }
}

@Composable
private fun ReplayProjectionMenuItems(state: AdminState, controller: AdminController, onSelected: () -> Unit) {
    val tokens = LocalTokens.current
    state.replayProjections.forEach { projection ->
        DropdownMenuItem(
            text = { Text(text = projection.projectionName, color = tokens.cardForeground) },
            onClick = {
                controller.setReplayScope(projectionName = projection.projectionName)
                onSelected()
            },
        )
    }
}

/** Shows exactly what a replay will re-apply — the counted preview, the target projection, and the tenant —
 * BEFORE the replay commits. Confirming re-applies exactly that many events; the journal itself is never
 * mutated, only the projection's read model is re-folded. */
@Composable
private fun EventReplayConfirmDialog(preview: AdminEventReplayPreview, onDismiss: () -> Unit, onConfirm: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_replay_confirm_title))
        Spacer(modifier = Modifier.height(spacing.s2))
        Text(
            text = stringResource(
                Res.string.admin_replay_confirm_body,
                preview.matchingEventCount,
                preview.projectionName,
                preview.broadcasterId,
            ),
            style = typography.sm,
            color = tokens.foreground,
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(variant = ButtonVariant.Outline, onClick = onConfirm) {
                Text(text = stringResource(Res.string.admin_replay_confirm_action))
            }
        }
    }
}
