// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.ui

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.material3.Button
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.FilterChip
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.Switch
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
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.tts.state.TtsState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.tts_error
import nomnomzbot.composeapp.generated.resources.tts_label_max_length
import nomnomzbot.composeapp.generated.resources.tts_label_min_permission
import nomnomzbot.composeapp.generated.resources.tts_label_read_usernames
import nomnomzbot.composeapp.generated.resources.tts_label_skip_bot_messages
import nomnomzbot.composeapp.generated.resources.tts_label_voice
import nomnomzbot.composeapp.generated.resources.tts_loading
import nomnomzbot.composeapp.generated.resources.tts_max_length_invalid
import nomnomzbot.composeapp.generated.resources.tts_permission_broadcaster
import nomnomzbot.composeapp.generated.resources.tts_permission_everyone
import nomnomzbot.composeapp.generated.resources.tts_permission_moderators
import nomnomzbot.composeapp.generated.resources.tts_permission_subscribers
import nomnomzbot.composeapp.generated.resources.tts_permission_vip
import nomnomzbot.composeapp.generated.resources.tts_retry
import nomnomzbot.composeapp.generated.resources.tts_save
import nomnomzbot.composeapp.generated.resources.tts_save_error
import nomnomzbot.composeapp.generated.resources.tts_saved
import nomnomzbot.composeapp.generated.resources.tts_saving
import nomnomzbot.composeapp.generated.resources.tts_status_disabled
import nomnomzbot.composeapp.generated.resources.tts_status_enabled
import nomnomzbot.composeapp.generated.resources.tts_toggle_enabled
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The TTS page: an editable form over the channel's text-to-speech configuration — the enabled status plus
// the editable config values, all real data from [TtsController]. The screen seeds a local form from the
// controller's loaded config; Save persists the whole config and the controller echoes the saved values
// back. It loads on first composition and offers a retry on failure.
@Composable
fun TtsScreen(controller: TtsController) {
    val state: TtsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: TtsState = state) {
            is TtsState.Loading -> CenteredMessage(stringResource(Res.string.tts_loading))
            is TtsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is TtsState.Ready ->
                ReadyContent(state = current, onSave = { edited -> scope.launch { controller.save(edited) } })
        }
    }
}

// The minimum-permission values the backend accepts (UpdateTtsConfigDto regex). Each pairs the wire value
// the API persists with its localized label; the form picks one, never free text, so the value is always valid.
private val PERMISSIONS: List<Pair<String, StringResource>> =
    listOf(
        "everyone" to Res.string.tts_permission_everyone,
        "subscribers" to Res.string.tts_permission_subscribers,
        "vip" to Res.string.tts_permission_vip,
        "moderators" to Res.string.tts_permission_moderators,
        "broadcaster" to Res.string.tts_permission_broadcaster,
    )

@Composable
private fun ReadyContent(state: TtsState.Ready, onSave: (TtsConfig) -> Unit) {
    val spacing = LocalSpacing.current
    val loaded: TtsConfig = state.config

    // Local editable form, re-seeded whenever a new config loads (initial load or a successful save). Holding
    // it screen-side keeps the controller a thin persistence boundary; `remember(loaded)` resets every field
    // to the saved baseline so the "differs from loaded" check below is exact.
    var isEnabled: Boolean by remember(loaded) { mutableStateOf(loaded.isEnabled) }
    var defaultVoiceId: String by remember(loaded) { mutableStateOf(loaded.defaultVoiceId) }
    var maxLengthText: String by remember(loaded) { mutableStateOf(loaded.maxLength.toString()) }
    var minPermission: String by remember(loaded) { mutableStateOf(loaded.minPermission) }
    var skipBotMessages: Boolean by remember(loaded) { mutableStateOf(loaded.skipBotMessages) }
    var readUsernames: Boolean by remember(loaded) { mutableStateOf(loaded.readUsernames) }

    val maxLength: Int? = maxLengthText.toIntOrNull()
    val maxLengthValid: Boolean = maxLength != null && maxLength in 1..500

    val edited: TtsConfig =
        TtsConfig(
            isEnabled = isEnabled,
            defaultVoiceId = defaultVoiceId,
            maxLength = maxLength ?: loaded.maxLength,
            minPermission = minPermission,
            skipBotMessages = skipBotMessages,
            readUsernames = readUsernames,
        )

    // Save is offered only when the form is valid AND actually differs from the saved baseline — saving an
    // unchanged config is a no-op the user shouldn't be invited to make. A plain derivation: every input is a
    // local recomputed each recomposition (the `var`s are State-backed), so this re-evaluates as fields change.
    val canSave: Boolean = maxLengthValid && edited != loaded && !state.saving

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        StatusBanner(isEnabled = isEnabled)

        EditCard(
            isEnabled = isEnabled,
            onEnabledChange = { isEnabled = it },
            defaultVoiceId = defaultVoiceId,
            onVoiceChange = { defaultVoiceId = it },
            maxLengthText = maxLengthText,
            onMaxLengthChange = { maxLengthText = it.filter { c -> c.isDigit() } },
            maxLengthValid = maxLengthValid,
            minPermission = minPermission,
            onPermissionChange = { minPermission = it },
            skipBotMessages = skipBotMessages,
            onSkipBotMessagesChange = { skipBotMessages = it },
            readUsernames = readUsernames,
            onReadUsernamesChange = { readUsernames = it },
            enabled = !state.saving,
        )

        SaveBar(
            saving = state.saving,
            justSaved = state.justSaved,
            saveError = state.saveError,
            canSave = canSave,
            onSave = { onSave(edited) },
        )
    }
}

@Composable
private fun StatusBanner(isEnabled: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusText: String =
        stringResource(if (isEnabled) Res.string.tts_status_enabled else Res.string.tts_status_disabled)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4)
            // One node for screen readers: "TTS enabled" rather than a disconnected dot + label.
            .clearAndSetSemantics { contentDescription = statusText },
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Box(
            modifier = Modifier
                .size(spacing.s2)
                .clip(CircleShape)
                .background(if (isEnabled) tokens.primary else tokens.mutedForeground),
        )
        Text(text = statusText, style = typography.xl, color = tokens.cardForeground)
    }
}

@Composable
private fun EditCard(
    isEnabled: Boolean,
    onEnabledChange: (Boolean) -> Unit,
    defaultVoiceId: String,
    onVoiceChange: (String) -> Unit,
    maxLengthText: String,
    onMaxLengthChange: (String) -> Unit,
    maxLengthValid: Boolean,
    minPermission: String,
    onPermissionChange: (String) -> Unit,
    skipBotMessages: Boolean,
    onSkipBotMessagesChange: (Boolean) -> Unit,
    readUsernames: Boolean,
    onReadUsernamesChange: (Boolean) -> Unit,
    enabled: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        SwitchRow(
            labelRes = Res.string.tts_toggle_enabled,
            checked = isEnabled,
            onCheckedChange = onEnabledChange,
            enabled = enabled,
        )
        VoiceField(value = defaultVoiceId, onValueChange = onVoiceChange, enabled = enabled)
        MaxLengthField(
            value = maxLengthText,
            onValueChange = onMaxLengthChange,
            valid = maxLengthValid,
            enabled = enabled,
        )
        PermissionPicker(selected = minPermission, onSelect = onPermissionChange, enabled = enabled)
        SwitchRow(
            labelRes = Res.string.tts_label_skip_bot_messages,
            checked = skipBotMessages,
            onCheckedChange = onSkipBotMessagesChange,
            enabled = enabled,
        )
        SwitchRow(
            labelRes = Res.string.tts_label_read_usernames,
            checked = readUsernames,
            onCheckedChange = onReadUsernamesChange,
            enabled = enabled,
        )
    }
}

@Composable
private fun SwitchRow(
    labelRes: StringResource,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    enabled: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = stringResource(labelRes)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            // One toggleable node for screen readers: the label names what the switch controls.
            .clearAndSetSemantics { contentDescription = label },
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = label,
            style = typography.sm,
            color = tokens.cardForeground,
            modifier = Modifier.weight(1f),
        )
        Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = enabled)
    }
}

@Composable
private fun VoiceField(value: String, onValueChange: (String) -> Unit, enabled: Boolean) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(stringResource(Res.string.tts_label_voice)) },
    )
}

@Composable
private fun MaxLengthField(
    value: String,
    onValueChange: (String) -> Unit,
    valid: Boolean,
    enabled: Boolean,
) {
    OutlinedTextField(
        value = value,
        onValueChange = onValueChange,
        enabled = enabled,
        singleLine = true,
        isError = !valid,
        modifier = Modifier.fillMaxWidth(),
        label = { Text(stringResource(Res.string.tts_label_max_length)) },
        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
        supportingText =
            if (!valid) {
                { Text(stringResource(Res.string.tts_max_length_invalid)) }
            } else {
                null
            },
    )
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun PermissionPicker(selected: String, onSelect: (String) -> Unit, enabled: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.tts_label_min_permission),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        // Five chips never fit on one row at a phone width, so they wrap rather than clip off-screen.
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            for ((value: String, labelRes: StringResource) in PERMISSIONS) {
                val label: String = stringResource(labelRes)
                FilterChip(
                    selected = selected == value,
                    onClick = { onSelect(value) },
                    enabled = enabled,
                    label = { Text(label, maxLines = 1) },
                    modifier = Modifier.clearAndSetSemantics { contentDescription = label },
                )
            }
        }
    }
}

@Composable
private fun SaveBar(
    saving: Boolean,
    justSaved: Boolean,
    saveError: String?,
    canSave: Boolean,
    onSave: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // The save feedback line: an error takes priority, then the transient "Saved" confirmation.
        when {
            saveError != null ->
                Text(
                    text = stringResource(Res.string.tts_save_error, saveError),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.weight(1f),
                )
            justSaved ->
                Text(
                    text = stringResource(Res.string.tts_saved),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    modifier = Modifier.weight(1f),
                )
            else -> Box(modifier = Modifier.weight(1f))
        }

        if (saving) {
            val savingLabel: String = stringResource(Res.string.tts_saving)
            CircularProgressIndicator(
                modifier = Modifier
                    .size(spacing.s6)
                    .clearAndSetSemantics { contentDescription = savingLabel },
            )
        } else {
            Button(onClick = onSave, enabled = canSave, modifier = Modifier.wrapContentWidth()) {
                Text(stringResource(Res.string.tts_save))
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
                text = stringResource(Res.string.tts_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.tts_retry)) }
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
