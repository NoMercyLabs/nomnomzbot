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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
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
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.PickerOption
import bot.nomnomz.dashboard.core.designsystem.component.PickerRef
import bot.nomnomz.dashboard.core.designsystem.component.SearchPickerField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ActionPermission
import bot.nomnomz.dashboard.core.network.ChannelMembership
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.core.network.PermitGrantType
import bot.nomnomz.dashboard.core.network.UserSearchResult
import bot.nomnomz.dashboard.feature.roles.state.RolesController
import bot.nomnomz.dashboard.feature.roles.state.RolesState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole as NavManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlin.time.Duration
import kotlin.time.Duration.Companion.days
import kotlin.time.Duration.Companion.hours
import kotlinx.coroutines.launch
import kotlinx.datetime.Clock
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.roles_action_error
import nomnomzbot.composeapp.generated.resources.roles_action_overrides_section
import nomnomzbot.composeapp.generated.resources.roles_assign_confirm
import nomnomzbot.composeapp.generated.resources.roles_assign_pick_role
import nomnomzbot.composeapp.generated.resources.roles_assign_picker
import nomnomzbot.composeapp.generated.resources.roles_assign_viewer_action
import nomnomzbot.composeapp.generated.resources.roles_assign_viewer_title
import nomnomzbot.composeapp.generated.resources.roles_empty
import nomnomzbot.composeapp.generated.resources.roles_error
import nomnomzbot.composeapp.generated.resources.roles_grant_action
import nomnomzbot.composeapp.generated.resources.roles_grant_action_short
import nomnomzbot.composeapp.generated.resources.roles_grant_empty
import nomnomzbot.composeapp.generated.resources.roles_grant_permit_action
import nomnomzbot.composeapp.generated.resources.roles_grant_permit_for_title
import nomnomzbot.composeapp.generated.resources.roles_grant_permit_title
import nomnomzbot.composeapp.generated.resources.roles_grant_pick_action
import nomnomzbot.composeapp.generated.resources.roles_grant_pick_role
import nomnomzbot.composeapp.generated.resources.roles_loading
import nomnomzbot.composeapp.generated.resources.roles_member_description
import nomnomzbot.composeapp.generated.resources.roles_members_section
import nomnomzbot.composeapp.generated.resources.roles_override_active
import nomnomzbot.composeapp.generated.resources.roles_override_cancel
import nomnomzbot.composeapp.generated.resources.roles_override_confirm
import nomnomzbot.composeapp.generated.resources.roles_override_default
import nomnomzbot.composeapp.generated.resources.roles_override_dialog_desc
import nomnomzbot.composeapp.generated.resources.roles_override_dialog_title
import nomnomzbot.composeapp.generated.resources.roles_override_pick
import nomnomzbot.composeapp.generated.resources.roles_override_reset
import nomnomzbot.composeapp.generated.resources.roles_override_set
import nomnomzbot.composeapp.generated.resources.roles_permit_capability
import nomnomzbot.composeapp.generated.resources.roles_permit_description
import nomnomzbot.composeapp.generated.resources.roles_permit_expiry_1d
import nomnomzbot.composeapp.generated.resources.roles_permit_expiry_1h
import nomnomzbot.composeapp.generated.resources.roles_permit_expiry_1w
import nomnomzbot.composeapp.generated.resources.roles_permit_expiry_30d
import nomnomzbot.composeapp.generated.resources.roles_permit_expiry_label
import nomnomzbot.composeapp.generated.resources.roles_permit_expiry_never
import nomnomzbot.composeapp.generated.resources.roles_permit_reason_label
import nomnomzbot.composeapp.generated.resources.roles_permit_reason_placeholder
import nomnomzbot.composeapp.generated.resources.roles_permit_role
import nomnomzbot.composeapp.generated.resources.roles_permit_type_capability
import nomnomzbot.composeapp.generated.resources.roles_permit_type_label
import nomnomzbot.composeapp.generated.resources.roles_permit_type_role
import nomnomzbot.composeapp.generated.resources.roles_permits_empty
import nomnomzbot.composeapp.generated.resources.roles_permits_section
import nomnomzbot.composeapp.generated.resources.roles_remove_action
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
import nomnomzbot.composeapp.generated.resources.roles_role_artist
import nomnomzbot.composeapp.generated.resources.roles_role_broadcaster
import nomnomzbot.composeapp.generated.resources.roles_role_editor
import nomnomzbot.composeapp.generated.resources.roles_role_everyone
import nomnomzbot.composeapp.generated.resources.roles_role_lead_moderator
import nomnomzbot.composeapp.generated.resources.roles_role_moderator
import nomnomzbot.composeapp.generated.resources.roles_role_subscriber
import nomnomzbot.composeapp.generated.resources.roles_role_vip
import nomnomzbot.composeapp.generated.resources.roles_viewer_search_empty
import nomnomzbot.composeapp.generated.resources.roles_viewer_search_label
import nomnomzbot.composeapp.generated.resources.roles_viewer_search_placeholder
import nomnomzbot.composeapp.generated.resources.shell_nav_roles
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

    Column(modifier = Modifier.fillMaxSize().padding(spacing.s6), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
        PageHeader(title = stringResource(Res.string.shell_nav_roles))
        when (val current: RolesState = state) {
            is RolesState.Loading -> CenteredMessage(stringResource(Res.string.roles_loading))
            is RolesState.Empty -> CenteredMessage(stringResource(Res.string.roles_empty))
            is RolesState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is RolesState.Ready ->
                RolesContent(
                    state = current,
                    manage = manage,
                    searchViewers = { query -> controller.searchViewers(query) },
                    onAssignRole = { userId, assignedRole -> scope.launch { controller.assignRole(userId, assignedRole) } },
                    onRemoveRole = { userId -> scope.launch { controller.removeRole(userId) } },
                    onGrantCapability = { userId, key, expiresAt, reason ->
                        scope.launch { controller.grantCapability(userId, key, expiresAt, reason) }
                    },
                    onGrantRole = { userId, grantedRole, expiresAt, reason ->
                        scope.launch { controller.grantRole(userId, grantedRole, expiresAt, reason) }
                    },
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
    searchViewers: suspend (query: String) -> List<UserSearchResult>,
    onAssignRole: (userId: String, role: ManagementRole) -> Unit,
    onRemoveRole: (userId: String) -> Unit,
    onGrantCapability: (userId: String, actionKey: String, expiresAt: String?, reason: String?) -> Unit,
    onGrantRole: (userId: String, role: ManagementRole, expiresAt: String?, reason: String?) -> Unit,
    onRevoke: (userId: String, selector: String?) -> Unit,
    onSetOverride: (actionKey: String, level: Int) -> Unit,
    onResetOverride: (actionKey: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    // The member awaiting a remove confirmation, the permit awaiting a revoke, and the action awaiting an override
    // change — plus the two viewer-reaching flows: the assign-role-to-a-viewer dialog and the grant-permit dialog
    // (opened either with search, or preselected to a member via their row's Grant control).
    var pendingRemove: ChannelMembership? by remember { mutableStateOf(null) }
    var pendingRevoke: PermitGrant? by remember { mutableStateOf(null) }
    var pendingOverride: ActionPermission? by remember { mutableStateOf(null) }
    var assignViewerOpen: Boolean by remember { mutableStateOf(false) }
    var permitTarget: PermitTarget? by remember { mutableStateOf(null) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        state.actionError?.let { detail ->
            item(key = "action-error") {
                ActionErrorBanner(message = stringResource(Res.string.roles_action_error, detail))
            }
        }

        item(key = "members-section") {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                // The section header carries the reach-a-viewer action: assign a management role to any viewer the
                // operator searches for — not just re-pick an existing member's role.
                SectionHeader(
                    label = stringResource(Res.string.roles_members_section),
                    manage = manage,
                    actionLabel = stringResource(Res.string.roles_assign_viewer_action),
                    onAction = { assignViewerOpen = true },
                )
                if (state.members.isNotEmpty()) {
                    Card(modifier = Modifier.fillMaxWidth()) {
                        Column {
                            state.members.forEachIndexed { index, member ->
                                MemberRow(
                                    member = member,
                                    manage = manage,
                                    onAssignRole = { role -> onAssignRole(member.userId, role) },
                                    onRemove = { pendingRemove = member },
                                    onGrant = {
                                        permitTarget =
                                            PermitTarget.Preselected(
                                                ViewerRef(member.userId, memberName(member))
                                            )
                                    },
                                )
                                if (index < state.members.lastIndex) {
                                    Separator()
                                }
                            }
                        }
                    }
                }
            }
        }

        item(key = "permits-section") {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                // The section header carries the reach-a-viewer action: grant a permit (a whole role or a single
                // capability, optionally expiring) to any viewer the operator searches for.
                SectionHeader(
                    label = stringResource(Res.string.roles_permits_section),
                    manage = manage,
                    actionLabel = stringResource(Res.string.roles_grant_permit_action),
                    onAction = { permitTarget = PermitTarget.Search },
                )
                if (state.permits.isEmpty()) {
                    Text(
                        text = stringResource(Res.string.roles_permits_empty),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
                    )
                } else {
                    Card(modifier = Modifier.fillMaxWidth()) {
                        Column {
                            state.permits.forEachIndexed { index, permit ->
                                PermitRow(
                                    permit = permit,
                                    manage = manage,
                                    onRevoke = { pendingRevoke = permit },
                                )
                                if (index < state.permits.lastIndex) {
                                    Separator()
                                }
                            }
                        }
                    }
                }
            }
        }

        if (state.allActions.isNotEmpty()) {
            item(key = "actions-section") {
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    SectionLabel(stringResource(Res.string.roles_action_overrides_section))
                    Card(modifier = Modifier.fillMaxWidth()) {
                        Column {
                            state.allActions.forEachIndexed { index, action ->
                                ActionPermissionRow(
                                    action = action,
                                    manage = manage,
                                    onEdit = { pendingOverride = action },
                                    onReset = { onResetOverride(action.actionKey) },
                                )
                                if (index < state.allActions.lastIndex) {
                                    Separator()
                                }
                            }
                        }
                    }
                }
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

    if (assignViewerOpen) {
        AssignRoleToViewerDialog(
            searchViewers = searchViewers,
            onConfirm = { userId, role ->
                onAssignRole(userId, role)
                assignViewerOpen = false
            },
            onDismiss = { assignViewerOpen = false },
        )
    }

    permitTarget?.let { target ->
        GrantPermitDialog(
            preselected = (target as? PermitTarget.Preselected)?.viewer,
            grantableActions = state.grantableActions,
            searchViewers = searchViewers,
            onGrantCapability = { userId, actionKey, expiresAt, reason ->
                onGrantCapability(userId, actionKey, expiresAt, reason)
                permitTarget = null
            },
            onGrantRole = { userId, role, expiresAt, reason ->
                onGrantRole(userId, role, expiresAt, reason)
                permitTarget = null
            },
            onDismiss = { permitTarget = null },
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
        GrantButton(name = name, manage = manage, onGrant = onGrant)
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
        GlyphButton(
            imageVector = TrashGlyph,
            label = removeLabel,
            onClick = onRemove,
            enabled = enabled,
            tint = tokens.destructive,
        )
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

// ── Viewer-reaching flows (B6 / B7) ──────────────────────────────────────────

/** A chosen viewer for the assign-role / grant-permit flows: the platform User GUID plus a display [name]. */
private data class ViewerRef(val userId: String, val name: String)

/** Which viewer the grant-permit dialog targets: one found via search, or one preselected from a member row. */
private sealed interface PermitTarget {
    /** Open the dialog with the viewer picker enabled (reach an arbitrary viewer). */
    data object Search : PermitTarget

    /** Open the dialog already targeting [viewer] (a member's Grant control). */
    data class Preselected(val viewer: ViewerRef) : PermitTarget
}

/** Whether the grant-permit dialog is issuing a whole-role permit or a single-capability permit. */
private enum class PermitKind { Capability, Role }

/**
 * The offered permit expiries. [duration] is added to "now" at confirm to produce the ISO-8601 `expiresAt`; a null
 * duration means a permanent grant (no expiry sent). Users never see raw timestamps — only these named choices.
 */
private enum class PermitExpiry(val label: StringResource, val duration: Duration?) {
    Never(Res.string.roles_permit_expiry_never, null),
    OneHour(Res.string.roles_permit_expiry_1h, 1.hours),
    OneDay(Res.string.roles_permit_expiry_1d, 24.hours),
    OneWeek(Res.string.roles_permit_expiry_1w, 7.days),
    ThirtyDays(Res.string.roles_permit_expiry_30d, 30.days),
}

// A section header row: the muted section label plus a trailing, manage-gated action button (the reach-a-viewer
// entry point). Below the manage floor the button is disabled with the same "Requires Broadcaster" reason as
// every other write on the page.
@Composable
private fun SectionHeader(
    label: String,
    manage: ManageDecision,
    actionLabel: String,
    onAction: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Box(modifier = Modifier.weight(1f)) { SectionLabel(label) }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onAction,
                enabled = enabled,
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = actionLabel
                },
            ) {
                Text(
                    text = actionLabel,
                    style = typography.sm,
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// The viewer picker: a search field that queries the platform's users (community/search) and lists the matches
// for selection. Once a viewer is chosen it collapses to the selected name plus a "change" affordance. This is
// the piece that lets the assign-role and grant-permit flows reach a viewer who is NOT already a member. The
// [selected] viewer is owned by the parent dialog; this composable owns only its transient query + results.
@Composable
private fun ViewerSearchField(
    searchViewers: suspend (query: String) -> List<UserSearchResult>,
    selected: ViewerRef?,
    onSelect: (ViewerRef) -> Unit,
    onClear: () -> Unit,
) {
    // Delegates to the shared [SearchPickerField] (which was itself extracted from this very search) so the
    // assign-role and grant-permit flows reach a non-member viewer through the exact same debounced picker as
    // economy/tts/moderation — one implementation, not a fourth hand-rolled copy. Maps the platform user shape
    // to the picker's [PickerOption]/[PickerRef] and back to the caller's [ViewerRef].
    SearchPickerField(
        search = { query ->
            searchViewers(query).map { user ->
                PickerOption(
                    id = user.id,
                    label = user.displayName.ifBlank { user.username }.ifBlank { user.id },
                    sublabel = user.username,
                )
            }
        },
        selected = selected?.let { PickerRef(it.userId, it.name) },
        onSelect = { ref -> onSelect(ViewerRef(ref.id, ref.name)) },
        onClear = onClear,
        label = stringResource(Res.string.roles_viewer_search_label),
        placeholder = stringResource(Res.string.roles_viewer_search_placeholder),
        emptyText = stringResource(Res.string.roles_viewer_search_empty),
    )
}

// Assign a PERMANENT management role to any viewer (B6 — the make-a-mod flow). Pick a viewer via search, pick a
// role by NAME, confirm. The backend re-checks no-escalation, so an over-reach surfaces as the page's error banner.
@Composable
private fun AssignRoleToViewerDialog(
    searchViewers: suspend (query: String) -> List<UserSearchResult>,
    onConfirm: (userId: String, role: ManagementRole) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var selectedViewer: ViewerRef? by remember { mutableStateOf(null) }
    var selectedRole: ManagementRole? by remember { mutableStateOf(null) }
    var roleExpanded: Boolean by remember { mutableStateOf(false) }

    val pickRoleLabel: String = stringResource(Res.string.roles_assign_pick_role)
    val roleTrigger: String = selectedRole?.let { stringResource(roleLabel(it)) } ?: pickRoleLabel
    val dismissLabel: String = stringResource(Res.string.roles_remove_dismiss)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.roles_assign_viewer_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                ViewerSearchField(
                    searchViewers = searchViewers,
                    selected = selectedViewer,
                    onSelect = { selectedViewer = it },
                    onClear = { selectedViewer = null },
                )
                Box {
                    TextButton(
                        onClick = { roleExpanded = true },
                        modifier = Modifier
                            .fillMaxWidth()
                            .semantics { contentDescription = pickRoleLabel },
                    ) {
                        Text(
                            text = roleTrigger,
                            style = typography.sm,
                            color = tokens.primary,
                            maxLines = 1,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                    DropdownMenu(expanded = roleExpanded, onDismissRequest = { roleExpanded = false }) {
                        ManagementRole.byLevelDescending.forEach { role ->
                            val label: String = stringResource(roleLabel(role))
                            DropdownMenuItem(
                                text = {
                                    Text(text = label, style = typography.sm, color = tokens.popoverForeground)
                                },
                                modifier = Modifier.semantics { this.role = Role.Button },
                                onClick = {
                                    selectedRole = role
                                    roleExpanded = false
                                },
                            )
                        }
                    }
                }
            }
        },
        confirmButton = {
            val viewer: ViewerRef? = selectedViewer
            val role: ManagementRole? = selectedRole
            val enabled: Boolean = viewer != null && role != null
            TextButton(
                onClick = { if (viewer != null && role != null) onConfirm(viewer.userId, role) },
                enabled = enabled,
            ) {
                Text(
                    text = stringResource(Res.string.roles_assign_confirm),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
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

// Grant a PERMIT to a viewer (B7): a whole management role or a single capability, optionally expiring, with an
// optional reason. Reaches an arbitrary viewer via search when [preselected] is null, or targets a member row's
// viewer directly. The confirm stays disabled until a viewer AND a role/capability are chosen.
@Composable
private fun GrantPermitDialog(
    preselected: ViewerRef?,
    grantableActions: List<ActionPermission>,
    searchViewers: suspend (query: String) -> List<UserSearchResult>,
    onGrantCapability: (userId: String, actionKey: String, expiresAt: String?, reason: String?) -> Unit,
    onGrantRole: (userId: String, role: ManagementRole, expiresAt: String?, reason: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var selectedViewer: ViewerRef? by remember { mutableStateOf(preselected) }
    var kind: PermitKind by remember { mutableStateOf(PermitKind.Capability) }
    var selectedActionKey: String? by remember { mutableStateOf(null) }
    var selectedRole: ManagementRole? by remember { mutableStateOf(null) }
    var expiry: PermitExpiry by remember { mutableStateOf(PermitExpiry.Never) }
    var reason: String by remember { mutableStateOf("") }

    var kindExpanded: Boolean by remember { mutableStateOf(false) }
    var actionExpanded: Boolean by remember { mutableStateOf(false) }
    var roleExpanded: Boolean by remember { mutableStateOf(false) }
    var expiryExpanded: Boolean by remember { mutableStateOf(false) }

    val title: String =
        preselected?.let { stringResource(Res.string.roles_grant_permit_for_title, it.name) }
            ?: stringResource(Res.string.roles_grant_permit_title)
    val dismissLabel: String = stringResource(Res.string.roles_remove_dismiss)

    val kindLabel: String =
        when (kind) {
            PermitKind.Capability -> stringResource(Res.string.roles_permit_type_capability)
            PermitKind.Role -> stringResource(Res.string.roles_permit_type_role)
        }
    val actionTrigger: String = selectedActionKey ?: stringResource(Res.string.roles_grant_pick_action)
    val roleTrigger: String =
        selectedRole?.let { stringResource(roleLabel(it)) } ?: stringResource(Res.string.roles_grant_pick_role)

    val viewer: ViewerRef? = selectedViewer
    val enabled: Boolean =
        viewer != null &&
            when (kind) {
                PermitKind.Capability -> selectedActionKey != null
                PermitKind.Role -> selectedRole != null
            }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                ViewerSearchField(
                    searchViewers = searchViewers,
                    selected = selectedViewer,
                    onSelect = { selectedViewer = it },
                    onClear = { selectedViewer = null },
                )

                // Permit type: whole role vs single capability.
                LabeledDropdown(
                    label = stringResource(Res.string.roles_permit_type_label),
                    trigger = kindLabel,
                    expanded = kindExpanded,
                    onExpandedChange = { kindExpanded = it },
                ) {
                    DropdownMenuItem(
                        text = {
                            Text(
                                text = stringResource(Res.string.roles_permit_type_capability),
                                style = typography.sm,
                                color = tokens.popoverForeground,
                            )
                        },
                        modifier = Modifier.semantics { role = Role.Button },
                        onClick = {
                            kind = PermitKind.Capability
                            kindExpanded = false
                        },
                    )
                    DropdownMenuItem(
                        text = {
                            Text(
                                text = stringResource(Res.string.roles_permit_type_role),
                                style = typography.sm,
                                color = tokens.popoverForeground,
                            )
                        },
                        modifier = Modifier.semantics { role = Role.Button },
                        onClick = {
                            kind = PermitKind.Role
                            kindExpanded = false
                        },
                    )
                }

                when (kind) {
                    PermitKind.Capability ->
                        if (grantableActions.isEmpty()) {
                            Text(
                                text = stringResource(Res.string.roles_grant_empty),
                                style = typography.sm,
                                color = tokens.mutedForeground,
                            )
                        } else {
                            LabeledDropdown(
                                label = stringResource(Res.string.roles_permit_type_capability),
                                trigger = actionTrigger,
                                expanded = actionExpanded,
                                onExpandedChange = { actionExpanded = it },
                            ) {
                                grantableActions.forEach { action ->
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
                                            selectedActionKey = action.actionKey
                                            actionExpanded = false
                                        },
                                    )
                                }
                            }
                        }
                    PermitKind.Role ->
                        LabeledDropdown(
                            label = stringResource(Res.string.roles_permit_type_role),
                            trigger = roleTrigger,
                            expanded = roleExpanded,
                            onExpandedChange = { roleExpanded = it },
                        ) {
                            ManagementRole.byLevelDescending.forEach { role ->
                                val label: String = stringResource(roleLabel(role))
                                DropdownMenuItem(
                                    text = {
                                        Text(text = label, style = typography.sm, color = tokens.popoverForeground)
                                    },
                                    modifier = Modifier.semantics { this.role = Role.Button },
                                    onClick = {
                                        selectedRole = role
                                        roleExpanded = false
                                    },
                                )
                            }
                        }
                }

                // Optional expiry — a named duration; "Never" sends no expiry.
                LabeledDropdown(
                    label = stringResource(Res.string.roles_permit_expiry_label),
                    trigger = stringResource(expiry.label),
                    expanded = expiryExpanded,
                    onExpandedChange = { expiryExpanded = it },
                ) {
                    PermitExpiry.entries.forEach { choice ->
                        DropdownMenuItem(
                            text = {
                                Text(
                                    text = stringResource(choice.label),
                                    style = typography.sm,
                                    color = tokens.popoverForeground,
                                )
                            },
                            modifier = Modifier.semantics { role = Role.Button },
                            onClick = {
                                expiry = choice
                                expiryExpanded = false
                            },
                        )
                    }
                }

                AppTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.roles_permit_reason_label),
                    placeholder = stringResource(Res.string.roles_permit_reason_placeholder),
                )
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    val target: ViewerRef = viewer ?: return@TextButton
                    val expiresAt: String? = expiry.duration?.let { Clock.System.now().plus(it).toString() }
                    val reasonOrNull: String? = reason.trim().ifBlank { null }
                    when (kind) {
                        PermitKind.Capability ->
                            selectedActionKey?.let { key ->
                                onGrantCapability(target.userId, key, expiresAt, reasonOrNull)
                            }
                        PermitKind.Role ->
                            selectedRole?.let { role ->
                                onGrantRole(target.userId, role, expiresAt, reasonOrNull)
                            }
                    }
                },
                enabled = enabled,
            ) {
                Text(
                    text = stringResource(Res.string.roles_grant_action_short),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
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

// A labelled dropdown control: a muted caption above a full-width trigger that opens [menuContent]. Shared by the
// grant-permit dialog's type / capability / role / expiry pickers so each reads consistently.
@Composable
private fun LabeledDropdown(
    label: String,
    trigger: String,
    expanded: Boolean,
    onExpandedChange: (Boolean) -> Unit,
    menuContent: @Composable ColumnScope.() -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = label, style = typography.sm, color = tokens.foreground)
        Box {
            TextButton(
                onClick = { onExpandedChange(true) },
                modifier = Modifier
                    .fillMaxWidth()
                    .semantics { contentDescription = label },
            ) {
                Text(
                    text = trigger,
                    style = typography.sm,
                    color = tokens.primary,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                    modifier = Modifier.fillMaxWidth(),
                )
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { onExpandedChange(false) }, content = menuContent)
        }
    }
}

@Composable
private fun SectionLabel(label: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Text(
        text = label,
        style = typography.sm,
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

/**
 * One named rung of the unified authorization ladder (roles-permissions §0). [level] is the internal comparison
 * value the backend gates on; [label] is the only thing users ever see — the numbers stay internal.
 */
private data class LadderRung(val level: Int, val label: StringResource)

/**
 * The unified ladder low→high (0/2/4/6/10/20/30/40): Plane-A community rungs then Plane-B management rungs,
 * aligned on their shared Moderator rung. The action-floor picker offers these by name; the row label maps a
 * level onto one. This is the single source for the ladder→name mapping the screen renders.
 */
private val LadderRungs: List<LadderRung> = listOf(
    LadderRung(0, Res.string.roles_role_everyone),
    LadderRung(2, Res.string.roles_role_subscriber),
    LadderRung(4, Res.string.roles_role_vip),
    LadderRung(6, Res.string.roles_role_artist),
    LadderRung(10, Res.string.roles_role_moderator),
    LadderRung(20, Res.string.roles_role_lead_moderator),
    LadderRung(30, Res.string.roles_role_editor),
    LadderRung(40, Res.string.roles_role_broadcaster),
)

/**
 * The localized NAME of the ladder rung a [level] satisfies — the highest rung whose threshold it meets. Enforces
 * the "users never see numeric permission levels" rule: every effective/override level renders as a role name.
 */
private fun ladderRoleLabel(level: Int): StringResource =
    LadderRungs.lastOrNull { level >= it.level }?.label ?: Res.string.roles_role_everyone

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

    // Users see the role NAME, never the numeric level (the numbers are internal gating only).
    val roleName: String = stringResource(ladderRoleLabel(action.overrideLevel ?: action.effectiveLevel))
    val levelLabel: String =
        if (action.overrideLevel != null) {
            stringResource(Res.string.roles_override_active, roleName)
        } else {
            stringResource(Res.string.roles_override_default, roleName)
        }

    Row(
        modifier = Modifier
            .fillMaxWidth()
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
            GlyphButton(
                imageVector = EditGlyph,
                label = stringResource(Res.string.roles_override_set),
                onClick = onEdit,
                enabled = enabled,
            )
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

// Dialog for setting the minimum role that can use an action. The user picks a role NAME (never a numeric level —
// those are internal); the picker offers the named ladder rungs at or above the action's safety floor, pre-selected
// to the current override (or the effective floor when none is set). Confirm sends the picked rung's ladder level.
@Composable
private fun SetOverrideDialog(
    action: ActionPermission,
    onConfirm: (level: Int) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The selectable rungs: named ladder positions at or above the action's safety floor — a lower floor may not
    // be set (DangerTier guards that server-side; the picker mirrors the guard). Highest first, like the role picker.
    val choices: List<LadderRung> =
        LadderRungs.filter { it.level >= action.floorLevel }.sortedByDescending { it.level }
    var selected: Int by remember { mutableStateOf(action.overrideLevel ?: action.effectiveLevel) }
    var expanded: Boolean by remember { mutableStateOf(false) }

    val title: String = stringResource(Res.string.roles_override_dialog_title, action.actionKey)
    val desc: String = stringResource(Res.string.roles_override_dialog_desc)
    val activeLabel: String = stringResource(ladderRoleLabel(selected))
    val pickLabel: String = stringResource(Res.string.roles_override_pick)

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(text = desc, style = typography.sm, color = tokens.mutedForeground)
                Box {
                    TextButton(
                        onClick = { expanded = true },
                        modifier = Modifier
                            .fillMaxWidth()
                            .semantics { contentDescription = pickLabel },
                    ) {
                        Text(
                            text = activeLabel,
                            style = typography.sm,
                            color = tokens.primary,
                            maxLines = 1,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                    DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                        choices.forEach { rung ->
                            val label: String = stringResource(rung.label)
                            DropdownMenuItem(
                                text = {
                                    Text(text = label, style = typography.sm, color = tokens.popoverForeground)
                                },
                                modifier = Modifier.semantics { role = Role.Button },
                                onClick = {
                                    selected = rung.level
                                    expanded = false
                                },
                            )
                        }
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = { onConfirm(selected) }) {
                Text(
                    text = stringResource(Res.string.roles_override_confirm),
                    color = tokens.primary,
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
