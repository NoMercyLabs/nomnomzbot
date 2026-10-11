// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.obs.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.RevealableSecretField
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.feature.obs.state.ObsController
import bot.nomnomz.dashboard.feature.obs.state.ObsUiState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.obs_bridge_copied
import nomnomzbot.composeapp.generated.resources.obs_bridge_copy
import nomnomzbot.composeapp.generated.resources.obs_bridge_desc
import nomnomzbot.composeapp.generated.resources.obs_bridge_offline
import nomnomzbot.composeapp.generated.resources.obs_bridge_online
import nomnomzbot.composeapp.generated.resources.obs_bridge_rotate
import nomnomzbot.composeapp.generated.resources.obs_bridge_title
import nomnomzbot.composeapp.generated.resources.obs_bridge_url_label
import nomnomzbot.composeapp.generated.resources.obs_clear_password
import nomnomzbot.composeapp.generated.resources.obs_connection_desc
import nomnomzbot.composeapp.generated.resources.obs_connection_title
import nomnomzbot.composeapp.generated.resources.obs_direct_remote_warning
import nomnomzbot.composeapp.generated.resources.obs_enabled_label
import nomnomzbot.composeapp.generated.resources.obs_host_label
import nomnomzbot.composeapp.generated.resources.obs_mode_bridge
import nomnomzbot.composeapp.generated.resources.obs_mode_direct
import nomnomzbot.composeapp.generated.resources.obs_mode_label
import nomnomzbot.composeapp.generated.resources.obs_password_hint
import nomnomzbot.composeapp.generated.resources.obs_password_label
import nomnomzbot.composeapp.generated.resources.obs_password_stored
import nomnomzbot.composeapp.generated.resources.obs_port_label
import nomnomzbot.composeapp.generated.resources.obs_save
import nomnomzbot.composeapp.generated.resources.obs_status_disabled
import nomnomzbot.composeapp.generated.resources.obs_status_enabled
import nomnomzbot.composeapp.generated.resources.obs_status_error
import org.jetbrains.compose.resources.stringResource

@Composable
fun ObsConnectionCard(controller: ObsController, role: ManagementRole?, backendOrigin: String? = null) {
    val state: ObsUiState by controller.state.collectAsStateWithLifecycle()
    val scope: CoroutineScope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.ObsLiveControl)

    // Direct mode dials the bot SERVER's own machine, so it only works when OBS runs beside the bot (self-host).
    // A remote or public server can never reach the user's OBS, so they must use the browser-source bridge.
    val isRemoteDeployment: Boolean = remember(backendOrigin) { !isLocalOrigin(backendOrigin) }

    LaunchedEffect(Unit) { controller.load() }

    val current: ObsUiState = state
    if (current is ObsUiState.Ready) {
        Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
            ConnectionCard(
                connection = current.connection,
                manage = manage,
                isRemoteDeployment = isRemoteDeployment,
                onSave = { mode, host, port, password, enabled ->
                    controller.saveConnection(mode, host, port, password, enabled)
                },
                onClearPassword = { scope.launch { controller.clearPassword() } },
            )
            BridgeCard(
                bridgeUrl = current.bridgeSetup?.bridgeUrl,
                instanceCount = current.bridgeStatus?.instanceCount ?: 0,
                online = (current.bridgeStatus?.instanceCount ?: 0) > 0,
                manage = manage,
                onRotate = { scope.launch { controller.rotateBridgeToken() } },
            )
        }
    }
}

@Composable
private fun ConnectionCard(
    connection: ObsConnection,
    manage: ManageDecision,
    isRemoteDeployment: Boolean,
    onSave: suspend (mode: String, host: String?, port: Int?, password: String?, enabled: Boolean) -> ApiResult<Unit>,
    onClearPassword: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope: CoroutineScope = rememberCoroutineScope()

    // Save waits for the server: pending disables the button (no second write), a failure keeps every typed
    // value and shows the reason next to Save.
    var saving: Boolean by remember { mutableStateOf(false) }
    var saveError: String? by remember { mutableStateOf(null) }

    var mode: String by remember(connection.mode) { mutableStateOf(connection.mode) }
    var host: String by remember(connection.host) { mutableStateOf(connection.host.orEmpty()) }
    var port: String by remember(connection.port) { mutableStateOf(connection.port?.toString().orEmpty()) }
    var password: String by remember(connection.hasPassword) { mutableStateOf("") }
    var enabled: Boolean by remember(connection.isEnabled) { mutableStateOf(connection.isEnabled) }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.obs_connection_title),
                description = stringResource(Res.string.obs_connection_desc),
                trailing = { StatusChip(connection = connection) },
            )

            // Mode: direct (bot opens the socket) vs bridge (a browser source relays through the bot).
            Text(text = stringResource(Res.string.obs_mode_label), style = typography.sm, color = tokens.mutedForeground)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ModeChip(label = stringResource(Res.string.obs_mode_direct), selected = mode == "direct", onClick = { mode = "direct" })
                ModeChip(label = stringResource(Res.string.obs_mode_bridge), selected = mode == "bridge", onClick = { mode = "bridge" })
            }

            // Direct mode opens the socket from the BOT SERVER, so it only reaches OBS when OBS runs on that same
            // machine. When this dashboard talks to a remote/hosted server, direct can never reach the user's PC —
            // steer them to the browser-source bridge instead of leaving them to wonder why it never connects.
            if (mode == "direct" && isRemoteDeployment) {
                Text(
                    text = stringResource(Res.string.obs_direct_remote_warning),
                    style = typography.sm,
                    color = tokens.destructiveForeground,
                )
            }

            // Host + port are the SERVER's dial target — direct mode only (in bridge mode the browser source
            // dials its own localhost, so these don't apply).
            if (mode == "direct") {
                AppTextField(
                    value = host,
                    onValueChange = { host = it },
                    label = stringResource(Res.string.obs_host_label),
                    modifier = Modifier.fillMaxWidth(),
                )
                AppTextField(
                    value = port,
                    onValueChange = { new -> port = new.filter { it.isDigit() } },
                    label = stringResource(Res.string.obs_port_label),
                    modifier = Modifier.fillMaxWidth(),
                )
            }

            // OBS-WebSocket password — needed in BOTH modes. Direct opens the socket server-side with it; bridge
            // has it delivered to the browser source so its LOCAL OBS Identify authenticates. Modern OBS enables
            // auth with a generated password by default, so a blank password fails control on most setups.
            RevealableSecretField(
                value = password,
                onValueChange = { password = it },
                label = stringResource(Res.string.obs_password_label),
                supportingText =
                    if (connection.hasPassword) stringResource(Res.string.obs_password_stored)
                    else stringResource(Res.string.obs_password_hint),
                modifier = Modifier.fillMaxWidth(),
            )
            if (connection.hasPassword) {
                ManageGate(decision = manage) { gateEnabled ->
                    TextButton(onClick = onClearPassword, enabled = gateEnabled) {
                        Text(
                            text = stringResource(Res.string.obs_clear_password),
                            color = if (gateEnabled) tokens.destructive else tokens.mutedForeground,
                        )
                    }
                }
            }

            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(text = stringResource(Res.string.obs_enabled_label), color = tokens.cardForeground)
                ManageGate(decision = manage) { gateEnabled ->
                    Switch(checked = enabled, onCheckedChange = { enabled = it }, enabled = gateEnabled)
                }
            }

            ManageGate(decision = manage) { gateEnabled ->
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                ) {
                    Button(
                        onClick = {
                            saving = true
                            saveError = null
                            scope.launch {
                                val result: ApiResult<Unit> =
                                    onSave(
                                        mode,
                                        host.ifBlank { null },
                                        port.toIntOrNull(),
                                        password.ifBlank { null },
                                        enabled,
                                    )
                                saving = false
                                if (result is ApiResult.Failure) saveError = result.error.message
                            }
                        },
                        enabled = gateEnabled && !saving,
                        loading = saving,
                    ) {
                        Text(text = stringResource(Res.string.obs_save))
                    }
                    saveError?.let { detail ->
                        Text(text = detail, style = typography.sm, color = tokens.destructive)
                    }
                }
            }
        }
    }
}

@Composable
private fun BridgeCard(
    bridgeUrl: String?,
    instanceCount: Int,
    online: Boolean,
    manage: ManageDecision,
    onRotate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.obs_bridge_title),
                description = stringResource(Res.string.obs_bridge_desc),
                trailing = { LiveDot(online = online) },
            )

            if (!bridgeUrl.isNullOrBlank()) {
                Text(text = stringResource(Res.string.obs_bridge_url_label), style = typography.sm, color = tokens.mutedForeground)
                Text(
                    text = bridgeUrl,
                    style = typography.sm,
                    color = tokens.cardForeground,
                    maxLines = 2,
                    overflow = TextOverflow.Ellipsis,
                )
                CopyValue(
                    value = bridgeUrl,
                    copyLabel = stringResource(Res.string.obs_bridge_copy),
                    copiedLabel = stringResource(Res.string.obs_bridge_copied),
                )
            }

            Text(
                text =
                    if (online) stringResource(Res.string.obs_bridge_online, instanceCount)
                    else stringResource(Res.string.obs_bridge_offline),
                style = typography.xs,
                color = if (online) tokens.mutedForeground else tokens.destructiveForeground,
            )

            ManageGate(decision = manage) { gateEnabled ->
                Button(onClick = onRotate, enabled = gateEnabled, variant = ButtonVariant.Outline) {
                    Text(text = stringResource(Res.string.obs_bridge_rotate))
                }
            }
        }
    }
}

@Composable
private fun StatusChip(connection: ObsConnection) {
    val tokens = LocalTokens.current
    when {
        connection.lastError != null ->
            Badge(variant = BadgeVariant.Destructive) {
                Text(text = stringResource(Res.string.obs_status_error, connection.lastError), maxLines = 1)
            }
        connection.isEnabled ->
            Badge(variant = BadgeVariant.Default) { Text(text = stringResource(Res.string.obs_status_enabled)) }
        else ->
            Badge(variant = BadgeVariant.Secondary) {
                Text(text = stringResource(Res.string.obs_status_disabled), color = tokens.mutedForeground)
            }
    }
}

@Composable
private fun ModeChip(label: String, selected: Boolean, onClick: () -> Unit) {
    Badge(
        variant = if (selected) BadgeVariant.Default else BadgeVariant.Outline,
        selected = selected,
        onClick = onClick,
    ) {
        Text(text = label, maxLines = 1)
    }
}

@Composable
private fun LiveDot(online: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    Box(
        modifier = Modifier
            .size(spacing.s2)
            .clip(CircleShape)
            .background(if (online) tokens.success else tokens.mutedForeground)
            .semantics { contentDescription = if (online) "online" else "offline" },
    )
}

// A backend origin counts as "local" (a self-host deployment where direct mode can reach a co-located OBS) when
// it targets loopback, a private LAN range (RFC 1918), or an .local host. A null/blank origin is treated as local
// (desktop dev / not yet resolved) so the direct-mode remote warning only fires when we actually KNOW the server
// is remote/public — anything else (a public domain) is remote.
private fun isLocalOrigin(origin: String?): Boolean {
    if (origin.isNullOrBlank()) return true
    val authority: String =
        origin.substringAfter("://", origin).substringBefore('/').trim().lowercase()
    // IPv6 loopback appears bracketed in a URL, e.g. http://[::1]:5080.
    if (authority.startsWith("[")) return authority.startsWith("[::1]") || authority.startsWith("[::]")
    val host: String = authority.substringBefore(':')
    if (host.isEmpty()) return true
    if (host == "localhost" || host == "127.0.0.1" || host == "0.0.0.0") return true
    if (host.endsWith(".local") || host.endsWith(".localhost")) return true
    if (host.startsWith("10.") || host.startsWith("192.168.")) return true
    if (host.startsWith("172.")) {
        val second: Int? = host.split('.').getOrNull(1)?.toIntOrNull()
        return second != null && second in 16..31
    }
    return false
}
