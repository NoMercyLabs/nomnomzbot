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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.background
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
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.RevealableSecretField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.feature.obs.state.ObsController
import bot.nomnomz.dashboard.feature.obs.state.ObsLive
import bot.nomnomz.dashboard.feature.obs.state.ObsUiState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.obs_action_error
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
import nomnomzbot.composeapp.generated.resources.obs_control_desc
import nomnomzbot.composeapp.generated.resources.obs_control_title
import nomnomzbot.composeapp.generated.resources.obs_current_scene
import nomnomzbot.composeapp.generated.resources.obs_enabled_label
import nomnomzbot.composeapp.generated.resources.obs_error
import nomnomzbot.composeapp.generated.resources.obs_host_label
import nomnomzbot.composeapp.generated.resources.obs_loading
import nomnomzbot.composeapp.generated.resources.obs_mode_bridge
import nomnomzbot.composeapp.generated.resources.obs_mode_direct
import nomnomzbot.composeapp.generated.resources.obs_mode_label
import nomnomzbot.composeapp.generated.resources.obs_no_scenes
import nomnomzbot.composeapp.generated.resources.obs_not_reachable
import nomnomzbot.composeapp.generated.resources.obs_not_reachable_detail
import nomnomzbot.composeapp.generated.resources.obs_password_label
import nomnomzbot.composeapp.generated.resources.obs_password_stored
import nomnomzbot.composeapp.generated.resources.obs_port_label
import nomnomzbot.composeapp.generated.resources.obs_recording_start
import nomnomzbot.composeapp.generated.resources.obs_recording_stop
import nomnomzbot.composeapp.generated.resources.obs_retry
import nomnomzbot.composeapp.generated.resources.obs_save
import nomnomzbot.composeapp.generated.resources.obs_scenes_label
import nomnomzbot.composeapp.generated.resources.obs_status_disabled
import nomnomzbot.composeapp.generated.resources.obs_status_enabled
import nomnomzbot.composeapp.generated.resources.obs_status_error
import nomnomzbot.composeapp.generated.resources.obs_streaming_start
import nomnomzbot.composeapp.generated.resources.obs_streaming_stop
import nomnomzbot.composeapp.generated.resources.obs_subtitle
import nomnomzbot.composeapp.generated.resources.shell_nav_obs
import org.jetbrains.compose.resources.stringResource

// The OBS-control page (frontend-ia.md, Connect group): the channel's OBS WebSocket connection config, the
// browser-source bridge, and live scene/output control (obs-control.md §4/§5). A pure projection of
// [ObsController]. Config writes gate at the page's Broadcaster manage floor; scene switching at Moderator
// (obs:control); streaming/recording at Broadcaster (obs:control:broadcast). The `/obs-bridge` browser-source
// page itself is a server-served static asset (see the for-backend handoff entry) — not built here.
@Composable
fun ObsScreen(controller: ObsController, role: ManagementRole?) {
    val state: ObsUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Config writes = the page's own manage floor (Broadcaster). Scene switching = Moderator (obs:control).
    // Streaming/recording = Broadcaster (obs:control:broadcast) — the two named sub-floors gate at explicit floors.
    val configManage: ManageDecision = rememberManageDecision(role, ShellRoute.Obs)
    val controlManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Moderator)
    val broadcastManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Broadcaster)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: ObsUiState = state) {
            is ObsUiState.Loading -> CenteredMessage(stringResource(Res.string.obs_loading))
            is ObsUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is ObsUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.shell_nav_obs),
                        subtitle = stringResource(Res.string.obs_subtitle),
                    )
                    current.actionError?.let {
                        ActionErrorBanner(message = stringResource(Res.string.obs_action_error, it))
                    }
                    ConnectionCard(
                        connection = current.connection,
                        manage = configManage,
                        onSave = { mode, host, port, password, enabled ->
                            scope.launch { controller.saveConnection(mode, host, port, password, enabled) }
                        },
                        onClearPassword = {
                            scope.launch {
                                controller.saveConnection(
                                    mode = current.connection.mode,
                                    host = current.connection.host,
                                    port = current.connection.port,
                                    password = "",
                                    isEnabled = current.connection.isEnabled,
                                )
                            }
                        },
                    )
                    BridgeCard(
                        bridgeUrl = current.bridgeSetup?.bridgeUrl,
                        instanceCount = current.bridgeStatus?.instanceCount ?: 0,
                        online = (current.bridgeStatus?.instanceCount ?: 0) > 0,
                        manage = configManage,
                        onRotate = { scope.launch { controller.rotateBridgeToken() } },
                    )
                    ControlCard(
                        live = current.live,
                        controlManage = controlManage,
                        broadcastManage = broadcastManage,
                        onSwitchScene = { scene -> scope.launch { controller.switchScene(scene) } },
                        onToggleStreaming = { scope.launch { controller.toggleStreaming() } },
                        onToggleRecording = { scope.launch { controller.toggleRecording() } },
                        onRefresh = { scope.launch { controller.refreshLive() } },
                    )
                }
        }
    }
}

@Composable
private fun ConnectionCard(
    connection: ObsConnection,
    manage: ManageDecision,
    onSave: (mode: String, host: String?, port: Int?, password: String?, enabled: Boolean) -> Unit,
    onClearPassword: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

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
                RevealableSecretField(
                    value = password,
                    onValueChange = { password = it },
                    label = stringResource(Res.string.obs_password_label),
                    supportingText =
                        if (connection.hasPassword) stringResource(Res.string.obs_password_stored) else null,
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
                Button(
                    onClick = {
                        onSave(
                            mode,
                            host.ifBlank { null },
                            port.toIntOrNull(),
                            password.ifBlank { null },
                            enabled,
                        )
                    },
                    enabled = gateEnabled,
                ) {
                    Text(text = stringResource(Res.string.obs_save))
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
private fun ControlCard(
    live: ObsLive,
    controlManage: ManageDecision,
    broadcastManage: ManageDecision,
    onSwitchScene: (String) -> Unit,
    onToggleStreaming: () -> Unit,
    onToggleRecording: () -> Unit,
    onRefresh: () -> Unit,
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
                title = stringResource(Res.string.obs_control_title),
                description = stringResource(Res.string.obs_control_desc),
                trailing = {
                    TextButton(onClick = onRefresh) {
                        Text(text = stringResource(Res.string.obs_retry), color = tokens.primary)
                    }
                },
            )

            if (!live.reachable) {
                Text(text = stringResource(Res.string.obs_not_reachable), style = typography.base, color = tokens.cardForeground)
                live.error?.let {
                    Text(
                        text = stringResource(Res.string.obs_not_reachable_detail, it),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
                return@Column
            }

            Text(
                text = stringResource(Res.string.obs_current_scene, live.state.currentScene ?: "—"),
                style = typography.sm,
                color = tokens.cardForeground,
            )

            Separator()

            Text(text = stringResource(Res.string.obs_scenes_label), style = typography.sm, color = tokens.mutedForeground)
            if (live.scenes.isEmpty()) {
                Text(text = stringResource(Res.string.obs_no_scenes), style = typography.sm, color = tokens.mutedForeground)
            } else {
                ManageGate(decision = controlManage) { gateEnabled ->
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        live.scenes.forEach { scene ->
                            Badge(
                                variant = if (scene.isCurrent) BadgeVariant.Default else BadgeVariant.Outline,
                                selected = scene.isCurrent,
                                enabled = gateEnabled,
                                onClick = if (gateEnabled) ({ onSwitchScene(scene.name) }) else null,
                            ) {
                                Text(text = scene.name, maxLines = 1)
                            }
                        }
                    }
                }
            }

            Separator()

            // Streaming / recording — broadcast keys, gated at Broadcaster.
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ManageGate(decision = broadcastManage) { gateEnabled ->
                    Button(
                        onClick = onToggleStreaming,
                        enabled = gateEnabled,
                        variant = if (live.state.streaming) ButtonVariant.Destructive else ButtonVariant.Default,
                    ) {
                        Text(
                            text =
                                if (live.state.streaming) stringResource(Res.string.obs_streaming_stop)
                                else stringResource(Res.string.obs_streaming_start)
                        )
                    }
                }
                ManageGate(decision = broadcastManage) { gateEnabled ->
                    Button(
                        onClick = onToggleRecording,
                        enabled = gateEnabled,
                        variant = if (live.state.recording) ButtonVariant.Destructive else ButtonVariant.Outline,
                    ) {
                        Text(
                            text =
                                if (live.state.recording) stringResource(Res.string.obs_recording_stop)
                                else stringResource(Res.string.obs_recording_start)
                        )
                    }
                }
            }
        }
    }
}

// ── shared bits ────────────────────────────────────────────────────────────

@Composable
private fun SectionHeader(title: String, description: String, trailing: @Composable () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Text(text = title, style = typography.lg, color = tokens.cardForeground)
            Text(text = description, style = typography.sm, color = tokens.mutedForeground)
        }
        trailing()
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
                text = stringResource(Res.string.obs_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.obs_retry)) }
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
