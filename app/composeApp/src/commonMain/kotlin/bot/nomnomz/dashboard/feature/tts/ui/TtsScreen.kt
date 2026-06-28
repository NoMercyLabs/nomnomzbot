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
import androidx.compose.material3.ButtonDefaults
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
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
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
import nomnomzbot.composeapp.generated.resources.tts_voices_count
import nomnomzbot.composeapp.generated.resources.tts_voices_default
import nomnomzbot.composeapp.generated.resources.tts_voices_more
import nomnomzbot.composeapp.generated.resources.tts_voices_search
import nomnomzbot.composeapp.generated.resources.tts_voices_use
import nomnomzbot.composeapp.generated.resources.tts_voices_use_action
import nomnomzbot.composeapp.generated.resources.tts_voices_title
import nomnomzbot.composeapp.generated.resources.shell_nav_tts
import nomnomzbot.composeapp.generated.resources.tts_test_error
import nomnomzbot.composeapp.generated.resources.tts_test_play
import nomnomzbot.composeapp.generated.resources.tts_test_prompt
import nomnomzbot.composeapp.generated.resources.tts_test_success
import nomnomzbot.composeapp.generated.resources.tts_test_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The TTS page: an editable form over the channel's text-to-speech configuration — the enabled status plus
// the editable config values, all real data from [TtsController]. The screen seeds a local form from the
// controller's loaded config; Save persists the whole config and the controller echoes the saved values
// back. It loads on first composition and offers a retry on failure.
@Composable
fun TtsScreen(controller: TtsController, role: ManagementRole?) {
    val state: TtsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: TTS gates every config write control at its single Editor manage floor
    // (frontend-ia.md §3). A caller below it reads the current config but every field, toggle, and Save renders
    // disabled with "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Tts)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: TtsState = state) {
            is TtsState.Loading -> CenteredMessage(stringResource(Res.string.tts_loading))
            is TtsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is TtsState.Ready ->
                ReadyContent(
                    state = current,
                    manage = manage,
                    onSave = { edited -> scope.launch { controller.save(edited) } },
                    onTestSpeak = { voiceId, text -> scope.launch { controller.testSpeak(voiceId, text) } },
                )
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
private fun ReadyContent(
    state: TtsState.Ready,
    manage: ManageDecision,
    onSave: (TtsConfig) -> Unit,
    onTestSpeak: (voiceId: String, text: String) -> Unit,
) {
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
        PageHeader(title = stringResource(Res.string.shell_nav_tts))
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
            manage = manage,
            enabled = !state.saving,
        )

        SaveBar(
            saving = state.saving,
            justSaved = state.justSaved,
            saveError = state.saveError,
            canSave = canSave,
            manage = manage,
            onSave = { onSave(edited) },
        )

        VoicePicker(
            voices = state.voices,
            currentVoiceId = defaultVoiceId,
            manage = manage,
            onSelect = { defaultVoiceId = it },
        )

        TestSpeakSection(
            currentVoiceId = defaultVoiceId,
            testing = state.testing,
            testResult = state.testResult,
            testError = state.testError,
            manage = manage,
            onTest = { voiceId, text -> onTestSpeak(voiceId, text) },
        )
    }
}

// A searchable voice picker under the form: the count + current selection, then a search box. Typing filters
// the channel's voices by display name / locale / id and shows the top matches (capped, so a provider's
// hundreds never flood the page); "Use" sets the defaultVoiceId field above. Nothing renders if no voices load.
@Composable
private fun VoicePicker(
    voices: List<TtsVoice>,
    currentVoiceId: String,
    manage: ManageDecision,
    onSelect: (String) -> Unit,
) {
    if (voices.isEmpty()) return

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var query: String by remember { mutableStateOf("") }
    val trimmed: String = query.trim()
    val matches: List<TtsVoice> =
        if (trimmed.isBlank()) {
            emptyList()
        } else {
            voices.filter {
                it.displayName.contains(trimmed, ignoreCase = true) ||
                    it.locale.contains(trimmed, ignoreCase = true) ||
                    it.id.contains(trimmed, ignoreCase = true)
            }
        }
    val shown: List<TtsVoice> = matches.take(8)

    val current: TtsVoice? = voices.firstOrNull { it.id == currentVoiceId }
    val selectedLabel: String? = current?.let { "${it.displayName} (${it.locale})" }

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(
            text = stringResource(Res.string.tts_voices_title),
            style = typography.base,
            color = tokens.cardForeground,
            maxLines = 1,
        )
        Text(
            text = stringResource(Res.string.tts_voices_count, voices.size),
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 1,
        )
        if (selectedLabel != null) {
            Text(
                text = stringResource(Res.string.tts_voices_default, selectedLabel),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        AppTextField(
            value = query,
            onValueChange = { query = it },
            label = stringResource(Res.string.tts_voices_search),
            modifier = Modifier.fillMaxWidth(),
        )
        shown.forEach { voice ->
            VoiceMatchRow(
                voice = voice,
                manage = manage,
                onUse = {
                    onSelect(voice.id)
                    query = ""
                },
            )
        }
        if (matches.size > shown.size) {
            Text(
                text = stringResource(Res.string.tts_voices_more, matches.size),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
    }
}

// One voice match: name + locale/provider, with a "Use" action that sets it as the default voice (Editor floor).
@Composable
private fun VoiceMatchRow(voice: TtsVoice, manage: ManageDecision, onUse: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val useLabel: String = stringResource(Res.string.tts_voices_use_action, voice.displayName)
    val rowDescription: String = "${voice.displayName}, ${voice.locale}, ${voice.provider}"

    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = voice.displayName,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
            )
            Text(
                text = "${voice.locale} · ${voice.provider}",
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        ManageGate(decision = manage) { enabled ->
            TextButton(
                onClick = onUse,
                enabled = enabled,
                modifier = Modifier.semantics { contentDescription = useLabel },
            ) {
                Text(
                    text = stringResource(Res.string.tts_voices_use),
                    color = if (enabled) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        }
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
    manage: ManageDecision,
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
            manage = manage,
            enabled = enabled,
        )
        VoiceField(value = defaultVoiceId, onValueChange = onVoiceChange, manage = manage, enabled = enabled)
        MaxLengthField(
            value = maxLengthText,
            onValueChange = onMaxLengthChange,
            valid = maxLengthValid,
            manage = manage,
            enabled = enabled,
        )
        PermissionPicker(
            selected = minPermission,
            onSelect = onPermissionChange,
            manage = manage,
            enabled = enabled,
        )
        SwitchRow(
            labelRes = Res.string.tts_label_skip_bot_messages,
            checked = skipBotMessages,
            onCheckedChange = onSkipBotMessagesChange,
            manage = manage,
            enabled = enabled,
        )
        SwitchRow(
            labelRes = Res.string.tts_label_read_usernames,
            checked = readUsernames,
            onCheckedChange = onReadUsernamesChange,
            manage = manage,
            enabled = enabled,
        )
    }
}

@Composable
private fun SwitchRow(
    labelRes: StringResource,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    manage: ManageDecision,
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
        ManageGate(decision = manage) { gateEnabled ->
            Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = gateEnabled && enabled)
        }
    }
}

@Composable
private fun VoiceField(
    value: String,
    onValueChange: (String) -> Unit,
    manage: ManageDecision,
    enabled: Boolean,
) {
    ManageGate(decision = manage) { gateEnabled ->
        OutlinedTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = gateEnabled && enabled,
            singleLine = true,
            modifier = Modifier.fillMaxWidth(),
            label = { Text(stringResource(Res.string.tts_label_voice)) },
        )
    }
}

@Composable
private fun MaxLengthField(
    value: String,
    onValueChange: (String) -> Unit,
    valid: Boolean,
    manage: ManageDecision,
    enabled: Boolean,
) {
    ManageGate(decision = manage) { gateEnabled ->
        OutlinedTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = gateEnabled && enabled,
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
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun PermissionPicker(
    selected: String,
    onSelect: (String) -> Unit,
    manage: ManageDecision,
    enabled: Boolean,
) {
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
                ManageGate(decision = manage) { gateEnabled ->
                    FilterChip(
                        selected = selected == value,
                        onClick = { onSelect(value) },
                        enabled = gateEnabled && enabled,
                        label = { Text(label, maxLines = 1) },
                        modifier = Modifier.clearAndSetSemantics { contentDescription = label },
                    )
                }
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
    manage: ManageDecision,
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
            ManageGate(decision = manage) { gateEnabled ->
                Button(
                    onClick = onSave,
                    enabled = gateEnabled && canSave,
                    colors = ButtonDefaults.buttonColors(
                        containerColor = tokens.primary,
                        contentColor = tokens.primaryForeground,
                        disabledContainerColor = tokens.muted,
                        disabledContentColor = tokens.mutedForeground,
                    ),
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(stringResource(Res.string.tts_save))
                }
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

// The test-speak panel: a text input + "Play" button. On success the backend returns base64 audio that the
// dashboard plays via a JS interop data URI. [testResult] carries the audio; [testError] surfaces synthesis
// failures without losing the loaded config. Nothing renders if no voice is selected.
@Composable
private fun TestSpeakSection(
    currentVoiceId: String,
    testing: Boolean,
    testResult: TtsTestResult?,
    testError: String?,
    manage: ManageDecision,
    onTest: (voiceId: String, text: String) -> Unit,
) {
    if (currentVoiceId.isBlank()) return

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var testText: String by remember { mutableStateOf("") }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = stringResource(Res.string.tts_test_title),
            style = typography.lg,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )

        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            AppTextField(
                value = testText,
                onValueChange = { testText = it },
                label = stringResource(Res.string.tts_test_prompt),
                modifier = Modifier.weight(1f),
                enabled = !testing,
            )
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { if (testText.isNotBlank()) onTest(currentVoiceId, testText.trim()) },
                    enabled = enabled && !testing && testText.isNotBlank(),
                    colors = ButtonDefaults.buttonColors(
                        containerColor = tokens.primary,
                        contentColor = tokens.primaryForeground,
                        disabledContainerColor = tokens.muted,
                        disabledContentColor = tokens.mutedForeground,
                    ),
                ) {
                    if (testing) {
                        CircularProgressIndicator(
                            modifier = Modifier.size(spacing.s4),
                            color = tokens.primaryForeground,
                            strokeWidth = spacing.s1,
                        )
                    } else {
                        Text(text = stringResource(Res.string.tts_test_play))
                    }
                }
            }
        }

        testError?.let { error ->
            Text(
                text = stringResource(Res.string.tts_test_error, error),
                style = typography.sm,
                color = tokens.destructive,
            )
        }

        testResult?.let { result ->
            Text(
                text = stringResource(Res.string.tts_test_success, result.provider, result.durationMs),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
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
