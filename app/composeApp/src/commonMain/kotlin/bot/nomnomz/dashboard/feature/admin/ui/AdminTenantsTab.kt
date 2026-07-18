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
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.foundation.text.KeyboardActions
import androidx.compose.foundation.text.KeyboardOptions
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Sheet
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.AdminTenant
import bot.nomnomz.dashboard.core.network.AdminTenantDetail
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_tenant_ban
import nomnomzbot.composeapp.generated.resources.admin_tenant_close
import nomnomzbot.composeapp.generated.resources.admin_tenant_deployment
import nomnomzbot.composeapp.generated.resources.admin_tenant_detail_title
import nomnomzbot.composeapp.generated.resources.admin_tenant_empty
import nomnomzbot.composeapp.generated.resources.admin_tenant_filter_active
import nomnomzbot.composeapp.generated.resources.admin_tenant_filter_all
import nomnomzbot.composeapp.generated.resources.admin_tenant_filter_banned
import nomnomzbot.composeapp.generated.resources.admin_tenant_filter_suspended
import nomnomzbot.composeapp.generated.resources.admin_tenant_justification
import nomnomzbot.composeapp.generated.resources.admin_tenant_members
import nomnomzbot.composeapp.generated.resources.admin_tenant_owner
import nomnomzbot.composeapp.generated.resources.admin_tenant_reason
import nomnomzbot.composeapp.generated.resources.admin_tenant_reinstate
import nomnomzbot.composeapp.generated.resources.admin_tenant_reinstate_desc
import nomnomzbot.composeapp.generated.resources.admin_tenant_reinstate_title
import nomnomzbot.composeapp.generated.resources.admin_tenant_search
import nomnomzbot.composeapp.generated.resources.admin_tenant_status
import nomnomzbot.composeapp.generated.resources.admin_tenant_suspend
import nomnomzbot.composeapp.generated.resources.admin_tenant_suspend_desc
import nomnomzbot.composeapp.generated.resources.admin_tenant_suspend_title
import nomnomzbot.composeapp.generated.resources.admin_tenant_suspended_banner
import nomnomzbot.composeapp.generated.resources.admin_tenant_tier
import nomnomzbot.composeapp.generated.resources.admin_tenant_view
import org.jetbrains.compose.resources.stringResource

private const val STATUS_ACTIVE: String = "active"
private const val STATUS_SUSPENDED: String = "suspended"
private const val STATUS_BANNED: String = "platform_banned"

/**
 * The tenant-operations console: a searchable + status-filterable tenant list (with a clear suspended banner
 * state), suspend/reinstate dialogs (reason/justification required), and a tenant detail drawer. Suspension is
 * enforced server-side (a suspended tenant's API 403s at Gate 1) so the state shown here is truthful, not cosmetic.
 */
@Composable
internal fun TenantsTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    var searchText: String by remember { mutableStateOf(state.tenantSearch) }
    var suspendFor: AdminTenant? by remember { mutableStateOf(null) }
    var reinstateFor: AdminTenant? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.actionError?.let { ActionErrorBanner(message = it) }

        AppTextField(
            value = searchText,
            onValueChange = { searchText = it },
            label = stringResource(Res.string.admin_tenant_search),
            modifier = Modifier.fillMaxWidth(),
            keyboardOptions = KeyboardOptions(imeAction = ImeAction.Search),
            keyboardActions = KeyboardActions(onSearch = { scope.launch { controller.loadTenants(search = searchText) } }),
        )

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            StatusFilterChip(stringResource(Res.string.admin_tenant_filter_all), state.tenantStatusFilter == null) {
                scope.launch { controller.loadTenants(status = null) }
            }
            StatusFilterChip(stringResource(Res.string.admin_tenant_filter_active), state.tenantStatusFilter == STATUS_ACTIVE) {
                scope.launch { controller.loadTenants(status = STATUS_ACTIVE) }
            }
            StatusFilterChip(stringResource(Res.string.admin_tenant_filter_suspended), state.tenantStatusFilter == STATUS_SUSPENDED) {
                scope.launch { controller.loadTenants(status = STATUS_SUSPENDED) }
            }
            StatusFilterChip(stringResource(Res.string.admin_tenant_filter_banned), state.tenantStatusFilter == STATUS_BANNED) {
                scope.launch { controller.loadTenants(status = STATUS_BANNED) }
            }
        }

        if (state.tenantsLoading) {
            Spinner(color = tokens.primary)
        } else if (state.tenants.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_tenant_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.tenants.forEachIndexed { index, tenant ->
                        TenantRow(
                            tenant = tenant,
                            onView = { scope.launch { controller.openTenant(tenant.id) } },
                            onSuspend = { controller.clearActionError(); suspendFor = tenant },
                            onReinstate = { controller.clearActionError(); reinstateFor = tenant },
                        )
                        if (index < state.tenants.lastIndex) Separator()
                    }
                }
            }
        }
    }

    // Detail drawer.
    state.selectedTenant?.let { detail ->
        TenantDetailDrawer(
            detail = detail,
            onDismiss = { controller.closeTenant() },
            onSuspend = { controller.clearActionError(); suspendFor = state.tenants.firstOrNull { it.id == detail.id } ?: AdminTenant(detail.id, detail.name, detail.twitchChannelId, detail.status, detail.billingTierKey, false, detail.createdAt, detail.suspendedAt) },
            onReinstate = { controller.clearActionError(); reinstateFor = state.tenants.firstOrNull { it.id == detail.id } ?: AdminTenant(detail.id, detail.name, detail.twitchChannelId, detail.status, detail.billingTierKey, false, detail.createdAt, detail.suspendedAt) },
        )
    }

    suspendFor?.let { tenant ->
        SuspendDialog(
            onDismiss = { suspendFor = null },
            onConfirm = { newStatus, reason ->
                scope.launch { controller.suspendTenant(tenant.id, newStatus, reason) }
                suspendFor = null
            },
        )
    }

    reinstateFor?.let { tenant ->
        ReinstateDialog(
            onDismiss = { reinstateFor = null },
            onConfirm = { justification ->
                scope.launch { controller.reinstateTenant(tenant.id, justification) }
                reinstateFor = null
            },
        )
    }
}

@Composable
private fun StatusFilterChip(label: String, selected: Boolean, onClick: () -> Unit) {
    val typography = LocalTypography.current
    Badge(selected = selected, onClick = onClick) { Text(text = label, style = typography.sm) }
}

@Composable
private fun TenantRow(
    tenant: AdminTenant,
    onView: () -> Unit,
    onSuspend: () -> Unit,
    onReinstate: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val isSuspended: Boolean = tenant.status == STATUS_SUSPENDED || tenant.status == STATUS_BANNED

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = tenant.name, style = typography.sm, color = tokens.cardForeground)
                Text(text = stringResource(Res.string.admin_tenant_tier, tenant.billingTierKey), style = typography.xs, color = tokens.mutedForeground)
            }
            if (isSuspended) {
                Badge(variant = BadgeVariant.Destructive) { Text(text = stringResource(Res.string.admin_tenant_suspended_banner), style = typography.xs) }
            } else if (tenant.isLive) {
                Text(text = "●", style = typography.sm, color = tokens.primary)
            }
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onView) { Text(text = stringResource(Res.string.admin_tenant_view), style = typography.xs) }
            Spacer(modifier = Modifier.weight(1f))
            if (isSuspended) {
                TextButton(onClick = onReinstate) { Text(text = stringResource(Res.string.admin_tenant_reinstate), color = tokens.primary, style = typography.xs) }
            } else {
                TextButton(onClick = onSuspend) { Text(text = stringResource(Res.string.admin_tenant_suspend), color = tokens.destructive, style = typography.xs) }
            }
        }
    }
}

@Composable
private fun TenantDetailDrawer(
    detail: AdminTenantDetail,
    onDismiss: () -> Unit,
    onSuspend: () -> Unit,
    onReinstate: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val isSuspended: Boolean = detail.status == STATUS_SUSPENDED || detail.status == STATUS_BANNED

    Sheet(open = true, onDismissRequest = onDismiss) {
        Column(modifier = Modifier.fillMaxWidth().padding(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
            Text(text = stringResource(Res.string.admin_tenant_detail_title), style = typography.lg, color = tokens.foreground)
            Text(text = detail.name, style = typography.base, color = tokens.foreground)

            if (isSuspended) {
                ActionErrorBanner(message = stringResource(Res.string.admin_tenant_suspended_banner) + (detail.suspendedReason?.let { ": $it" } ?: ""))
            }

            DetailLine(stringResource(Res.string.admin_tenant_status, detail.status))
            DetailLine(stringResource(Res.string.admin_tenant_tier, detail.billingTierKey))
            DetailLine(stringResource(Res.string.admin_tenant_deployment, detail.deploymentMode))
            DetailLine(stringResource(Res.string.admin_tenant_owner, detail.ownerDisplayName))
            DetailLine(stringResource(Res.string.admin_tenant_members, detail.membershipCount))

            Spacer(modifier = Modifier.height(spacing.s1))
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                if (isSuspended) {
                    Button(onClick = onReinstate) { Text(text = stringResource(Res.string.admin_tenant_reinstate)) }
                } else {
                    Button(onClick = onSuspend) { Text(text = stringResource(Res.string.admin_tenant_suspend)) }
                }
                OutlinedButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_tenant_close)) }
            }
        }
    }
}

@Composable
private fun DetailLine(text: String) {
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    Text(text = text, style = typography.sm, color = tokens.mutedForeground)
}

@Composable
private fun SuspendDialog(onDismiss: () -> Unit, onConfirm: (newStatus: String, reason: String) -> Unit) {
    val spacing = LocalSpacing.current
    var reason: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_tenant_suspend_title))
        DialogDescription(text = stringResource(Res.string.admin_tenant_suspend_desc))
        AppTextField(
            value = reason,
            onValueChange = { reason = it },
            label = stringResource(Res.string.admin_tenant_reason),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s1))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            OutlinedButton(onClick = { onConfirm(STATUS_BANNED, reason) }, enabled = reason.isNotBlank()) {
                Text(text = stringResource(Res.string.admin_tenant_ban))
            }
            Button(
                onClick = { onConfirm(STATUS_SUSPENDED, reason) },
                enabled = reason.isNotBlank(),
                variant = bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant.Destructive,
            ) { Text(text = stringResource(Res.string.admin_tenant_suspend)) }
        }
    }
}

@Composable
private fun ReinstateDialog(onDismiss: () -> Unit, onConfirm: (justification: String) -> Unit) {
    val spacing = LocalSpacing.current
    var justification: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_tenant_reinstate_title))
        DialogDescription(text = stringResource(Res.string.admin_tenant_reinstate_desc))
        AppTextField(
            value = justification,
            onValueChange = { justification = it },
            label = stringResource(Res.string.admin_tenant_justification),
            modifier = Modifier.fillMaxWidth(),
        )
        Spacer(modifier = Modifier.height(spacing.s1))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(onClick = { onConfirm(justification) }, enabled = justification.isNotBlank()) {
                Text(text = stringResource(Res.string.admin_tenant_reinstate))
            }
        }
    }
}
