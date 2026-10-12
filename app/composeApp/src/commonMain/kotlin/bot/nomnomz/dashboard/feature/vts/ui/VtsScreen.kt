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
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.OutlinedButton
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.VtsModelInventory
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import bot.nomnomz.dashboard.feature.vts.state.VtsController
import bot.nomnomz.dashboard.feature.vts.state.VtsUiState
import kotlinx.coroutines.launch
import kotlinx.serialization.json.buildJsonObject
import kotlinx.serialization.json.put
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_vts
import nomnomzbot.composeapp.generated.resources.vts_activate_expression
import nomnomzbot.composeapp.generated.resources.vts_connect_action
import nomnomzbot.composeapp.generated.resources.vts_connect_missing
import nomnomzbot.composeapp.generated.resources.vts_control_failed
import nomnomzbot.composeapp.generated.resources.vts_control_ok
import nomnomzbot.composeapp.generated.resources.vts_error
import nomnomzbot.composeapp.generated.resources.vts_expressions_label
import nomnomzbot.composeapp.generated.resources.vts_hotkeys_label
import nomnomzbot.composeapp.generated.resources.vts_load_model
import nomnomzbot.composeapp.generated.resources.vts_loading
import nomnomzbot.composeapp.generated.resources.vts_models_label
import nomnomzbot.composeapp.generated.resources.vts_no_expressions
import nomnomzbot.composeapp.generated.resources.vts_no_hotkeys
import nomnomzbot.composeapp.generated.resources.vts_no_models
import nomnomzbot.composeapp.generated.resources.vts_pick_placeholder
import nomnomzbot.composeapp.generated.resources.vts_retry
import nomnomzbot.composeapp.generated.resources.vts_subtitle
import nomnomzbot.composeapp.generated.resources.vts_trigger_hotkey
import org.jetbrains.compose.resources.stringResource

// Which tab of the VTube Studio page is showing (frontend-ia.md).
enum class VtsSection(val route: ShellRoute) {
    Hotkeys(ShellRoute.VtsHotkeys),
    Expressions(ShellRoute.VtsExpressions),
}

// The VTube Studio page (frontend-ia.md): live model/hotkey/expression control (vtube-studio.md §4), split into
// a Hotkeys tab (models + hotkeys) and an Expressions tab. The connection itself lives on the Integrations page
// ([VtsConnectionCard]); until it is connected and authorized, each tab shows a step that opens Integrations.
// A pure projection of [VtsController]. Control gates at the Moderator floor (vts:control).
@Composable
fun VtsScreen(
    controller: VtsController,
    role: ManagementRole?,
    section: VtsSection,
    onOpenIntegrations: () -> Unit,
) {
    val state: VtsUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val controlManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Moderator)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: VtsUiState = state) {
            is VtsUiState.Loading -> CenteredMessage(stringResource(Res.string.vts_loading))
            is VtsUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is VtsUiState.Ready -> {
                val inventory: VtsModelInventory? = current.inventory
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s6),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    PageHeader(
                        title = stringResource(Res.string.shell_nav_vts),
                        subtitle = stringResource(Res.string.vts_subtitle),
                    )
                    if (!current.connection.hasPluginToken || inventory == null) {
                        ConnectStep(onOpenIntegrations = onOpenIntegrations)
                    } else if (section == VtsSection.Hotkeys) {
                        HotkeysTab(inventory = inventory, manage = controlManage, controller = controller)
                    } else {
                        ExpressionsTab(inventory = inventory, manage = controlManage, controller = controller)
                    }
                }
            }
        }
    }
}

@Composable
private fun ConnectStep(onOpenIntegrations: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(
                text = stringResource(Res.string.vts_connect_missing),
                style = typography.base,
                color = tokens.cardForeground,
            )
            Button(onClick = onOpenIntegrations) { Text(text = stringResource(Res.string.vts_connect_action)) }
        }
    }
}

// Runs one VTS control request and keeps the settled outcome message for the tab that fired it.
private class ControlOutcome {
    var message: String? by mutableStateOf(null)
    var ok: Boolean by mutableStateOf(true)
}

@Composable
private fun rememberControlFire(controller: VtsController): Pair<ControlOutcome, (String, String?) -> Unit> {
    val scope = rememberCoroutineScope()
    val outcome: ControlOutcome = remember { ControlOutcome() }
    val okLabel: String = stringResource(Res.string.vts_control_ok)
    return outcome to { requestType: String, payloadJson: String? ->
        scope.launch {
            val result = controller.control(requestType, payloadJson)
            if (result == null) {
                outcome.message = null
            } else {
                outcome.ok = result.ok
                outcome.message = if (result.ok) okLabel else (result.error ?: "")
            }
        }
        Unit
    }
}

@Composable
private fun ControlOutcomeText(outcome: ControlOutcome) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    outcome.message?.let {
        Text(
            text = if (outcome.ok) it else stringResource(Res.string.vts_control_failed, it),
            style = typography.sm,
            color = if (outcome.ok) tokens.success else tokens.destructiveForeground,
        )
    }
}

@Composable
private fun HotkeysTab(inventory: VtsModelInventory, manage: ManageDecision, controller: VtsController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var selectedModel: String by remember { mutableStateOf("") }
    var selectedHotkey: String by remember { mutableStateOf("") }
    val (outcome: ControlOutcome, fire: (String, String?) -> Unit) = rememberControlFire(controller)

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
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
                        OutlinedButton(
                            onClick = { fire("ModelLoadRequest", buildJsonObject { put("modelID", selectedModel) }.toString()) },
                            enabled = gateEnabled && selectedModel.isNotBlank(),
                        ) {
                            Text(text = stringResource(Res.string.vts_load_model))
                        }
                    }
                }
            }

            Separator()

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
                            onClick = { fire("HotkeyTriggerRequest", buildJsonObject { put("hotkeyID", selectedHotkey) }.toString()) },
                            enabled = gateEnabled && selectedHotkey.isNotBlank(),
                        ) {
                            Text(text = stringResource(Res.string.vts_trigger_hotkey))
                        }
                    }
                }
            }

            ControlOutcomeText(outcome)
        }
    }
}

@Composable
private fun ExpressionsTab(inventory: VtsModelInventory, manage: ManageDecision, controller: VtsController) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var selectedExpression: String by remember { mutableStateOf("") }
    val (outcome: ControlOutcome, fire: (String, String?) -> Unit) = rememberControlFire(controller)

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
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
                                fire(
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

            ControlOutcomeText(outcome)
        }
    }
}

// ── shared bits ────────────────────────────────────────────────────────────

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
                maxLines = 2,
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
