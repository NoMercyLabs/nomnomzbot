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
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ErasureRequest
import bot.nomnomz.dashboard.core.network.ErasureRequestStatuses
import bot.nomnomz.dashboard.core.network.ErasureRequestSummary
import bot.nomnomz.dashboard.core.network.ErasureRequestTypes
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_data_requests_empty
import nomnomzbot.composeapp.generated.resources.admin_data_requests_filter_all
import nomnomzbot.composeapp.generated.resources.admin_data_requests_intro
import nomnomzbot.composeapp.generated.resources.admin_data_requests_requested_by_broadcaster
import nomnomzbot.composeapp.generated.resources.admin_data_requests_requested_by_platform
import nomnomzbot.composeapp.generated.resources.admin_data_requests_requested_by_self
import nomnomzbot.composeapp.generated.resources.admin_data_requests_rows_affected
import nomnomzbot.composeapp.generated.resources.admin_data_requests_scope_channel
import nomnomzbot.composeapp.generated.resources.admin_data_requests_scope_deployment
import nomnomzbot.composeapp.generated.resources.admin_data_requests_scope_instance
import nomnomzbot.composeapp.generated.resources.admin_data_requests_status_cancelled
import nomnomzbot.composeapp.generated.resources.admin_data_requests_status_completed
import nomnomzbot.composeapp.generated.resources.admin_data_requests_status_failed
import nomnomzbot.composeapp.generated.resources.admin_data_requests_status_pending
import nomnomzbot.composeapp.generated.resources.admin_data_requests_status_running
import nomnomzbot.composeapp.generated.resources.admin_data_requests_subject
import nomnomzbot.composeapp.generated.resources.admin_data_requests_summary
import nomnomzbot.composeapp.generated.resources.admin_data_requests_type_erasure
import nomnomzbot.composeapp.generated.resources.admin_data_requests_type_export
import nomnomzbot.composeapp.generated.resources.admin_data_requests_type_opt_out
import org.jetbrains.compose.resources.stringResource

/** How much of the subject hash a row shows — enough to tell two subjects apart, never the full 64 hex chars. */
private const val SUBJECT_HASH_PREFIX_LENGTH: Int = 12

/**
 * The platform-wide GDPR request monitor (A7): every subject's erasure / export / opt-out request from the
 * real ledger, counted by status above the list and narrowed by two chip rows. A failed request shows its
 * recorded failure reason inline — the row an operator has to act on must never look like a completed one.
 * Read-only by design: acting on a subject (an operator erasure or export) lives on the subject's own page.
 */
@Composable
internal fun DataRequestsTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    fun reload(status: String? = state.dataRequestStatusFilter, requestType: String? = state.dataRequestTypeFilter) {
        scope.launch { controller.loadDataRequests(status = status, requestType = requestType) }
    }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(text = stringResource(Res.string.admin_data_requests_intro), style = typography.sm, color = tokens.mutedForeground)
        state.dataRequestsError?.let { InlineError(message = it) }
        state.dataRequestSummary?.let { SummaryLine(it) }

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            FilterChip(stringResource(Res.string.admin_data_requests_filter_all), state.dataRequestStatusFilter == null) {
                reload(status = null)
            }
            FilterChip(
                stringResource(Res.string.admin_data_requests_status_running),
                state.dataRequestStatusFilter == ErasureRequestStatuses.RUNNING,
            ) { reload(status = ErasureRequestStatuses.RUNNING) }
            FilterChip(
                stringResource(Res.string.admin_data_requests_status_completed),
                state.dataRequestStatusFilter == ErasureRequestStatuses.COMPLETED,
            ) { reload(status = ErasureRequestStatuses.COMPLETED) }
            FilterChip(
                stringResource(Res.string.admin_data_requests_status_failed),
                state.dataRequestStatusFilter == ErasureRequestStatuses.FAILED,
            ) { reload(status = ErasureRequestStatuses.FAILED) }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            FilterChip(stringResource(Res.string.admin_data_requests_filter_all), state.dataRequestTypeFilter == null) {
                reload(requestType = null)
            }
            FilterChip(
                stringResource(Res.string.admin_data_requests_type_erasure),
                state.dataRequestTypeFilter == ErasureRequestTypes.ERASURE,
            ) { reload(requestType = ErasureRequestTypes.ERASURE) }
            FilterChip(
                stringResource(Res.string.admin_data_requests_type_export),
                state.dataRequestTypeFilter == ErasureRequestTypes.EXPORT,
            ) { reload(requestType = ErasureRequestTypes.EXPORT) }
            FilterChip(
                stringResource(Res.string.admin_data_requests_type_opt_out),
                state.dataRequestTypeFilter == ErasureRequestTypes.OPT_OUT,
            ) { reload(requestType = ErasureRequestTypes.OPT_OUT) }
        }

        if (state.dataRequestsLoading) {
            Spinner(color = tokens.primary)
        } else if (state.dataRequests.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_data_requests_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.dataRequests.forEachIndexed { index, request ->
                        DataRequestRow(request)
                        if (index < state.dataRequests.lastIndex) Separator()
                    }
                }
            }
        }
    }
}

@Composable
private fun SummaryLine(summary: ErasureRequestSummary) {
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    Text(
        text = stringResource(Res.string.admin_data_requests_summary, summary.total, summary.running, summary.failed),
        style = typography.sm,
        color = if (summary.failed > 0) tokens.destructive else tokens.foreground,
    )
}

@Composable
private fun FilterChip(label: String, selected: Boolean, onClick: () -> Unit) {
    val typography = LocalTypography.current
    Badge(selected = selected, onClick = onClick) { Text(text = label, style = typography.sm) }
}

@Composable
private fun DataRequestRow(request: ErasureRequest) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val failed: Boolean = request.status == ErasureRequestStatuses.FAILED

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(
                text = stringResource(Res.string.admin_data_requests_subject, request.subjectIdHash.take(SUBJECT_HASH_PREFIX_LENGTH)),
                style = typography.sm,
                color = tokens.cardForeground,
                modifier = Modifier.weight(1f),
            )
            Badge(variant = BadgeVariant.Outline) { Text(text = typeLabel(request.requestType), style = typography.xs) }
            Badge(variant = if (failed) BadgeVariant.Destructive else BadgeVariant.Secondary) {
                Text(text = statusLabel(request.status), style = typography.xs)
            }
        }
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = requestedByLabel(request.requestedBy), style = typography.xs, color = tokens.mutedForeground)
            Text(text = scopeLabel(request.scope), style = typography.xs, color = tokens.mutedForeground)
            Text(
                text = stringResource(Res.string.admin_data_requests_rows_affected, request.rowsAffected),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            Text(text = request.completedAt ?: request.requestedAt, style = typography.xs, color = tokens.mutedForeground)
        }
        request.failureReason?.takeIf { it.isNotBlank() }?.let {
            Text(text = it, style = typography.xs, color = tokens.destructive)
        }
    }
}

@Composable
private fun typeLabel(requestType: String): String =
    when (requestType) {
        ErasureRequestTypes.ERASURE -> stringResource(Res.string.admin_data_requests_type_erasure)
        ErasureRequestTypes.EXPORT -> stringResource(Res.string.admin_data_requests_type_export)
        ErasureRequestTypes.OPT_OUT -> stringResource(Res.string.admin_data_requests_type_opt_out)
        else -> requestType
    }

@Composable
private fun statusLabel(status: String): String =
    when (status) {
        "pending" -> stringResource(Res.string.admin_data_requests_status_pending)
        ErasureRequestStatuses.RUNNING -> stringResource(Res.string.admin_data_requests_status_running)
        ErasureRequestStatuses.COMPLETED -> stringResource(Res.string.admin_data_requests_status_completed)
        ErasureRequestStatuses.FAILED -> stringResource(Res.string.admin_data_requests_status_failed)
        "cancelled" -> stringResource(Res.string.admin_data_requests_status_cancelled)
        else -> status
    }

@Composable
private fun requestedByLabel(requestedBy: String): String =
    when (requestedBy) {
        "self_service" -> stringResource(Res.string.admin_data_requests_requested_by_self)
        "broadcaster" -> stringResource(Res.string.admin_data_requests_requested_by_broadcaster)
        "platform_iam" -> stringResource(Res.string.admin_data_requests_requested_by_platform)
        else -> requestedBy
    }

@Composable
private fun scopeLabel(scope: String): String =
    when (scope) {
        "deployment" -> stringResource(Res.string.admin_data_requests_scope_deployment)
        "instance" -> stringResource(Res.string.admin_data_requests_scope_instance)
        "channel" -> stringResource(Res.string.admin_data_requests_scope_channel)
        else -> scope
    }
