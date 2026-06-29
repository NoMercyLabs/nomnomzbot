// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.federation.ui

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
import androidx.compose.material3.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.Button
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
import androidx.compose.material3.Text
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.icon.AddGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.RemoveGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.FederatedOptIn
import bot.nomnomz.dashboard.core.network.FederatedPeer
import bot.nomnomz.dashboard.feature.federation.state.FederationController
import bot.nomnomz.dashboard.feature.federation.state.FederationState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.federation_action_error
import nomnomzbot.composeapp.generated.resources.federation_error
import nomnomzbot.composeapp.generated.resources.federation_loading
import nomnomzbot.composeapp.generated.resources.federation_optin_add
import nomnomzbot.composeapp.generated.resources.federation_optin_capability
import nomnomzbot.composeapp.generated.resources.federation_optin_capability_required
import nomnomzbot.composeapp.generated.resources.federation_optin_confirm
import nomnomzbot.composeapp.generated.resources.federation_optin_dismiss
import nomnomzbot.composeapp.generated.resources.federation_optin_peer_required
import nomnomzbot.composeapp.generated.resources.federation_optin_peerid
import nomnomzbot.composeapp.generated.resources.federation_optin_remove_cancel
import nomnomzbot.composeapp.generated.resources.federation_optin_remove_confirm
import nomnomzbot.composeapp.generated.resources.federation_optin_remove_message
import nomnomzbot.composeapp.generated.resources.federation_optin_remove_title
import nomnomzbot.composeapp.generated.resources.federation_optin_title
import nomnomzbot.composeapp.generated.resources.federation_peer_add
import nomnomzbot.composeapp.generated.resources.federation_peer_baseurl
import nomnomzbot.composeapp.generated.resources.federation_peer_baseurl_required
import nomnomzbot.composeapp.generated.resources.federation_peer_confirm
import nomnomzbot.composeapp.generated.resources.federation_peer_dismiss
import nomnomzbot.composeapp.generated.resources.federation_peer_name
import nomnomzbot.composeapp.generated.resources.federation_peer_name_required
import nomnomzbot.composeapp.generated.resources.federation_peer_revoke
import nomnomzbot.composeapp.generated.resources.federation_peer_revoke_cancel
import nomnomzbot.composeapp.generated.resources.federation_peer_revoke_confirm
import nomnomzbot.composeapp.generated.resources.federation_peer_revoke_message
import nomnomzbot.composeapp.generated.resources.federation_peer_revoke_title
import nomnomzbot.composeapp.generated.resources.federation_peer_status_revoked
import nomnomzbot.composeapp.generated.resources.federation_peer_status_trusted
import nomnomzbot.composeapp.generated.resources.federation_peer_status_untrusted
import nomnomzbot.composeapp.generated.resources.federation_peer_title
import nomnomzbot.composeapp.generated.resources.federation_peer_trust
import nomnomzbot.composeapp.generated.resources.federation_peer_title_dialog
import nomnomzbot.composeapp.generated.resources.federation_retry
import nomnomzbot.composeapp.generated.resources.federation_subtitle

import nomnomzbot.composeapp.generated.resources.shell_nav_federation
import org.jetbrains.compose.resources.stringResource

// The Federation page: global peer list and the channel's federated opt-in subscriptions. All data comes from
// [FederationController]; all mutations go back through it and reload on success.
@Composable
fun FederationScreen(controller: FederationController, role: ManagementRole?) {
    val state: FederationState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Federation)

    var showAddPeer: Boolean by remember { mutableStateOf(false) }
    var showAddOptIn: Boolean by remember { mutableStateOf(false) }
    var pendingRevoke: FederatedPeer? by remember { mutableStateOf(null) }
    var pendingRemoveOptIn: FederatedOptIn? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s6),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_federation), subtitle = stringResource(Res.string.federation_subtitle))

        when (val current: FederationState = state) {
            is FederationState.Loading -> CenteredMessage(stringResource(Res.string.federation_loading))
            is FederationState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is FederationState.Ready -> {
                current.actionError?.let { detail ->
                    ActionErrorBanner(message = stringResource(Res.string.federation_action_error, detail))
                }
                LazyColumn(
                    modifier = Modifier.fillMaxSize(),
                    verticalArrangement = Arrangement.spacedBy(spacing.s3),
                ) {
                    item(key = "peers-header") {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text(stringResource(Res.string.federation_peer_title), style = typography.lg, color = tokens.cardForeground)
                            ManageGate(manage) { enabled ->
                                GlyphButton(
                                    imageVector = AddGlyph,
                                    label = stringResource(Res.string.federation_peer_add),
                                    onClick = { showAddPeer = true },
                                    enabled = enabled,
                                )
                            }
                        }
                    }
                    items(items = current.peers, key = { "peer-${it.id}" }) { peer ->
                        PeerRow(
                            peer = peer,
                            manage = manage,
                            onTrust = { scope.launch { controller.trustPeer(peer.id) } },
                            onRevoke = { pendingRevoke = peer },
                        )
                    }
                    item(key = "optins-divider") { HorizontalDivider() }
                    item(key = "optins-header") {
                        Row(
                            modifier = Modifier.fillMaxWidth(),
                            horizontalArrangement = Arrangement.SpaceBetween,
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Text(stringResource(Res.string.federation_optin_title), style = typography.lg, color = tokens.cardForeground)
                            ManageGate(manage) { enabled ->
                                GlyphButton(
                                    imageVector = AddGlyph,
                                    label = stringResource(Res.string.federation_optin_add),
                                    onClick = { showAddOptIn = true },
                                    enabled = enabled,
                                )
                            }
                        }
                    }
                    items(items = current.optIns, key = { "optin-${it.id}" }) { optIn ->
                        OptInRow(
                            optIn = optIn,
                            manage = manage,
                            onToggle = { scope.launch { controller.upsertOptIn(optIn.peerId, optIn.capability, !optIn.isEnabled) } },
                            onRemove = { pendingRemoveOptIn = optIn },
                        )
                    }
                }
            }
        }
    }

    pendingRevoke?.let { peer ->
        ConfirmDialog(
            title = stringResource(Res.string.federation_peer_revoke_title),
            message = stringResource(Res.string.federation_peer_revoke_message, peer.name),
            confirmLabel = stringResource(Res.string.federation_peer_revoke_confirm),
            dismissLabel = stringResource(Res.string.federation_peer_revoke_cancel),
            destructive = true,
            onConfirm = { pendingRevoke = null; scope.launch { controller.revokePeer(peer.id) } },
            onDismiss = { pendingRevoke = null },
        )
    }

    pendingRemoveOptIn?.let { optIn ->
        ConfirmDialog(
            title = stringResource(Res.string.federation_optin_remove_title),
            message = stringResource(Res.string.federation_optin_remove_message, optIn.capability, optIn.peerName),
            confirmLabel = stringResource(Res.string.federation_optin_remove_confirm),
            dismissLabel = stringResource(Res.string.federation_optin_remove_cancel),
            destructive = true,
            onConfirm = { pendingRemoveOptIn = null; scope.launch { controller.removeOptIn(optIn.id) } },
            onDismiss = { pendingRemoveOptIn = null },
        )
    }

    if (showAddPeer) {
        AddPeerDialog(
            onConfirm = { name, url -> showAddPeer = false; scope.launch { controller.registerPeer(name, url) } },
            onDismiss = { showAddPeer = false },
        )
    }

    if (showAddOptIn) {
        AddOptInDialog(
            onConfirm = { peerId, capability -> showAddOptIn = false; scope.launch { controller.upsertOptIn(peerId, capability, true) } },
            onDismiss = { showAddOptIn = false },
        )
    }
}

@Composable
private fun PeerRow(
    peer: FederatedPeer,
    manage: ManageDecision,
    onTrust: () -> Unit,
    onRevoke: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusLabel: String = stringResource(
        when {
            peer.isRevoked -> Res.string.federation_peer_status_revoked
            peer.isTrusted -> Res.string.federation_peer_status_trusted
            else -> Res.string.federation_peer_status_untrusted
        }
    )

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = peer.name, style = typography.base, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(text = peer.baseUrl, style = typography.xs, color = tokens.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            Text(
                text = statusLabel,
                style = typography.xs,
                color = when {
                    peer.isRevoked -> tokens.destructive
                    peer.isTrusted -> tokens.primary
                    else -> tokens.mutedForeground
                },
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
            if (!peer.isTrusted && !peer.isRevoked) {
                ManageGate(manage) { enabled ->
                    GlyphButton(
                        imageVector = CheckCircleGlyph,
                        label = stringResource(Res.string.federation_peer_trust),
                        onClick = onTrust,
                        enabled = enabled,
                        tint = tokens.primary,
                    )
                }
            }
            if (!peer.isRevoked) {
                ManageGate(manage) { enabled ->
                    GlyphButton(
                        imageVector = RemoveGlyph,
                        label = stringResource(Res.string.federation_peer_revoke),
                        onClick = onRevoke,
                        enabled = enabled,
                        tint = tokens.destructive,
                    )
                }
            }
        }
    }
}

@Composable
private fun OptInRow(
    optIn: FederatedOptIn,
    manage: ManageDecision,
    onToggle: () -> Unit,
    onRemove: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = optIn.capability, style = typography.base, color = tokens.cardForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
                Text(text = optIn.peerName, style = typography.xs, color = tokens.mutedForeground, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
            ManageGate(manage) { enabled ->
                Switch(
                    checked = optIn.isEnabled,
                    onCheckedChange = { onToggle() },
                    enabled = enabled,
                    colors = SwitchDefaults.colors(
                        checkedThumbColor = tokens.primaryForeground,
                        checkedTrackColor = tokens.primary,
                        uncheckedThumbColor = tokens.mutedForeground,
                        uncheckedTrackColor = tokens.muted,
                        uncheckedBorderColor = tokens.border,
                    ),
                )
            }
        }
        ManageGate(manage) { enabled ->
            GlyphButton(
                imageVector = TrashGlyph,
                label = stringResource(Res.string.federation_optin_remove_confirm),
                onClick = onRemove,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

@Composable
private fun AddPeerDialog(onConfirm: (name: String, url: String) -> Unit, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var name: String by remember { mutableStateOf("") }
    var url: String by remember { mutableStateOf("") }
    var nameError: Boolean by remember { mutableStateOf(false) }
    var urlError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.federation_peer_title_dialog), style = typography.lg, color = tokens.cardForeground) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = name, onValueChange = { name = it; nameError = false },
                    label = stringResource(Res.string.federation_peer_name),
                    isError = nameError,
                    errorText = if (nameError) stringResource(Res.string.federation_peer_name_required) else null,
                )
                AppTextField(
                    value = url, onValueChange = { url = it; urlError = false },
                    label = stringResource(Res.string.federation_peer_baseurl),
                    isError = urlError,
                    errorText = if (urlError) stringResource(Res.string.federation_peer_baseurl_required) else null,
                )
            }
        },
        confirmButton = {
            Button(onClick = {
                var valid: Boolean = true
                if (name.isBlank()) { nameError = true; valid = false }
                if (url.isBlank()) { urlError = true; valid = false }
                if (valid) onConfirm(name.trim(), url.trim())
            }) { Text(stringResource(Res.string.federation_peer_confirm)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.federation_peer_dismiss)) } },
        containerColor = tokens.card,
    )
}

@Composable
private fun AddOptInDialog(onConfirm: (peerId: String, capability: String) -> Unit, onDismiss: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var peerId: String by remember { mutableStateOf("") }
    var capability: String by remember { mutableStateOf("") }
    var peerError: Boolean by remember { mutableStateOf(false) }
    var capError: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(stringResource(Res.string.federation_optin_add), style = typography.lg, color = tokens.cardForeground) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                AppTextField(
                    value = peerId, onValueChange = { peerId = it; peerError = false },
                    label = stringResource(Res.string.federation_optin_peerid),
                    isError = peerError,
                    errorText = if (peerError) stringResource(Res.string.federation_optin_peer_required) else null,
                )
                AppTextField(
                    value = capability, onValueChange = { capability = it; capError = false },
                    label = stringResource(Res.string.federation_optin_capability),
                    isError = capError,
                    errorText = if (capError) stringResource(Res.string.federation_optin_capability_required) else null,
                )
            }
        },
        confirmButton = {
            Button(onClick = {
                var valid: Boolean = true
                if (peerId.isBlank()) { peerError = true; valid = false }
                if (capability.isBlank()) { capError = true; valid = false }
                if (valid) onConfirm(peerId.trim(), capability.trim())
            }) { Text(stringResource(Res.string.federation_optin_confirm)) }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(stringResource(Res.string.federation_optin_dismiss)) } },
        containerColor = tokens.card,
    )
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally, verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            Text(text = stringResource(Res.string.federation_error, detail), style = typography.base, color = tokens.mutedForeground, textAlign = TextAlign.Center)
            TextButton(onClick = onRetry) { Text(stringResource(Res.string.federation_retry)) }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Box(modifier = Modifier.fillMaxWidth(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
