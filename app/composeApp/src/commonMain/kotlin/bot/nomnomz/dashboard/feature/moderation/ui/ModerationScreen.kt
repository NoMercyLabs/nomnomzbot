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
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.feature.moderation.state.ModerationController
import bot.nomnomz.dashboard.feature.moderation.state.ModerationState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.moderation_action_error
import nomnomzbot.composeapp.generated.resources.moderation_bans_title
import nomnomzbot.composeapp.generated.resources.moderation_log_by
import nomnomzbot.composeapp.generated.resources.moderation_log_row_description
import nomnomzbot.composeapp.generated.resources.moderation_log_title
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable
import nomnomzbot.composeapp.generated.resources.moderation_shield_disable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable
import nomnomzbot.composeapp.generated.resources.moderation_shield_enable_action
import nomnomzbot.composeapp.generated.resources.moderation_shield_title
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove
import nomnomzbot.composeapp.generated.resources.moderation_terms_remove_action
import nomnomzbot.composeapp.generated.resources.moderation_terms_title
import nomnomzbot.composeapp.generated.resources.moderation_banned_by
import nomnomzbot.composeapp.generated.resources.moderation_banned_on
import nomnomzbot.composeapp.generated.resources.moderation_empty
import nomnomzbot.composeapp.generated.resources.moderation_error
import nomnomzbot.composeapp.generated.resources.moderation_loading
import nomnomzbot.composeapp.generated.resources.moderation_no_reason
import nomnomzbot.composeapp.generated.resources.moderation_retry
import nomnomzbot.composeapp.generated.resources.moderation_unban_action
import nomnomzbot.composeapp.generated.resources.moderation_unban_action_short
import nomnomzbot.composeapp.generated.resources.moderation_unban_confirm
import nomnomzbot.composeapp.generated.resources.moderation_unban_dismiss
import nomnomzbot.composeapp.generated.resources.moderation_unban_message
import nomnomzbot.composeapp.generated.resources.moderation_unban_title
import org.jetbrains.compose.resources.stringResource

// The Moderation page: the channel's currently-banned viewers, all real data from [ModerationController].
// The screen is a pure projection of the controller's state; it loads on first composition and offers a
// retry on failure. Each ban is actionable — an Unban affordance that, only once the moderator confirms it
// in the shared ConfirmDialog, lifts the ban via the controller (which reloads the list on success).
@Composable
fun ModerationScreen(controller: ModerationController, role: ManagementRole?) {
    val state: ModerationState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: Moderation gates its write affordance at its single Moderator manage
    // floor (frontend-ia.md §3). A caller below it sees the ban list but the Unban control is disabled with
    // "Requires Moderator" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Moderation)

    LaunchedEffect(Unit) { controller.load() }

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
                    actionError = current.actionError,
                    manage = manage,
                    onUnban = { userId -> scope.launch { controller.unban(userId) } },
                    onToggleShield = { on -> scope.launch { controller.setShieldMode(on) } },
                    onRemoveTerm = { term -> scope.launch { controller.removeBlockedTerm(term) } },
                )
        }
    }
}

@Composable
private fun BansList(
    bans: List<BannedUser>,
    modLog: List<ModLogEntry>,
    shieldEnabled: Boolean,
    blockedTerms: List<String>,
    actionError: String?,
    manage: ManageDecision,
    onUnban: (userId: String) -> Unit,
    onToggleShield: (Boolean) -> Unit,
    onRemoveTerm: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The ban awaiting confirmation, if any — the screen owns the dialog's open/closed state.
    var pendingUnban: BannedUser? by remember { mutableStateOf(null) }

    LazyColumn(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        actionError?.let { detail ->
            item(key = "unban-error") {
                Text(
                    text = stringResource(Res.string.moderation_action_error, detail),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
                )
            }
        }
        item(key = "shield-toggle") {
            ShieldToggle(enabled = shieldEnabled, manage = manage, onToggle = onToggleShield)
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
            items(items = bans, key = { it.id }) { ban ->
                BanRow(ban = ban, manage = manage, onUnban = { pendingUnban = ban })
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
            items(items = modLog, key = { "log-${it.id}" }) { entry -> ModLogRow(entry = entry) }
        }
        if (blockedTerms.isNotEmpty()) {
            item(key = "terms-header") {
                Text(
                    text = stringResource(Res.string.moderation_terms_title),
                    style = typography.lg,
                    color = tokens.cardForeground,
                    maxLines = 1,
                )
            }
            items(items = blockedTerms, key = { "term-$it" }) { term ->
                BlockedTermRow(term = term, manage = manage, onRemove = { onRemoveTerm(term) })
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
}

@Composable
private fun BanRow(ban: BannedUser, manage: ManageDecision, onUnban: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val name: String = ban.displayName.takeIf { it.isNotBlank() } ?: ban.username
    val reason: String =
        ban.reason.takeIf { it.isNotBlank() } ?: stringResource(Res.string.moderation_no_reason)
    val bannedOn: String? = ban.bannedAt.takeIf { it.isNotBlank() }?.let { datePart(it) }
    val unbanLabel: String = stringResource(Res.string.moderation_unban_action, name)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
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
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
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
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
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
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
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
