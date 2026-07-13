// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.Text
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.TabsList
import bot.nomnomz.dashboard.core.designsystem.component.TabsTrigger
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.network.AutomodConfig
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.UnbanRequest
import bot.nomnomz.dashboard.core.network.ViewerReport
import bot.nomnomz.dashboard.core.network.ModerationActionLog
import bot.nomnomz.dashboard.core.network.ModerationRule
import bot.nomnomz.dashboard.core.network.ModerationStats
import bot.nomnomz.dashboard.core.network.UserModerationContext
import bot.nomnomz.dashboard.feature.moderation.state.AutomodFilter
import bot.nomnomz.dashboard.feature.moderation.state.ModerationController
import bot.nomnomz.dashboard.feature.moderation.state.ModerationState
import bot.nomnomz.dashboard.feature.moderation.state.UserContextState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_action_error
import nomnomzbot.composeapp.generated.resources.moderation_stats_automod
import nomnomzbot.composeapp.generated.resources.moderation_stats_bans_today
import nomnomzbot.composeapp.generated.resources.moderation_stats_deleted
import nomnomzbot.composeapp.generated.resources.moderation_stats_timeouts
import nomnomzbot.composeapp.generated.resources.moderation_automod_caps
import nomnomzbot.composeapp.generated.resources.moderation_automod_caps_detail
import nomnomzbot.composeapp.generated.resources.moderation_automod_disable
import nomnomzbot.composeapp.generated.resources.moderation_automod_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_automod_enable
import nomnomzbot.composeapp.generated.resources.moderation_automod_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_automod_emote_detail
import nomnomzbot.composeapp.generated.resources.moderation_automod_emotes
import nomnomzbot.composeapp.generated.resources.moderation_automod_link
import nomnomzbot.composeapp.generated.resources.moderation_automod_off
import nomnomzbot.composeapp.generated.resources.moderation_automod_on
import nomnomzbot.composeapp.generated.resources.moderation_automod_phrases
import nomnomzbot.composeapp.generated.resources.moderation_automod_title
import nomnomzbot.composeapp.generated.resources.moderation_bans_title
import nomnomzbot.composeapp.generated.resources.moderation_log_by
import nomnomzbot.composeapp.generated.resources.moderation_log_row_description
import nomnomzbot.composeapp.generated.resources.moderation_log_title
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_title
import nomnomzbot.composeapp.generated.resources.moderation_terms_add
import nomnomzbot.composeapp.generated.resources.moderation_terms_add_label
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove_action
import nomnomzbot.composeapp.generated.resources.moderation_terms_title
import nomnomzbot.composeapp.generated.resources.moderation_banned_by
import nomnomzbot.composeapp.generated.resources.moderation_banned_on
import nomnomzbot.composeapp.generated.resources.moderation_context_action_short
import nomnomzbot.composeapp.generated.resources.moderation_context_bans
import nomnomzbot.composeapp.generated.resources.moderation_actions_title
import nomnomzbot.composeapp.generated.resources.moderation_context_close
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_clear
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_label
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_monitor
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_restrict
import nomnomzbot.composeapp.generated.resources.moderation_suspicious_restrict_confirm
import nomnomzbot.composeapp.generated.resources.moderation_warn_action
import nomnomzbot.composeapp.generated.resources.moderation_warn_reason
import nomnomzbot.composeapp.generated.resources.moderation_context_disclaimer
import nomnomzbot.composeapp.generated.resources.moderation_context_error
import nomnomzbot.composeapp.generated.resources.moderation_context_last
import nomnomzbot.composeapp.generated.resources.moderation_context_loading
import nomnomzbot.composeapp.generated.resources.moderation_context_recent
import nomnomzbot.composeapp.generated.resources.moderation_context_recent_empty
import nomnomzbot.composeapp.generated.resources.moderation_context_timeouts
import nomnomzbot.composeapp.generated.resources.moderation_context_title
import nomnomzbot.composeapp.generated.resources.moderation_context_unbans
import nomnomzbot.composeapp.generated.resources.moderation_context_view
import nomnomzbot.composeapp.generated.resources.moderation_context_warns
import nomnomzbot.composeapp.generated.resources.moderation_empty
import nomnomzbot.composeapp.generated.resources.moderation_error
import nomnomzbot.composeapp.generated.resources.moderation_loading
import nomnomzbot.composeapp.generated.resources.moderation_no_reason
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_confirm
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_message
import nomnomzbot.composeapp.generated.resources.moderation_rules_delete_title
import nomnomzbot.composeapp.generated.resources.moderation_rules_disable
import nomnomzbot.composeapp.generated.resources.moderation_rules_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_enable
import nomnomzbot.composeapp.generated.resources.moderation_rules_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_add
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_action
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_confirm
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_name
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_name_required
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_title
import nomnomzbot.composeapp.generated.resources.moderation_rules_create_type
import nomnomzbot.composeapp.generated.resources.moderation_rules_title
import nomnomzbot.composeapp.generated.resources.moderation_action_apply
import nomnomzbot.composeapp.generated.resources.moderation_action_confirm
import nomnomzbot.composeapp.generated.resources.moderation_action_dialog_title
import nomnomzbot.composeapp.generated.resources.moderation_action_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_action_duration
import nomnomzbot.composeapp.generated.resources.moderation_action_reason
import nomnomzbot.composeapp.generated.resources.moderation_action_type_ban
import nomnomzbot.composeapp.generated.resources.moderation_action_type_delete
import nomnomzbot.composeapp.generated.resources.moderation_action_type_timeout
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_caps
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_emotes
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_links
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_profanity
import nomnomzbot.composeapp.generated.resources.moderation_rule_type_spam
import nomnomzbot.composeapp.generated.resources.moderation_action_user_id
import nomnomzbot.composeapp.generated.resources.moderation_action_user_id_required
import nomnomzbot.composeapp.generated.resources.moderation_announce_action
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_blue
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_green
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_label
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_orange
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_primary
import nomnomzbot.composeapp.generated.resources.moderation_announce_color_purple
import nomnomzbot.composeapp.generated.resources.moderation_announce_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_announce_message_label
import nomnomzbot.composeapp.generated.resources.moderation_announce_message_required
import nomnomzbot.composeapp.generated.resources.moderation_announce_send
import nomnomzbot.composeapp.generated.resources.moderation_announce_title
import nomnomzbot.composeapp.generated.resources.moderation_reports_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_reports_escalate
import nomnomzbot.composeapp.generated.resources.moderation_reports_reported_by
import nomnomzbot.composeapp.generated.resources.moderation_reports_title
import nomnomzbot.composeapp.generated.resources.moderation_retry
import nomnomzbot.composeapp.generated.resources.moderation_unban_action
import nomnomzbot.composeapp.generated.resources.moderation_unban_action_short
import nomnomzbot.composeapp.generated.resources.moderation_unban_confirm
import nomnomzbot.composeapp.generated.resources.moderation_unban_approve
import nomnomzbot.composeapp.generated.resources.moderation_unban_deny
import nomnomzbot.composeapp.generated.resources.moderation_unban_deny_message
import nomnomzbot.composeapp.generated.resources.moderation_unban_deny_title
import nomnomzbot.composeapp.generated.resources.moderation_unban_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_confirm
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_message
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_short
import nomnomzbot.composeapp.generated.resources.moderation_unban_everywhere_title
import nomnomzbot.composeapp.generated.resources.moderation_unban_requests_title
import nomnomzbot.composeapp.generated.resources.moderation_unban_message
import nomnomzbot.composeapp.generated.resources.moderation_unban_title
import nomnomzbot.composeapp.generated.resources.shell_nav_moderation
import bot.nomnomz.dashboard.core.realtime.HubEvent
import kotlinx.coroutines.flow.SharedFlow
import org.jetbrains.compose.resources.stringResource

// The Moderation page: the channel's currently-banned viewers, all real data from [ModerationController].
// The screen is a pure projection of the controller's state; it loads on first composition and offers a
// retry on failure. Each ban is actionable — an Unban affordance that, only once the moderator confirms it
// in the shared ConfirmDialog, lifts the ban via the controller (which reloads the list on success).
@Composable
fun ModerationScreen(
    controller: ModerationController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
) {
    val state: ModerationState by controller.state.collectAsStateWithLifecycle()
    val userContext: UserContextState? by controller.userContext.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Moderation gates its write affordance at its single Moderator manage
    // floor (frontend-ia.md §3). A caller below it sees the ban list but the Unban control is disabled with
    // "Requires Moderator" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Moderation)
    // Marking a viewer suspicious (monitor / restrict) is a Lead-Moderator (SuperMod) action — a higher floor
    // than the page's Moderator gate (warn / bans). The backend re-checks regardless.
    val suspiciousManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.SuperMod)

    LaunchedEffect(Unit) { controller.load() }
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: ModerationState = state) {
            is ModerationState.Loading ->
                CenteredMessage(stringResource(Res.string.moderation_loading))
            is ModerationState.Empty ->
                CenteredMessage(stringResource(Res.string.moderation_empty))
            is ModerationState.Error ->
                ErrorContent(
                    detail = current.detail,
                    onRetry = { scope.launch { controller.load() } },
                )
            is ModerationState.Ready ->
                BansList(
                    bans = current.bans,
                    modLog = current.modLog,
                    shieldEnabled = current.shieldEnabled,
                    blockedTerms = current.blockedTerms,
                    automod = current.automod,
                    rules = current.rules,
                    stats = current.stats,
                    actionError = current.actionError,
                    unbanRequests = current.unbanRequests,
                    reports = current.reports,
                    manage = manage,
                    suspiciousManage = suspiciousManage,
                    onResolveUnban = { requestId, approve, note ->
                        scope.launch { controller.resolveUnbanRequest(requestId, approve, note) }
                    },
                    onResolveReport = { reportId, action ->
                        scope.launch { controller.resolveReport(reportId, action) }
                    },
                    onUnban = { userId -> scope.launch { controller.unban(userId) } },
                    onNetworkUnban = { userId ->
                        scope.launch { controller.networkUnban(userId, "all_moderated") }
                    },
                    onViewContext = { userId -> scope.launch { controller.openUserContext(userId) } },
                    onPerformAction = { action, userId, duration, reason ->
                        scope.launch { controller.performAction(action, userId, duration, reason) }
                    },
                    onToggleShield = { on -> scope.launch { controller.setShieldMode(on) } },
                    onAddTerm = { term -> scope.launch { controller.addBlockedTerm(term) } },
                    onRemoveTerm = { term -> scope.launch { controller.removeBlockedTerm(term) } },
                    onToggleFilter = { f -> scope.launch { controller.toggleAutomodFilter(f) } },
                    onToggleRule = { id, on -> scope.launch { controller.toggleRule(id, on) } },
                    onDeleteRule = { id -> scope.launch { controller.deleteRule(id) } },
                    onCreateRule = { name, type, action, duration, reason ->
                        scope.launch { controller.createRule(name, type, action, duration, reason) }
                    },
                    onSendAnnouncement = { msg, color ->
                        scope.launch { controller.sendAnnouncement(msg, color) }
                    },
                )
        }
    }

    userContext?.let { ctx ->
        UserModerationContextDialog(
            state = ctx,
            manage = manage,
            suspiciousManage = suspiciousManage,
            onWarn = { userId, reason -> scope.launch { controller.warn(userId, reason) } },
            onSuspicious = { userId, status -> scope.launch { controller.setSuspicious(userId, status) } },
            onClearSuspicious = { userId -> scope.launch { controller.clearSuspicious(userId) } },
            onDismiss = { controller.closeUserContext() },
        )
    }
}

@Composable
private fun BansList(
    bans: List<BannedUser>,
    modLog: List<ModLogEntry>,
    shieldEnabled: Boolean,
    blockedTerms: List<String>,
    automod: AutomodConfig,
    rules: List<ModerationRule>,
    stats: ModerationStats,
    actionError: String?,
    unbanRequests: List<UnbanRequest>,
    reports: List<ViewerReport>,
    manage: ManageDecision,
    suspiciousManage: ManageDecision,
    onResolveUnban: (requestId: String, approve: Boolean, note: String?) -> Unit,
    onResolveReport: (reportId: String, action: String) -> Unit,
    onUnban: (userId: String) -> Unit,
    onNetworkUnban: (userId: String) -> Unit,
    onViewContext: (userId: String) -> Unit,
    onPerformAction: (action: String, targetUserId: String, durationSeconds: Int?, reason: String?) -> Unit,
    onToggleShield: (Boolean) -> Unit,
    onAddTerm: (String) -> Unit,
    onRemoveTerm: (String) -> Unit,
    onToggleFilter: (AutomodFilter) -> Unit,
    onToggleRule: (Int, Boolean) -> Unit,
    onDeleteRule: (Int) -> Unit,
    onCreateRule: (name: String, type: String, action: String, durationSeconds: Int?, reason: String?) -> Unit,
    onSendAnnouncement: (message: String, color: String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The ban awaiting confirmation, if any — the screen owns the dialog's open/closed state.
    var pendingUnban: BannedUser? by remember { mutableStateOf(null) }
    // The unban-request appeal awaiting a deny confirmation, if any.
    var pendingDeny: UnbanRequest? by remember { mutableStateOf(null) }
    // The banned viewer awaiting an "unban everywhere" (all-moderated) confirmation, if any.
    var pendingNetworkUnban: BannedUser? by remember { mutableStateOf(null) }
    // The filter rule awaiting delete confirmation, if any.
    var pendingDeleteRule: ModerationRule? by remember { mutableStateOf(null) }
    // Whether the "moderate a viewer" action dialog is open.
    var showActionDialog: Boolean by remember { mutableStateOf(false) }
    // Whether the "add filter rule" dialog is open.
    var showCreateRuleDialog: Boolean by remember { mutableStateOf(false) }
    // Whether the "send announcement" dialog is open.
    var showAnnounceDialog: Boolean by remember { mutableStateOf(false) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        item(key = "page-header") {
            PageHeader(title = stringResource(Res.string.shell_nav_moderation))
        }
        actionError?.let { detail ->
            item(key = "unban-error") {
                ActionErrorBanner(message = stringResource(Res.string.moderation_action_error, detail))
            }
        }
        item(key = "stats") {
            Card(modifier = Modifier.fillMaxWidth()) {
                ModerationStatsBanner(stats = stats)
            }
        }
        item(key = "shield-toggle") {
            Card(modifier = Modifier.fillMaxWidth()) {
                ShieldToggle(enabled = shieldEnabled, manage = manage, onToggle = onToggleShield)
            }
        }
        // "Moderate a viewer" and "Send Announcement" quick-action buttons.
        item(key = "action-button") {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ManageGate(decision = manage) { enabled ->
                    TextButton(
                        onClick = { showActionDialog = true },
                        enabled = enabled,
                    ) {
                        Text(
                            text = stringResource(Res.string.moderation_action_apply),
                            color = if (enabled) tokens.primary else tokens.mutedForeground,
                        )
                    }
                }
                ManageGate(decision = manage) { enabled ->
                    TextButton(
                        onClick = { showAnnounceDialog = true },
                        enabled = enabled,
                    ) {
                        Text(
                            text = stringResource(Res.string.moderation_announce_action),
                            color = if (enabled) tokens.primary else tokens.mutedForeground,
                        )
                    }
                }
            }
        }
        if (bans.isNotEmpty()) {
            item(key = "bans-header") {
                Text(
                    text = stringResource(Res.string.moderation_bans_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "bans-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        bans.forEachIndexed { index, ban ->
                            BanRow(
                                ban = ban,
                                manage = manage,
                                onUnban = { pendingUnban = ban },
                                onNetworkUnban = { pendingNetworkUnban = ban },
                                onViewContext = { onViewContext(ban.id) },
                            )
                            if (index < bans.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        if (unbanRequests.isNotEmpty()) {
            item(key = "unban-header") {
                Text(
                    text = stringResource(Res.string.moderation_unban_requests_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "unban-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        unbanRequests.forEachIndexed { index, request ->
                            UnbanRequestRow(
                                request = request,
                                manage = suspiciousManage,
                                onApprove = { onResolveUnban(request.id, true, null) },
                                onDeny = { pendingDeny = request },
                            )
                            if (index < unbanRequests.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        if (reports.isNotEmpty()) {
            item(key = "reports-header") {
                Text(
                    text = stringResource(Res.string.moderation_reports_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "reports-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        reports.forEachIndexed { index, report ->
                            ViewerReportRow(
                                report = report,
                                manage = suspiciousManage,
                                onDismiss = { onResolveReport(report.id, "dismiss") },
                                onEscalate = { onResolveReport(report.id, "escalate") },
                            )
                            if (index < reports.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        if (modLog.isNotEmpty()) {
            item(key = "log-header") {
                Text(
                    text = stringResource(Res.string.moderation_log_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            item(key = "log-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        modLog.forEachIndexed { index, entry ->
                            ModLogRow(entry = entry)
                            if (index < modLog.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        // Always shown in Ready so the add input is reachable even with no terms yet.
        item(key = "terms-header") {
            Text(
                text = stringResource(Res.string.moderation_terms_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        item(key = "terms-add") { AddTermRow(manage = manage, onAdd = onAddTerm) }
        if (blockedTerms.isNotEmpty()) {
            item(key = "terms-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        blockedTerms.forEachIndexed { index, term ->
                            BlockedTermRow(term = term, manage = manage, onRemove = { onRemoveTerm(term) })
                            if (index < blockedTerms.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
        item(key = "automod-header") {
            Text(
                text = stringResource(Res.string.moderation_automod_title),
                style = typography.lg,
                color = tokens.cardForeground,
                maxLines = 1,
            )
        }
        item(key = "automod-card") {
            Card(modifier = Modifier.fillMaxWidth()) {
                Column {
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_link),
                        enabled = automod.linkFilter.enabled,
                        detail = null,
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Link) },
                    )
                    Separator()
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_caps),
                        enabled = automod.capsFilter.enabled,
                        detail =
                            stringResource(
                                Res.string.moderation_automod_caps_detail,
                                automod.capsFilter.threshold,
                            ),
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Caps) },
                    )
                    Separator()
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_phrases),
                        enabled = automod.bannedPhrases.enabled,
                        detail = null,
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Phrases) },
                    )
                    Separator()
                    AutomodRow(
                        name = stringResource(Res.string.moderation_automod_emotes),
                        enabled = automod.emoteSpam.enabled,
                        detail =
                            stringResource(
                                Res.string.moderation_automod_emote_detail,
                                automod.emoteSpam.maxEmotes,
                            ),
                        manage = manage,
                        onToggle = { onToggleFilter(AutomodFilter.Emotes) },
                    )
                }
            }
        }
        item(key = "rules-header") {
            Row(
                modifier = Modifier.fillMaxWidth(),
                horizontalArrangement = Arrangement.SpaceBetween,
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_rules_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
                ManageGate(manage) {
                    TextButton(onClick = { showCreateRuleDialog = true }) {
                        Text(
                            text = stringResource(Res.string.moderation_rules_add),
                            style = typography.sm,
                            color = tokens.primary,
                        )
                    }
                }
            }
        }
        if (rules.isNotEmpty()) {
            item(key = "rules-card") {
                Card(modifier = Modifier.fillMaxWidth()) {
                    Column {
                        rules.forEachIndexed { index, rule ->
                            RuleRow(
                                rule = rule,
                                manage = manage,
                                onToggle = { onToggleRule(rule.id, !rule.isEnabled) },
                                onDelete = { pendingDeleteRule = rule },
                            )
                            if (index < rules.lastIndex) {
                                Separator()
                            }
                        }
                    }
                }
            }
        }
    }

    pendingUnban?.let { ban ->
        val name: String = ban.displayName.takeIf { it.isNotBlank() } ?: ban.username
        ConfirmDialog(
            title = stringResource(Res.string.moderation_unban_title),
            message = stringResource(Res.string.moderation_unban_message, name),
            confirmLabel = stringResource(Res.string.moderation_unban_confirm),
            dismissLabel = stringResource(Res.string.moderation_unban_dismiss),
            destructive = true,
            onConfirm = {
                onUnban(ban.id)
                pendingUnban = null
            },
            onDismiss = { pendingUnban = null },
        )
    }

    pendingDeny?.let { request ->
        val name: String = request.userName.takeIf { it.isNotBlank() } ?: request.userLogin
        ConfirmDialog(
            title = stringResource(Res.string.moderation_unban_deny_title),
            message = stringResource(Res.string.moderation_unban_deny_message, name),
            confirmLabel = stringResource(Res.string.moderation_unban_deny),
            dismissLabel = stringResource(Res.string.moderation_unban_dismiss),
            destructive = true,
            onConfirm = {
                onResolveUnban(request.id, false, null)
                pendingDeny = null
            },
            onDismiss = { pendingDeny = null },
        )
    }

    pendingNetworkUnban?.let { ban ->
        val name: String = ban.displayName.takeIf { it.isNotBlank() } ?: ban.username
        ConfirmDialog(
            title = stringResource(Res.string.moderation_unban_everywhere_title),
            message = stringResource(Res.string.moderation_unban_everywhere_message, name),
            confirmLabel = stringResource(Res.string.moderation_unban_everywhere_confirm),
            dismissLabel = stringResource(Res.string.moderation_unban_dismiss),
            destructive = true,
            onConfirm = {
                onNetworkUnban(ban.id)
                pendingNetworkUnban = null
            },
            onDismiss = { pendingNetworkUnban = null },
        )
    }

    pendingDeleteRule?.let { rule ->
        ConfirmDialog(
            title = stringResource(Res.string.moderation_rules_delete_title),
            message = stringResource(Res.string.moderation_rules_delete_message, rule.name),
            confirmLabel = stringResource(Res.string.moderation_rules_delete_confirm),
            dismissLabel = stringResource(Res.string.moderation_rules_delete_dismiss),
            destructive = true,
            onConfirm = {
                onDeleteRule(rule.id)
                pendingDeleteRule = null
            },
            onDismiss = { pendingDeleteRule = null },
        )
    }

    if (showActionDialog) {
        ModerateViewerDialog(
            onConfirm = { action, userId, duration, reason ->
                onPerformAction(action, userId, duration, reason)
                showActionDialog = false
            },
            onDismiss = { showActionDialog = false },
        )
    }

    if (showCreateRuleDialog) {
        CreateRuleDialog(
            onConfirm = { name, type, action, duration, reason ->
                onCreateRule(name, type, action, duration, reason)
                showCreateRuleDialog = false
            },
            onDismiss = { showCreateRuleDialog = false },
        )
    }

    if (showAnnounceDialog) {
        AnnounceDialog(
            onConfirm = { msg, color ->
                onSendAnnouncement(msg, color)
                showAnnounceDialog = false
            },
            onDismiss = { showAnnounceDialog = false },
        )
    }
}

// Dialog to apply a ban or timeout to a viewer identified by their Twitch user ID.
// The moderator selects the action type (ban vs timeout), optionally enters a reason and, for timeouts,
// a duration in seconds (default 600 = 10 minutes). The caller owns open/closed state.
@Composable
private fun ModerateViewerDialog(
    onConfirm: (action: String, targetUserId: String, durationSeconds: Int?, reason: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var userId: String by remember { mutableStateOf("") }
    var reason: String by remember { mutableStateOf("") }
    var durationText: String by remember { mutableStateOf("600") }
    var isBan: Boolean by remember { mutableStateOf(true) }
    var showUserIdError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_action_dialog_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = userId,
                    onValueChange = { userId = it; showUserIdError = false },
                    label = stringResource(Res.string.moderation_action_user_id),
                    isError = showUserIdError,
                    errorText =
                        if (showUserIdError) {
                            stringResource(Res.string.moderation_action_user_id_required)
                        } else {
                            null
                        },
                    modifier = Modifier.fillMaxWidth(),
                )
                TabsList {
                    TabsTrigger(
                        selected = isBan,
                        onClick = { isBan = true },
                    ) {
                        Text(stringResource(Res.string.moderation_action_type_ban))
                    }
                    TabsTrigger(
                        selected = !isBan,
                        onClick = { isBan = false },
                    ) {
                        Text(stringResource(Res.string.moderation_action_type_timeout))
                    }
                }
                if (!isBan) {
                    AppTextField(
                        value = durationText,
                        onValueChange = { durationText = it },
                        label = stringResource(Res.string.moderation_action_duration),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
                AppTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.moderation_action_reason),
                    modifier = Modifier.fillMaxWidth(),
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (userId.isBlank()) {
                        showUserIdError = true
                        return@Button
                    }
                    val action: String = if (isBan) "ban" else "timeout"
                    val duration: Int? =
                        if (!isBan) durationText.trim().toIntOrNull()?.takeIf { it > 0 } else null
                    val reasonOrNull: String? = reason.trim().takeIf { it.isNotEmpty() }
                    onConfirm(action, userId.trim(), duration, reasonOrNull)
                },
            ) {
                Text(stringResource(Res.string.moderation_action_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_action_dismiss))
            }
        },
    )
}

@Composable
private fun BanRow(
    ban: BannedUser,
    manage: ManageDecision,
    onUnban: () -> Unit,
    onNetworkUnban: () -> Unit,
    onViewContext: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = ban.displayName.takeIf { it.isNotBlank() } ?: ban.username
    val reason: String =
        ban.reason.takeIf { it.isNotBlank() } ?: stringResource(Res.string.moderation_no_reason)
    val bannedOn: String? = ban.bannedAt.takeIf { it.isNotBlank() }?.let { datePart(it) }
    val unbanLabel: String = stringResource(Res.string.moderation_unban_action, name)
    val viewHistoryLabel: String = stringResource(Res.string.moderation_context_view, name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(spacing.s4),
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                // One node for the row's detail: name, reason, and when (the Unban button stays separate).
                .clearAndSetSemantics {
                    contentDescription =
                        if (bannedOn != null) "$name · $reason · $bannedOn" else "$name · $reason"
                },
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
                text = reason,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
            bannedOn?.let { on ->
                Text(
                    text = stringResource(Res.string.moderation_banned_on, on),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
            ban.bannedBy.takeIf { it.isNotBlank() }?.let { by ->
                Text(
                    text = stringResource(Res.string.moderation_banned_by, by),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
        }

        TextButton(
            onClick = onViewContext,
            modifier = Modifier.clearAndSetSemantics { contentDescription = viewHistoryLabel },
        ) {
            Text(
                text = stringResource(Res.string.moderation_context_action_short),
                color = tokens.primary,
                maxLines = 1,
            )
        }

        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onUnban,
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = unbanLabel },
            ) {
                Text(
                    text = stringResource(Res.string.moderation_unban_action_short),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(onClick = onNetworkUnban, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_unban_everywhere_short),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// One pending unban-request appeal: the viewer + their appeal text, with Approve (lifts the ban) / Deny buttons
// gated at the Lead-Moderator (SuperMod) floor. Deny confirms first (the ban stays); approve acts immediately.
@Composable
private fun UnbanRequestRow(
    request: UnbanRequest,
    manage: ManageDecision,
    onApprove: () -> Unit,
    onDeny: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = request.userName.takeIf { it.isNotBlank() } ?: request.userLogin

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = name,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (request.text.isNotBlank()) {
            Text(
                text = request.text,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onApprove, enabled = enabled) {
                    Text(stringResource(Res.string.moderation_unban_approve))
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onDeny, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.moderation_unban_deny),
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// One open viewer report awaiting triage — a viewer flagged another chatter. A Lead-Moderator (SuperMod) action.
// Dismiss closes it with no action; Escalate flags it for a moderator to punish separately (it does NOT auto-punish
// — the mod then acts via the existing ban/timeout tools). The reporter name is shown when the backend supplies it.
@Composable
private fun ViewerReportRow(
    report: ViewerReport,
    manage: ManageDecision,
    onDismiss: () -> Unit,
    onEscalate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = report.reportedUsername.takeIf { it.isNotBlank() } ?: report.reportedTwitchUserId,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        if (report.reason.isNotBlank()) {
            Text(
                text = report.reason,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 4,
                overflow = TextOverflow.Ellipsis,
            )
        }
        report.reporterName?.takeIf { it.isNotBlank() }?.let { reporter ->
            Text(
                text = stringResource(Res.string.moderation_reports_reported_by, reporter),
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onEscalate, enabled = enabled) {
                    Text(stringResource(Res.string.moderation_reports_escalate))
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onDismiss, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.moderation_reports_dismiss),
                        color = tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// The per-user moderation panel (handoff item: mod-panel read side). A dialog opened from a banned-user row that
// shows the viewer's rap sheet — the bot's OWN recorded ban/timeout/warn/unban history, explicitly NOT the full
// Twitch record (the disclaimer says so). Read-only; the mod then acts via the existing ban/timeout/unban tools.
@Composable
private fun UserModerationContextDialog(
    state: UserContextState,
    manage: ManageDecision,
    suspiciousManage: ManageDecision,
    onWarn: (userId: String, reason: String) -> Unit,
    onSuspicious: (userId: String, status: String) -> Unit,
    onClearSuspicious: (userId: String) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_context_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                Text(
                    text = stringResource(Res.string.moderation_context_disclaimer),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                when (state) {
                    is UserContextState.Loading ->
                        Text(
                            text = stringResource(Res.string.moderation_context_loading),
                            style = typography.sm,
                            color = tokens.mutedForeground,
                        )
                    is UserContextState.Error ->
                        Text(
                            text = stringResource(Res.string.moderation_context_error, state.detail),
                            style = typography.sm,
                            color = tokens.destructive,
                        )
                    is UserContextState.Ready -> {
                        UserModerationContextBody(state.context)
                        UserModerationActions(
                            userId = state.context.userId,
                            manage = manage,
                            suspiciousManage = suspiciousManage,
                            onWarn = onWarn,
                            onSuspicious = onSuspicious,
                            onClearSuspicious = onClearSuspicious,
                        )
                    }
                }
            }
        },
        confirmButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_context_close))
            }
        },
    )
}

// The write side of the per-user mod panel (gated at the page's Moderator floor): issue a Twitch warning (needs
// a reason) and set/clear a suspicious flag (monitor = watch, restrict = hold their messages — confirmed). Each
// acts immediately and the controller reloads the rap sheet above. Below the manage floor the controls disable
// with the standard reason.
@Composable
private fun UserModerationActions(
    userId: String,
    manage: ManageDecision,
    suspiciousManage: ManageDecision,
    onWarn: (userId: String, reason: String) -> Unit,
    onSuspicious: (userId: String, status: String) -> Unit,
    onClearSuspicious: (userId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var reason: String by remember(userId) { mutableStateOf("") }
    var confirmRestrict: Boolean by remember(userId) { mutableStateOf(false) }

    Separator()
    Text(
        text = stringResource(Res.string.moderation_actions_title),
        style = typography.sm,
        color = tokens.mutedForeground,
    )

    AppTextField(
        value = reason,
        onValueChange = { reason = it },
        label = stringResource(Res.string.moderation_warn_reason),
        modifier = Modifier.fillMaxWidth(),
    )
    ManageGate(decision = manage) { enabled ->
        Button(
            onClick = {
                onWarn(userId, reason.trim())
                reason = ""
            },
            enabled = enabled && reason.isNotBlank(),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Text(stringResource(Res.string.moderation_warn_action))
        }
    }

    Text(
        text = stringResource(Res.string.moderation_suspicious_label),
        style = typography.xs,
        color = tokens.mutedForeground,
    )
    Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { onSuspicious(userId, "active_monitoring") }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_suspicious_monitor),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { confirmRestrict = true }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_suspicious_restrict),
                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                )
            }
        }
        ManageGate(decision = suspiciousManage) { enabled ->
            TextButton(onClick = { onClearSuspicious(userId) }, enabled = enabled) {
                Text(
                    text = stringResource(Res.string.moderation_suspicious_clear),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                )
            }
        }
    }

    if (confirmRestrict) {
        ConfirmDialog(
            title = stringResource(Res.string.moderation_suspicious_restrict),
            message = stringResource(Res.string.moderation_suspicious_restrict_confirm),
            confirmLabel = stringResource(Res.string.moderation_suspicious_restrict),
            dismissLabel = stringResource(Res.string.moderation_context_close),
            destructive = true,
            onConfirm = {
                confirmRestrict = false
                onSuspicious(userId, "restricted")
            },
            onDismiss = { confirmRestrict = false },
        )
    }
}

// The loaded rap sheet: the viewer's counters + last action + the recent recorded actions.
@Composable
private fun UserModerationContextBody(context: UserModerationContext) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = context.username?.takeIf { it.isNotBlank() } ?: context.userId

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = name,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            StatChip(label = stringResource(Res.string.moderation_context_bans), value = context.banCount)
            StatChip(label = stringResource(Res.string.moderation_context_timeouts), value = context.timeoutCount)
            StatChip(label = stringResource(Res.string.moderation_context_warns), value = context.warnCount)
            StatChip(label = stringResource(Res.string.moderation_context_unbans), value = context.unbanCount)
        }
        context.lastActionType?.takeIf { it.isNotBlank() }?.let { last ->
            val on: String = context.lastActionAt?.takeIf { it.isNotBlank() }?.let { datePart(it) } ?: ""
            Text(
                text = stringResource(Res.string.moderation_context_last, last, on),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
        Text(
            text = stringResource(Res.string.moderation_context_recent),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        if (context.recentActions.isEmpty()) {
            Text(
                text = stringResource(Res.string.moderation_context_recent_empty),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        } else {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                context.recentActions.forEach { action -> UserContextActionRow(action) }
            }
        }
    }
}

// One recorded action: "<action> · <moderator> · <date>" with the reason beneath it when present.
@Composable
private fun UserContextActionRow(action: ModerationActionLog) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    val on: String = action.timestamp.takeIf { it.isNotBlank() }?.let { datePart(it) } ?: ""
    val by: String = action.moderatorUsername.takeIf { it.isNotBlank() } ?: action.moderatorId
    val head: String = if (on.isNotBlank()) "${action.action} · $by · $on" else "${action.action} · $by"

    Column {
        Text(
            text = head,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )
        action.reason?.takeIf { it.isNotBlank() }?.let { r ->
            Text(
                text = r,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
            )
        }
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
                text = stringResource(Res.string.moderation_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.moderation_retry)) }
        }
    }
}

// One filter-rule row: the rule name + its type, with Enable/Disable + Delete actions (Editor floor; the
// backend re-checks moderation:filter:write). Delete is confirmed via a dialog. The rule editor (the settings
// form) is a follow-up.
@Composable
private fun RuleRow(
    rule: ModerationRule,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onDelete: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val toggleLabel: String =
        stringResource(
            if (rule.isEnabled) Res.string.moderation_rules_disable_action
            else Res.string.moderation_rules_enable_action,
            rule.name,
        )
    val deleteLabel: String = stringResource(Res.string.moderation_rules_delete_action, rule.name)
    val rowDescription: String = "${rule.name}, ${rule.type}"

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
                text = rule.name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = rule.type,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onToggle,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (rule.isEnabled) Res.string.moderation_rules_disable
                            else Res.string.moderation_rules_enable
                        ),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

// One AutoMod filter row: the filter name + an On/Off status (with the caps % / emote max detail when enabled)
// plus an Enable/Disable action; the per-filter threshold / list editing is a follow-up.
@Composable
private fun AutomodRow(
    name: String,
    enabled: Boolean,
    detail: String?,
    manage: ManageDecision,
    onToggle: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusWord: String =
        stringResource(
            if (enabled) Res.string.moderation_automod_on else Res.string.moderation_automod_off
        )
    val rowDescription: String =
        if (enabled && detail != null) "$name, $statusWord, $detail" else "$name, $statusWord"
    val toggleLabel: String =
        stringResource(
            if (enabled) Res.string.moderation_automod_disable_action
            else Res.string.moderation_automod_enable_action,
            name,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        // The name + status + detail is one semantics node; the enable/disable button keeps its own.
        Row(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = name,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (enabled && detail != null) {
                Text(
                    text = detail,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    modifier = Modifier.wrapContentWidth(),
                )
            }
            Text(
                text = statusWord,
                style = typography.sm,
                color = if (enabled) tokens.primary else tokens.mutedForeground,
                maxLines = 1,
                modifier = Modifier.wrapContentWidth(),
            )
        }
        ManageGate(decision = manage) { canManage ->
            TextButton(
                onClick = onToggle,
                enabled = canManage,
                modifier = Modifier.semantics { contentDescription = toggleLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (enabled) Res.string.moderation_automod_disable
                            else Res.string.moderation_automod_enable
                        ),
                    color = if (canManage) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// The add-blocked-term input: the shared AppTextField + an Add button, both gated at the Editor manage floor.
// Add is enabled only for a non-blank term; on submit it fires onAdd with the trimmed term and clears the field.
@Composable
private fun AddTermRow(manage: ManageDecision, onAdd: (String) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    var term: String by remember { mutableStateOf("") }

    ManageGate(decision = manage) { enabled ->
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            AppTextField(
                value = term,
                onValueChange = { term = it },
                label = stringResource(Res.string.moderation_terms_add_label),
                enabled = enabled,
                modifier = Modifier.weight(1f),
            )
            val canSubmit: Boolean = enabled && term.isNotBlank()
            TextButton(
                onClick = {
                    val trimmed: String = term.trim()
                    if (trimmed.isNotEmpty()) {
                        onAdd(trimmed)
                        term = ""
                    }
                },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(Res.string.moderation_terms_add),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// One blocked-term row: the term + a Remove action (Editor floor; the backend re-checks moderation:blocklist).
// Removing a term un-blocks it, so the action reads in the neutral primary colour, not destructive.
@Composable
private fun BlockedTermRow(term: String, manage: ManageDecision, onRemove: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val removeLabel: String = stringResource(Res.string.moderation_terms_remove_action, term)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = term,
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onRemove,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = removeLabel },
            ) {
                Text(
                    text = stringResource(Res.string.moderation_terms_remove),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// The page-level emergency Shield Mode toggle: a prominent row that turns Twitch's lockdown on/off. The title
// reads destructive (red) when active. Editor floor (ManageGate); the backend re-checks moderation:shieldmode.
@Composable
private fun ModerationStatsBanner(stats: ModerationStats) {
    val spacing = LocalSpacing.current

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(spacing.s3),
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        StatChip(label = stringResource(Res.string.moderation_stats_bans_today), value = stats.bansToday)
        StatChip(label = stringResource(Res.string.moderation_stats_timeouts), value = stats.timeouts)
        StatChip(label = stringResource(Res.string.moderation_stats_deleted), value = stats.deletedMessages)
        StatChip(label = stringResource(Res.string.moderation_stats_automod), value = stats.automodActions)
    }
}

@Composable
private fun StatChip(label: String, value: Int) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Column(horizontalAlignment = Alignment.CenterHorizontally) {
        Text(text = value.toString(), style = typography.xl, color = tokens.primary)
        Text(text = label, style = typography.xs, color = tokens.mutedForeground)
    }
}

@Composable
private fun ShieldToggle(enabled: Boolean, manage: ManageDecision, onToggle: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val actionLabel: String =
        stringResource(
            if (enabled) Res.string.moderation_shield_disable_action
            else Res.string.moderation_shield_enable_action
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(Res.string.moderation_shield_title),
            style = typography.base,
            color = if (enabled) tokens.destructive else tokens.cardForeground,
            maxLines = 1,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { canManage ->
            TextButton(
                onClick = { onToggle(!enabled) },
                enabled = canManage,
                modifier = Modifier.semantics { contentDescription = actionLabel },
            ) {
                Text(
                    text =
                        stringResource(
                            if (enabled) Res.string.moderation_shield_disable
                            else Res.string.moderation_shield_enable
                        ),
                    color = if (canManage) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
    }
}

// One mod-log row: the action + target (e.g. "timeout Baduser") over "by <moderator>". Read-only history.
@Composable
private fun ModLogRow(entry: ModLogEntry) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val primary: String = "${entry.action} ${entry.target ?: ""}".trim()
    val byLabel: String = stringResource(Res.string.moderation_log_by, entry.moderator)
    val rowDescription: String =
        stringResource(
            Res.string.moderation_log_row_description,
            entry.action,
            entry.target ?: "",
            entry.moderator,
        )

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3)
            .clearAndSetSemantics { contentDescription = rowDescription },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier.weight(1f),
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = primary,
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = byLabel,
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
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

/** The date portion of an ISO-8601 timestamp (`2026-06-24T18:05:00Z` → `2026-06-24`); the whole value
 *  when it carries no time component. Avoids pulling a date library into this read-only slice. */
private fun datePart(timestamp: String): String = timestamp.substringBefore('T')

// Dialog to create a new filter rule. The moderator enters a name, picks a type and action, then fills the
// optional duration (for timeout action) and reason. The caller owns open/closed state.
@Composable
private fun CreateRuleDialog(
    onConfirm: (name: String, type: String, action: String, durationSeconds: Int?, reason: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val types: List<Pair<String, String>> = listOf(
        "profanity" to stringResource(Res.string.moderation_rule_type_profanity),
        "links" to stringResource(Res.string.moderation_rule_type_links),
        "caps" to stringResource(Res.string.moderation_rule_type_caps),
        "emotes" to stringResource(Res.string.moderation_rule_type_emotes),
        "spam" to stringResource(Res.string.moderation_rule_type_spam),
    )
    val actions: List<Pair<String, String>> = listOf(
        "delete" to stringResource(Res.string.moderation_action_type_delete),
        "timeout" to stringResource(Res.string.moderation_action_type_timeout),
        "ban" to stringResource(Res.string.moderation_action_type_ban),
    )

    var name: String by remember { mutableStateOf("") }
    var selectedType: String by remember { mutableStateOf(types.first().first) }
    var selectedAction: String by remember { mutableStateOf(actions.first().first) }
    var durationInput: String by remember { mutableStateOf("600") }
    var reason: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_rules_create_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name,
                    onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.moderation_rules_create_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.moderation_rules_create_name_required) else null,
                )
                Text(
                    text = stringResource(Res.string.moderation_rules_create_type),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    types.forEach { (key, label) ->
                        Badge(
                            selected = selectedType == key,
                            onClick = { selectedType = key },
                        ) { Text(label, style = typography.xs) }
                    }
                }
                Text(
                    text = stringResource(Res.string.moderation_rules_create_action),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    actions.forEach { (key, label) ->
                        Badge(
                            selected = selectedAction == key,
                            onClick = { selectedAction = key },
                        ) { Text(label, style = typography.xs) }
                    }
                }
                if (selectedAction == "timeout") {
                    AppTextField(
                        value = durationInput,
                        onValueChange = { durationInput = it },
                        label = stringResource(Res.string.moderation_action_duration),
                        isError = false,
                        errorText = null,
                    )
                }
                AppTextField(
                    value = reason,
                    onValueChange = { reason = it },
                    label = stringResource(Res.string.moderation_action_reason),
                    isError = false,
                    errorText = null,
                )
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (name.isBlank()) { nameError = true; return@Button }
                    val duration: Int? = if (selectedAction == "timeout") durationInput.trim().toIntOrNull() else null
                    onConfirm(name.trim(), selectedType, selectedAction, duration, reason.trim().takeIf { it.isNotBlank() })
                },
            ) {
                Text(stringResource(Res.string.moderation_rules_create_confirm))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_rules_create_dismiss))
            }
        },
    )
}

// Dialog to send a Twitch chat announcement with an optional color tint. The message is required; color
// defaults to the channel's primary accent when null. The caller owns open/closed state.
@Composable
private fun AnnounceDialog(
    onConfirm: (message: String, color: String?) -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var message: String by remember { mutableStateOf("") }
    val messageError: Boolean = message.isBlank()
    // null = "primary" (the Twitch default)
    var selectedColor: String? by remember { mutableStateOf(null) }

    val colorOptions: List<Pair<String?, String>> = listOf(
        null to stringResource(Res.string.moderation_announce_color_primary),
        "blue" to stringResource(Res.string.moderation_announce_color_blue),
        "green" to stringResource(Res.string.moderation_announce_color_green),
        "orange" to stringResource(Res.string.moderation_announce_color_orange),
        "purple" to stringResource(Res.string.moderation_announce_color_purple),
    )

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.moderation_announce_title),
                style = typography.lg,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = message,
                    onValueChange = { message = it },
                    label = stringResource(Res.string.moderation_announce_message_label),
                    isError = messageError && message.isNotEmpty(),
                    errorText = if (messageError && message.isNotEmpty())
                        stringResource(Res.string.moderation_announce_message_required) else null,
                )
                Text(
                    text = stringResource(Res.string.moderation_announce_color_label),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(
                    horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                    modifier = Modifier.fillMaxWidth(),
                ) {
                    colorOptions.forEach { (colorKey, label) ->
                        Badge(
                            selected = selectedColor == colorKey,
                            onClick = { selectedColor = colorKey },
                        ) { Text(text = label, style = typography.xs) }
                    }
                }
            }
        },
        confirmButton = {
            Button(
                onClick = {
                    if (message.isNotBlank()) onConfirm(message.trim(), selectedColor)
                },
                enabled = message.isNotBlank(),
            ) {
                Text(stringResource(Res.string.moderation_announce_send))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(stringResource(Res.string.moderation_announce_dismiss))
            }
        },
    )
}
