// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.roles.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionPermission
import bot.nomnomz.dashboard.core.network.ChannelMembership
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.core.network.PermitGrantType
import bot.nomnomz.dashboard.feature.roles.state.RolesController
import bot.nomnomz.dashboard.feature.roles.state.RolesState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole as NavManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.roles_action_error
import nomnomzbot.composeapp.generated.resources.roles_assign_picker
import nomnomzbot.composeapp.generated.resources.roles_empty
import nomnomzbot.composeapp.generated.resources.roles_error
import nomnomzbot.composeapp.generated.resources.roles_grant_action
import nomnomzbot.composeapp.generated.resources.roles_grant_action_short
import nomnomzbot.composeapp.generated.resources.roles_grant_dialog_title
import nomnomzbot.composeapp.generated.resources.roles_grant_empty
import nomnomzbot.composeapp.generated.resources.roles_grant_pick_action
import nomnomzbot.composeapp.generated.resources.roles_loading
import nomnomzbot.composeapp.generated.resources.roles_member_description
import nomnomzbot.composeapp.generated.resources.roles_permit_capability
import nomnomzbot.composeapp.generated.resources.roles_permit_description
import nomnomzbot.composeapp.generated.resources.roles_permit_role
import nomnomzbot.composeapp.generated.resources.roles_permits_empty
import nomnomzbot.composeapp.generated.resources.roles_permits_section
import nomnomzbot.composeapp.generated.resources.roles_remove_action
import nomnomzbot.composeapp.generated.resources.roles_remove_action_short
import nomnomzbot.composeapp.generated.resources.roles_remove_confirm
import nomnomzbot.composeapp.generated.resources.roles_remove_dismiss
import nomnomzbot.composeapp.generated.resources.roles_remove_message
import nomnomzbot.composeapp.generated.resources.roles_remove_title
import nomnomzbot.composeapp.generated.resources.roles_retry
import nomnomzbot.composeapp.generated.resources.roles_revoke_action
import nomnomzbot.composeapp.generated.resources.roles_revoke_action_short
import nomnomzbot.composeapp.generated.resources.roles_revoke_confirm
import nomnomzbot.composeapp.generated.resources.roles_revoke_dismiss
import nomnomzbot.composeapp.generated.resources.roles_revoke_message
import nomnomzbot.composeapp.generated.resources.roles_revoke_title
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.OutlinedTextField
import androidx.compose.ui.text.input.KeyboardType
import nomnomzbot.composeapp.generated.resources.roles_action_overrides_section
import nomnomzbot.composeapp.generated.resources.roles_members_section
import nomnomzbot.composeapp.generated.resources.roles_override_active
import nomnomzbot.composeapp.generated.resources.roles_override_cancel
import nomnomzbot.composeapp.generated.resources.roles_override_confirm
import nomnomzbot.composeapp.generated.resources.roles_override_default
import nomnomzbot.composeapp.generated.resources.roles_override_dialog_desc
import nomnomzbot.composeapp.generated.resources.roles_override_dialog_title
import nomnomzbot.composeapp.generated.resources.roles_override_level_invalid
import nomnomzbot.composeapp.generated.resources.roles_override_level_label
import nomnomzbot.composeapp.generated.resources.roles_override_reset
import nomnomzbot.composeapp.generated.resources.roles_override_set
import nomnomzbot.composeapp.generated.resources.roles_role_broadcaster
import nomnomzbot.composeapp.generated.resources.roles_role_editor
import nomnomzbot.composeapp.generated.resources.roles_role_lead_moderator
import nomnomzbot.composeapp.generated.resources.roles_role_moderator
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Roles & Permits page (the bot's IAM management, roles-permissions §5): the channel's management members
// and the active per-user permit grants — all real data from [RolesController]. The screen is a pure projection
// of the controller's state; it loads on first composition and offers a retry on failure. Each member is
// actionable — an assign-role picker that re-grants the member's management role directly, and a Remove
// affordance; each permit is revocable. Removing a role and revoking a permit both strip elevated access, so
// they only run once the operator confirms in the shared ConfirmDialog. Granting a capability picks one of the
// channel's permit-grantable action keys for the chosen member.
@Composable
fun RolesScreen(controller: RolesController, role: NavManagementRole?) {
    val state: RolesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Roles & Permits gates every write control at its single Broadcaster manage
    // floor (frontend-ia.md §3) — assigning/removing a management role, granting a capability, revoking a permit.
    // A caller below it sees the members and permits but each write disabled with "Requires Broadcaster" (§7);
    // the backend re-checks every write (and no-escalation) regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Roles)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: RolesState = state) {
            is RolesState.Loading -> CenteredMessage(stringResource(Res.string.roles_loading))
            is RolesState.Empty -> CenteredMessage(stringResource(Res.string.roles_empty))
            is RolesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is RolesState.Ready ->
                RolesContent(
                    state = current,
                    manage = manage,
                    onAssignRole = { userId, role -> scope.launch { controller.assignRole(userId, role) } },
                    onRemoveRole = { userId -> scope.launch { controller.removeRole(userId) } },
                    onGrant = { userId, key -> scope.launch { controller.grantCapability(userId, key, null) } },
                    onRevoke = { userId, selector -> scope.launch { controller.revokePermit(userId, selector) } },
                    onSetOverride = { actionKey, level -> scope.launch { controller.setOverride(actionKey, level) } },
                    onResetOverride = { actionKey -> scope.launch { controller.resetOverride(actionKey) } },
                )
        }
    }
}

@Composable
private fun RolesContent(
    state: RolesState.Ready,
    manage: ManageDecision,
    onAssignRole: (userId: String, role: ManagementRole) -> Unit,
    onRemoveRole: (userId: String) -> Unit,
    onGrant: (userId: String, actionKey: String) -> Unit,
    onRevoke: (userId: String, selector: String?) -> Unit,
    onSetOverride: (actionKey: String, level: Int) -> Unit,
    onResetOverride: (actionKey: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The member awaiting a remove confirmation / a capability grant, the permit awaiting a revoke, and the
    // action awaiting an override change — the screen owns all dialogs' open/closed state.
    var pendingRemove: ChannelMembership? by remember { mutableStateOf(null) }
    var pendingGrant: ChannelMembership? by remember { mutableStateOf(null) }
    var pendingRevoke: PermitGrant? by remember { mutableStateOf(null) }
    var pendingOverride: ActionPermission? by remember { mutableStateOf(null) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        state.actionError?.let { detail ->
            item(key = "action-error") {
                Text(
                    text = stringResource(Res.string.roles_action_error, detail),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
                )
            }
        }

        item(key = "members-header") { SectionLabel(stringResource(Res.string.roles_members_section)) }
        items(items = state.members, key = { "member-${it.id}" }) { member ->
            MemberRow(
                member = member,
                manage = manage,
                canGrant = state.grantableActions.isNotEmpty(),
                onAssignRole = { role -> onAssignRole(member.userId, role) },
                onRemove = { pendingRemove = member },
                onGrant = { pendingGrant = member },
            )
        }

        item(key = "permits-header") { SectionLabel(stringResource(Res.string.roles_permits_section)) }
        if (state.permits.isEmpty()) {
            item(key = "permits-empty") {
                Text(
                    text = stringResource(Res.string.roles_permits_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
                )
            }
        } else {
            items(items = state.permits, key = { "permit-${it.id}" }) { permit ->
                PermitRow(permit = permit, manage = manage, onRevoke = { pendingRevoke = permit })
            }
        }

        if (state.allActions.isNotEmpty()) {
            item(key = "actions-header") {
                SectionLabel(stringResource(Res.string.roles_action_overrides_section))
            }
            items(items = state.allActions, key = { "action-${it.actionKey}" }) { action ->
                ActionPermissionRow(
                    action = action,
                    manage = manage,
                    onEdit = { pendingOverride = action },
                    onReset = { onResetOverride(action.actionKey) },
                )
            }
        }
    }

    pendingRemove?.let { member ->
        val name: String = memberName(member)
        ConfirmDialog(
            title = stringResource(Res.string.roles_remove_title),
            message = stringResource(Res.string.roles_remove_message, name),
            confirmLabel = stringResource(Res.string.roles_remove_confirm),
            dismissLabel = stringResource(Res.string.roles_remove_dismiss),
            destructive = true,
            onConfirm = {
                onRemoveRole(member.userId)
                pendingRemove = null
            },
            onDismiss = { pendingRemove = null },
        )
    }

    pendingGrant?.let { member ->
        GrantCapabilityDialog(
            memberName = memberName(member),
            actions = state.grantableActions,
            onConfirm = { actionKey ->
                onGrant(member.userId, actionKey)
                pendingGrant = null
            },
            onDismiss = { pendingGrant = null },
        )
    }

    pendingRevoke?.let { permit ->
        val name: String = permitName(permit)
        ConfirmDialog(
            title = stringResource(Res.string.roles_revoke_title),
            message = stringResource(Res.string.roles_revoke_message, name),
            confirmLabel = stringResource(Res.string.roles_revoke_confirm),
            dismissLabel = stringResource(Res.string.roles_revoke_dismiss),
            destructive = true,
            onConfirm = {
                onRevoke(permit.userId, permit.revokeSelector)
                pendingRevoke = null
            },
            onDismiss = { pendingRevoke = null },
        )
    }

    pendingOverride?.let { action ->
        SetOverrideDialog(
            action = action,
            onConfirm = { level ->
                onSetOverride(action.actionKey, level)
                pendingOverride = null
            },
            onDismiss = { pendingOverride = null },
        )
    }
}

@Composable
private fun MemberRow(
    member: ChannelMembership,
    manage: ManageDecision,
    canGrant: Boolean,
    onAssignRole: (role: ManagementRole) -> Unit,
    onRemove: () -> Unit,
    onGrant: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = memberName(member)
    val roleLabel: String = stringResource(roleLabel(member.managementRole))
    val rowDescription: String = stringResource(Res.string.roles_member_description, name, roleLabel)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = name,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            // The identity + current role reads as one node; the controls below carry their own action labels.
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
        )
        RolePicker(name = name, current = member.managementRole, manage = manage, onSelect = onAssignRole)
        if (canGrant) GrantButton(name = name, manage = manage, onGrant = onGrant)
        RemoveButton(name = name, manage = manage, onRemove = onRemove)
    }
}

// The role-assignment control: a labelled trigger that opens the closed ladder of management roles and assigns
// the chosen one directly (the backend re-checks no-escalation, so an over-reach surfaces as an error banner).
// The trigger announces the member and the active role; each item is a menu option for screen readers.
@Composable
private fun RolePicker(
    name: String,
    current: ManagementRole,
    manage: ManageDecision,
    onSelect: (role: ManagementRole) -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    val activeLabel: String = stringResource(roleLabel(current))
    val pickerLabel: String = stringResource(Res.string.roles_assign_picker, name, activeLabel)

    Box {
        // Re-assigning a member's management role is the write; the trigger that opens the closed ladder is gated.
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = { expanded = true },
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = pickerLabel },
            ) {
                Text(
                    text = activeLabel,
                    style = typography.sm,
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }

        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            ManagementRole.byLevelDescending.forEach { role ->
                val label: String = stringResource(roleLabel(role))
                DropdownMenuItem(
                    text = { Text(text = label, style = typography.sm, color = tokens.popoverForeground) },
                    modifier = Modifier.semantics { this.role = Role.Button },
                    onClick = {
                        expanded = false
                        if (role != current) onSelect(role)
                    },
                )
            }
        }
    }
}

@Composable
private fun GrantButton(name: String, manage: ManageDecision, onGrant: () -> Unit) {
    val tokens = LocalTokens.current
    val grantLabel: String = stringResource(Res.string.roles_grant_action, name)

    ManageGate(decision = manage) { enabled ->
        TextButton(
            onClick = onGrant,
            enabled = enabled,
            modifier = Modifier.clearAndSetSemantics {
                role = Role.Button
                contentDescription = grantLabel
            },
        ) {
            Text(
                text = stringResource(Res.string.roles_grant_action_short),
                color = if (enabled) tokens.primary else tokens.mutedForeground,
                maxLines = 1,
            )
        }
    }
}

@Composable
private fun RemoveButton(name: String, manage: ManageDecision, onRemove: () -> Unit) {
    val tokens = LocalTokens.current
    val removeLabel: String = stringResource(Res.string.roles_remove_action, name)

    ManageGate(decision = manage) { enabled ->
        TextButton(
            onClick = onRemove,
            enabled = enabled,
            modifier = Modifier.clearAndSetSemantics {
                role = Role.Button
                contentDescription = removeLabel
            },
        ) {
            Text(
                text = stringResource(Res.string.roles_remove_action_short),
                color = if (enabled) tokens.destructive else tokens.mutedForeground,
                maxLines = 1,
            )
        }
    }
}

@Composable
private fun PermitRow(permit: PermitGrant, manage: ManageDecision, onRevoke: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = permitName(permit)
    val detail: String = permitDetail(permit)
    val rowDescription: String = stringResource(Res.string.roles_permit_description, name, detail)
    val revokeLabel: String = stringResource(Res.string.roles_revoke_action, name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = detail,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onRevoke,
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics {
                    role = Role.Button
                    contentDescription = revokeLabel
                },
            ) {
                Text(
                    text = stringResource(Res.string.roles_revoke_action_short),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// The capability-grant dialog: pick one of the channel's permit-grantable action keys for the member, then
// confirm. Each key is selectable from the closed list; the confirm stays disabled until one is chosen.
@Composable
private fun GrantCapabilityDialog(
    memberName: String,
    actions: List<ActionPermission>,
    onConfirm: (actionKey: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var selectedKey: String? by remember { mutableStateOf(null) }
    var expanded: Boolean by remember { mutableStateOf(false) }

    val title: String = stringResource(Res.string.roles_grant_dialog_title, memberName)
    val pickLabel: String = stringResource(Res.string.roles_grant_pick_action)
    val triggerLabel: String = selectedKey ?: pickLabel
    val confirmLabel: String = stringResource(Res.string.roles_grant_action_short)
    val dismissLabel: String = stringResource(Res.string.roles_remove_dismiss)

    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = title) },
        text = {
            if (actions.isEmpty()) {
                Text(text = stringResource(Res.string.roles_grant_empty), color = tokens.mutedForeground)
            } else {
                Box {
                    TextButton(
                        onClick = { expanded = true },
                        modifier = Modifier
                            .fillMaxWidth()
                            .semantics { contentDescription = pickLabel },
                    ) {
                        Text(
                            text = triggerLabel,
                            style = typography.sm,
                            color = tokens.primary,
                            maxLines = 1,
                            overflow = TextOverflow.Ellipsis,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                    DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                        actions.forEach { action ->
                            DropdownMenuItem(
                                text = {
                                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
                                        Text(
                                            text = action.actionKey,
                                            style = typography.sm,
                                            color = tokens.popoverForeground,
                                        )
                                        action.description?.takeIf { it.isNotBlank() }?.let { description ->
                                            Text(
                                                text = description,
                                                style = typography.xs,
                                                color = tokens.mutedForeground,
                                            )
                                        }
                                    }
                                },
                                modifier = Modifier.semantics {
                                    role = Role.Button
                                    contentDescription = action.actionKey
                                },
                                onClick = {
                                    selectedKey = action.actionKey
                                    expanded = false
                                },
                            )
                        }
                    }
                }
            }
        },
        confirmButton = {
            val chosen: String? = selectedKey
            TextButton(onClick = { chosen?.let(onConfirm) }, enabled = chosen != null) {
                Text(
                    text = confirmLabel,
                    color = if (chosen != null) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = dismissLabel, color = tokens.mutedForeground, maxLines = 1)
            }
        },
    )
}

@Composable
private fun SectionLabel(label: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = label,
        style = typography.xs,
        color = tokens.mutedForeground,
        modifier = Modifier.padding(start = spacing.s1, top = spacing.s2, bottom = spacing.s1),
    )
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.roles_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.roles_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

/** The member's best display name: username, then the raw user id. */
private fun memberName(member: ChannelMembership): String =
    member.username?.takeIf { it.isNotBlank() } ?: member.userId

/** The permit's grantee name: username, then the raw user id. */
private fun permitName(permit: PermitGrant): String =
    permit.username?.takeIf { it.isNotBlank() } ?: permit.userId

/** A grant's one-line detail: the granted role token, or the capability's action key. */
@Composable
private fun permitDetail(permit: PermitGrant): String =
    when (permit.type) {
        PermitGrantType.Role ->
            stringResource(
                Res.string.roles_permit_role,
                permit.role?.let { stringResource(roleLabel(it)) } ?: "",
            )
        PermitGrantType.Capability ->
            stringResource(Res.string.roles_permit_capability, permit.capabilityActionKey ?: "")
    }

/** Map a [ManagementRole] to its localized label (the canonical Plane-B vocabulary). */
private fun roleLabel(role: ManagementRole): StringResource =
    when (role) {
        ManagementRole.Moderator -> Res.string.roles_role_moderator
        ManagementRole.LeadModerator -> Res.string.roles_role_lead_moderator
        ManagementRole.Editor -> Res.string.roles_role_editor
        ManagementRole.Broadcaster -> Res.string.roles_role_broadcaster
    }

// One row in the action-permission matrix: shows the action key, its effective level, whether an override is
// active, and two write controls (Override + Reset). The Reset button is only shown when overrideLevel != null.
@Composable
private fun ActionPermissionRow(
    action: ActionPermission,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onReset: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val levelLabel: String =
        if (action.overrideLevel != null) {
            stringResource(Res.string.roles_override_active, action.overrideLevel)
        } else {
            stringResource(Res.string.roles_override_default, action.effectiveLevel)
        }

    Row(
        modifier =
            Modifier
                .fillMaxWidth()
                .clip(RoundedCornerShape(spacing.s1))
                .background(tokens.card)
                .padding(horizontal = spacing.s3, vertical = spacing.s2),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s0_5)) {
            Text(
                text = action.actionKey,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = levelLabel,
                style = typography.xs,
                color = if (action.overrideLevel != null) tokens.primary else tokens.mutedForeground,
            )
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = onEdit, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.roles_override_set),
                    style = typography.sm,
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
        if (action.overrideLevel != null) {
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onReset, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.roles_override_reset),
                        style = typography.sm,
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// Dialog for setting a numeric level override on an action. Pre-fills with the current overrideLevel if set.
@Composable
private fun SetOverrideDialog(
    action: ActionPermission,
    onConfirm: (level: Int) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var raw: String by remember { mutableStateOf(action.overrideLevel?.toString() ?: action.effectiveLevel.toString()) }
    val parsed: Int? = raw.toIntOrNull()?.takeIf { it >= 0 }
    val isValid: Boolean = parsed != null

    val title: String = stringResource(Res.string.roles_override_dialog_title, action.actionKey)
    val desc: String = stringResource(Res.string.roles_override_dialog_desc)

    androidx.compose.material3.AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(text = desc, style = typography.sm, color = tokens.mutedForeground)
                OutlinedTextField(
                    value = raw,
                    onValueChange = { raw = it },
                    label = { Text(stringResource(Res.string.roles_override_level_label)) },
                    isError = !isValid && raw.isNotEmpty(),
                    supportingText = {
                        if (!isValid && raw.isNotEmpty()) {
                            Text(stringResource(Res.string.roles_override_level_invalid), color = tokens.destructive)
                        }
                    },
                    keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                    singleLine = true,
                )
            }
        },
        confirmButton = {
            TextButton(onClick = { parsed?.let(onConfirm) }, enabled = isValid) {
                Text(
                    text = stringResource(Res.string.roles_override_confirm),
                    color = if (isValid) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.roles_override_cancel), color = tokens.mutedForeground)
            }
        },
    )
}
