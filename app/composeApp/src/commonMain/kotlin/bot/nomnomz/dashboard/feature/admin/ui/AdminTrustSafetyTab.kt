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
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
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
import bot.nomnomz.dashboard.core.network.NetworkBlock
import bot.nomnomz.dashboard.core.network.TrustSafetyReviewItem
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_network_block_apply_button
import nomnomzbot.composeapp.generated.resources.admin_network_block_apply_confirm_cancel
import nomnomzbot.composeapp.generated.resources.admin_network_block_apply_confirm_confirm
import nomnomzbot.composeapp.generated.resources.admin_network_block_apply_confirm_message
import nomnomzbot.composeapp.generated.resources.admin_network_block_apply_confirm_title
import nomnomzbot.composeapp.generated.resources.admin_network_block_applied_by
import nomnomzbot.composeapp.generated.resources.admin_network_block_lift_button
import nomnomzbot.composeapp.generated.resources.admin_network_block_lift_partial_notice
import nomnomzbot.composeapp.generated.resources.admin_network_block_no_active
import nomnomzbot.composeapp.generated.resources.admin_network_block_notice
import nomnomzbot.composeapp.generated.resources.admin_network_block_preview_button
import nomnomzbot.composeapp.generated.resources.admin_network_block_preview_summary
import nomnomzbot.composeapp.generated.resources.admin_network_block_reason_label
import nomnomzbot.composeapp.generated.resources.admin_network_block_row_type
import nomnomzbot.composeapp.generated.resources.admin_network_block_section_active
import nomnomzbot.composeapp.generated.resources.admin_network_block_section_title
import nomnomzbot.composeapp.generated.resources.admin_network_block_status_active
import nomnomzbot.composeapp.generated.resources.admin_network_block_status_lifted
import nomnomzbot.composeapp.generated.resources.admin_network_block_status_partial
import nomnomzbot.composeapp.generated.resources.admin_network_block_target_label
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

        state.crossTenantSignalsError?.let { InlineError(message = it) }
        state.reviewQueueError?.let { InlineError(message = it) }

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

        Separator()
        NetworkBlockSection(state = state, controller = controller, scope = scope)
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

/**
 * The network-wide block (S-ADMIN-8b) — the most dangerous control on this desk. The flow is strictly
 * sequential: a target and justification unlock ONLY the neutral "preview" action; a real, counted blast
 * radius must be on screen before "apply" is even enabled; applying opens a destructive confirm dialog
 * that repeats the exact count. Preview and apply are never equal-weight siblings — preview is the quiet
 * outline step, apply is the single full-chroma destructive action on this section.
 */
@Composable
private fun NetworkBlockSection(state: AdminState, controller: AdminController, scope: CoroutineScope) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    val canPreview: Boolean =
        state.trustSafetyJustification.isNotBlank() && state.networkBlockTargetTwitchUserId.isNotBlank()
    val preview = state.networkBlockPreview

    Column(
        modifier = Modifier.fillMaxWidth(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        Text(
            text = stringResource(Res.string.admin_network_block_section_title),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        Text(
            text = stringResource(Res.string.admin_network_block_notice),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        AppTextField(
            value = state.networkBlockTargetTwitchUserId,
            onValueChange = { controller.setNetworkBlockTargetTwitchUserId(it) },
            label = stringResource(Res.string.admin_network_block_target_label),
            modifier = Modifier.fillMaxWidth(),
        )
        AppTextField(
            value = state.networkBlockReason,
            onValueChange = { controller.setNetworkBlockReason(it) },
            label = stringResource(Res.string.admin_network_block_reason_label),
            modifier = Modifier.fillMaxWidth(),
        )

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            // The quiet, neutral step every apply must go through first.
            Button(
                onClick = { scope.launch { controller.previewNetworkBlock() } },
                enabled = canPreview && !state.networkBlockPreviewLoading,
                variant = ButtonVariant.Outline,
            ) {
                Text(text = stringResource(Res.string.admin_network_block_preview_button), maxLines = 1)
            }
            // The one full-chroma destructive action on this section — unreachable without a real preview.
            Button(
                onClick = { controller.requestApplyNetworkBlock() },
                enabled = preview != null && !state.networkBlockApplyInFlight,
                variant = ButtonVariant.Destructive,
            ) {
                Text(text = stringResource(Res.string.admin_network_block_apply_button), maxLines = 1)
            }
        }

        if (state.networkBlockPreviewLoading) Spinner(color = tokens.primary)
        state.networkBlockPreviewError?.let { InlineError(message = it) }

        preview?.let {
            Text(
                text = stringResource(Res.string.admin_network_block_preview_summary, it.tenantCount),
                style = typography.xs,
                color = tokens.destructive,
            )
        }

        Text(
            text = stringResource(Res.string.admin_network_block_section_active),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        LaunchedEffect(state.trustSafetyJustification.isNotBlank()) {
            if (state.trustSafetyJustification.isNotBlank() && !state.networkBlocksLoaded) {
                controller.loadNetworkBlocks()
            }
        }
        when {
            state.networkBlocksLoading -> Spinner(color = tokens.primary)
            state.networkBlocks.isNotEmpty() -> Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.networkBlocks.forEachIndexed { index, block ->
                        NetworkBlockRow(
                            block = block,
                            busy = state.networkBlockLiftInFlight == block.id,
                            onLift = { scope.launch { controller.liftNetworkBlock(block.id) } },
                        )
                        if (index < state.networkBlocks.lastIndex) Separator()
                    }
                }
            }
            state.networkBlocksLoaded -> EmptyLine(stringResource(Res.string.admin_network_block_no_active))
        }
    }

    if (state.networkBlockApplyConfirmOpen && preview != null) {
        ConfirmDialog(
            title = stringResource(Res.string.admin_network_block_apply_confirm_title),
            message = stringResource(
                Res.string.admin_network_block_apply_confirm_message,
                preview.tenantCount,
            ),
            confirmLabel = stringResource(Res.string.admin_network_block_apply_confirm_confirm),
            dismissLabel = stringResource(Res.string.admin_network_block_apply_confirm_cancel),
            destructive = true,
            onConfirm = { scope.launch { controller.applyNetworkBlock() } },
            onDismiss = { controller.dismissApplyNetworkBlockRequest() },
        )
    }
}

@Composable
private fun NetworkBlockRow(block: NetworkBlock, busy: Boolean, onLift: () -> Unit) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = resolveRowLabel(
                primary = block.targetDisplayName ?: block.targetTwitchUserId,
                secondary = block.reason ?: "",
                typeLabel = stringResource(Res.string.admin_network_block_row_type),
                discriminatorSource = block.id,
            ),
            style = typography.sm,
            color = tokens.cardForeground,
        )
        Text(
            text = stringResource(Res.string.admin_network_block_applied_by, block.appliedByPrincipalId, block.appliedAt),
            style = typography.xs,
            color = tokens.mutedForeground,
        )

        when (block.status) {
            "lifted" ->
                Text(
                    text = stringResource(Res.string.admin_network_block_status_lifted),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            "partial" -> {
                Text(
                    text = stringResource(Res.string.admin_network_block_status_partial),
                    style = typography.xs,
                    color = tokens.destructive,
                )
                // A lift already attempted and left channels still banned shows that honestly rather
                // than a silent "lifted" claim.
                if (block.liftAttemptedAt != null) {
                    Text(
                        text = stringResource(
                            Res.string.admin_network_block_lift_partial_notice,
                            block.liftFailedChannelIds.size,
                        ),
                        style = typography.xs,
                        color = tokens.destructive,
                    )
                }
                Button(onClick = onLift, enabled = !busy, variant = ButtonVariant.Outline) {
                    Text(text = stringResource(Res.string.admin_network_block_lift_button), maxLines = 1)
                }
            }
            else -> {
                Text(
                    text = stringResource(Res.string.admin_network_block_status_active, block.channelCount),
                    style = typography.xs,
                    color = tokens.destructive,
                )
                Button(onClick = onLift, enabled = !busy, variant = ButtonVariant.Outline) {
                    Text(text = stringResource(Res.string.admin_network_block_lift_button), maxLines = 1)
                }
            }
        }
        if (busy) Spinner(color = tokens.primary)
    }
}
