// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.mediashare.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
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
import androidx.compose.ui.text.input.KeyboardType
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.MediaShareConfig
import bot.nomnomz.dashboard.feature.mediashare.state.MediaShareController
import bot.nomnomz.dashboard.feature.mediashare.state.MediaShareUiState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.mediashare_allow_twitch_clips
import nomnomzbot.composeapp.generated.resources.mediashare_allow_youtube
import nomnomzbot.composeapp.generated.resources.mediashare_config_title
import nomnomzbot.composeapp.generated.resources.mediashare_cooldown
import nomnomzbot.composeapp.generated.resources.mediashare_enabled
import nomnomzbot.composeapp.generated.resources.mediashare_entry_cost
import nomnomzbot.composeapp.generated.resources.mediashare_loading
import nomnomzbot.composeapp.generated.resources.mediashare_max_duration
import nomnomzbot.composeapp.generated.resources.mediashare_max_queue
import nomnomzbot.composeapp.generated.resources.mediashare_require_approval
import nomnomzbot.composeapp.generated.resources.mediashare_save
import nomnomzbot.composeapp.generated.resources.mediashare_submit_hint
import org.jetbrains.compose.resources.stringResource

// The Media-Share Settings tab: the channel's Media-Share config (enable switches, source limits, cost, queue
// length, cooldown). The config write gates at the Editor floor, one rung above the queue moderation. Viewers
// submit clips via the `!media <url>` chat command or a channel-point redeem, never from this page.
@Composable
fun MediaShareSettingsScreen(controller: MediaShareController, role: ManagementRole?) {
    val state: MediaShareUiState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current
    val configManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Editor)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize()) {
        when (val current: MediaShareUiState = state) {
            is MediaShareUiState.Loading -> CenteredMessage(stringResource(Res.string.mediashare_loading))
            is MediaShareUiState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is MediaShareUiState.Ready ->
                Column(
                    modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(spacing.s6),
                    verticalArrangement = Arrangement.spacedBy(spacing.s4),
                ) {
                    ConfigCard(
                        config = current.config,
                        manage = configManage,
                        onSave = { edited -> controller.saveConfig(edited) },
                    )
                }
        }
    }
}

@Composable
private fun ConfigCard(config: MediaShareConfig, manage: ManageDecision, onSave: suspend (MediaShareConfig) -> ApiResult<Unit>) {
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val tokens = LocalTokens.current
    val scope: CoroutineScope = rememberCoroutineScope()

    // Save waits for the server: pending disables the button (no second write), a failure keeps every typed
    // value and shows the reason next to Save.
    var saving: Boolean by remember { mutableStateOf(false) }
    var saveError: String? by remember { mutableStateOf(null) }

    // The edited config is held locally, seeded from the Ready config and re-synced whenever it changes (a save
    // or reload). Number fields are edited as strings so a transiently-empty field is a valid intermediate state.
    var isEnabled: Boolean by remember { mutableStateOf(config.isEnabled) }
    var requireApproval: Boolean by remember { mutableStateOf(config.requireApproval) }
    var allowTwitchClips: Boolean by remember { mutableStateOf(config.allowTwitchClips) }
    var allowYouTube: Boolean by remember { mutableStateOf(config.allowYouTube) }
    var maxDuration: String by remember { mutableStateOf(config.maxDurationSeconds.toString()) }
    var entryCost: String by remember { mutableStateOf(config.entryCost?.toString() ?: "") }
    var maxQueue: String by remember { mutableStateOf(config.maxQueueLength.toString()) }
    var cooldown: String by remember { mutableStateOf(config.perUserCooldownSeconds.toString()) }

    LaunchedEffect(config) {
        isEnabled = config.isEnabled
        requireApproval = config.requireApproval
        allowTwitchClips = config.allowTwitchClips
        allowYouTube = config.allowYouTube
        maxDuration = config.maxDurationSeconds.toString()
        entryCost = config.entryCost?.toString() ?: ""
        maxQueue = config.maxQueueLength.toString()
        cooldown = config.perUserCooldownSeconds.toString()
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Text(text = stringResource(Res.string.mediashare_config_title), style = typography.lg, color = tokens.cardForeground)

            SwitchRow(label = stringResource(Res.string.mediashare_enabled), checked = isEnabled, manage = manage, onCheckedChange = { isEnabled = it })
            SwitchRow(label = stringResource(Res.string.mediashare_require_approval), checked = requireApproval, manage = manage, onCheckedChange = { requireApproval = it })
            SwitchRow(label = stringResource(Res.string.mediashare_allow_twitch_clips), checked = allowTwitchClips, manage = manage, onCheckedChange = { allowTwitchClips = it })
            SwitchRow(label = stringResource(Res.string.mediashare_allow_youtube), checked = allowYouTube, manage = manage, onCheckedChange = { allowYouTube = it })

            NumberField(label = stringResource(Res.string.mediashare_max_duration), value = maxDuration, manage = manage, onValueChange = { maxDuration = it })
            NumberField(label = stringResource(Res.string.mediashare_entry_cost), value = entryCost, manage = manage, onValueChange = { entryCost = it })
            NumberField(label = stringResource(Res.string.mediashare_max_queue), value = maxQueue, manage = manage, onValueChange = { maxQueue = it })
            NumberField(label = stringResource(Res.string.mediashare_cooldown), value = cooldown, manage = manage, onValueChange = { cooldown = it })

            Text(text = stringResource(Res.string.mediashare_submit_hint), style = typography.xs, color = tokens.mutedForeground)

            ManageGate(decision = manage) { enabled ->
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
                                        config.copy(
                                            isEnabled = isEnabled,
                                            requireApproval = requireApproval,
                                            allowTwitchClips = allowTwitchClips,
                                            allowYouTube = allowYouTube,
                                            maxDurationSeconds = maxDuration.toIntOrNull() ?: 0,
                                            entryCost = entryCost.toLongOrNull(),
                                            maxQueueLength = maxQueue.toIntOrNull() ?: 0,
                                            perUserCooldownSeconds = cooldown.toIntOrNull() ?: 0,
                                        )
                                    )
                                saving = false
                                if (result is ApiResult.Failure) saveError = result.error.message
                            }
                        },
                        enabled = enabled && !saving,
                        loading = saving,
                    ) {
                        Text(text = stringResource(Res.string.mediashare_save))
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
private fun SwitchRow(label: String, checked: Boolean, manage: ManageDecision, onCheckedChange: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(text = label, color = tokens.cardForeground)
        ManageGate(decision = manage) { enabled ->
            Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = enabled)
        }
    }
}

@Composable
private fun NumberField(label: String, value: String, manage: ManageDecision, onValueChange: (String) -> Unit) {
    ManageGate(decision = manage) { enabled ->
        AppTextField(
            value = value,
            onValueChange = onValueChange,
            label = label,
            enabled = enabled,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth(),
        )
    }
}
