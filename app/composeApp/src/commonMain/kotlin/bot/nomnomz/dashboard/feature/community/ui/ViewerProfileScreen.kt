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
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.shape.RoundedCornerShape
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.FieldPair
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.LinkedIdentity
import bot.nomnomz.dashboard.core.network.ManagementRole
import bot.nomnomz.dashboard.core.network.ModerationHistoryEntry
import bot.nomnomz.dashboard.core.network.PermitGrant
import bot.nomnomz.dashboard.core.network.Quote
import bot.nomnomz.dashboard.core.network.ShoutoutOverrideKind
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.ViewerIdentity
import bot.nomnomz.dashboard.core.network.ViewerProfileSummary
import bot.nomnomz.dashboard.feature.community.state.ViewerProfileController
import bot.nomnomz.dashboard.feature.community.state.ViewerProfileState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole as ShellManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.community_ban_action_short
import nomnomzbot.composeapp.generated.resources.community_ban_confirm
import nomnomzbot.composeapp.generated.resources.community_ban_dismiss
import nomnomzbot.composeapp.generated.resources.community_ban_message
import nomnomzbot.composeapp.generated.resources.community_ban_reason
import nomnomzbot.composeapp.generated.resources.community_ban_title
import nomnomzbot.composeapp.generated.resources.community_banned
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
import nomnomzbot.composeapp.generated.resources.community_gdpr_erase
import nomnomzbot.composeapp.generated.resources.community_gdpr_erase_confirm
import nomnomzbot.composeapp.generated.resources.community_gdpr_erase_desc
import nomnomzbot.composeapp.generated.resources.community_gdpr_export
import nomnomzbot.composeapp.generated.resources.community_gdpr_export_confirm
import nomnomzbot.composeapp.generated.resources.community_gdpr_export_desc
import nomnomzbot.composeapp.generated.resources.community_gdpr_section
import nomnomzbot.composeapp.generated.resources.community_messages_clear
import nomnomzbot.composeapp.generated.resources.community_messages_clear_confirm
import nomnomzbot.composeapp.generated.resources.community_messages_clear_message
import nomnomzbot.composeapp.generated.resources.community_messages_clear_title
import nomnomzbot.composeapp.generated.resources.community_messages_placeholder
import nomnomzbot.composeapp.generated.resources.community_messages_raid_help
import nomnomzbot.composeapp.generated.resources.community_messages_raid_label
import nomnomzbot.composeapp.generated.resources.community_messages_save
import nomnomzbot.composeapp.generated.resources.community_messages_section
import nomnomzbot.composeapp.generated.resources.community_messages_shoutout_help
import nomnomzbot.composeapp.generated.resources.community_messages_shoutout_label
import nomnomzbot.composeapp.generated.resources.community_shoutout_action
import nomnomzbot.composeapp.generated.resources.community_shoutout_action_desc
import nomnomzbot.composeapp.generated.resources.community_stats_close
import nomnomzbot.composeapp.generated.resources.community_stats_commands_used
import nomnomzbot.composeapp.generated.resources.community_stats_follower
import nomnomzbot.composeapp.generated.resources.community_stats_never
import nomnomzbot.composeapp.generated.resources.community_stats_redemptions
import nomnomzbot.composeapp.generated.resources.community_stats_subscriber
import nomnomzbot.composeapp.generated.resources.community_stats_watch_hours
import nomnomzbot.composeapp.generated.resources.community_stats_yes
import nomnomzbot.composeapp.generated.resources.community_stats_no
import nomnomzbot.composeapp.generated.resources.community_trust_label
import nomnomzbot.composeapp.generated.resources.community_trust_moderator
import nomnomzbot.composeapp.generated.resources.community_trust_picker
import nomnomzbot.composeapp.generated.resources.community_trust_subscriber
import nomnomzbot.composeapp.generated.resources.community_trust_viewer
import nomnomzbot.composeapp.generated.resources.community_trust_vip
import nomnomzbot.composeapp.generated.resources.community_unban_action_short
import nomnomzbot.composeapp.generated.resources.community_unban_confirm
import nomnomzbot.composeapp.generated.resources.community_unban_dismiss
import nomnomzbot.composeapp.generated.resources.community_unban_message
import nomnomzbot.composeapp.generated.resources.community_unban_title
import nomnomzbot.composeapp.generated.resources.community_vip_grant
import nomnomzbot.composeapp.generated.resources.community_vip_revoke
import nomnomzbot.composeapp.generated.resources.community_profile_activity_section
import nomnomzbot.composeapp.generated.resources.community_profile_age_consent_confirmed_never
import nomnomzbot.composeapp.generated.resources.community_profile_age_consent_granted
import nomnomzbot.composeapp.generated.resources.community_profile_age_consent_label
import nomnomzbot.composeapp.generated.resources.community_profile_age_consent_not_granted
import nomnomzbot.composeapp.generated.resources.community_profile_age_consent_unknown
import nomnomzbot.composeapp.generated.resources.community_profile_back
import nomnomzbot.composeapp.generated.resources.community_profile_command_usage_last_used
import nomnomzbot.composeapp.generated.resources.community_profile_command_usage_never
import nomnomzbot.composeapp.generated.resources.community_profile_command_usage_section
import nomnomzbot.composeapp.generated.resources.community_profile_command_usage_total
import nomnomzbot.composeapp.generated.resources.community_profile_economy_section
import nomnomzbot.composeapp.generated.resources.community_profile_error
import nomnomzbot.composeapp.generated.resources.community_profile_first_seen
import nomnomzbot.composeapp.generated.resources.community_profile_frozen
import nomnomzbot.composeapp.generated.resources.community_profile_giveaway_entries
import nomnomzbot.composeapp.generated.resources.community_profile_giveaway_wins
import nomnomzbot.composeapp.generated.resources.community_profile_history_add_note
import nomnomzbot.composeapp.generated.resources.community_profile_history_bans
import nomnomzbot.composeapp.generated.resources.community_profile_history_empty
import nomnomzbot.composeapp.generated.resources.community_profile_history_last_action
import nomnomzbot.composeapp.generated.resources.community_profile_history_load_more
import nomnomzbot.composeapp.generated.resources.community_profile_history_messages_deleted
import nomnomzbot.composeapp.generated.resources.community_profile_history_no_summary
import nomnomzbot.composeapp.generated.resources.community_profile_history_note_label
import nomnomzbot.composeapp.generated.resources.community_profile_history_note_placeholder
import nomnomzbot.composeapp.generated.resources.community_profile_history_section
import nomnomzbot.composeapp.generated.resources.community_profile_history_timeouts
import nomnomzbot.composeapp.generated.resources.community_profile_history_warnings
import nomnomzbot.composeapp.generated.resources.community_profile_identity_section
import nomnomzbot.composeapp.generated.resources.community_profile_leaderboard_opted_out
import nomnomzbot.composeapp.generated.resources.community_profile_lifetime_earned
import nomnomzbot.composeapp.generated.resources.community_profile_lifetime_spent
import nomnomzbot.composeapp.generated.resources.community_profile_linked_identities
import nomnomzbot.composeapp.generated.resources.community_profile_loading
import nomnomzbot.composeapp.generated.resources.community_profile_management_role
import nomnomzbot.composeapp.generated.resources.community_profile_management_role_none
import nomnomzbot.composeapp.generated.resources.community_profile_management_role_picker
import nomnomzbot.composeapp.generated.resources.community_profile_member_since
import nomnomzbot.composeapp.generated.resources.community_profile_no_wallet
import nomnomzbot.composeapp.generated.resources.community_profile_permit_grant_role
import nomnomzbot.composeapp.generated.resources.community_profile_permit_revoke
import nomnomzbot.composeapp.generated.resources.community_profile_permits_empty
import nomnomzbot.composeapp.generated.resources.community_profile_permits_section
import nomnomzbot.composeapp.generated.resources.community_profile_pronouns
import nomnomzbot.composeapp.generated.resources.community_profile_quotes_empty
import nomnomzbot.composeapp.generated.resources.community_profile_quotes_more
import nomnomzbot.composeapp.generated.resources.community_profile_quotes_section
import nomnomzbot.composeapp.generated.resources.community_profile_remove_role
import nomnomzbot.composeapp.generated.resources.community_retry
import nomnomzbot.composeapp.generated.resources.community_profile_standing
import nomnomzbot.composeapp.generated.resources.community_profile_streak_current
import nomnomzbot.composeapp.generated.resources.community_profile_streak_max
import nomnomzbot.composeapp.generated.resources.community_profile_tts_voice_clear
import nomnomzbot.composeapp.generated.resources.community_profile_tts_voice_label
import nomnomzbot.composeapp.generated.resources.community_profile_tts_voice_none
import nomnomzbot.composeapp.generated.resources.community_profile_wallet_balance
import org.jetbrains.compose.resources.stringResource

// The Community PROFILE page (owner punch list 2026-09-08 §3B) — a real single-person page, opened from the
// Directory, surfacing EVERY per-channel-per-user data point the domain model tracks per the owner's punch-list
// table. Read sections mirror the table's groups 1:1; genuinely editable sections (overrides, TTS voice,
// management role, permits, free-form data, moderation-history notes, plus trust/ban/VIP/shoutout) each act
// through the SAME endpoint another page already owns — never a second write path. Sections with no backend
// mutation endpoint (community standing, trust score/heat, economy, command usage, activity/streak, age
// consent, quotes) stay display-only, called out in each section's own comment.
@Composable
fun ViewerProfileScreen(
    controller: ViewerProfileController,
    userId: String,
    role: ShellManagementRole?,
    onBack: () -> Unit,
) {
    val state: ViewerProfileState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()

    LaunchedEffect(userId) { controller.load(userId) }

    // One write floor per action group (owner punch list §3, each section's own comment explains the floor):
    // Moderator for the quick moderation actions + history notes (matches the old Community page's manage
    // floor), Editor for config-shaped edits (overrides/TTS voice/free-form data — the SAME floor the old
    // Community page already used for these), Broadcaster for management-role assignment (RolesController's
    // own `roles:manage` floor) and permit grants (PermitsController's own `permit:issue` floor is Editor, one
    // notch below role assignment).
    val moderate: ManageDecision = rememberManageDecisionAtFloor(role, ShellManagementRole.Moderator)
    val configWrite: ManageDecision = rememberManageDecisionAtFloor(role, ShellManagementRole.Editor)
    val permitWrite: ManageDecision = rememberManageDecisionAtFloor(role, ShellManagementRole.Editor)
    val roleWrite: ManageDecision = rememberManageDecisionAtFloor(role, ShellManagementRole.Broadcaster)
    val isBroadcaster: Boolean = role == ShellManagementRole.Broadcaster

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: ViewerProfileState = state) {
            is ViewerProfileState.Loading -> CenteredProfileMessage(stringResource(Res.string.community_profile_loading))
            is ViewerProfileState.Error ->
                ProfileErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load(userId) } })
            is ViewerProfileState.Ready ->
                ProfileContent(
                    state = current,
                    moderate = moderate,
                    configWrite = configWrite,
                    permitWrite = permitWrite,
                    roleWrite = roleWrite,
                    isBroadcaster = isBroadcaster,
                    onBack = onBack,
                    controller = controller,
                    scope = scope,
                )
        }
    }
}

@Composable
private fun ProfileErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = stringResource(Res.string.community_profile_error, detail), style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.community_retry)) }
        }
    }
}

@Composable
private fun CenteredProfileMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}

@Composable
private fun ProfileContent(
    state: ViewerProfileState.Ready,
    moderate: ManageDecision,
    configWrite: ManageDecision,
    permitWrite: ManageDecision,
    roleWrite: ManageDecision,
    isBroadcaster: Boolean,
    onBack: () -> Unit,
    controller: ViewerProfileController,
    scope: kotlinx.coroutines.CoroutineScope,
) {
    val spacing = LocalSpacing.current
    val profile: ViewerProfileSummary = state.profile
    val identity: ViewerIdentity = profile.identity
    val name: String = identity.displayName.ifBlank { identity.username.ifBlank { identity.userId } }

    var pendingBan: Boolean by remember { mutableStateOf(false) }
    var pendingUnban: Boolean by remember { mutableStateOf(false) }
    var pendingExport: Boolean by remember { mutableStateOf(false) }
    var pendingErase: Boolean by remember { mutableStateOf(false) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        contentPadding = PaddingValues(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        item(key = "header") {
            PageHeader(
                title = name,
                subtitle = identity.username.takeIf { it.isNotBlank() && it != name },
                trailing = { TextButton(onClick = onBack) { Text(text = stringResource(Res.string.community_profile_back)) } },
            )
        }
        item(key = "identity") {
            IdentitySection(
                identity = identity,
                isBanned = state.isBanned,
                moderate = moderate,
                roleWrite = roleWrite,
                isBroadcaster = isBroadcaster,
                onSetTrust = { level -> scope.launch { controller.setTrust(level) } },
                onBanRequested = { pendingBan = true },
                onUnbanRequested = { pendingUnban = true },
                onToggleVip = { isVip ->
                    scope.launch { if (isVip) controller.removeVip() else controller.addVip() }
                },
                onShoutoutNow = { scope.launch { controller.shoutoutNow() } },
                onSetRole = { newRole -> scope.launch { controller.setManagementRole(newRole) } },
                onRemoveRole = { scope.launch { controller.removeManagementRole() } },
                onExportRequested = { pendingExport = true },
                onEraseRequested = { pendingErase = true },
            )
        }

        item(key = "history") {
            HistorySection(
                summary = profile.moderationHistory,
                history = state.history,
                hasMore = state.historyHasMore,
                moderate = moderate,
                onLoadMore = { scope.launch { controller.loadMoreHistory() } },
                onAddNote = { note -> scope.launch { controller.addHistoryNote(note) } },
            )
        }

        item(key = "activity") { ActivitySection(profile) }
        item(key = "economy") { EconomySection(profile) }
        item(key = "permits") {
            PermitsSection(
                profile = profile,
                write = permitWrite,
                onGrantRole = { grantedRole, expiresAt, reason ->
                    scope.launch { controller.grantPermitRole(grantedRole, expiresAt, reason) }
                },
                onRevoke = { selector -> scope.launch { controller.revokePermit(selector) } },
            )
        }
        item(key = "overrides") {
            OverridesSection(
                name = name,
                overrides = profile.overrides,
                availableVoices = state.availableVoices,
                write = configWrite,
                onSaveMessage = { kind, template ->
                    scope.launch { controller.saveOverrideMessage(kind, template) }
                },
                onClearMessage = { kind -> scope.launch { controller.clearOverrideMessage(kind) } },
                onSaveVoice = { voiceId -> scope.launch { controller.saveTtsVoice(voiceId) } },
                onClearVoice = { scope.launch { controller.clearTtsVoice() } },
            )
        }
        item(key = "command-usage") { CommandUsageSection(profile) }
        item(key = "free-form") {
            FreeFormDataSection(
                initial = profile.freeFormData.associate { it.key to it.value },
                write = configWrite,
                controller = controller,
                scope = scope,
            )
        }
        item(key = "quotes") { QuotesSection(profile) }
    }

    val banReason: String = stringResource(Res.string.community_ban_reason)
    if (pendingBan) {
        ConfirmDialog(
            title = stringResource(Res.string.community_ban_title),
            message = stringResource(Res.string.community_ban_message, name),
            confirmLabel = stringResource(Res.string.community_ban_confirm),
            dismissLabel = stringResource(Res.string.community_ban_dismiss),
            destructive = true,
            onConfirm = {
                scope.launch { controller.ban(banReason) }
                pendingBan = false
            },
            onDismiss = { pendingBan = false },
        )
    }
    if (pendingUnban) {
        ConfirmDialog(
            title = stringResource(Res.string.community_unban_title),
            message = stringResource(Res.string.community_unban_message, name),
            confirmLabel = stringResource(Res.string.community_unban_confirm),
            dismissLabel = stringResource(Res.string.community_unban_dismiss),
            destructive = true,
            onConfirm = {
                scope.launch { controller.unban() }
                pendingUnban = false
            },
            onDismiss = { pendingUnban = false },
        )
    }
    if (pendingExport) {
        ConfirmDialog(
            title = stringResource(Res.string.community_gdpr_export),
            message = stringResource(Res.string.community_gdpr_export_desc, name),
            confirmLabel = stringResource(Res.string.community_gdpr_export_confirm),
            dismissLabel = stringResource(Res.string.community_stats_close),
            destructive = false,
            onConfirm = {
                scope.launch { controller.exportUserData() }
                pendingExport = false
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
                scope.launch { controller.eraseUserData() }
                pendingErase = false
            },
            onDismiss = { pendingErase = false },
        )
    }
}

// ── 1. Identity & standing (owner punch list §3 row 1) ─────────────────────────────────────────────────────
// Community standing (Plane A) is a SYSTEM-DERIVED read — it reflects live Twitch follow/sub/VIP/mod state, not
// something this page writes, so it stays display-only. Management role (Plane B) IS genuinely editable here
// via RolesApi (roles:manage, Broadcaster floor). The quick moderation row (trust/ban/VIP/shoutout) reuses the
// SAME CommunityApi endpoints the old Community page's row actions called — moved here, not duplicated.
@Composable
private fun IdentitySection(
    identity: ViewerIdentity,
    isBanned: Boolean,
    moderate: ManageDecision,
    roleWrite: ManageDecision,
    isBroadcaster: Boolean,
    onSetTrust: (String) -> Unit,
    onBanRequested: () -> Unit,
    onUnbanRequested: () -> Unit,
    onToggleVip: (currentlyVip: Boolean) -> Unit,
    onShoutoutNow: () -> Unit,
    onSetRole: (ManagementRole) -> Unit,
    onRemoveRole: () -> Unit,
    onExportRequested: () -> Unit,
    onEraseRequested: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    ProfileCard(title = stringResource(Res.string.community_profile_identity_section)) {
        if (isBanned) {
            Text(
                text = stringResource(Res.string.community_banned),
                style = typography.xs,
                color = tokens.destructiveForeground,
                modifier = Modifier
                    .background(tokens.destructive, RoundedCornerShape(tokens.radius.sm))
                    .padding(horizontal = spacing.s2, vertical = spacing.s1),
            )
        }
        identity.pronoun?.let { pronoun ->
            ProfileFact(label = stringResource(Res.string.community_profile_pronouns), value = pronoun + (identity.altPronoun?.let { " / $it" } ?: ""))
        }
        ProfileFact(label = stringResource(Res.string.community_profile_standing), value = identity.communityStanding.replaceFirstChar { it.uppercase() })
        identity.subTier?.let { tier -> ProfileFact(label = stringResource(Res.string.community_stats_subscriber), value = tier) }
        ProfileFact(label = stringResource(Res.string.community_profile_member_since), value = identity.memberSinceUtc ?: stringResource(Res.string.community_stats_never))
        ProfileFact(label = stringResource(Res.string.community_profile_first_seen), value = identity.firstSeenUtc)

        if (identity.linkedIdentities.isNotEmpty()) {
            Text(text = stringResource(Res.string.community_profile_linked_identities), style = typography.xs, color = tokens.mutedForeground, modifier = Modifier.padding(top = spacing.s2))
            identity.linkedIdentities.forEach { linked: LinkedIdentity ->
                Text(text = "${linked.provider}: ${linked.providerDisplayName ?: linked.providerUsername}", style = typography.sm, color = tokens.foreground)
            }
        }

        Spacer(modifier = Modifier.height(spacing.s2))
        Separator()
        Spacer(modifier = Modifier.height(spacing.s2))

        // Management role — Broadcaster floor (RolesController.SetRole/RemoveRole).
        ManagementRolePicker(current = identity.managementRole, write = roleWrite, onSetRole = onSetRole, onRemoveRole = onRemoveRole)

        Spacer(modifier = Modifier.height(spacing.s2))
        Separator()
        Spacer(modifier = Modifier.height(spacing.s2))

        // Quick moderation actions — Moderator floor, the SAME CommunityApi endpoints the Directory row used to
        // call directly.
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = moderate) { enabled ->
                TrustPicker(current = identity.communityStanding, enabled = enabled, onSelect = onSetTrust)
            }
            ManageGate(decision = moderate) { enabled ->
                if (isBanned) {
                    TextButton(onClick = onUnbanRequested, enabled = enabled) {
                        Text(text = stringResource(Res.string.community_unban_action_short), color = if (enabled) tokens.primary else tokens.mutedForeground)
                    }
                } else {
                    TextButton(onClick = onBanRequested, enabled = enabled) {
                        Text(text = stringResource(Res.string.community_ban_action_short), color = if (enabled) tokens.destructive else tokens.mutedForeground)
                    }
                }
            }
            ManageGate(decision = moderate) { enabled ->
                val isVip: Boolean = identity.communityStanding.equals("vip", ignoreCase = true)
                TextButton(onClick = { onToggleVip(isVip) }, enabled = enabled) {
                    Text(
                        text = stringResource(if (isVip) Res.string.community_vip_revoke else Res.string.community_vip_grant),
                        color = if (!enabled) tokens.mutedForeground else if (isVip) tokens.destructive else tokens.primary,
                    )
                }
            }
            ManageGate(decision = moderate) { enabled ->
                val shoutoutDesc: String = stringResource(Res.string.community_shoutout_action_desc, identity.displayName)
                TextButton(onClick = onShoutoutNow, enabled = enabled, modifier = Modifier.semantics { role = Role.Button; contentDescription = shoutoutDesc }) {
                    Text(text = stringResource(Res.string.community_shoutout_action), color = if (enabled) tokens.primary else tokens.mutedForeground)
                }
            }
        }

        if (isBroadcaster) {
            Spacer(modifier = Modifier.height(spacing.s2))
            Separator()
            Spacer(modifier = Modifier.height(spacing.s2))
            Text(text = stringResource(Res.string.community_gdpr_section), style = typography.xs, color = tokens.mutedForeground)
            TextButton(onClick = onExportRequested, modifier = Modifier.fillMaxWidth()) {
                Text(text = stringResource(Res.string.community_gdpr_export), color = tokens.primary)
            }
            TextButton(onClick = onEraseRequested, modifier = Modifier.fillMaxWidth()) {
                Text(text = stringResource(Res.string.community_gdpr_erase), color = tokens.destructive)
            }
        }
    }
}

@Composable
private fun ManagementRolePicker(
    current: String?,
    write: ManageDecision,
    onSetRole: (ManagementRole) -> Unit,
    onRemoveRole: () -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    var expanded: Boolean by remember { mutableStateOf(false) }
    val currentLabel: String = current ?: stringResource(Res.string.community_profile_management_role_none)
    val pickerLabel: String = stringResource(Res.string.community_profile_management_role_picker, currentLabel)

    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(LocalSpacing.current.s2)) {
        Text(text = stringResource(Res.string.community_profile_management_role), style = typography.sm, color = tokens.mutedForeground)
        Box {
            ManageGate(decision = write) { enabled ->
                TextButton(onClick = { expanded = true }, enabled = enabled, modifier = Modifier.semantics { contentDescription = pickerLabel }) {
                    Text(text = currentLabel, color = if (enabled) tokens.primary else tokens.mutedForeground)
                }
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                ManagementRole.entries.forEach { candidate ->
                    DropdownMenuItem(
                        text = { Text(text = candidate.name, style = typography.sm, color = tokens.popoverForeground) },
                        onClick = { expanded = false; onSetRole(candidate) },
                    )
                }
                if (current != null) {
                    DropdownMenuItem(
                        text = { Text(text = stringResource(Res.string.community_profile_remove_role), style = typography.sm, color = tokens.destructive) },
                        onClick = { expanded = false; onRemoveRole() },
                    )
                }
            }
        }
    }
}

@Composable
private fun TrustPicker(current: String, enabled: Boolean, onSelect: (String) -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    var expanded: Boolean by remember { mutableStateOf(false) }
    val activeLabel: String = stringResource(trustLabelRes(current))
    val pickerLabel: String = stringResource(Res.string.community_trust_picker, activeLabel, activeLabel)

    Box {
        TextButton(onClick = { expanded = true }, enabled = enabled, modifier = Modifier.semantics { contentDescription = pickerLabel }) {
            Text(text = activeLabel, color = if (enabled) tokens.primary else tokens.mutedForeground)
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            listOf("viewer", "subscriber", "vip", "moderator").forEach { level ->
                val label: String = stringResource(trustLabelRes(level))
                val itemDescription: String = stringResource(Res.string.community_trust_label, label)
                DropdownMenuItem(
                    text = { Text(text = label, style = typography.sm, color = tokens.popoverForeground) },
                    modifier = Modifier.semantics { role = Role.Button; contentDescription = itemDescription },
                    onClick = { expanded = false; if (!level.equals(current, ignoreCase = true)) onSelect(level) },
                )
            }
        }
    }
}

private fun trustLabelRes(level: String) =
    when (level.lowercase()) {
        "moderator" -> Res.string.community_trust_moderator
        "vip" -> Res.string.community_trust_vip
        "subscriber" -> Res.string.community_trust_subscriber
        else -> Res.string.community_trust_viewer
    }

// ── 2. Moderation history (owner punch list §3 row 2 / §12) ────────────────────────────────────────────────
// [summary] is the SAME UserModerationHistorySummaryDto the Moderation Desk quick-action popup already renders
// — not a second shape. The full log pages through the SAME endpoint the Moderation History page browses.
// Adding a note is genuinely editable (Moderator floor, ModerationController.AddHistoryNote).
@Composable
private fun HistorySection(
    summary: bot.nomnomz.dashboard.core.network.UserModerationHistorySummary?,
    history: List<ModerationHistoryEntry>,
    hasMore: Boolean,
    moderate: ManageDecision,
    onLoadMore: () -> Unit,
    onAddNote: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var noteDraft: String by remember { mutableStateOf("") }

    ProfileCard(title = stringResource(Res.string.community_profile_history_section)) {
        if (summary == null) {
            Text(text = stringResource(Res.string.community_profile_history_no_summary), style = typography.sm, color = tokens.mutedForeground)
        } else {
            ProfileFact(label = stringResource(Res.string.community_profile_history_timeouts), value = summary.timeoutCount.toString())
            ProfileFact(label = stringResource(Res.string.community_profile_history_bans), value = summary.banCount.toString())
            ProfileFact(label = stringResource(Res.string.community_profile_history_warnings), value = summary.warningCount.toString())
            ProfileFact(label = stringResource(Res.string.community_profile_history_messages_deleted), value = summary.messagesDeletedCount.toString())
            summary.lastActionType?.let { last -> ProfileFact(label = stringResource(Res.string.community_profile_history_last_action), value = last) }
        }

        Spacer(modifier = Modifier.height(spacing.s2))
        Separator()
        Spacer(modifier = Modifier.height(spacing.s2))

        if (history.isEmpty()) {
            Text(text = stringResource(Res.string.community_profile_history_empty), style = typography.sm, color = tokens.mutedForeground)
        } else {
            history.forEachIndexed { index, entry ->
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(text = entry.actionType, style = typography.sm, color = tokens.foreground)
                        entry.reason?.let { reason -> Text(text = reason, style = typography.xs, color = tokens.mutedForeground) }
                    }
                    Text(text = entry.occurredAt, style = typography.xs, color = tokens.mutedForeground)
                }
                if (index < history.lastIndex) Separator()
            }
            if (hasMore) {
                TextButton(onClick = onLoadMore, modifier = Modifier.fillMaxWidth()) {
                    Text(text = stringResource(Res.string.community_profile_history_load_more), color = tokens.primary)
                }
            }
        }

        Spacer(modifier = Modifier.height(spacing.s2))
        ManageGate(decision = moderate) { enabled ->
            Column {
                AppTextField(
                    value = noteDraft,
                    onValueChange = { noteDraft = it },
                    label = stringResource(Res.string.community_profile_history_note_label),
                    placeholder = stringResource(Res.string.community_profile_history_note_placeholder),
                    enabled = enabled,
                    modifier = Modifier.fillMaxWidth(),
                )
                TextButton(
                    onClick = {
                        val trimmed: String = noteDraft.trim()
                        if (trimmed.isNotBlank()) {
                            onAddNote(trimmed)
                            noteDraft = ""
                        }
                    },
                    enabled = enabled && noteDraft.isNotBlank(),
                ) {
                    Text(text = stringResource(Res.string.community_profile_history_add_note), color = if (enabled) tokens.primary else tokens.mutedForeground)
                }
            }
        }
    }
}

// ── 3. Chat & watch activity (owner punch list §3 row 3) — display-only: no mutation endpoint exists for
// analytics/streak data; it is folded, computed state. ──────────────────────────────────────────────────────
@Composable
private fun ActivitySection(profile: ViewerProfileSummary) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val activity = profile.activity
    val streak = profile.streak

    ProfileCard(title = stringResource(Res.string.community_profile_activity_section)) {
        if (activity == null && streak == null) {
            Text(text = stringResource(Res.string.community_profile_history_no_summary), style = typography.sm, color = tokens.mutedForeground)
            return@ProfileCard
        }
        activity?.let {
            ProfileFact(label = stringResource(Res.string.community_stats_watch_hours), value = (it.totalWatchSeconds / 3600.0).toFixed1())
            ProfileFact(label = stringResource(Res.string.community_profile_command_usage_total), value = it.totalMessages.toString())
            ProfileFact(label = stringResource(Res.string.community_stats_commands_used), value = it.totalCommandsUsed.toString())
            ProfileFact(label = stringResource(Res.string.community_stats_redemptions), value = it.totalRedemptions.toString())
            ProfileFact(
                label = stringResource(Res.string.community_stats_follower),
                value = stringResource(if (it.isFollower) Res.string.community_stats_yes else Res.string.community_stats_no),
            )
        }
        streak?.let {
            ProfileFact(label = stringResource(Res.string.community_profile_streak_current), value = it.currentStreak.toString())
            ProfileFact(label = stringResource(Res.string.community_profile_streak_max), value = it.maxStreak.toString())
        }
    }
}

// ── 4. Economy & games (owner punch list §3 row 4) — display-only: the Economy pages own currency mutation. ──
@Composable
private fun EconomySection(profile: ViewerProfileSummary) {
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val economy = profile.economy

    ProfileCard(title = stringResource(Res.string.community_profile_economy_section)) {
        val wallet = economy.wallet
        if (wallet == null) {
            Text(text = stringResource(Res.string.community_profile_no_wallet), style = typography.sm, color = tokens.mutedForeground)
        } else {
            ProfileFact(label = stringResource(Res.string.community_profile_wallet_balance), value = wallet.balance.toString())
            ProfileFact(label = stringResource(Res.string.community_profile_lifetime_earned), value = wallet.lifetimeEarned.toString())
            ProfileFact(label = stringResource(Res.string.community_profile_lifetime_spent), value = wallet.lifetimeSpent.toString())
            if (wallet.isFrozen) {
                Text(text = stringResource(Res.string.community_profile_frozen), style = typography.xs, color = tokens.destructive)
            }
        }
        ProfileFact(label = stringResource(Res.string.community_profile_giveaway_entries), value = economy.giveawayEntryCount.toString())
        ProfileFact(label = stringResource(Res.string.community_profile_giveaway_wins), value = economy.giveawayWinCount.toString())
        if (economy.leaderboardOptedOut) {
            Text(text = stringResource(Res.string.community_profile_leaderboard_opted_out), style = typography.xs, color = tokens.mutedForeground)
        }
    }
}

// ── 5. Permits & consent (owner punch list §3 row 5) ────────────────────────────────────────────────────────
// [activePermits] is genuinely editable — grant a role permit / revoke, via RolesApi's permit endpoints
// (permit:issue, Editor floor). Age consent has no mutation endpoint on this page (it is the viewer's own GDPR
// consent record) so it stays display-only.
@Composable
private fun PermitsSection(
    profile: ViewerProfileSummary,
    write: ManageDecision,
    onGrantRole: (ManagementRole, String?, String?) -> Unit,
    onRevoke: (String?) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val permits = profile.permits
    var expanded: Boolean by remember { mutableStateOf(false) }

    ProfileCard(title = stringResource(Res.string.community_profile_permits_section)) {
        val consentLabel: String =
            when (permits.ageConsentGranted) {
                true -> stringResource(Res.string.community_profile_age_consent_granted)
                false -> stringResource(Res.string.community_profile_age_consent_not_granted)
                null -> stringResource(Res.string.community_profile_age_consent_unknown)
            }
        ProfileFact(label = stringResource(Res.string.community_profile_age_consent_label), value = consentLabel)
        ProfileFact(
            label = stringResource(Res.string.community_profile_first_seen),
            value = permits.ageConsentConfirmedUtc ?: stringResource(Res.string.community_profile_age_consent_confirmed_never),
        )

        Spacer(modifier = Modifier.height(spacing.s2))
        Separator()
        Spacer(modifier = Modifier.height(spacing.s2))

        if (permits.activePermits.isEmpty()) {
            Text(text = stringResource(Res.string.community_profile_permits_empty), style = typography.sm, color = tokens.mutedForeground)
        } else {
            permits.activePermits.forEach { grant: PermitGrant ->
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween, verticalAlignment = Alignment.CenterVertically) {
                    Text(
                        text = grant.role?.name ?: grant.capabilityActionKey.orEmpty(),
                        style = typography.sm,
                        color = tokens.foreground,
                    )
                    ManageGate(decision = write) { enabled ->
                        TextButton(onClick = { onRevoke(grant.revokeSelector) }, enabled = enabled) {
                            Text(text = stringResource(Res.string.community_profile_permit_revoke), color = if (enabled) tokens.destructive else tokens.mutedForeground)
                        }
                    }
                }
            }
        }

        Spacer(modifier = Modifier.height(spacing.s2))
        ManageGate(decision = write) { enabled ->
            Box {
                TextButton(onClick = { expanded = true }, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.community_profile_permit_grant_role),
                        color = if (enabled) tokens.primary else tokens.mutedForeground,
                    )
                }
                DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                    ManagementRole.entries.forEach { candidate ->
                        DropdownMenuItem(
                            text = { Text(text = candidate.name, style = typography.sm, color = tokens.popoverForeground) },
                            onClick = { expanded = false; onGrantRole(candidate, null, null) },
                        )
                    }
                }
            }
        }
    }
}

// ── 6. Custom bot behavior (owner punch list §3 row 6) ──────────────────────────────────────────────────────
// All three are genuinely editable: shoutout/raid lines via ModerationApi (the SAME endpoint the old Community
// "Messages" section used, keyed on the person's Twitch id), TTS voice via TtsApi's per-viewer voice endpoints
// — a real picker from [availableVoices], not a raw voice-id text field.
@Composable
private fun OverridesSection(
    name: String,
    overrides: bot.nomnomz.dashboard.core.network.ViewerOverrides,
    availableVoices: List<TtsVoice>,
    write: ManageDecision,
    onSaveMessage: (kind: String, template: String) -> Unit,
    onClearMessage: (kind: String) -> Unit,
    onSaveVoice: (voiceId: String) -> Unit,
    onClearVoice: () -> Unit,
) {
    ProfileCard(title = stringResource(Res.string.community_messages_section)) {
        OverrideMessageField(
            kind = ShoutoutOverrideKind.Shoutout,
            label = stringResource(Res.string.community_messages_shoutout_label),
            help = stringResource(Res.string.community_messages_shoutout_help),
            saved = overrides.shoutoutMessageTemplate,
            write = write,
            name = name,
            onSave = onSaveMessage,
            onClear = onClearMessage,
        )
        OverrideMessageField(
            kind = ShoutoutOverrideKind.Raid,
            label = stringResource(Res.string.community_messages_raid_label),
            help = stringResource(Res.string.community_messages_raid_help),
            saved = overrides.raidMessageTemplate,
            write = write,
            name = name,
            onSave = onSaveMessage,
            onClear = onClearMessage,
        )

        Spacer(modifier = Modifier.height(LocalSpacing.current.s2))
        Separator()
        Spacer(modifier = Modifier.height(LocalSpacing.current.s2))

        VoicePicker(current = overrides.ttsVoice, availableVoices = availableVoices, write = write, onSave = onSaveVoice, onClear = onClearVoice)
    }
}

@Composable
private fun OverrideMessageField(
    kind: String,
    label: String,
    help: String,
    saved: String?,
    write: ManageDecision,
    name: String,
    onSave: (kind: String, template: String) -> Unit,
    onClear: (kind: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    var draft: String by remember(kind) { mutableStateOf(saved.orEmpty()) }
    var pendingClear: Boolean by remember(kind) { mutableStateOf(false) }
    LaunchedEffect(saved) { draft = saved.orEmpty() }

    ManageGate(decision = write) { enabled ->
        Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            AppTextField(
                value = draft,
                onValueChange = { draft = it },
                label = label,
                placeholder = stringResource(Res.string.community_messages_placeholder),
                enabled = enabled,
                modifier = Modifier.fillMaxWidth(),
            )
            Text(text = help, style = typography.xs, color = tokens.mutedForeground)
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                val canSave: Boolean = enabled && draft.trim().isNotBlank() && draft.trim() != saved.orEmpty()
                TextButton(onClick = { if (canSave) onSave(kind, draft.trim()) }, enabled = canSave) {
                    Text(text = stringResource(Res.string.community_messages_save), color = if (canSave) tokens.primary else tokens.mutedForeground)
                }
                if (!saved.isNullOrBlank()) {
                    TextButton(onClick = { pendingClear = true }, enabled = enabled) {
                        Text(text = stringResource(Res.string.community_messages_clear), color = if (enabled) tokens.foreground else tokens.mutedForeground)
                    }
                }
            }
        }
    }
    if (pendingClear) {
        ConfirmDialog(
            title = stringResource(Res.string.community_messages_clear_title),
            message = stringResource(Res.string.community_messages_clear_message, label.lowercase(), name),
            confirmLabel = stringResource(Res.string.community_messages_clear_confirm),
            dismissLabel = stringResource(Res.string.community_stats_close),
            destructive = false,
            onConfirm = { pendingClear = false; onClear(kind) },
            onDismiss = { pendingClear = false },
        )
    }
}

@Composable
private fun VoicePicker(
    current: bot.nomnomz.dashboard.core.network.UserTtsVoice?,
    availableVoices: List<TtsVoice>,
    write: ManageDecision,
    onSave: (voiceId: String) -> Unit,
    onClear: () -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    var expanded: Boolean by remember { mutableStateOf(false) }
    val currentLabel: String =
        availableVoices.firstOrNull { it.id == current?.voiceId }?.displayName
            ?: current?.voiceId
            ?: stringResource(Res.string.community_profile_tts_voice_none)

    Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(LocalSpacing.current.s2)) {
        Text(text = stringResource(Res.string.community_profile_tts_voice_label), style = typography.sm, color = tokens.mutedForeground)
        Box {
            ManageGate(decision = write) { enabled ->
                TextButton(onClick = { expanded = true }, enabled = enabled) {
                    Text(text = currentLabel, color = if (enabled) tokens.primary else tokens.mutedForeground)
                }
            }
            DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
                availableVoices.take(50).forEach { voice ->
                    DropdownMenuItem(
                        text = {
                            Text(
                                text =
                                    resolveRowLabel(
                                        primary = voice.displayName,
                                        secondary = voice.id,
                                        typeLabel = "Voice",
                                        discriminatorSource = voice.id,
                                    ),
                                style = typography.sm,
                                color = tokens.popoverForeground,
                            )
                        },
                        onClick = { expanded = false; onSave(voice.id) },
                    )
                }
            }
        }
        if (current != null) {
            ManageGate(decision = write) { enabled ->
                TextButton(onClick = onClear, enabled = enabled) {
                    Text(text = stringResource(Res.string.community_profile_tts_voice_clear), color = if (enabled) tokens.foreground else tokens.mutedForeground)
                }
            }
        }
    }
}

// ── 7. Command usage (owner punch list §3 row 7) — display-only: tracked automatically, nothing to edit. ────
@Composable
private fun CommandUsageSection(profile: ViewerProfileSummary) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val usage = profile.commandUsage
    ProfileCard(title = stringResource(Res.string.community_profile_command_usage_section)) {
        ProfileFact(label = stringResource(Res.string.community_profile_command_usage_total), value = usage.totalCommandsUsed.toString())
        ProfileFact(
            label = stringResource(Res.string.community_profile_command_usage_last_used),
            value = usage.lastUsedUtc ?: stringResource(Res.string.community_profile_command_usage_never),
        )
        if (usage.totalCommandsUsed == 0) {
            Text(text = stringResource(Res.string.community_profile_command_usage_never), style = typography.xs, color = tokens.mutedForeground)
        }
    }
}

// ── 8. Free-form data (owner punch list §3 row 8) — the existing generic "Community Data" editor, reused. ───
@Composable
private fun FreeFormDataSection(
    initial: Map<String, String>,
    write: ManageDecision,
    controller: ViewerProfileController,
    scope: kotlinx.coroutines.CoroutineScope,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var data: Map<String, String> by remember(initial) { mutableStateOf(initial) }
    var newKey: String by remember { mutableStateOf("") }
    var newValue: String by remember { mutableStateOf("") }
    var keyError: Boolean by remember { mutableStateOf(false) }
    var saveError: String? by remember { mutableStateOf(null) }
    var pendingDelete: String? by remember { mutableStateOf(null) }

    ProfileCard(title = stringResource(Res.string.community_data_section)) {
        val entries: List<Map.Entry<String, String>> = data.entries.sortedBy { it.key }
        if (entries.isEmpty()) {
            Text(text = stringResource(Res.string.community_data_empty), style = typography.sm, color = tokens.mutedForeground)
        } else {
            entries.forEach { entry ->
                Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalAlignment = Alignment.CenterVertically) {
                    Column(modifier = Modifier.weight(1f)) {
                        Text(text = entry.key, style = typography.sm, color = tokens.foreground, maxLines = 2, overflow = TextOverflow.Ellipsis)
                        Text(text = entry.value, style = typography.xs, color = tokens.mutedForeground, maxLines = 2, overflow = TextOverflow.Ellipsis)
                    }
                    if (write.isAllowed) {
                        GlyphButton(icon = TrashGlyph, label = stringResource(Res.string.community_data_delete, entry.key), onClick = { pendingDelete = entry.key }, tint = tokens.destructive)
                    }
                }
            }
        }
        if (write.isAllowed) {
            FieldPair(
                verticalAlignment = Alignment.Top,
                first = { fieldModifier -> AppTextField(value = newKey, onValueChange = { newKey = it; keyError = false }, label = stringResource(Res.string.community_data_key), isError = keyError, errorText = if (keyError) stringResource(Res.string.community_data_key_required) else null, modifier = fieldModifier) },
                second = { fieldModifier -> AppTextField(value = newValue, onValueChange = { newValue = it }, label = stringResource(Res.string.community_data_value), modifier = fieldModifier) },
            )
            TextButton(
                onClick = {
                    val key: String = newKey.trim().lowercase()
                    if (key.isEmpty()) { keyError = true; return@TextButton }
                    scope.launch {
                        val err: String? = controller.setViewerDatum(key, newValue)
                        saveError = err
                        if (err == null) {
                            data = data + (key to newValue)
                            newKey = ""; newValue = ""
                        }
                    }
                },
                modifier = Modifier.fillMaxWidth(),
            ) { Text(text = stringResource(Res.string.community_data_add), color = tokens.primary) }
        }
        saveError?.let { detail -> Text(text = detail, style = typography.xs, color = tokens.destructive) }
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
                scope.launch {
                    val err: String? = controller.deleteViewerDatum(key)
                    saveError = err
                    if (err == null) data = data - key
                }
            },
            onDismiss = { pendingDelete = null },
        )
    }
}

// ── 9. Quotes (owner punch list §3 row 9) — display-only bounded first page; paging further needs a backend
// `?userId=` filter on GET /quotes that does not exist yet (flagged in ViewerProfileSummary's own doc comment).
@Composable
private fun QuotesSection(profile: ViewerProfileSummary) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    ProfileCard(title = stringResource(Res.string.community_profile_quotes_section)) {
        if (profile.recentQuotes.isEmpty()) {
            Text(text = stringResource(Res.string.community_profile_quotes_empty), style = typography.sm, color = tokens.mutedForeground)
        } else {
            profile.recentQuotes.forEach { quote: Quote ->
                Text(text = "#${quote.number} — ${quote.text}", style = typography.sm, color = tokens.foreground)
            }
            if (profile.totalQuoteCount > profile.recentQuotes.size) {
                Text(
                    text = stringResource(Res.string.community_profile_quotes_more, profile.totalQuoteCount - profile.recentQuotes.size),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }
    }
}

// ── Shared section chrome ─────────────────────────────────────────────────────────────────────────────────

@Composable
private fun ProfileCard(
    title: String,
    content: @Composable androidx.compose.foundation.layout.ColumnScope.() -> Unit,
) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.padding(spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = title, style = typography.sm, color = tokens.mutedForeground)
            content()
        }
    }
}

@Composable
private fun ProfileFact(label: String, value: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.SpaceBetween) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        Text(text = value, style = typography.sm, color = tokens.foreground, maxLines = 1, overflow = TextOverflow.Ellipsis)
    }
}

/** Format a [Double] to one decimal place without JVM-only String.format (mirrors the old Community dialog). */
private fun Double.toFixed1(): String {
    val scaled: Long = (this * 10).toLong()
    return "${scaled / 10}.${kotlin.math.abs(scaled % 10)}"
}
