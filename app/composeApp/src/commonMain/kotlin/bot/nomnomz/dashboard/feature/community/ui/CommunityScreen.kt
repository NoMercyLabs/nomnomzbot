// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.DotsVerticalGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.height
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityTrustLevel
import bot.nomnomz.dashboard.core.network.UserStats
import bot.nomnomz.dashboard.core.network.ViewerAnalyticsProfile
import bot.nomnomz.dashboard.feature.community.state.CommunityController
import bot.nomnomz.dashboard.feature.community.state.CommunityState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_community
import nomnomzbot.composeapp.generated.resources.community_action_error
import nomnomzbot.composeapp.generated.resources.community_data_add
import nomnomzbot.composeapp.generated.resources.community_data_delete
import nomnomzbot.composeapp.generated.resources.community_data_delete_confirm
import nomnomzbot.composeapp.generated.resources.community_data_delete_message
import nomnomzbot.composeapp.generated.resources.community_data_delete_title
import nomnomzbot.composeapp.generated.resources.community_data_empty
import nomnomzbot.composeapp.generated.resources.community_data_key
import nomnomzbot.composeapp.generated.resources.community_data_key_required
import nomnomzbot.composeapp.generated.resources.community_data_section
import nomnomzbot.composeapp.generated.resources.community_data_value
import nomnomzbot.composeapp.generated.resources.community_ban_action
import nomnomzbot.composeapp.generated.resources.community_ban_action_short
import nomnomzbot.composeapp.generated.resources.community_ban_confirm
import nomnomzbot.composeapp.generated.resources.community_ban_dismiss
import nomnomzbot.composeapp.generated.resources.community_ban_message
import nomnomzbot.composeapp.generated.resources.community_ban_reason
import nomnomzbot.composeapp.generated.resources.community_ban_title
import nomnomzbot.composeapp.generated.resources.community_banned
import nomnomzbot.composeapp.generated.resources.community_empty
import nomnomzbot.composeapp.generated.resources.community_error
import nomnomzbot.composeapp.generated.resources.community_loading
import nomnomzbot.composeapp.generated.resources.community_retry
import nomnomzbot.composeapp.generated.resources.community_row_description
import nomnomzbot.composeapp.generated.resources.community_trust_label
import nomnomzbot.composeapp.generated.resources.community_trust_moderator
import nomnomzbot.composeapp.generated.resources.community_trust_picker
import nomnomzbot.composeapp.generated.resources.community_trust_subscriber
import nomnomzbot.composeapp.generated.resources.community_trust_viewer
import nomnomzbot.composeapp.generated.resources.community_trust_vip
import nomnomzbot.composeapp.generated.resources.community_unban_action
import nomnomzbot.composeapp.generated.resources.community_unban_action_short
import nomnomzbot.composeapp.generated.resources.community_more_actions
import nomnomzbot.composeapp.generated.resources.community_shoutout_action
import nomnomzbot.composeapp.generated.resources.community_top_chatters_messages
import nomnomzbot.composeapp.generated.resources.community_top_chatters_title
import nomnomzbot.composeapp.generated.resources.community_shoutout_action_desc
import nomnomzbot.composeapp.generated.resources.community_unban_confirm
import nomnomzbot.composeapp.generated.resources.community_unban_dismiss
import nomnomzbot.composeapp.generated.resources.community_unban_message
import nomnomzbot.composeapp.generated.resources.community_unban_title
import nomnomzbot.composeapp.generated.resources.community_vip_grant
import nomnomzbot.composeapp.generated.resources.community_vip_grant_desc
import nomnomzbot.composeapp.generated.resources.community_vip_revoke
import nomnomzbot.composeapp.generated.resources.community_vip_revoke_desc
import nomnomzbot.composeapp.generated.resources.community_view_stats
import nomnomzbot.composeapp.generated.resources.community_stats_title
import nomnomzbot.composeapp.generated.resources.community_stats_messages
import nomnomzbot.composeapp.generated.resources.community_stats_watch_hours
import nomnomzbot.composeapp.generated.resources.community_stats_commands_used
import nomnomzbot.composeapp.generated.resources.community_stats_redemptions
import nomnomzbot.composeapp.generated.resources.community_stats_follower
import nomnomzbot.composeapp.generated.resources.community_stats_subscriber
import nomnomzbot.composeapp.generated.resources.community_stats_yes
import nomnomzbot.composeapp.generated.resources.community_stats_no
import nomnomzbot.composeapp.generated.resources.community_stats_first_seen
import nomnomzbot.composeapp.generated.resources.community_stats_last_active
import nomnomzbot.composeapp.generated.resources.community_stats_never
import nomnomzbot.composeapp.generated.resources.community_stats_loading
import nomnomzbot.composeapp.generated.resources.community_stats_error
import nomnomzbot.composeapp.generated.resources.community_stats_close
import nomnomzbot.composeapp.generated.resources.community_gdpr_section
import nomnomzbot.composeapp.generated.resources.community_gdpr_export
import nomnomzbot.composeapp.generated.resources.community_gdpr_export_desc
import nomnomzbot.composeapp.generated.resources.community_gdpr_export_confirm
import nomnomzbot.composeapp.generated.resources.community_gdpr_export_done
import nomnomzbot.composeapp.generated.resources.community_gdpr_erase
import nomnomzbot.composeapp.generated.resources.community_gdpr_erase_desc
import nomnomzbot.composeapp.generated.resources.community_gdpr_erase_confirm
import nomnomzbot.composeapp.generated.resources.community_gdpr_erase_done
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Community page (frontend-ia.md §3): the channel's viewers — every member is real data from
// [CommunityController] (the backend sources it from the Twitch API + chat history). The screen is a pure
// projection of the controller's state; it loads on first composition and offers a retry on failure. Each
// member is actionable: a trust-level picker that acts directly (non-destructive) and a Ban / Unban
// affordance that lifts/applies a ban only once the moderator confirms it in the shared ConfirmDialog.
@Composable
fun CommunityScreen(controller: CommunityController, role: ManagementRole?) {
    val state: CommunityState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Community gates every write control at its single Moderator manage floor
    // (frontend-ia.md §3). A caller below it sees the member list but the trust-picker / ban / unban controls
    // are disabled with "Requires Moderator" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Community)
    val isBroadcaster: Boolean = role == ManagementRole.Broadcaster
    // Viewer custom-data WRITE floor is Editor (handoff: read Moderator, write Editor) — higher than the page's
    // Moderator manage floor, so the add/delete controls get their own decision, disabled-with-reason below it.
    val dataWrite: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Editor)

    // Per-user stats dialog state — null means closed.
    var statsTarget: CommunityMember? by remember { mutableStateOf(null) }
    var statsData: UserStats? by remember { mutableStateOf(null) }
    // The foreign-viewer-capable channel analytics profile (fetched via internalUserId) — the moderator-readable
    // per-viewer engagement, distinct from the self-only usersApi stats.
    var viewerAnalytics: ViewerAnalyticsProfile? by remember { mutableStateOf(null) }
    var statsLoading: Boolean by remember { mutableStateOf(false) }
    var statsError: Boolean by remember { mutableStateOf(false) }
    // The selected viewer's custom key/value data (null until loaded; empty map = loaded, none). Independent of
    // the stats load so the data section shows even when the self-only stats call fails for a foreign viewer.
    var viewerData: Map<String, String>? by remember { mutableStateOf(null) }
    var viewerDataError: String? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    // When a stats target is selected, load their engagement (channel analytics profile + self stats) and their
    // custom data. The analytics profile works for ANY viewer (moderator read via internalUserId); the self-only
    // usersApi stats supplements it with first-seen / last-active when available.
    LaunchedEffect(statsTarget) {
        val target: CommunityMember = statsTarget ?: return@LaunchedEffect
        statsData = null
        viewerAnalytics = null
        statsError = false
        statsLoading = true
        viewerData = null
        viewerDataError = null
        viewerAnalytics = controller.getViewerAnalytics(target)
        statsData = controller.getUserStats(target.id)
        // Only a true error when NEITHER source produced anything (e.g. a foreign viewer with no internal id).
        statsError = viewerAnalytics == null && statsData == null
        statsLoading = false
        viewerData = controller.getViewerData(target.id)
    }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: CommunityState = state) {
            is CommunityState.Loading -> CenteredMessage(stringResource(Res.string.community_loading))
            is CommunityState.Empty -> CenteredMessage(stringResource(Res.string.community_empty))
            is CommunityState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is CommunityState.Ready ->
                MemberList(
                    members = current.members,
                    topChatters = current.topChatters,
                    actionError = current.actionError,
                    manage = manage,
                    onSetTrust = { userId, level -> scope.launch { controller.setTrust(userId, level) } },
                    onBan = { userId, reason -> scope.launch { controller.ban(userId, reason) } },
                    onUnban = { userId -> scope.launch { controller.unban(userId) } },
                    onShoutout = { userId -> scope.launch { controller.shoutout(userId) } },
                    onVipToggle = { userId, isVip ->
                        scope.launch {
                            if (isVip) controller.removeVip(userId) else controller.addVip(userId)
                        }
                    },
                    onViewStats = { member -> statsTarget = member },
                )
        }
    }

    // Per-member stats + GDPR dialog (shown above the main content).
    statsTarget?.let { target ->
        val name: String = memberName(target)
        ViewerStatsDialog(
            name = name,
            stats = statsData,
            analytics = viewerAnalytics,
            loading = statsLoading,
            error = statsError,
            isBroadcaster = isBroadcaster,
            viewerData = viewerData,
            dataWrite = dataWrite,
            dataError = viewerDataError,
            onSetDatum = { key, value ->
                scope.launch {
                    val err: String? = controller.setViewerDatum(target.id, key, value)
                    viewerDataError = err
                    if (err == null) viewerData = controller.getViewerData(target.id)
                }
            },
            onDeleteDatum = { key ->
                scope.launch {
                    val err: String? = controller.deleteViewerDatum(target.id, key)
                    viewerDataError = err
                    if (err == null) viewerData = controller.getViewerData(target.id)
                }
            },
            onExport = {
                scope.launch {
                    controller.exportUserData(target.id)
                    statsTarget = null
                }
            },
            onErase = {
                scope.launch {
                    controller.eraseUserData(target.id)
                    statsTarget = null
                }
            },
            onDismiss = { statsTarget = null },
        )
    }
}

@Composable
private fun MemberList(
    members: List<CommunityMember>,
    topChatters: List<ChatActivityEntry>,
    actionError: String?,
    manage: ManageDecision,
    onSetTrust: (userId: String, level: String) -> Unit,
    onBan: (userId: String, reason: String) -> Unit,
    onUnban: (userId: String) -> Unit,
    onShoutout: (userId: String) -> Unit,
    onVipToggle: (userId: String, isVip: Boolean) -> Unit,
    onViewStats: (CommunityMember) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The member awaiting a ban / unban confirmation, if any — the screen owns the dialog's open/closed state.
    var pendingBan: CommunityMember? by remember { mutableStateOf(null) }
    var pendingUnban: CommunityMember? by remember { mutableStateOf(null) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        item(key = "page-header") { PageHeader(title = stringResource(Res.string.shell_nav_community)) }
        actionError?.let { detail ->
            item(key = "action-error") {
                ActionErrorBanner(message = stringResource(Res.string.community_action_error, detail))
            }
        }
        if (topChatters.isNotEmpty()) {
            item(key = "top-chatters-header") {
                Text(
                    text = stringResource(Res.string.community_top_chatters_title),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    modifier = Modifier
                        .fillMaxWidth()
                        .padding(horizontal = spacing.s1, vertical = spacing.s1),
                )
            }
            item(key = "top-chatters-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    val chatters: List<ChatActivityEntry> = topChatters.take(10)
                    chatters.forEachIndexed { index, entry ->
                        Row(
                            modifier = Modifier
                                .fillMaxWidth()
                                .padding(horizontal = spacing.s4, vertical = spacing.s3),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Row(
                                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                                verticalAlignment = Alignment.CenterVertically,
                            ) {
                                Text(
                                    text = "#${entry.rank}",
                                    style = typography.sm,
                                    color = tokens.mutedForeground,
                                )
                                Text(
                                    text = entry.displayName.ifBlank { entry.userId },
                                    style = typography.sm,
                                    color = tokens.foreground,
                                    maxLines = 1,
                                    overflow = TextOverflow.Ellipsis,
                                )
                            }
                            Text(
                                text = stringResource(Res.string.community_top_chatters_messages, entry.points),
                                style = typography.sm,
                                color = tokens.mutedForeground,
                            )
                        }
                        if (index < chatters.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
            item(key = "top-chatters-spacer") {
                androidx.compose.foundation.layout.Spacer(
                    modifier = Modifier.padding(top = spacing.s2),
                )
            }
        }
        item(key = "members-card") {
            Card(modifier = Modifier.fillMaxWidth()) {
                members.forEachIndexed { index, member ->
                    MemberRow(
                        member = member,
                        manage = manage,
                        onSetTrust = { level -> onSetTrust(member.id, level) },
                        onBan = { pendingBan = member },
                        onUnban = { pendingUnban = member },
                        onShoutout = { onShoutout(member.id) },
                        onVipToggle = { onVipToggle(member.id, member.trustLevel == CommunityTrustLevel.Vip) },
                        onViewStats = { onViewStats(member) },
                    )
                    if (index < members.lastIndex) {
                        Separator()
                    }
                }
            }
        }
    }

    val banReason: String = stringResource(Res.string.community_ban_reason)
    pendingBan?.let { member ->
        val name: String = memberName(member)
        ConfirmDialog(
            title = stringResource(Res.string.community_ban_title),
            message = stringResource(Res.string.community_ban_message, name),
            confirmLabel = stringResource(Res.string.community_ban_confirm),
            dismissLabel = stringResource(Res.string.community_ban_dismiss),
            destructive = true,
            onConfirm = {
                onBan(member.id, banReason)
                pendingBan = null
            },
            onDismiss = { pendingBan = null },
        )
    }

    pendingUnban?.let { member ->
        val name: String = memberName(member)
        ConfirmDialog(
            title = stringResource(Res.string.community_unban_title),
            message = stringResource(Res.string.community_unban_message, name),
            confirmLabel = stringResource(Res.string.community_unban_confirm),
            dismissLabel = stringResource(Res.string.community_unban_dismiss),
            destructive = true,
            onConfirm = {
                onUnban(member.id)
                pendingUnban = null
            },
            onDismiss = { pendingUnban = null },
        )
    }
}

@Composable
private fun MemberRow(
    member: CommunityMember,
    manage: ManageDecision,
    onSetTrust: (level: String) -> Unit,
    onBan: () -> Unit,
    onUnban: () -> Unit,
    onShoutout: () -> Unit,
    onVipToggle: () -> Unit,
    onViewStats: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = memberName(member)
    val standingLabel: String = stringResource(trustLabel(member.trustLevel))
    val rowDescription: String =
        stringResource(Res.string.community_row_description, name, standingLabel)

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
            // The identity reads as one node; the controls below carry their own action labels.
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
        )
        if (member.isBanned) {
            Badge(
                label = stringResource(Res.string.community_banned),
                background = tokens.destructive,
                foreground = tokens.destructiveForeground,
            )
        }
        TrustPicker(name = name, current = member.trustLevel, manage = manage, onSelect = onSetTrust)
        if (member.isBanned) {
            UnbanButton(name = name, manage = manage, onUnban = onUnban)
        } else {
            BanButton(name = name, manage = manage, onBan = onBan)
        }
        MoreActionsMenu(
            name = name,
            isVip = member.trustLevel == CommunityTrustLevel.Vip,
            manage = manage,
            onShoutout = onShoutout,
            onVipToggle = onVipToggle,
            onViewStats = onViewStats,
        )
    }
}

// Overflow menu (⋮) that holds the secondary per-member actions: /shoutout and VIP grant/revoke. Both are
// gated behind the manage decision so they stay unreachable when the caller lacks the Moderator floor.
@Composable
private fun MoreActionsMenu(
    name: String,
    isVip: Boolean,
    manage: ManageDecision,
    onShoutout: () -> Unit,
    onVipToggle: () -> Unit,
    onViewStats: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val moreDesc: String = stringResource(Res.string.community_more_actions, name)

    var expanded: Boolean by remember { mutableStateOf(false) }

    Box {
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = DotsVerticalGlyph,
                label = moreDesc,
                onClick = { expanded = true },
                enabled = enabled,
            )
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.community_view_stats),
                        style = typography.sm,
                        color = tokens.popoverForeground,
                    )
                },
                onClick = {
                    expanded = false
                    onViewStats()
                },
            )
            val shoutoutLabel: String = stringResource(Res.string.community_shoutout_action_desc, name)
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(Res.string.community_shoutout_action),
                        style = typography.sm,
                        color = tokens.popoverForeground,
                    )
                },
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = shoutoutLabel
                },
                onClick = {
                    expanded = false
                    onShoutout()
                },
            )
            val vipLabel: String = stringResource(
                if (isVip) Res.string.community_vip_revoke_desc else Res.string.community_vip_grant_desc,
                name,
            )
            DropdownMenuItem(
                text = {
                    Text(
                        text = stringResource(
                            if (isVip) Res.string.community_vip_revoke else Res.string.community_vip_grant
                        ),
                        style = typography.sm,
                        color = if (isVip) tokens.destructive else tokens.popoverForeground,
                    )
                },
                modifier = Modifier.semantics {
                    role = Role.Button
                    contentDescription = vipLabel
                },
                onClick = {
                    expanded = false
                    onVipToggle()
                },
            )
        }
    }
}

// The non-destructive trust control: a labelled trigger that opens the closed menu of trust levels and sets
// the chosen one directly (no confirmation — it's reversible). The trigger announces the member and the
// active level; each item is a menu option for screen readers.
@Composable
private fun TrustPicker(
    name: String,
    current: String,
    manage: ManageDecision,
    onSelect: (level: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    val activeLabel: String = stringResource(trustLabel(current))
    val pickerLabel: String = stringResource(Res.string.community_trust_picker, name, activeLabel)

    // The picker trigger is the write affordance: opening the menu is the only path to setting a trust level,
    // so gating the trigger gates the write (the menu items stay unreachable when denied).
    Box {
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
            CommunityTrustLevel.all.forEach { level ->
                val label: String = stringResource(trustLabel(level))
                // Resolved here (a @Composable call) so the semantics lambda only reads the plain string.
                val itemLabel: String = stringResource(Res.string.community_trust_label, label)
                DropdownMenuItem(
                    text = { Text(text = label, style = typography.sm, color = tokens.popoverForeground) },
                    modifier = Modifier.semantics {
                        role = Role.Button
                        contentDescription = itemLabel
                    },
                    onClick = {
                        expanded = false
                        if (level != current) onSelect(level)
                    },
                )
            }
        }
    }
}

@Composable
private fun BanButton(name: String, manage: ManageDecision, onBan: () -> Unit) {
    val tokens = LocalTokens.current
    val banLabel: String = stringResource(Res.string.community_ban_action, name)

    ManageGate(decision = manage) { enabled ->
        TextButton(
            onClick = onBan,
            enabled = enabled,
            modifier = Modifier.clearAndSetSemantics {
                role = Role.Button
                contentDescription = banLabel
            },
        ) {
            Text(
                text = stringResource(Res.string.community_ban_action_short),
                color = if (enabled) tokens.destructive else tokens.mutedForeground,
                maxLines = 1,
            )
        }
    }
}

@Composable
private fun UnbanButton(name: String, manage: ManageDecision, onUnban: () -> Unit) {
    val tokens = LocalTokens.current
    val unbanLabel: String = stringResource(Res.string.community_unban_action, name)

    ManageGate(decision = manage) { enabled ->
        TextButton(
            onClick = onUnban,
            enabled = enabled,
            modifier = Modifier.clearAndSetSemantics {
                role = Role.Button
                contentDescription = unbanLabel
            },
        ) {
            Text(
                text = stringResource(Res.string.community_unban_action_short),
                color = if (enabled) tokens.primary else tokens.mutedForeground,
                maxLines = 1,
            )
        }
    }
}

@Composable
private fun Badge(
    label: String,
    background: Color,
    foreground: Color,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(
        modifier = Modifier
            .clip(RoundedCornerShape(tokens.radius.sm))
            .background(background)
            .padding(horizontal = spacing.s2, vertical = spacing.s1),
    ) {
        Text(text = label, style = typography.xs, color = foreground, maxLines = 1)
    }
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
                text = stringResource(Res.string.community_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.community_retry)) }
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

// Per-viewer engagement stats + GDPR dialog. Shown when a moderator taps "View stats" in the overflow menu.
// Stats load in the background (LaunchedEffect in the parent); the dialog handles loading/error inline.
// GDPR actions (export/erase) are hidden for non-Broadcaster callers.
@Composable
private fun ViewerStatsDialog(
    name: String,
    stats: UserStats?,
    analytics: ViewerAnalyticsProfile?,
    loading: Boolean,
    error: Boolean,
    isBroadcaster: Boolean,
    viewerData: Map<String, String>?,
    dataWrite: ManageDecision,
    dataError: String?,
    onSetDatum: (key: String, value: String) -> Unit,
    onDeleteDatum: (key: String) -> Unit,
    onExport: () -> Unit,
    onErase: () -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var pendingErase: Boolean by remember { mutableStateOf(false) }
    var pendingExport: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.community_stats_title, name), style = typography.base) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                when {
                    loading -> {
                        Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
                            Spinner(color = tokens.primary)
                        }
                        Text(
                            text = stringResource(Res.string.community_stats_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                            textAlign = TextAlign.Center,
                            modifier = Modifier.fillMaxWidth(),
                        )
                    }
                    error -> {
                        Text(
                            text = stringResource(Res.string.community_stats_error),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    }
                    else -> {
                        val yes: String = stringResource(Res.string.community_stats_yes)
                        val no: String = stringResource(Res.string.community_stats_no)
                        // The channel analytics profile (foreign-viewer-capable) is the authoritative engagement
                        // source; the self-only usersApi stats is the fallback when no internal id resolved.
                        if (analytics != null) {
                            StatRow(label = stringResource(Res.string.community_stats_messages), value = analytics.totalMessages.toString())
                            StatRow(label = stringResource(Res.string.community_stats_watch_hours), value = (analytics.totalWatchSeconds / 3600.0).toFixed1())
                            StatRow(label = stringResource(Res.string.community_stats_commands_used), value = analytics.totalCommandsUsed.toString())
                            StatRow(label = stringResource(Res.string.community_stats_redemptions), value = analytics.totalRedemptions.toString())
                            StatRow(label = stringResource(Res.string.community_stats_follower), value = if (analytics.isFollower) yes else no)
                            StatRow(
                                label = stringResource(Res.string.community_stats_subscriber),
                                value = if (analytics.isSubscriber) analytics.subTier?.takeIf { it.isNotBlank() } ?: yes else no,
                            )
                        } else if (stats != null) {
                            StatRow(label = stringResource(Res.string.community_stats_messages), value = stats.messageCount.toString())
                            StatRow(label = stringResource(Res.string.community_stats_watch_hours), value = stats.watchHours.toFixed1())
                            StatRow(label = stringResource(Res.string.community_stats_commands_used), value = stats.commandsUsed.toString())
                        }
                        stats?.let {
                            StatRow(
                                label = stringResource(Res.string.community_stats_first_seen),
                                value = it.firstSeen ?: stringResource(Res.string.community_stats_never),
                            )
                            StatRow(
                                label = stringResource(Res.string.community_stats_last_active),
                                value = it.lastActive ?: stringResource(Res.string.community_stats_never),
                            )
                        }

                        if (isBroadcaster) {
                            Spacer(modifier = Modifier.height(spacing.s2))
                            Separator()
                            Spacer(modifier = Modifier.height(spacing.s2))
                            Text(
                                text = stringResource(Res.string.community_gdpr_section),
                                style = typography.xs,
                                color = tokens.mutedForeground,
                            )
                            TextButton(
                                onClick = { pendingExport = true },
                                modifier = Modifier.fillMaxWidth(),
                            ) {
                                Text(
                                    text = stringResource(Res.string.community_gdpr_export),
                                    style = typography.sm,
                                    color = tokens.primary,
                                )
                            }
                            TextButton(
                                onClick = { pendingErase = true },
                                modifier = Modifier.fillMaxWidth(),
                            ) {
                                Text(
                                    text = stringResource(Res.string.community_gdpr_erase),
                                    style = typography.sm,
                                    color = tokens.destructive,
                                )
                            }
                        }
                    }
                }

                Spacer(modifier = Modifier.height(spacing.s2))
                Separator()
                Spacer(modifier = Modifier.height(spacing.s2))
                ViewerDataSection(
                    data = viewerData,
                    write = dataWrite,
                    saveError = dataError,
                    onSet = onSetDatum,
                    onDelete = onDeleteDatum,
                )
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.community_stats_close), color = tokens.primary)
            }
        },
    )

    if (pendingExport) {
        ConfirmDialog(
            title = stringResource(Res.string.community_gdpr_export),
            message = stringResource(Res.string.community_gdpr_export_desc, name),
            confirmLabel = stringResource(Res.string.community_gdpr_export_confirm),
            dismissLabel = stringResource(Res.string.community_stats_close),
            destructive = false,
            onConfirm = {
                pendingExport = false
                onExport()
            },
            onDismiss = { pendingExport = false },
        )
    }

    if (pendingErase) {
        ConfirmDialog(
            title = stringResource(Res.string.community_gdpr_erase),
            message = stringResource(Res.string.community_gdpr_erase_desc, name),
            confirmLabel = stringResource(Res.string.community_gdpr_erase_confirm),
            dismissLabel = stringResource(Res.string.community_stats_close),
            destructive = true,
            onConfirm = {
                pendingErase = false
                onErase()
            },
            onDismiss = { pendingErase = false },
        )
    }
}

@Composable
private fun StatRow(label: String, value: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.SpaceBetween,
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground, modifier = Modifier.padding(end = spacing.s2))
        Text(text = value, style = typography.sm, color = tokens.foreground)
    }
}

// The viewer's custom key/value data (per-viewer-data.md) — the map pipelines write (death counters, quest
// flags, "favorite game"). Read is shown to anyone who can open the dialog; add/delete are gated at [write]
// (Editor) and disabled-with-reason below it. Values over the backend cap are rejected (not truncated) — the
// backend's message surfaces in [saveError]. Delete confirms first (destructive). [data] is null until loaded.
@Composable
private fun ViewerDataSection(
    data: Map<String, String>?,
    write: ManageDecision,
    saveError: String?,
    onSet: (key: String, value: String) -> Unit,
    onDelete: (key: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var newKey: String by remember { mutableStateOf("") }
    var newValue: String by remember { mutableStateOf("") }
    var keyError: Boolean by remember { mutableStateOf(false) }
    var pendingDelete: String? by remember { mutableStateOf(null) }

    Text(
        text = stringResource(Res.string.community_data_section),
        style = typography.xs,
        color = tokens.mutedForeground,
    )

    val entries: List<Map.Entry<String, String>> =
        (data ?: emptyMap()).entries.sortedBy { it.key }

    if (entries.isEmpty()) {
        Text(
            text = stringResource(Res.string.community_data_empty),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
    } else {
        entries.forEach { entry ->
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Column(modifier = Modifier.weight(1f)) {
                    Text(
                        text = entry.key,
                        style = typography.sm,
                        color = tokens.foreground,
                        maxLines = 1,
                        overflow = TextOverflow.Ellipsis,
                    )
                    Text(
                        text = entry.value,
                        style = typography.xs,
                        color = tokens.mutedForeground,
                        maxLines = 2,
                        overflow = TextOverflow.Ellipsis,
                    )
                }
                if (write.isAllowed) {
                    GlyphButton(
                        imageVector = TrashGlyph,
                        label = stringResource(Res.string.community_data_delete, entry.key),
                        onClick = { pendingDelete = entry.key },
                        tint = tokens.destructive,
                    )
                }
            }
        }
    }

    if (write.isAllowed) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.Top,
        ) {
            AppTextField(
                value = newKey,
                onValueChange = { newKey = it; keyError = false },
                label = stringResource(Res.string.community_data_key),
                isError = keyError,
                errorText = if (keyError) stringResource(Res.string.community_data_key_required) else null,
                modifier = Modifier.weight(1f),
            )
            AppTextField(
                value = newValue,
                onValueChange = { newValue = it },
                label = stringResource(Res.string.community_data_value),
                modifier = Modifier.weight(1f),
            )
        }
        Button(
            onClick = {
                val key: String = newKey.trim().lowercase()
                if (key.isEmpty()) {
                    keyError = true
                    return@Button
                }
                onSet(key, newValue)
                newKey = ""
                newValue = ""
            },
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(stringResource(Res.string.community_data_add))
        }
    } else {
        write.deniedReason?.let { reason ->
            Text(text = reason, style = typography.xs, color = tokens.mutedForeground)
        }
    }

    saveError?.let { detail ->
        Text(text = detail, style = typography.xs, color = tokens.destructive)
    }

    pendingDelete?.let { key ->
        ConfirmDialog(
            title = stringResource(Res.string.community_data_delete_title),
            message = stringResource(Res.string.community_data_delete_message, key),
            confirmLabel = stringResource(Res.string.community_data_delete_confirm),
            dismissLabel = stringResource(Res.string.community_stats_close),
            destructive = true,
            onConfirm = {
                pendingDelete = null
                onDelete(key)
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

/** Format a [Double] to one decimal place without JVM-only String.format. */
private fun Double.toFixed1(): String {
    val scaled: Long = (this * 10).toLong()
    return "${scaled / 10}.${kotlin.math.abs(scaled % 10)}"
}

/** The member's best display name: display name, then login, then the raw id. */
private fun memberName(member: CommunityMember): String =
    member.displayName.takeIf { it.isNotBlank() }
        ?: member.username.takeIf { it.isNotBlank() }
        ?: member.id

/** Map a backend `trustLevel` to its localized badge label, falling back to the viewer label. */
private fun trustLabel(trustLevel: String): StringResource =
    when (trustLevel.lowercase()) {
        CommunityTrustLevel.Moderator -> Res.string.community_trust_moderator
        CommunityTrustLevel.Vip -> Res.string.community_trust_vip
        CommunityTrustLevel.Subscriber -> Res.string.community_trust_subscriber
        else -> Res.string.community_trust_viewer
    }
