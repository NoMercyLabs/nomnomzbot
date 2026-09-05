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
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
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
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
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
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrant
import bot.nomnomz.dashboard.core.network.AdminEntitlementGrantPreview
import bot.nomnomz.dashboard.core.network.AdminIssueEntitlementGrantRequest
import bot.nomnomz.dashboard.core.network.AdminTier
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_grant_blast_radius_counted
import nomnomzbot.composeapp.generated.resources.admin_grant_blast_radius_loading
import nomnomzbot.composeapp.generated.resources.admin_grant_blast_radius_none
import nomnomzbot.composeapp.generated.resources.admin_grant_expires_label
import nomnomzbot.composeapp.generated.resources.admin_grant_field_expires
import nomnomzbot.composeapp.generated.resources.admin_grant_field_reason
import nomnomzbot.composeapp.generated.resources.admin_grant_field_tier
import nomnomzbot.composeapp.generated.resources.admin_grant_issue
import nomnomzbot.composeapp.generated.resources.admin_grant_new
import nomnomzbot.composeapp.generated.resources.admin_grants_broadcaster_label
import nomnomzbot.composeapp.generated.resources.admin_grants_broadcaster_load
import nomnomzbot.composeapp.generated.resources.admin_grants_empty
import nomnomzbot.composeapp.generated.resources.admin_grants_heading
import nomnomzbot.composeapp.generated.resources.admin_grants_type_label
import org.jetbrains.compose.resources.stringResource

/**
 * Comps and per-tenant entitlement grants (S-ADMIN-4b) — an operator looks up a tenant by broadcaster id,
 * sees every LIVE grant on it (reason + expiry, never a raw domain field — [resolveRowLabel]), and can issue
 * a new one. Issuing shows the COUNTED blast radius — the limit keys that would actually change for THIS
 * tenant — BEFORE the issue button can be pressed (consequences-must-be-visible), mirroring
 * [TierEditDialog]'s counted-preview-then-apply shape.
 */
@Composable
internal fun EntitlementGrantSection(state: AdminState, controller: AdminController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var broadcasterIdInput: String by remember { mutableStateOf(state.grantBroadcasterId.orEmpty()) }
    var showIssueDialog: Boolean by remember { mutableStateOf(false) }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = stringResource(Res.string.admin_grants_heading), style = typography.base, color = tokens.foreground)

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            AppTextField(
                value = broadcasterIdInput,
                onValueChange = { broadcasterIdInput = it },
                label = stringResource(Res.string.admin_grants_broadcaster_label),
                modifier = Modifier.fillMaxWidth(),
            )
            TextButton(
                onClick = { scope.launch { controller.selectGrantBroadcaster(broadcasterIdInput) } },
                enabled = broadcasterIdInput.isNotBlank(),
            ) {
                Text(text = stringResource(Res.string.admin_grants_broadcaster_load))
            }
        }

        if (state.grantBroadcasterId != null) {
            if (state.entitlementGrants.isEmpty()) {
                EmptyLine(stringResource(Res.string.admin_grants_empty))
            } else {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        state.entitlementGrants.forEachIndexed { index, grant ->
                            GrantRow(grant = grant)
                            if (index < state.entitlementGrants.lastIndex) Separator()
                        }
                    }
                }
            }

            Button(onClick = { showIssueDialog = true }) {
                Text(text = stringResource(Res.string.admin_grant_new))
            }
        }
    }

    if (showIssueDialog && state.grantBroadcasterId != null) {
        IssueGrantDialog(
            tiers = state.tiers,
            preview = state.entitlementGrantPreview,
            onPreview = { tierId -> scope.launch { controller.previewEntitlementGrant(tierId) } },
            onDismiss = {
                showIssueDialog = false
                controller.dismissEntitlementGrantPreview()
            },
            onIssue = { request ->
                scope.launch {
                    controller.issueEntitlementGrant(request)
                    showIssueDialog = false
                }
            },
        )
    }
}

@Composable
private fun GrantRow(grant: AdminEntitlementGrant) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s1),
    ) {
        Text(
            text =
                resolveRowLabel(
                    primary = grant.grantedTierKey,
                    secondary = null,
                    typeLabel = stringResource(Res.string.admin_grants_type_label),
                    discriminatorSource = grant.id,
                ),
            style = typography.sm,
            color = tokens.cardForeground,
        )
        // The reason is a mandatory, operator-authored field — always rendered on its own line, never
        // folded into resolveRowLabel's blank-name fallback chain (which would silently drop it whenever
        // the tier key, always non-blank, wins precedence).
        Text(
            text = grant.reason,
            style = typography.xs,
            color = tokens.mutedForeground,
        )
        Text(
            text = stringResource(Res.string.admin_grant_expires_label, grant.expiresAt),
            style = typography.xs,
            color = tokens.mutedForeground,
        )
    }
}

/**
 * The comp-issuing dialog. [preview] is null while its counted-blast-radius fetch is in flight or before a
 * tier is picked, or once loaded, the REAL set of limit keys this comp would change for this tenant. Issue is
 * disabled until it resolves — the operator can never comp a tenant blind.
 */
@Composable
private fun IssueGrantDialog(
    tiers: List<AdminTier>,
    preview: AdminEntitlementGrantPreview?,
    onPreview: (String) -> Unit,
    onDismiss: () -> Unit,
    onIssue: (AdminIssueEntitlementGrantRequest) -> Unit,
) {
    val spacing = LocalSpacing.current

    var selectedTierId: String? by remember { mutableStateOf(null) }
    var reason: String by remember { mutableStateOf("") }
    var expiresAt: String by remember { mutableStateOf("") }

    val formValid: Boolean = selectedTierId != null && reason.isNotBlank() && expiresAt.isNotBlank()

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_grant_new))

        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            tiers.forEach { tier ->
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                    TextButton(
                        onClick = {
                            selectedTierId = tier.id
                            onPreview(tier.id)
                        },
                    ) {
                        Text(text = if (selectedTierId == tier.id) "[${tier.key}]" else tier.key)
                    }
                }
            }
        }
        Spacer(modifier = Modifier.height(spacing.s2))

        AppTextField(
            value = reason,
            onValueChange = { reason = it },
            label = stringResource(Res.string.admin_grant_field_reason),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s2))
        AppTextField(
            value = expiresAt,
            onValueChange = { expiresAt = it },
            label = stringResource(Res.string.admin_grant_field_expires),
            modifier = Modifier.fillMaxWidth(),
        )

        Spacer(modifier = Modifier.height(spacing.s2))
        GrantBlastRadiusNotice(selectedTierId, preview)

        Spacer(modifier = Modifier.height(spacing.s1))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(
                onClick = {
                    onIssue(
                        AdminIssueEntitlementGrantRequest(
                            tierId = selectedTierId.orEmpty(),
                            reason = reason,
                            expiresAt = expiresAt,
                            confirmedChangedLimitCount = preview?.changedLimitCount ?: 0,
                        ),
                    )
                },
                enabled = formValid && preview != null,
            ) {
                Text(text = stringResource(Res.string.admin_grant_issue))
            }
        }
    }
}

/** The counted blast radius, shown BEFORE the issue button can be pressed — never a guess, never hidden. */
@Composable
private fun GrantBlastRadiusNotice(selectedTierId: String?, preview: AdminEntitlementGrantPreview?) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Row(verticalAlignment = Alignment.CenterVertically) {
        when {
            selectedTierId == null -> Text(
                text = stringResource(Res.string.admin_grant_field_tier),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            preview == null -> {
                Spinner(color = tokens.mutedForeground)
                Spacer(modifier = Modifier.width(LocalSpacing.current.s1))
                Text(
                    text = stringResource(Res.string.admin_grant_blast_radius_loading),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
            preview.changedLimitCount <= 0 -> Text(
                text = stringResource(Res.string.admin_grant_blast_radius_none),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            else -> Text(
                text = stringResource(
                    Res.string.admin_grant_blast_radius_counted,
                    preview.changedLimitCount,
                    preview.changedLimitKeys.joinToString(", "),
                ),
                style = typography.xs,
                color = tokens.destructive,
            )
        }
    }
}
