// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.vts.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
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
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.BadgeVariant
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.SpinnerSize
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.toDialogResult
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.vts.state.VtsAuthorizeOutcome
import bot.nomnomz.dashboard.feature.vts.state.VtsController
import bot.nomnomz.dashboard.feature.vts.state.VtsUiState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.vts_authorize
import nomnomzbot.composeapp.generated.resources.vts_authorize_denied
import nomnomzbot.composeapp.generated.resources.vts_authorize_failed
import nomnomzbot.composeapp.generated.resources.vts_authorize_granted
import nomnomzbot.composeapp.generated.resources.vts_authorize_hint
import nomnomzbot.composeapp.generated.resources.vts_authorize_waiting
import nomnomzbot.composeapp.generated.resources.vts_connection_desc
import nomnomzbot.composeapp.generated.resources.vts_connection_title
import nomnomzbot.composeapp.generated.resources.vts_enabled_label
import nomnomzbot.composeapp.generated.resources.vts_endpoint_label
import nomnomzbot.composeapp.generated.resources.vts_mode_bridge
import nomnomzbot.composeapp.generated.resources.vts_mode_direct
import nomnomzbot.composeapp.generated.resources.vts_mode_label
import nomnomzbot.composeapp.generated.resources.vts_plugin_token_missing
import nomnomzbot.composeapp.generated.resources.vts_plugin_token_stored
import nomnomzbot.composeapp.generated.resources.vts_rotate_bridge
import nomnomzbot.composeapp.generated.resources.vts_rotate_cancel
import nomnomzbot.composeapp.generated.resources.vts_rotate_confirm
import nomnomzbot.composeapp.generated.resources.vts_rotate_message
import nomnomzbot.composeapp.generated.resources.vts_rotate_title
import nomnomzbot.composeapp.generated.resources.vts_save
import nomnomzbot.composeapp.generated.resources.vts_status_authorized
import nomnomzbot.composeapp.generated.resources.vts_status_connected
import nomnomzbot.composeapp.generated.resources.vts_status_error
import nomnomzbot.composeapp.generated.resources.vts_status_unauthorized
import org.jetbrains.compose.resources.stringResource

// The VTube Studio connection on the Integrations page (frontend-ia.md): mode, endpoint, enabled switch, Save,
// bridge-token rotation and the plugin-token authorize handshake. Connection config + authorize + rotate gate at
// the page's Broadcaster manage floor (vts:config:write). Renders once the controller holds a Ready state.
@Composable
fun VtsConnectionCard(controller: VtsController, role: ManagementRole?) {
    val state: VtsUiState by controller.state.collectAsStateWithLifecycle()
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.VtsHotkeys)

    LaunchedEffect(Unit) { controller.load() }

    val current: VtsUiState = state
    if (current is VtsUiState.Ready) {
        ConnectionCard(
            connection = current.connection,
            manage = manage,
            controller = controller,
            onSave = { mode, endpoint, enabled -> controller.saveConnection(mode, endpoint, enabled) },
            onRotate = { controller.rotateBridgeToken() },
        )
    }
}

@Composable
private fun ConnectionCard(
    connection: VtsConnection,
    manage: ManageDecision,
    controller: VtsController,
    onSave: suspend (mode: String, endpoint: String?, enabled: Boolean) -> ApiResult<Unit>,
    onRotate: suspend () -> ApiResult<Unit>,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    // Save waits for the server: pending disables the button (no second write), a failure keeps every typed
    // value and shows the reason next to Save.
    var saving: Boolean by remember { mutableStateOf(false) }
    var saveError: String? by remember { mutableStateOf(null) }
    var confirmRotate: Boolean by remember { mutableStateOf(false) }

    var mode: String by remember(connection.mode) { mutableStateOf(connection.mode) }
    var endpoint: String by remember(connection.endpoint) { mutableStateOf(connection.endpoint) }
    var enabled: Boolean by remember(connection.isEnabled) { mutableStateOf(connection.isEnabled) }

    // The blocking-authorize UI: a "waiting" spinner while the streamer approves in VTS, then a settled outcome.
    var authorizing: Boolean by remember { mutableStateOf(false) }
    var authorizeOutcome: VtsAuthorizeOutcome? by remember { mutableStateOf(null) }

    if (confirmRotate) {
        ConfirmDialog(
            title = stringResource(Res.string.vts_rotate_title),
            message = stringResource(Res.string.vts_rotate_message),
            confirmLabel = stringResource(Res.string.vts_rotate_confirm),
            dismissLabel = stringResource(Res.string.vts_rotate_cancel),
            destructive = true,
            action = { onRotate().toDialogResult() },
            onDismiss = { confirmRotate = false },
        )
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.vts_connection_title),
                description = stringResource(Res.string.vts_connection_desc),
                trailing = { StatusChip(status = connection.status) },
            )

            Text(text = stringResource(Res.string.vts_mode_label), style = typography.sm, color = tokens.mutedForeground)
            FlowRow(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ModeChip(label = stringResource(Res.string.vts_mode_direct), selected = mode == "direct", onClick = { mode = "direct" })
                ModeChip(label = stringResource(Res.string.vts_mode_bridge), selected = mode == "bridge", onClick = { mode = "bridge" })
            }

            AppTextField(
                value = endpoint,
                onValueChange = { endpoint = it },
                label = stringResource(Res.string.vts_endpoint_label),
                modifier = Modifier.fillMaxWidth(),
            )

            Text(
                text =
                    if (connection.hasPluginToken) stringResource(Res.string.vts_plugin_token_stored)
                    else stringResource(Res.string.vts_plugin_token_missing),
                style = typography.xs,
                color = if (connection.hasPluginToken) tokens.mutedForeground else tokens.destructiveForeground,
            )

            Row(
                modifier = Modifier.fillMaxWidth(),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.SpaceBetween,
            ) {
                Text(text = stringResource(Res.string.vts_enabled_label), color = tokens.cardForeground)
                ManageGate(decision = manage) { gateEnabled ->
                    Switch(checked = enabled, onCheckedChange = { enabled = it }, enabled = gateEnabled)
                }
            }

            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                ManageGate(decision = manage) { gateEnabled ->
                    Button(
                        onClick = {
                            saving = true
                            saveError = null
                            scope.launch {
                                val result: ApiResult<Unit> = onSave(mode, endpoint.ifBlank { null }, enabled)
                                saving = false
                                if (result is ApiResult.Failure) saveError = result.error.message
                            }
                        },
                        enabled = gateEnabled && !saving,
                        loading = saving,
                    ) {
                        Text(text = stringResource(Res.string.vts_save))
                    }
                }
                saveError?.let { detail ->
                    Text(text = detail, style = typography.sm, color = tokens.destructive)
                }
                if (mode == "bridge") {
                    ManageGate(decision = manage) { gateEnabled ->
                        OutlinedButton(onClick = { confirmRotate = true }, enabled = gateEnabled) {
                            Text(text = stringResource(Res.string.vts_rotate_bridge))
                        }
                    }
                }
            }

            Separator()

            // Authorize handshake — the blocking call, its "waiting" spinner, and the settled outcome message.
            Text(text = stringResource(Res.string.vts_authorize_hint), style = typography.sm, color = tokens.mutedForeground)
            Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s3)) {
                ManageGate(decision = manage) { gateEnabled ->
                    Button(
                        onClick = {
                            authorizeOutcome = null
                            authorizing = true
                            scope.launch {
                                authorizeOutcome = controller.authorize()
                                authorizing = false
                            }
                        },
                        enabled = gateEnabled && !authorizing,
                    ) {
                        Text(text = stringResource(Res.string.vts_authorize))
                    }
                }
                if (authorizing) {
                    Spinner(size = SpinnerSize.Sm)
                    Text(text = stringResource(Res.string.vts_authorize_waiting), style = typography.sm, color = tokens.mutedForeground)
                }
            }
            authorizeOutcome?.let { outcome ->
                val (message, color) =
                    when (outcome) {
                        VtsAuthorizeOutcome.Granted -> stringResource(Res.string.vts_authorize_granted) to tokens.success
                        VtsAuthorizeOutcome.Denied -> stringResource(Res.string.vts_authorize_denied) to tokens.destructiveForeground
                        VtsAuthorizeOutcome.Failed -> stringResource(Res.string.vts_authorize_failed) to tokens.destructiveForeground
                    }
                Text(text = message, style = typography.sm, color = color)
            }
        }
    }
}

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
private fun StatusChip(status: String) {
    val tokens = LocalTokens.current
    when (status) {
        "connected" ->
            Badge(variant = BadgeVariant.Default) { Text(text = stringResource(Res.string.vts_status_connected)) }
        "authorized" ->
            Badge(variant = BadgeVariant.Secondary) { Text(text = stringResource(Res.string.vts_status_authorized)) }
        "error" ->
            Badge(variant = BadgeVariant.Destructive) { Text(text = stringResource(Res.string.vts_status_error)) }
        else ->
            Badge(variant = BadgeVariant.Outline) {
                Text(text = stringResource(Res.string.vts_status_unauthorized), color = tokens.mutedForeground)
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
