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
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.IamPrincipalSummary
import bot.nomnomz.dashboard.core.network.IamRole
import bot.nomnomz.dashboard.feature.admin.state.AdminController
import bot.nomnomz.dashboard.feature.admin.state.AdminState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.admin_cancel
import nomnomzbot.composeapp.generated.resources.admin_iam_assign_role
import nomnomzbot.composeapp.generated.resources.admin_iam_create_service
import nomnomzbot.composeapp.generated.resources.admin_iam_deactivate
import nomnomzbot.composeapp.generated.resources.admin_iam_deactivate_confirm
import nomnomzbot.composeapp.generated.resources.admin_iam_deactivate_title
import nomnomzbot.composeapp.generated.resources.admin_iam_display_name
import nomnomzbot.composeapp.generated.resources.admin_iam_effective
import nomnomzbot.composeapp.generated.resources.admin_iam_empty
import nomnomzbot.composeapp.generated.resources.admin_iam_inactive
import nomnomzbot.composeapp.generated.resources.admin_iam_key_desc
import nomnomzbot.composeapp.generated.resources.admin_iam_key_title
import nomnomzbot.composeapp.generated.resources.admin_iam_no_assignments
import nomnomzbot.composeapp.generated.resources.admin_iam_permission_keys
import nomnomzbot.composeapp.generated.resources.admin_iam_principals
import nomnomzbot.composeapp.generated.resources.admin_iam_promote
import nomnomzbot.composeapp.generated.resources.admin_iam_promote_desc
import nomnomzbot.composeapp.generated.resources.admin_iam_promote_title
import nomnomzbot.composeapp.generated.resources.admin_iam_reactivate
import nomnomzbot.composeapp.generated.resources.admin_iam_reason
import nomnomzbot.composeapp.generated.resources.admin_iam_revoke
import nomnomzbot.composeapp.generated.resources.admin_iam_role
import nomnomzbot.composeapp.generated.resources.admin_iam_roles_empty
import nomnomzbot.composeapp.generated.resources.admin_iam_roles_title
import nomnomzbot.composeapp.generated.resources.admin_iam_service_desc
import nomnomzbot.composeapp.generated.resources.admin_iam_service_name
import nomnomzbot.composeapp.generated.resources.admin_iam_type_employee
import nomnomzbot.composeapp.generated.resources.admin_iam_type_service
import nomnomzbot.composeapp.generated.resources.admin_save
import org.jetbrains.compose.resources.stringResource

/**
 * The Plane-C IAM management surface: the principal list (inactive badge, per-principal role assignments with
 * revoke, deactivate/reactivate, effective-permissions reveal, assign-role), a promote/create-service-account
 * header, the role catalog, and the show-once service-account key dialog. The whole admin area is admin-gated;
 * the backend re-checks iam:manage / iam:principal:create per call and any denial surfaces via [AdminState.actionError].
 */
@Composable
internal fun IamTab(state: AdminState, controller: AdminController) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()

    var promoteOpen: Boolean by remember { mutableStateOf(false) }
    var serviceOpen: Boolean by remember { mutableStateOf(false) }
    var assignFor: IamPrincipalSummary? by remember { mutableStateOf(null) }
    var deactivateFor: IamPrincipalSummary? by remember { mutableStateOf(null) }

    Column(
        modifier = Modifier
            .fillMaxSize()
            .verticalScroll(rememberScrollState())
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        state.actionError?.let { ActionErrorBanner(message = it) }

        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = stringResource(Res.string.admin_iam_principals),
                style = typography.base,
                color = tokens.foreground,
                modifier = Modifier.weight(1f),
            )
            OutlinedButton(onClick = { controller.clearActionError(); promoteOpen = true }) {
                Text(text = stringResource(Res.string.admin_iam_promote))
            }
            Button(onClick = { controller.clearActionError(); serviceOpen = true }) {
                Text(text = stringResource(Res.string.admin_iam_create_service))
            }
        }

        if (state.principals.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_iam_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.principals.forEachIndexed { index, principal ->
                        PrincipalRow(
                            principal = principal,
                            permissionKeys = state.effectivePermissions[principal.id],
                            onAssign = { controller.clearActionError(); assignFor = principal },
                            onDeactivate = { controller.clearActionError(); deactivateFor = principal },
                            onReactivate = { scope.launch { controller.reactivatePrincipal(principal.id) } },
                            onRevoke = { assignmentId -> scope.launch { controller.revokeAssignment(assignmentId, null) } },
                            onEffective = { scope.launch { controller.loadEffectivePermissions(principal.id) } },
                        )
                        if (index < state.principals.lastIndex) Separator()
                    }
                }
            }
        }

        Text(
            text = stringResource(Res.string.admin_iam_roles_title),
            style = typography.base,
            color = tokens.foreground,
            modifier = Modifier.padding(top = spacing.s2),
        )
        if (state.roles.isEmpty()) {
            EmptyLine(stringResource(Res.string.admin_iam_roles_empty))
        } else {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    state.roles.forEachIndexed { index, role ->
                        Column(
                            modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
                            verticalArrangement = Arrangement.spacedBy(spacing.s1),
                        ) {
                            Text(text = role.name, style = typography.sm, color = tokens.cardForeground)
                            role.description?.takeIf { it.isNotBlank() }?.let {
                                Text(text = it, style = typography.xs, color = tokens.mutedForeground)
                            }
                            Text(
                                text = stringResource(Res.string.admin_iam_permission_keys) + ": " + role.permissionKeys.size,
                                style = typography.xs,
                                color = tokens.mutedForeground,
                            )
                        }
                        if (index < state.roles.lastIndex) Separator()
                    }
                }
            }
        }
    }

    if (promoteOpen) {
        PromoteDialog(
            state = state,
            onDismiss = { promoteOpen = false },
            onConfirm = { userId, displayName, roleId ->
                scope.launch { controller.promoteUser(userId, displayName, listOfNotNull(roleId)) }
                promoteOpen = false
            },
        )
    }

    if (serviceOpen) {
        ServiceAccountDialog(
            roles = state.roles,
            onDismiss = { serviceOpen = false },
            onConfirm = { name, roleId ->
                scope.launch { controller.createServiceAccount(name, listOfNotNull(roleId)) }
                serviceOpen = false
            },
        )
    }

    assignFor?.let { principal ->
        AssignRoleDialog(
            roles = state.roles,
            onDismiss = { assignFor = null },
            onConfirm = { roleId, reason ->
                scope.launch { controller.assignRole(principal.id, roleId, reason.ifBlank { null }) }
                assignFor = null
            },
        )
    }

    deactivateFor?.let { principal ->
        ConfirmDialog(
            title = stringResource(Res.string.admin_iam_deactivate_title),
            message = stringResource(Res.string.admin_iam_deactivate_confirm),
            confirmLabel = stringResource(Res.string.admin_iam_deactivate),
            dismissLabel = stringResource(Res.string.admin_cancel),
            destructive = true,
            onConfirm = {
                scope.launch { controller.deactivatePrincipal(principal.id, null) }
                deactivateFor = null
            },
            onDismiss = { deactivateFor = null },
        )
    }

    // The service-account key comes back exactly once — show it, let the operator copy, then clear it forever.
    state.issuedServiceAccountKey?.let { key ->
        ServiceAccountKeyDialog(key = key, onDismiss = { controller.dismissIssuedKey() })
    }
}

@Composable
private fun PrincipalRow(
    principal: IamPrincipalSummary,
    permissionKeys: List<String>?,
    onAssign: () -> Unit,
    onDeactivate: () -> Unit,
    onReactivate: () -> Unit,
    onRevoke: (String) -> Unit,
    onEffective: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = principal.name, style = typography.sm, color = tokens.cardForeground, modifier = Modifier.weight(1f))
            Badge(variant = BadgeVariant.Secondary) {
                Text(
                    text = if (principal.principalType == 1) stringResource(Res.string.admin_iam_type_service)
                    else stringResource(Res.string.admin_iam_type_employee),
                    style = typography.xs,
                )
            }
            if (!principal.isActive) {
                Badge(variant = BadgeVariant.Destructive) {
                    Text(text = stringResource(Res.string.admin_iam_inactive), style = typography.xs)
                }
            }
        }

        if (principal.activeAssignments.isEmpty()) {
            Text(text = stringResource(Res.string.admin_iam_no_assignments), style = typography.xs, color = tokens.mutedForeground)
        } else {
            principal.activeAssignments.forEach { assignment ->
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Badge(variant = BadgeVariant.Outline) { Text(text = assignment.roleName, style = typography.xs) }
                    Spacer(modifier = Modifier.weight(1f))
                    TextButton(onClick = { onRevoke(assignment.id) }) {
                        Text(text = stringResource(Res.string.admin_iam_revoke), color = tokens.destructive, style = typography.xs)
                    }
                }
            }
        }

        permissionKeys?.let { keys ->
            Text(
                text = keys.joinToString(", ").ifBlank { "—" },
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }

        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = onEffective) { Text(text = stringResource(Res.string.admin_iam_effective), style = typography.xs) }
            TextButton(onClick = onAssign) { Text(text = stringResource(Res.string.admin_iam_assign_role), style = typography.xs) }
            Spacer(modifier = Modifier.weight(1f))
            if (principal.isActive) {
                TextButton(onClick = onDeactivate) {
                    Text(text = stringResource(Res.string.admin_iam_deactivate), color = tokens.destructive, style = typography.xs)
                }
            } else {
                TextButton(onClick = onReactivate) {
                    Text(text = stringResource(Res.string.admin_iam_reactivate), color = tokens.primary, style = typography.xs)
                }
            }
        }
    }
}

@Composable
private fun PromoteDialog(
    state: AdminState,
    onDismiss: () -> Unit,
    onConfirm: (userId: String, displayName: String, roleId: String?) -> Unit,
) {
    val spacing = LocalSpacing.current
    var selectedUserId: String? by remember { mutableStateOf(null) }
    var selectedUserName: String by remember { mutableStateOf("") }
    var selectedRoleId: String? by remember { mutableStateOf(null) }
    var selectedRoleName: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_iam_promote_title))
        DialogDescription(text = stringResource(Res.string.admin_iam_promote_desc))

        PickerField(
            label = stringResource(Res.string.admin_iam_display_name),
            selectedLabel = selectedUserName,
            options = state.users.map { it.id to (it.displayName + " (" + it.login + ")") },
            onSelect = { id, label -> selectedUserId = id; selectedUserName = label },
        )
        PickerField(
            label = stringResource(Res.string.admin_iam_role),
            selectedLabel = selectedRoleName,
            options = state.roles.map { it.id to it.name },
            onSelect = { id, label -> selectedRoleId = id; selectedRoleName = label },
        )

        Spacer(modifier = Modifier.height(spacing.s1))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(
                onClick = { selectedUserId?.let { onConfirm(it, selectedUserName, selectedRoleId) } },
                enabled = selectedUserId != null,
            ) { Text(text = stringResource(Res.string.admin_iam_promote)) }
        }
    }
}

@Composable
private fun ServiceAccountDialog(
    roles: List<IamRole>,
    onDismiss: () -> Unit,
    onConfirm: (name: String, roleId: String?) -> Unit,
) {
    val spacing = LocalSpacing.current
    var name: String by remember { mutableStateOf("") }
    var selectedRoleId: String? by remember { mutableStateOf(null) }
    var selectedRoleName: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_iam_create_service))
        DialogDescription(text = stringResource(Res.string.admin_iam_service_desc))

        bot.nomnomz.dashboard.core.designsystem.component.AppTextField(
            value = name,
            onValueChange = { name = it },
            label = stringResource(Res.string.admin_iam_service_name),
            modifier = Modifier.fillMaxWidth(),
        )
        PickerField(
            label = stringResource(Res.string.admin_iam_role),
            selectedLabel = selectedRoleName,
            options = roles.map { it.id to it.name },
            onSelect = { id, label -> selectedRoleId = id; selectedRoleName = label },
        )

        Spacer(modifier = Modifier.height(spacing.s1))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(onClick = { onConfirm(name, selectedRoleId) }, enabled = name.isNotBlank()) {
                Text(text = stringResource(Res.string.admin_save))
            }
        }
    }
}

@Composable
private fun AssignRoleDialog(
    roles: List<IamRole>,
    onDismiss: () -> Unit,
    onConfirm: (roleId: String, reason: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    var selectedRoleId: String? by remember { mutableStateOf(null) }
    var selectedRoleName: String by remember { mutableStateOf("") }
    var reason: String by remember { mutableStateOf("") }

    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_iam_assign_role))

        PickerField(
            label = stringResource(Res.string.admin_iam_role),
            selectedLabel = selectedRoleName,
            options = roles.map { it.id to it.name },
            onSelect = { id, label -> selectedRoleId = id; selectedRoleName = label },
        )
        bot.nomnomz.dashboard.core.designsystem.component.AppTextField(
            value = reason,
            onValueChange = { reason = it },
            label = stringResource(Res.string.admin_iam_reason),
            modifier = Modifier.fillMaxWidth(),
        )

        Spacer(modifier = Modifier.height(spacing.s1))
        DialogFooter {
            TextButton(onClick = onDismiss) { Text(text = stringResource(Res.string.admin_cancel)) }
            Button(
                onClick = { selectedRoleId?.let { onConfirm(it, reason) } },
                enabled = selectedRoleId != null,
            ) { Text(text = stringResource(Res.string.admin_iam_assign_role)) }
        }
    }
}

@Composable
private fun ServiceAccountKeyDialog(key: String, onDismiss: () -> Unit) {
    Dialog(onDismissRequest = onDismiss) {
        DialogTitle(text = stringResource(Res.string.admin_iam_key_title))
        DialogDescription(text = stringResource(Res.string.admin_iam_key_desc))
        CopyValue(value = key, copyLabel = stringResource(Res.string.admin_iam_key_title), copiedLabel = stringResource(Res.string.admin_iam_key_title))
        DialogFooter {
            Button(onClick = onDismiss, variant = ButtonVariant.Default) { Text(text = stringResource(Res.string.admin_cancel)) }
        }
    }
}
