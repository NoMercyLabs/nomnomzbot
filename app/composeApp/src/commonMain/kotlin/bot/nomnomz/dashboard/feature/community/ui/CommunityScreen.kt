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
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.role
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.CommunityTrustLevel
import bot.nomnomz.dashboard.feature.community.state.CommunityController
import bot.nomnomz.dashboard.feature.community.state.CommunityState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.community_action_error
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
import nomnomzbot.composeapp.generated.resources.community_unban_confirm
import nomnomzbot.composeapp.generated.resources.community_unban_dismiss
import nomnomzbot.composeapp.generated.resources.community_unban_message
import nomnomzbot.composeapp.generated.resources.community_unban_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The Community page (frontend-ia.md §3): the channel's viewers — every member is real data from
// [CommunityController] (the backend sources it from the Twitch API + chat history). The screen is a pure
// projection of the controller's state; it loads on first composition and offers a retry on failure. Each
// member is actionable: a trust-level picker that acts directly (non-destructive) and a Ban / Unban
// affordance that lifts/applies a ban only once the moderator confirms it in the shared ConfirmDialog.
@Composable
fun CommunityScreen(controller: CommunityController) {
    val state: CommunityState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: CommunityState = state) {
            is CommunityState.Loading -> CenteredMessage(stringResource(Res.string.community_loading))
            is CommunityState.Empty -> CenteredMessage(stringResource(Res.string.community_empty))
            is CommunityState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is CommunityState.Ready ->
                MemberList(
                    members = current.members,
                    actionError = current.actionError,
                    onSetTrust = { userId, level -> scope.launch { controller.setTrust(userId, level) } },
                    onBan = { userId, reason -> scope.launch { controller.ban(userId, reason) } },
                    onUnban = { userId -> scope.launch { controller.unban(userId) } },
                )
        }
    }
}

@Composable
private fun MemberList(
    members: List<CommunityMember>,
    actionError: String?,
    onSetTrust: (userId: String, level: String) -> Unit,
    onBan: (userId: String, reason: String) -> Unit,
    onUnban: (userId: String) -> Unit,
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
        actionError?.let { detail ->
            item(key = "action-error") {
                Text(
                    text = stringResource(Res.string.community_action_error, detail),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s1),
                )
            }
        }
        items(items = members, key = { member -> member.id }) { member ->
            MemberRow(
                member = member,
                onSetTrust = { level -> onSetTrust(member.id, level) },
                onBan = { pendingBan = member },
                onUnban = { pendingUnban = member },
            )
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
    onSetTrust: (level: String) -> Unit,
    onBan: () -> Unit,
    onUnban: () -> Unit,
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
        TrustPicker(name = name, current = member.trustLevel, onSelect = onSetTrust)
        if (member.isBanned) {
            UnbanButton(name = name, onUnban = onUnban)
        } else {
            BanButton(name = name, onBan = onBan)
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
    onSelect: (level: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    val activeLabel: String = stringResource(trustLabel(current))
    val pickerLabel: String = stringResource(Res.string.community_trust_picker, name, activeLabel)

    Box {
        TextButton(
            onClick = { expanded = true },
            modifier = Modifier.semantics { contentDescription = pickerLabel },
        ) {
            Text(text = activeLabel, style = typography.sm, color = tokens.primary, maxLines = 1)
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
private fun BanButton(name: String, onBan: () -> Unit) {
    val tokens = LocalTokens.current
    val banLabel: String = stringResource(Res.string.community_ban_action, name)

    TextButton(
        onClick = onBan,
        modifier = Modifier.clearAndSetSemantics {
            role = Role.Button
            contentDescription = banLabel
        },
    ) {
        Text(
            text = stringResource(Res.string.community_ban_action_short),
            color = tokens.destructive,
            maxLines = 1,
        )
    }
}

@Composable
private fun UnbanButton(name: String, onUnban: () -> Unit) {
    val tokens = LocalTokens.current
    val unbanLabel: String = stringResource(Res.string.community_unban_action, name)

    TextButton(
        onClick = onUnban,
        modifier = Modifier.clearAndSetSemantics {
            role = Role.Button
            contentDescription = unbanLabel
        },
    ) {
        Text(
            text = stringResource(Res.string.community_unban_action_short),
            color = tokens.primary,
            maxLines = 1,
        )
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
