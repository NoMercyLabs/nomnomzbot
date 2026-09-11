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
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.CopyValue
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.component.RevealableSecretField
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Slider
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ObsConnection
import bot.nomnomz.dashboard.core.network.ObsFilter
import bot.nomnomz.dashboard.core.network.ObsInput
import bot.nomnomz.dashboard.core.network.ObsMediaAction
import bot.nomnomz.dashboard.core.network.ObsScene
import bot.nomnomz.dashboard.core.network.ObsSceneItem
import bot.nomnomz.dashboard.core.network.ObsStats
import bot.nomnomz.dashboard.core.network.ObsTransition
import bot.nomnomz.dashboard.core.realtime.HubEvent
import bot.nomnomz.dashboard.feature.obs.state.ObsController
import bot.nomnomz.dashboard.feature.obs.state.ObsFiltersView
import bot.nomnomz.dashboard.feature.obs.state.ObsLive
import bot.nomnomz.dashboard.feature.obs.state.ObsSceneItemsView
import bot.nomnomz.dashboard.feature.obs.state.ObsUiState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.flow.SharedFlow
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
import nomnomzbot.composeapp.generated.resources.obs_browser_refresh
import nomnomzbot.composeapp.generated.resources.obs_clear_password
import nomnomzbot.composeapp.generated.resources.obs_connection_desc
import nomnomzbot.composeapp.generated.resources.obs_connection_title
import nomnomzbot.composeapp.generated.resources.obs_control_desc
import nomnomzbot.composeapp.generated.resources.obs_control_title
import nomnomzbot.composeapp.generated.resources.obs_current_scene
import nomnomzbot.composeapp.generated.resources.obs_direct_remote_warning
import nomnomzbot.composeapp.generated.resources.obs_enabled_label
import nomnomzbot.composeapp.generated.resources.obs_error
import nomnomzbot.composeapp.generated.resources.obs_filter_enabled_action
import nomnomzbot.composeapp.generated.resources.obs_filter_row_type
import nomnomzbot.composeapp.generated.resources.obs_filters_desc
import nomnomzbot.composeapp.generated.resources.obs_filters_empty
import nomnomzbot.composeapp.generated.resources.obs_filters_pick_source
import nomnomzbot.composeapp.generated.resources.obs_filters_title
import nomnomzbot.composeapp.generated.resources.obs_host_label
import nomnomzbot.composeapp.generated.resources.obs_loading
import nomnomzbot.composeapp.generated.resources.obs_media_next
import nomnomzbot.composeapp.generated.resources.obs_media_pause
import nomnomzbot.composeapp.generated.resources.obs_media_play
import nomnomzbot.composeapp.generated.resources.obs_media_previous
import nomnomzbot.composeapp.generated.resources.obs_media_restart
import nomnomzbot.composeapp.generated.resources.obs_media_stop
import nomnomzbot.composeapp.generated.resources.obs_mixer_db
import nomnomzbot.composeapp.generated.resources.obs_mixer_desc
import nomnomzbot.composeapp.generated.resources.obs_mixer_empty
import nomnomzbot.composeapp.generated.resources.obs_mixer_mute_action
import nomnomzbot.composeapp.generated.resources.obs_mixer_title
import nomnomzbot.composeapp.generated.resources.obs_mode_bridge
import nomnomzbot.composeapp.generated.resources.obs_mode_direct
import nomnomzbot.composeapp.generated.resources.obs_mode_label
import nomnomzbot.composeapp.generated.resources.obs_no_scenes
import nomnomzbot.composeapp.generated.resources.obs_not_reachable
import nomnomzbot.composeapp.generated.resources.obs_not_reachable_detail
import nomnomzbot.composeapp.generated.resources.obs_outputs_label
import nomnomzbot.composeapp.generated.resources.obs_password_hint
import nomnomzbot.composeapp.generated.resources.obs_password_label
import nomnomzbot.composeapp.generated.resources.obs_password_stored
import nomnomzbot.composeapp.generated.resources.obs_port_label
import nomnomzbot.composeapp.generated.resources.obs_recording_pause
import nomnomzbot.composeapp.generated.resources.obs_recording_resume
import nomnomzbot.composeapp.generated.resources.obs_recording_split
import nomnomzbot.composeapp.generated.resources.obs_recording_start
import nomnomzbot.composeapp.generated.resources.obs_recording_stop
import nomnomzbot.composeapp.generated.resources.obs_replay_buffer_save
import nomnomzbot.composeapp.generated.resources.obs_replay_buffer_start
import nomnomzbot.composeapp.generated.resources.obs_replay_buffer_stop
import nomnomzbot.composeapp.generated.resources.obs_retry
import nomnomzbot.composeapp.generated.resources.obs_save
import nomnomzbot.composeapp.generated.resources.obs_scene_item_row_type
import nomnomzbot.composeapp.generated.resources.obs_scene_item_visible_action
import nomnomzbot.composeapp.generated.resources.obs_scene_items_desc
import nomnomzbot.composeapp.generated.resources.obs_scene_items_empty
import nomnomzbot.composeapp.generated.resources.obs_scene_items_pick_scene
import nomnomzbot.composeapp.generated.resources.obs_scene_items_title
import nomnomzbot.composeapp.generated.resources.obs_input_row_type
import nomnomzbot.composeapp.generated.resources.obs_scene_row_type
import nomnomzbot.composeapp.generated.resources.obs_scenes_label
import nomnomzbot.composeapp.generated.resources.obs_sources_desc
import nomnomzbot.composeapp.generated.resources.obs_sources_empty
import nomnomzbot.composeapp.generated.resources.obs_sources_title
import nomnomzbot.composeapp.generated.resources.obs_stats_cpu
import nomnomzbot.composeapp.generated.resources.obs_stats_desc
import nomnomzbot.composeapp.generated.resources.obs_stats_fps
import nomnomzbot.composeapp.generated.resources.obs_stats_memory
import nomnomzbot.composeapp.generated.resources.obs_stats_output_frames
import nomnomzbot.composeapp.generated.resources.obs_stats_render_frames
import nomnomzbot.composeapp.generated.resources.obs_stats_title
import nomnomzbot.composeapp.generated.resources.obs_status_disabled
import nomnomzbot.composeapp.generated.resources.obs_status_enabled
import nomnomzbot.composeapp.generated.resources.obs_status_error
import nomnomzbot.composeapp.generated.resources.obs_streaming_start
import nomnomzbot.composeapp.generated.resources.obs_streaming_stop
import nomnomzbot.composeapp.generated.resources.obs_studio_mode_cut
import nomnomzbot.composeapp.generated.resources.obs_studio_mode_desc
import nomnomzbot.composeapp.generated.resources.obs_studio_mode_disabled_hint
import nomnomzbot.composeapp.generated.resources.obs_studio_mode_enabled_label
import nomnomzbot.composeapp.generated.resources.obs_studio_mode_preview_label
import nomnomzbot.composeapp.generated.resources.obs_studio_mode_title
import nomnomzbot.composeapp.generated.resources.obs_subtitle
import nomnomzbot.composeapp.generated.resources.obs_transition_row_type
import nomnomzbot.composeapp.generated.resources.obs_transitions_desc
import nomnomzbot.composeapp.generated.resources.obs_transitions_empty
import nomnomzbot.composeapp.generated.resources.obs_transitions_title
import nomnomzbot.composeapp.generated.resources.obs_virtual_cam_start
import nomnomzbot.composeapp.generated.resources.obs_virtual_cam_stop
import nomnomzbot.composeapp.generated.resources.shell_nav_obs
import org.jetbrains.compose.resources.stringResource

// The OBS-control page (frontend-ia.md, Connect group): the channel's OBS WebSocket connection config, the
// browser-source bridge, and live scene/output control (obs-control.md §4/§5). A pure projection of
// [ObsController]. Config writes gate at the page's Broadcaster manage floor; scene switching at Moderator
// (obs:control); streaming/recording at Broadcaster (obs:control:broadcast). The `/obs-bridge` browser-source
// page itself is a server-served static asset (see the for-backend handoff entry) — not built here.
@Composable
fun ObsScreen(
    controller: ObsController,
    role: ManagementRole?,
    hubEvents: SharedFlow<HubEvent>? = null,
    backendOrigin: String? = null,
) {
    val state: ObsUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // Config writes = the page's own manage floor (Broadcaster). Scene switching = Moderator (obs:control).
    // Streaming/recording = Broadcaster (obs:control:broadcast) — the two named sub-floors gate at explicit floors.
    val configManage: ManageDecision = rememberManageDecision(role, ShellRoute.Obs)
    val controlManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Moderator)
    val broadcastManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Broadcaster)

    // Direct mode dials the bot SERVER's own machine — so it only works when OBS runs beside the bot (self-host).
    // If this dashboard is talking to a remote/public server (hosted / SaaS), direct can never reach the user's
    // OBS, and they must use the browser-source bridge instead. Detect that from the backend origin.
    val isRemoteDeployment: Boolean = remember(backendOrigin) { !isLocalOrigin(backendOrigin) }

    LaunchedEffect(Unit) { controller.load() }
    if (hubEvents != null) {
        LaunchedEffect(hubEvents) { controller.subscribeToHub(hubEvents) }
    }

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: ObsUiState = state) {
            is ObsUiState.Loading -> CenteredMessage(stringResource(Res.string.obs_loading))
            is ObsUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is ObsUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s6),
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
                        isRemoteDeployment = isRemoteDeployment,
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
                        onPauseRecording = { scope.launch { controller.pauseRecording() } },
                        onResumeRecording = { scope.launch { controller.resumeRecording() } },
                        onSplitRecording = { scope.launch { controller.splitRecording() } },
                        onToggleReplayBuffer = { scope.launch { controller.toggleReplayBuffer() } },
                        onSaveReplayBuffer = { scope.launch { controller.saveReplayBuffer() } },
                        onToggleVirtualCam = { scope.launch { controller.toggleVirtualCam() } },
                        // The retry re-reads EVERYTHING (bridge status + setup + probe + live), not just live — a
                        // bridge that just came online is reflected here, not only after a full page reload.
                        onRefresh = { scope.launch { controller.refresh() } },
                    )
                    if (current.live.reachable) {
                        MixerCard(
                            live = current.live,
                            controlManage = controlManage,
                            onSetMute = { input, muted ->
                                scope.launch { controller.setInputMute(input, muted) }
                            },
                            onSetVolume = { input, volumeDb ->
                                scope.launch { controller.setInputVolume(input, volumeDb) }
                            },
                        )
                        StatsCard(stats = current.live.stats)
                        StudioModeCard(
                            studioModeEnabled = current.live.studioModeEnabled,
                            scenes = current.live.scenes,
                            controlManage = controlManage,
                            onSetStudioMode = { enabled -> scope.launch { controller.setStudioMode(enabled) } },
                            onSetPreviewScene = { scene -> scope.launch { controller.setPreviewScene(scene) } },
                            onCutToProgram = { scope.launch { controller.triggerStudioTransition() } },
                        )
                        TransitionsCard(
                            transitions = current.live.transitions,
                            controlManage = controlManage,
                            onSelectTransition = { name -> scope.launch { controller.setCurrentTransition(name) } },
                        )
                        SceneItemsCard(
                            scenes = current.live.scenes,
                            view = current.sceneItemsView,
                            controlManage = controlManage,
                            onPickScene = { scene -> scope.launch { controller.loadSceneItems(scene) } },
                            onSetVisible = { scene, source, visible ->
                                scope.launch { controller.setSourceVisible(scene, source, visible) }
                            },
                        )
                        SourceFiltersCard(
                            inputs = current.live.inputs,
                            sceneItemsView = current.sceneItemsView,
                            view = current.filtersView,
                            controlManage = controlManage,
                            onPickSource = { source -> scope.launch { controller.loadSourceFilters(source) } },
                            onSetFilterEnabled = { source, filter, enabled ->
                                scope.launch { controller.setFilterEnabled(source, filter, enabled) }
                            },
                        )
                        SourceActionsCard(
                            inputs = current.live.inputs,
                            controlManage = controlManage,
                            onTriggerMedia = { input, action ->
                                scope.launch { controller.triggerMedia(input, action) }
                            },
                            onRefreshBrowser = { input -> scope.launch { controller.refreshBrowserSource(input) } },
                        )
                    }
                }
        }
    }
}

@Composable
private fun ConnectionCard(
    connection: ObsConnection,
    manage: ManageDecision,
    isRemoteDeployment: Boolean,
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
    onPauseRecording: () -> Unit,
    onResumeRecording: () -> Unit,
    onSplitRecording: () -> Unit,
    onToggleReplayBuffer: () -> Unit,
    onSaveReplayBuffer: () -> Unit,
    onToggleVirtualCam: () -> Unit,
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
                val sceneTypeLabel: String = stringResource(Res.string.obs_scene_row_type)
                ManageGate(decision = controlManage) { gateEnabled ->
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        live.scenes.forEachIndexed { index, scene ->
                            val sceneLabel: String =
                                resolveRowLabel(
                                    primary = scene.name,
                                    typeLabel = sceneTypeLabel,
                                    discriminatorSource = "$index-${scene.name}",
                                )
                            Badge(
                                variant = if (scene.isCurrent) BadgeVariant.Default else BadgeVariant.Outline,
                                selected = scene.isCurrent,
                                enabled = gateEnabled,
                                onClick = if (gateEnabled) ({ onSwitchScene(scene.name) }) else null,
                            ) {
                                Text(text = sceneLabel, maxLines = 1)
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
                // Pause/resume/split only make sense once a recording is running; split additionally needs it
                // to not already be paused. Compact secondary buttons — recording start/stop above already
                // carries this row's one attention-grabbing (destructive-while-active) control.
                ManageGate(decision = broadcastManage) { gateEnabled ->
                    Button(
                        onClick = if (live.state.recordPaused) onResumeRecording else onPauseRecording,
                        enabled = gateEnabled && live.state.recording,
                        variant = ButtonVariant.Outline,
                        size = ButtonSize.Sm,
                    ) {
                        Text(
                            text =
                                if (live.state.recordPaused) stringResource(Res.string.obs_recording_resume)
                                else stringResource(Res.string.obs_recording_pause)
                        )
                    }
                }
                ManageGate(decision = broadcastManage) { gateEnabled ->
                    Button(
                        onClick = onSplitRecording,
                        enabled = gateEnabled && live.state.recording,
                        variant = ButtonVariant.Outline,
                        size = ButtonSize.Sm,
                    ) {
                        Text(text = stringResource(Res.string.obs_recording_split))
                    }
                }
            }

            Separator()

            // Replay buffer / virtual cam — neither is broadcast-impacting (nothing a viewer sees changes), so
            // both gate at the control floor and stay Outline throughout: streaming already carries the page's
            // one primary accent, and these are secondary controls next to it, never a second competing accent.
            // Virtual cam has no OBS-WS status query today, so it is a single stateless toggle, not a start/stop
            // pair like the others.
            Text(text = stringResource(Res.string.obs_outputs_label), style = typography.sm, color = tokens.mutedForeground)
            ManageGate(decision = controlManage) { gateEnabled ->
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    Button(
                        onClick = onToggleReplayBuffer,
                        enabled = gateEnabled,
                        variant = if (live.state.replayBufferActive) ButtonVariant.Destructive else ButtonVariant.Outline,
                    ) {
                        Text(
                            text =
                                if (live.state.replayBufferActive) stringResource(Res.string.obs_replay_buffer_stop)
                                else stringResource(Res.string.obs_replay_buffer_start)
                        )
                    }
                    Button(
                        onClick = onSaveReplayBuffer,
                        enabled = gateEnabled && live.state.replayBufferActive,
                        variant = ButtonVariant.Outline,
                    ) {
                        Text(text = stringResource(Res.string.obs_replay_buffer_save))
                    }
                    Button(
                        onClick = onToggleVirtualCam,
                        enabled = gateEnabled,
                        variant = if (live.virtualCamActive) ButtonVariant.Destructive else ButtonVariant.Outline,
                    ) {
                        Text(
                            text =
                                if (live.virtualCamActive) stringResource(Res.string.obs_virtual_cam_stop)
                                else stringResource(Res.string.obs_virtual_cam_start)
                        )
                    }
                }
            }
        }
    }
}

// The audio mixer: every OBS audio input with a mute toggle + a volume fader (dB), gated at Moderator
// (obs:control). Only inputs that actually expose mixer state (mute or volume) are shown — a browser/image source
// has no audio and is skipped. Live control of the connected OBS; nothing is persisted here.
@Composable
private fun MixerCard(
    live: ObsLive,
    controlManage: ManageDecision,
    onSetMute: (String, Boolean) -> Unit,
    onSetVolume: (String, Double) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val audioInputs: List<ObsInput> = live.inputs.filter { it.muted != null || it.volumeDb != null }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.obs_mixer_title),
                description = stringResource(Res.string.obs_mixer_desc),
                trailing = {},
            )

            if (audioInputs.isEmpty()) {
                Text(
                    text = stringResource(Res.string.obs_mixer_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                return@Column
            }

            ManageGate(decision = controlManage) { gateEnabled ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                    audioInputs.forEach { input ->
                        MixerRow(
                            input = input,
                            enabled = gateEnabled,
                            onSetMute = onSetMute,
                            onSetVolume = onSetVolume,
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun MixerRow(
    input: ObsInput,
    enabled: Boolean,
    onSetMute: (String, Boolean) -> Unit,
    onSetVolume: (String, Double) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // OBS reports volume in dB; the fader spans a practical -60 dB … 0 dB. Local state drives the fader live and
    // commits to OBS only when the drag finishes, so a drag is one control call, not one per frame.
    var faderDb: Float by remember(input.name, input.volumeDb) {
        mutableStateOf((input.volumeDb ?: 0.0).toFloat().coerceIn(-60f, 0f))
    }
    val muted: Boolean = input.muted == true
    val displayName: String =
        resolveRowLabel(
            primary = input.name,
            secondary = input.kind,
            typeLabel = stringResource(Res.string.obs_input_row_type),
            discriminatorSource = input.name,
        )
    val muteLabel: String = stringResource(Res.string.obs_mixer_mute_action, displayName)

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.SpaceBetween,
        ) {
            Text(
                text = displayName,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            Text(
                text = stringResource(Res.string.obs_mixer_db, faderDb.toInt()),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            Switch(
                checked = !muted,
                onCheckedChange = { on -> onSetMute(input.name, !on) },
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = muteLabel },
            )
        }
        Slider(
            value = faderDb,
            onValueChange = { faderDb = it },
            valueRange = -60f..0f,
            enabled = enabled,
            onValueChangeFinished = { onSetVolume(input.name, faderDb.toDouble()) },
        )
    }
}

// The OBS performance stats (obs-control.md §5) — read-only, so no manage gate. Hidden entirely when the
// best-effort read failed (a genuine "not available" — distinct from a true zero reading), matching the rest
// of the live surface's best-effort-degrade behavior.
@Composable
private fun StatsCard(stats: ObsStats?) {
    if (stats == null) return
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.obs_stats_title),
                description = stringResource(Res.string.obs_stats_desc),
                trailing = {},
            )
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s4), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                Text(text = stringResource(Res.string.obs_stats_cpu, formatOneDecimal(stats.cpuUsage)), style = typography.sm, color = tokens.cardForeground)
                Text(text = stringResource(Res.string.obs_stats_memory, formatOneDecimal(stats.memoryUsage)), style = typography.sm, color = tokens.cardForeground)
                Text(text = stringResource(Res.string.obs_stats_fps, formatOneDecimal(stats.activeFps)), style = typography.sm, color = tokens.cardForeground)
            }
            Text(
                text = stringResource(Res.string.obs_stats_render_frames, stats.renderSkippedFrames, stats.renderTotalFrames),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
            Text(
                text = stringResource(Res.string.obs_stats_output_frames, stats.outputSkippedFrames, stats.outputTotalFrames),
                style = typography.xs,
                color = tokens.mutedForeground,
            )
        }
    }
}

// Studio mode (obs-control.md §5): preview/program. The preview-scene picker cannot highlight a "current"
// selection — OBS-WS exposes no read for it — so every scene renders as a plain Outline pick, never a false
// [selected] state (truthful-data rule: never show unenforced/unknown state as if it were known).
@Composable
private fun StudioModeCard(
    studioModeEnabled: Boolean,
    scenes: List<ObsScene>,
    controlManage: ManageDecision,
    onSetStudioMode: (Boolean) -> Unit,
    onSetPreviewScene: (String) -> Unit,
    onCutToProgram: () -> Unit,
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
                title = stringResource(Res.string.obs_studio_mode_title),
                description = stringResource(Res.string.obs_studio_mode_desc),
                trailing = {},
            )
            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(text = stringResource(Res.string.obs_studio_mode_enabled_label), color = tokens.cardForeground)
                ManageGate(decision = controlManage) { gateEnabled ->
                    Switch(checked = studioModeEnabled, onCheckedChange = onSetStudioMode, enabled = gateEnabled)
                }
            }
            if (!studioModeEnabled) {
                Text(
                    text = stringResource(Res.string.obs_studio_mode_disabled_hint),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
                return@Column
            }
            if (scenes.isEmpty()) return@Column

            Text(text = stringResource(Res.string.obs_studio_mode_preview_label), style = typography.sm, color = tokens.mutedForeground)
            val sceneTypeLabel: String = stringResource(Res.string.obs_scene_row_type)
            ManageGate(decision = controlManage) { gateEnabled ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                    FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        scenes.forEachIndexed { index, scene ->
                            val label: String =
                                resolveRowLabel(
                                    primary = scene.name,
                                    typeLabel = sceneTypeLabel,
                                    discriminatorSource = "preview-$index-${scene.name}",
                                )
                            Badge(
                                variant = BadgeVariant.Outline,
                                enabled = gateEnabled,
                                onClick = if (gateEnabled) ({ onSetPreviewScene(scene.name) }) else null,
                            ) {
                                Text(text = label, maxLines = 1)
                            }
                        }
                    }
                    Button(onClick = onCutToProgram, enabled = gateEnabled, variant = ButtonVariant.Outline) {
                        Text(text = stringResource(Res.string.obs_studio_mode_cut))
                    }
                }
            }
        }
    }
}

// Scene transitions (obs-control.md §5) — the same picker pattern as the program-scene picker in [ControlCard],
// but for which transition OBS uses on the next scene change.
@Composable
private fun TransitionsCard(
    transitions: List<ObsTransition>,
    controlManage: ManageDecision,
    onSelectTransition: (String) -> Unit,
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
                title = stringResource(Res.string.obs_transitions_title),
                description = stringResource(Res.string.obs_transitions_desc),
                trailing = {},
            )
            if (transitions.isEmpty()) {
                Text(text = stringResource(Res.string.obs_transitions_empty), style = typography.sm, color = tokens.mutedForeground)
                return@Column
            }
            val typeLabel: String = stringResource(Res.string.obs_transition_row_type)
            ManageGate(decision = controlManage) { gateEnabled ->
                FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    transitions.forEachIndexed { index, transition ->
                        val label: String =
                            resolveRowLabel(
                                primary = transition.name,
                                typeLabel = typeLabel,
                                discriminatorSource = "$index-${transition.name}",
                            )
                        Badge(
                            variant = if (transition.isCurrent) BadgeVariant.Default else BadgeVariant.Outline,
                            selected = transition.isCurrent,
                            enabled = gateEnabled,
                            onClick = if (gateEnabled) ({ onSelectTransition(transition.name) }) else null,
                        ) {
                            Text(text = label, maxLines = 1)
                        }
                    }
                }
            }
        }
    }
}

// Per-scene source visibility (obs-control.md §5, 7c3f0c09's endpoints): pick a scene, then show/hide each item
// placed in it. Distinct from the audio mixer's global input mute — this is per-SCENE placement, so the same
// source can be visible in one scene and hidden in another.
@Composable
private fun SceneItemsCard(
    scenes: List<ObsScene>,
    view: ObsSceneItemsView?,
    controlManage: ManageDecision,
    onPickScene: (String) -> Unit,
    onSetVisible: (sceneName: String, sourceName: String, visible: Boolean) -> Unit,
) {
    if (scenes.isEmpty()) return
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.obs_scene_items_title),
                description = stringResource(Res.string.obs_scene_items_desc),
                trailing = {},
            )
            Text(text = stringResource(Res.string.obs_scene_items_pick_scene), style = typography.sm, color = tokens.mutedForeground)
            val sceneTypeLabel: String = stringResource(Res.string.obs_scene_row_type)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                scenes.forEachIndexed { index, scene ->
                    val label: String =
                        resolveRowLabel(
                            primary = scene.name,
                            typeLabel = sceneTypeLabel,
                            discriminatorSource = "items-$index-${scene.name}",
                        )
                    Badge(
                        variant = if (view?.sceneName == scene.name) BadgeVariant.Default else BadgeVariant.Outline,
                        selected = view?.sceneName == scene.name,
                        onClick = { onPickScene(scene.name) },
                    ) {
                        Text(text = label, maxLines = 1)
                    }
                }
            }
            if (view != null) {
                Separator()
                if (view.items.isEmpty()) {
                    Text(text = stringResource(Res.string.obs_scene_items_empty), style = typography.sm, color = tokens.mutedForeground)
                } else {
                    val itemTypeLabel: String = stringResource(Res.string.obs_scene_item_row_type)
                    ManageGate(decision = controlManage) { gateEnabled ->
                        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            view.items.forEachIndexed { index, item ->
                                val displayName: String =
                                    resolveRowLabel(
                                        primary = item.sourceName,
                                        typeLabel = itemTypeLabel,
                                        discriminatorSource = "$index-${item.sourceName}",
                                    )
                                val visibleLabel: String = stringResource(Res.string.obs_scene_item_visible_action, displayName)
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                ) {
                                    Text(
                                        text = displayName,
                                        style = typography.sm,
                                        color = tokens.cardForeground,
                                        maxLines = 2,
                                        overflow = TextOverflow.Ellipsis,
                                        modifier = Modifier.weight(1f),
                                    )
                                    Switch(
                                        checked = item.enabled,
                                        onCheckedChange = { on -> onSetVisible(view.sceneName, item.sourceName, on) },
                                        enabled = gateEnabled,
                                        modifier = Modifier.semantics { contentDescription = visibleLabel },
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

// Source filters (obs-control.md §5): pick a source (from the live inputs or the currently browsed scene's
// items), then enable/disable each filter attached to it.
@Composable
private fun SourceFiltersCard(
    inputs: List<ObsInput>,
    sceneItemsView: ObsSceneItemsView?,
    view: ObsFiltersView?,
    controlManage: ManageDecision,
    onPickSource: (String) -> Unit,
    onSetFilterEnabled: (sourceName: String, filterName: String, enabled: Boolean) -> Unit,
) {
    val sourceNames: List<String> =
        (inputs.map { it.name } + (sceneItemsView?.items?.map { it.sourceName } ?: emptyList()))
            .filter { it.isNotBlank() }
            .distinct()
    if (sourceNames.isEmpty()) return

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.obs_filters_title),
                description = stringResource(Res.string.obs_filters_desc),
                trailing = {},
            )
            Text(text = stringResource(Res.string.obs_filters_pick_source), style = typography.sm, color = tokens.mutedForeground)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                sourceNames.forEachIndexed { index, name ->
                    val label: String =
                        resolveRowLabel(
                            primary = name,
                            typeLabel = stringResource(Res.string.obs_filter_row_type),
                            discriminatorSource = "$index-$name",
                        )
                    Badge(
                        variant = if (view?.sourceName == name) BadgeVariant.Default else BadgeVariant.Outline,
                        selected = view?.sourceName == name,
                        onClick = { onPickSource(name) },
                    ) {
                        Text(text = label, maxLines = 1)
                    }
                }
            }
            if (view != null) {
                Separator()
                if (view.filters.isEmpty()) {
                    Text(text = stringResource(Res.string.obs_filters_empty), style = typography.sm, color = tokens.mutedForeground)
                } else {
                    ManageGate(decision = controlManage) { gateEnabled ->
                        Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                            val filterTypeLabel: String = stringResource(Res.string.obs_filter_row_type)
                            view.filters.sortedBy { it.index }.forEachIndexed { index, filter ->
                                val filterLabel: String =
                                    resolveRowLabel(
                                        primary = filter.name,
                                        typeLabel = filterTypeLabel,
                                        discriminatorSource = "$index-${filter.name}",
                                    )
                                val enabledLabel: String = stringResource(Res.string.obs_filter_enabled_action, filterLabel)
                                Row(
                                    modifier = Modifier.fillMaxWidth(),
                                    verticalAlignment = Alignment.CenterVertically,
                                    horizontalArrangement = Arrangement.SpaceBetween,
                                ) {
                                    Text(
                                        text = filterLabel,
                                        style = typography.sm,
                                        color = tokens.cardForeground,
                                        maxLines = 2,
                                        overflow = TextOverflow.Ellipsis,
                                        modifier = Modifier.weight(1f),
                                    )
                                    Switch(
                                        checked = filter.enabled,
                                        onCheckedChange = { on -> onSetFilterEnabled(view.sourceName, filter.name, on) },
                                        enabled = gateEnabled,
                                        modifier = Modifier.semantics { contentDescription = enabledLabel },
                                    )
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}

// The kinds OBS reports for its two media-input plugins (Media Source / VLC Video Source) — the only inputs
// that answer to TriggerMediaInputAction.
private val MediaSourceKinds: Set<String> = setOf("ffmpeg_source", "vlc_source")

// Per-source actions that don't fit the mixer or the scene/filter pickers: reload a browser source's page, and
// transport-control a media source. Always rendered (with an empty state) rather than hidden, matching the
// rest of the page's cards — never silently omitting a section the operator might expect.
@Composable
private fun SourceActionsCard(
    inputs: List<ObsInput>,
    controlManage: ManageDecision,
    onTriggerMedia: (inputName: String, action: Int) -> Unit,
    onRefreshBrowser: (inputName: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val mediaInputs: List<ObsInput> = inputs.filter { it.kind in MediaSourceKinds }
    val browserInputs: List<ObsInput> = inputs.filter { it.kind == "browser_source" }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.obs_sources_title),
                description = stringResource(Res.string.obs_sources_desc),
                trailing = {},
            )
            if (mediaInputs.isEmpty() && browserInputs.isEmpty()) {
                Text(text = stringResource(Res.string.obs_sources_empty), style = typography.sm, color = tokens.mutedForeground)
                return@Column
            }
            ManageGate(decision = controlManage) { gateEnabled ->
                Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                    mediaInputs.forEach { input ->
                        MediaSourceRow(input = input, enabled = gateEnabled, onTriggerMedia = onTriggerMedia)
                    }
                    browserInputs.forEach { input ->
                        BrowserSourceRow(input = input, enabled = gateEnabled, onRefreshBrowser = onRefreshBrowser)
                    }
                }
            }
        }
    }
}

@Composable
private fun MediaSourceRow(input: ObsInput, enabled: Boolean, onTriggerMedia: (String, Int) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val displayName: String =
        resolveRowLabel(
            primary = input.name,
            secondary = input.kind,
            typeLabel = stringResource(Res.string.obs_input_row_type),
            discriminatorSource = input.name,
        )
    val transportActions: List<Pair<String, Int>> =
        listOf(
            stringResource(Res.string.obs_media_play) to ObsMediaAction.Play,
            stringResource(Res.string.obs_media_pause) to ObsMediaAction.Pause,
            stringResource(Res.string.obs_media_stop) to ObsMediaAction.Stop,
            stringResource(Res.string.obs_media_restart) to ObsMediaAction.Restart,
            stringResource(Res.string.obs_media_previous) to ObsMediaAction.Previous,
            stringResource(Res.string.obs_media_next) to ObsMediaAction.Next,
        )

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = displayName, style = typography.sm, color = tokens.cardForeground, maxLines = 2, overflow = TextOverflow.Ellipsis)
        FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
            transportActions.forEach { (label, action) ->
                Button(
                    onClick = { onTriggerMedia(input.name, action) },
                    enabled = enabled,
                    variant = ButtonVariant.Outline,
                    size = ButtonSize.Sm,
                ) {
                    Text(text = label)
                }
            }
        }
    }
}

@Composable
private fun BrowserSourceRow(input: ObsInput, enabled: Boolean, onRefreshBrowser: (String) -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    val displayName: String =
        resolveRowLabel(
            primary = input.name,
            secondary = input.kind,
            typeLabel = stringResource(Res.string.obs_input_row_type),
            discriminatorSource = input.name,
        )

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(
            text = displayName,
            style = typography.sm,
            color = tokens.cardForeground,
            maxLines = 2,
            overflow = TextOverflow.Ellipsis,
            modifier = Modifier.weight(1f),
        )
        Button(onClick = { onRefreshBrowser(input.name) }, enabled = enabled, variant = ButtonVariant.Outline, size = ButtonSize.Sm) {
            Text(text = stringResource(Res.string.obs_browser_refresh))
        }
    }
}

// OBS reports stats as raw doubles (e.g. 12.34567% CPU) — round to one decimal for a readable stat, without a
// locale-dependent String.format (unavailable in common Kotlin).
private fun formatOneDecimal(value: Double): String {
    val rounded: Double = kotlin.math.round(value * 10) / 10
    val whole: Long = rounded.toLong()
    val tenths: Long = kotlin.math.abs(((rounded - whole) * 10).toLong())
    return "$whole.$tenths"
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
