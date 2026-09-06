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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CrossTenantAbuseSignal
import bot.nomnomz.dashboard.core.network.TrustSafetyReviewItem
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_confirm
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_confirmed_label
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_hits
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_justification_label
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_look
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_no_cross_tenant_signals
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_no_queued_actions
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_notice
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_overturn
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_overturn_cancel
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_overturn_confirm
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_overturn_title
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_overturned_label
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_row_type
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_section_review_queue
import nomnomzbot.composeapp.generated.resources.admin_trust_safety_section_signals
import org.jetbrains.compose.resources.stringResource

/**
 * The platform-wide trust &amp; safety desk (S-ADMIN-8a): correlate one actor's spam-defence detections
 * ACROSS every tenant of this deployment, and review the account actions the engine took automatically.
 *
 * The lookup is AUDITED like the support desk, so the reason field is mandatory and the notice above the
 * one primary action says plainly what gets recorded. Confirming an automatic action is a neutral, quiet
 * action; overturning one is destructive and gets its OWN treatment — a confirm dialog that shows the
 * exact real effect it will reverse (the server-computed [TrustSafetyReviewItem.reversalPreview]) before
 * anything commits.
 */
@Composable
internal fun TrustSafetyTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    val canLook: Boolean = state.trustSafetyJustification.isNotBlank()

    Column(
        modifier = Modifier.fillMaxWidth().verticalScroll(rememberScrollState()).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        AppTextField(
            value = state.trustSafetyJustification,
            onValueChange = { controller.setTrustSafetyJustification(it) },
            label = stringResource(Res.string.admin_trust_safety_justification_label),
            modifier = Modifier.fillMaxWidth(),
        )
        Text(
            text = stringResource(Res.string.admin_trust_safety_notice),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        // The one full-chroma action on this screen. Row-level confirm/overturn stay neutral/destructive.
        Button(
            onClick = {
                scope.launch {
                    controller.loadCrossTenantSignals()
                    controller.loadReviewQueue()
                }
            },
            enabled = canLook,
        ) {
            Text(text = stringResource(Res.string.admin_trust_safety_look))
        }

        state.crossTenantSignalsError?.let { ActionErrorBanner(message = it) }
        state.reviewQueueError?.let { ActionErrorBanner(message = it) }
        state.reviewActionError?.let { ActionErrorBanner(message = it) }

        Text(
            text = stringResource(Res.string.admin_trust_safety_section_signals),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        when {
            state.crossTenantSignalsLoading -> Spinner(color = tokens.primary)
            state.crossTenantSignals.isNotEmpty() -> Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.crossTenantSignals.forEachIndexed { index, signal ->
                        SignalRow(signal = signal)
                        if (index < state.crossTenantSignals.lastIndex) Separator()
                    }
                }
            }
            state.crossTenantSignalsLoaded ->
                EmptyLine(stringResource(Res.string.admin_trust_safety_no_cross_tenant_signals))
        }

        Text(
            text = stringResource(Res.string.admin_trust_safety_section_review_queue),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        when {
            state.reviewQueueLoading -> Spinner(color = tokens.primary)
            state.reviewQueue.isNotEmpty() -> Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.reviewQueue.forEachIndexed { index, item ->
                        ReviewQueueRow(
                            item = item,
                            busy = state.reviewActionInFlight == item.detectionId,
                            onConfirm = { scope.launch { controller.confirmReviewItem(item.detectionId) } },
                            onRequestOverturn = { controller.requestOverturn(item) },
                        )
                        if (index < state.reviewQueue.lastIndex) Separator()
                    }
                }
            }
            state.reviewQueueLoaded -> EmptyLine(stringResource(Res.string.admin_trust_safety_no_queued_actions))
        }
    }

    state.reviewItemPendingOverturn?.let { pending ->
        ConfirmDialog(
            title = stringResource(Res.string.admin_trust_safety_overturn_title),
            message = pending.reversalPreview,
            confirmLabel = stringResource(Res.string.admin_trust_safety_overturn_confirm),
            dismissLabel = stringResource(Res.string.admin_trust_safety_overturn_cancel),
            destructive = true,
            onConfirm = { scope.launch { controller.overturnReviewItem(pending.detectionId) } },
            onDismiss = { controller.dismissOverturnRequest() },
        )
    }
}

@Composable
private fun SignalRow(signal: CrossTenantAbuseSignal) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val hitsLabel: String = stringResource(Res.string.admin_trust_safety_hits, signal.tenantCount)

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(
            text = resolveRowLabel(
                primary = signal.subjectDisplayName,
                secondary = signal.provider,
                typeLabel = stringResource(Res.string.admin_trust_safety_row_type),
                discriminatorSource = signal.subjectPlatformUserId,
            ),
            style = typography.sm,
            color = tokens.cardForeground,
        )
        Text(text = hitsLabel, style = typography.xs, color = tokens.mutedForeground)
        signal.hits.forEach { hit ->
            Text(
                text = "${hit.channelName} — ${hit.outcome} (${hit.confidence}): ${hit.reason}",
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }
}

@Composable
private fun ReviewQueueRow(
    item: TrustSafetyReviewItem,
    busy: Boolean,
    onConfirm: () -> Unit,
    onRequestOverturn: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = resolveRowLabel(
                primary = item.subjectDisplayName,
                secondary = item.channelName,
                typeLabel = stringResource(Res.string.admin_trust_safety_row_type),
                discriminatorSource = item.detectionId,
            ),
            style = typography.sm,
            color = tokens.cardForeground,
        )
        Text(text = item.messageText, style = typography.xs, color = tokens.mutedForeground)
        Text(text = item.reason, style = typography.xs, color = tokens.mutedForeground)

        when {
            item.overturnedAt != null ->
                Text(
                    text = stringResource(Res.string.admin_trust_safety_overturned_label),
                    style = typography.xs,
                    color = tokens.destructive,
                )
            item.confirmedAt != null ->
                Text(
                    text = stringResource(Res.string.admin_trust_safety_confirmed_label),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            else -> Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                // Confirm is the quiet, neutral sibling; overturn is destructive and reads that way.
                Button(onClick = onConfirm, enabled = !busy, variant = ButtonVariant.Outline) {
                    Text(text = stringResource(Res.string.admin_trust_safety_confirm), maxLines = 1)
                }
                Button(
                    onClick = onRequestOverturn,
                    enabled = !busy,
                    variant = ButtonVariant.DestructiveSecondary,
                ) {
                    Text(text = stringResource(Res.string.admin_trust_safety_overturn), maxLines = 1)
                }
            }
        }
        if (busy) Spinner(color = tokens.primary)
    }
}
