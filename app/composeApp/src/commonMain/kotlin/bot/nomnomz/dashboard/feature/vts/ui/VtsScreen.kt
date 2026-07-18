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
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
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
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.SpinnerSize
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.VtsConnection
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import bot.nomnomz.dashboard.feature.vts.state.VtsAuthorizeOutcome
import bot.nomnomz.dashboard.feature.vts.state.VtsController
import bot.nomnomz.dashboard.feature.vts.state.VtsUiState
import kotlinx.coroutines.launch
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.vts_action_error
import nomnomzbot.composeapp.generated.resources.vts_activate_expression
import nomnomzbot.composeapp.generated.resources.vts_authorize
import nomnomzbot.composeapp.generated.resources.vts_authorize_denied
import nomnomzbot.composeapp.generated.resources.vts_authorize_failed
import nomnomzbot.composeapp.generated.resources.vts_authorize_granted
import nomnomzbot.composeapp.generated.resources.vts_authorize_hint
import nomnomzbot.composeapp.generated.resources.vts_authorize_waiting
import nomnomzbot.composeapp.generated.resources.vts_connection_desc
import nomnomzbot.composeapp.generated.resources.vts_connection_title
import nomnomzbot.composeapp.generated.resources.vts_control_desc
import nomnomzbot.composeapp.generated.resources.vts_control_failed
import nomnomzbot.composeapp.generated.resources.vts_control_locked
import nomnomzbot.composeapp.generated.resources.vts_control_ok
import nomnomzbot.composeapp.generated.resources.vts_control_title
import nomnomzbot.composeapp.generated.resources.vts_enabled_label
import nomnomzbot.composeapp.generated.resources.vts_endpoint_label
import nomnomzbot.composeapp.generated.resources.vts_error
import nomnomzbot.composeapp.generated.resources.vts_expressions_label
import nomnomzbot.composeapp.generated.resources.vts_hotkeys_label
import nomnomzbot.composeapp.generated.resources.vts_load_model
import nomnomzbot.composeapp.generated.resources.vts_loading
import nomnomzbot.composeapp.generated.resources.vts_mode_bridge
import nomnomzbot.composeapp.generated.resources.vts_mode_direct
import nomnomzbot.composeapp.generated.resources.vts_mode_label
import nomnomzbot.composeapp.generated.resources.vts_models_label
import nomnomzbot.composeapp.generated.resources.vts_no_expressions
import nomnomzbot.composeapp.generated.resources.vts_no_hotkeys
import nomnomzbot.composeapp.generated.resources.vts_no_models
import nomnomzbot.composeapp.generated.resources.vts_pick_placeholder
import nomnomzbot.composeapp.generated.resources.vts_plugin_token_missing
import nomnomzbot.composeapp.generated.resources.vts_plugin_token_stored
import nomnomzbot.composeapp.generated.resources.vts_retry
import nomnomzbot.composeapp.generated.resources.vts_rotate_bridge
import nomnomzbot.composeapp.generated.resources.vts_save
import nomnomzbot.composeapp.generated.resources.vts_status_authorized
import nomnomzbot.composeapp.generated.resources.vts_status_connected
import nomnomzbot.composeapp.generated.resources.vts_status_error
import nomnomzbot.composeapp.generated.resources.vts_status_unauthorized
import nomnomzbot.composeapp.generated.resources.vts_subtitle
import nomnomzbot.composeapp.generated.resources.vts_trigger_hotkey
import nomnomzbot.composeapp.generated.resources.shell_nav_vts
import org.jetbrains.compose.resources.stringResource

// The VTube Studio page (frontend-ia.md, Connect group): the channel's VTS API connection config, the
// plugin-token authorization handshake, and live model/hotkey/expression control (vtube-studio.md §4). A pure
// projection of [VtsController]. Connection config + authorize + rotate gate at the page's Broadcaster manage
// floor (vts:config:write); model/hotkey/expression control gates at Moderator (vts:control). The `/obs-bridge`
// page also carries the VTS bridge leg (server-served — see the for-backend handoff entry), not built here.
@Composable
fun VtsScreen(controller: VtsController, role: ManagementRole?) {
    val state: VtsUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val configManage: ManageDecision = rememberManageDecision(role, ShellRoute.Vts)
    val controlManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Moderator)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: VtsUiState = state) {
            is VtsUiState.Loading -> CenteredMessage(stringResource(Res.string.vts_loading))
            is VtsUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is VtsUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.shell_nav_vts),
                        subtitle = stringResource(Res.string.vts_subtitle),
                    )
                    current.actionError?.let {
                        ActionErrorBanner(message = stringResource(Res.string.vts_action_error, it))
                    }
                    ConnectionCard(
                        connection = current.connection,
                        manage = configManage,
                        controller = controller,
                        onSave = { mode, endpoint, enabled ->
                            scope.launch { controller.saveConnection(mode, endpoint, enabled) }
                        },
                        onRotate = { scope.launch { controller.rotateBridgeToken() } },
                    )
                    ControlCard(
                        inventory = current.inventory,
                        authorized = current.connection.hasPluginToken,
                        manage = controlManage,
                        controller = controller,
                    )
                }
        }
    }
}

@Composable
private fun ConnectionCard(
    connection: VtsConnection,
    manage: ManageDecision,
    controller: VtsController,
    onSave: (mode: String, endpoint: String?, enabled: Boolean) -> Unit,
    onRotate: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var mode: String by remember(connection.mode) { mutableStateOf(connection.mode) }
    var endpoint: String by remember(connection.endpoint) { mutableStateOf(connection.endpoint) }
    var enabled: Boolean by remember(connection.isEnabled) { mutableStateOf(connection.isEnabled) }

    // The blocking-authorize UI: a "waiting" spinner while the streamer approves in VTS, then a settled outcome.
    var authorizing: Boolean by remember { mutableStateOf(false) }
    var authorizeOutcome: VtsAuthorizeOutcome? by remember { mutableStateOf(null) }

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

            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                ManageGate(decision = manage) { gateEnabled ->
                    Button(onClick = { onSave(mode, endpoint.ifBlank { null }, enabled) }, enabled = gateEnabled) {
                        Text(text = stringResource(Res.string.vts_save))
                    }
                }
                if (mode == "bridge") {
                    ManageGate(decision = manage) { gateEnabled ->
                        OutlinedButton(onClick = onRotate, enabled = gateEnabled) {
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
private fun ControlCard(
    inventory: VtsModelInventory?,
    authorized: Boolean,
    manage: ManageDecision,
    controller: VtsController,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    var selectedModel: String by remember { mutableStateOf("") }
    var selectedHotkey: String by remember { mutableStateOf("") }
    var selectedExpression: String by remember { mutableStateOf("") }
    var resultMessage: String? by remember { mutableStateOf(null) }
    var resultOk: Boolean by remember { mutableStateOf(true) }

    val okLabel: String = stringResource(Res.string.vts_control_ok)

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            SectionHeader(
                title = stringResource(Res.string.vts_control_title),
                description = stringResource(Res.string.vts_control_desc),
                trailing = {},
            )

            if (!authorized || inventory == null) {
                Text(text = stringResource(Res.string.vts_control_locked), style = typography.sm, color = tokens.mutedForeground)
                return@Column
            }

            fun fireControl(requestType: String, payloadJson: String?) {
                scope.launch {
                    val result = controller.control(requestType, payloadJson)
                    if (result == null) {
                        resultMessage = null
                    } else {
                        resultOk = result.ok
                        resultMessage = if (result.ok) okLabel else (result.error ?: "")
                    }
                }
            }

            // Models
            Text(text = stringResource(Res.string.vts_models_label), style = typography.sm, color = tokens.mutedForeground)
            if (inventory.models.isEmpty()) {
                Text(text = stringResource(Res.string.vts_no_models), style = typography.sm, color = tokens.mutedForeground)
            } else {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    PickerField(
                        modifier = Modifier.weight(1f),
                        options = inventory.models.map { it.id to it.name },
                        selectedId = selectedModel,
                        onSelect = { selectedModel = it },
                    )
                    ManageGate(decision = manage) { gateEnabled ->
                        Button(
                            onClick = { fireControl("ModelLoadRequest", buildJsonObject { put("modelID", selectedModel) }.toString()) },
                            enabled = gateEnabled && selectedModel.isNotBlank(),
                        ) {
                            Text(text = stringResource(Res.string.vts_load_model))
                        }
                    }
                }
            }

            Separator()

            // Hotkeys
            Text(text = stringResource(Res.string.vts_hotkeys_label), style = typography.sm, color = tokens.mutedForeground)
            if (inventory.hotkeys.isEmpty()) {
                Text(text = stringResource(Res.string.vts_no_hotkeys), style = typography.sm, color = tokens.mutedForeground)
            } else {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    PickerField(
                        modifier = Modifier.weight(1f),
                        options = inventory.hotkeys.map { it.id to it.name },
                        selectedId = selectedHotkey,
                        onSelect = { selectedHotkey = it },
                    )
                    ManageGate(decision = manage) { gateEnabled ->
                        Button(
                            onClick = { fireControl("HotkeyTriggerRequest", buildJsonObject { put("hotkeyID", selectedHotkey) }.toString()) },
                            enabled = gateEnabled && selectedHotkey.isNotBlank(),
                        ) {
                            Text(text = stringResource(Res.string.vts_trigger_hotkey))
                        }
                    }
                }
            }

            Separator()

            // Expressions
            Text(text = stringResource(Res.string.vts_expressions_label), style = typography.sm, color = tokens.mutedForeground)
            if (inventory.expressions.isEmpty()) {
                Text(text = stringResource(Res.string.vts_no_expressions), style = typography.sm, color = tokens.mutedForeground)
            } else {
                Row(verticalAlignment = Alignment.CenterVertically, horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    PickerField(
                        modifier = Modifier.weight(1f),
                        options = inventory.expressions.map { it to it },
                        selectedId = selectedExpression,
                        onSelect = { selectedExpression = it },
                    )
                    ManageGate(decision = manage) { gateEnabled ->
                        Button(
                            onClick = {
                                fireControl(
                                    "ExpressionActivationRequest",
                                    buildJsonObject {
                                        put("expressionFile", selectedExpression)
                                        put("active", true)
                                    }.toString(),
                                )
                            },
                            enabled = gateEnabled && selectedExpression.isNotBlank(),
                        ) {
                            Text(text = stringResource(Res.string.vts_activate_expression))
                        }
                    }
                }
            }

            resultMessage?.let {
                Text(
                    text = if (resultOk) it else stringResource(Res.string.vts_control_failed, it),
                    style = typography.sm,
                    color = if (resultOk) tokens.success else tokens.destructiveForeground,
                )
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

// A labelled dropdown over [options] (id to display label) — the shared model/hotkey/expression picker.
@Composable
private fun PickerField(
    options: List<Pair<String, String>>,
    selectedId: String,
    onSelect: (String) -> Unit,
    modifier: Modifier = Modifier,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    var expanded: Boolean by remember { mutableStateOf(false) }
    val selectedLabel: String? = options.firstOrNull { it.first == selectedId }?.second
    val placeholder: String = stringResource(Res.string.vts_pick_placeholder)

    Box(modifier = modifier) {
        OutlinedButton(onClick = { expanded = true }) {
            Text(
                text = selectedLabel ?: placeholder,
                color = if (selectedLabel != null) tokens.cardForeground else tokens.mutedForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
        }
        DropdownMenu(expanded = expanded, onDismissRequest = { expanded = false }) {
            options.forEach { (id, optionLabel) ->
                DropdownMenuItem(
                    text = { Text(text = optionLabel, style = typography.sm, color = tokens.cardForeground) },
                    onClick = {
                        onSelect(id)
                        expanded = false
                    },
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
                text = stringResource(Res.string.vts_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.vts_retry)) }
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
