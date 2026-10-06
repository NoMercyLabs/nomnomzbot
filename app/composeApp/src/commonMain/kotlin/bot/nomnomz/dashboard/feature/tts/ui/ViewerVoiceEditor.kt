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
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.selection.selectable
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.Role
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.resolveRowLabel
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TtsVoice
import kotlinx.coroutines.delay
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_assign
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_clear
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_current
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_default
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_picked
import nomnomzbot.composeapp.generated.resources.tts_voices_default
import nomnomzbot.composeapp.generated.resources.tts_voices_more
import nomnomzbot.composeapp.generated.resources.tts_voices_search
import org.jetbrains.compose.resources.stringResource

// The ONE per-viewer voice editor. The TTS page's Per-viewer tab and the viewer's profile page both render it,
// so a viewer's voice is chosen the same way everywhere: it states which voice the viewer uses now, lets the
// operator search the full voice catalogue, marks the chosen row, and offers Assign (the one primary action)
// plus Clear (only while an override exists). Writes gate at [manage]. It draws no card of its own: the host
// supplies the surface, so nothing nests a rounded card in a rounded card.
@Composable
fun ViewerVoiceEditor(
    userId: String,
    currentVoiceId: String?,
    busy: Boolean,
    error: String?,
    voices: List<TtsVoice>,
    manage: ManageDecision,
    searchAssignableVoices: suspend (query: String) -> List<TtsVoice>,
    onAssign: (voiceId: String) -> Unit,
    onClear: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The row the operator has chosen. It starts on the voice the viewer already has and re-seeds whenever a
    // reload brings a different saved voice, so after Assign the saved voice is the marked one.
    var pickedVoiceId: String by remember(userId, currentVoiceId) { mutableStateOf(currentVoiceId.orEmpty()) }
    var pickedVoice: TtsVoice? by remember(userId, currentVoiceId) { mutableStateOf(null) }

    val currentLabel: String? = currentVoiceId?.let { id -> voices.firstOrNull { it.id == id }?.label() ?: id }

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        error?.let { detail -> Text(text = detail, style = typography.sm, color = tokens.destructive) }
        Text(
            text =
                if (currentLabel == null) {
                    stringResource(Res.string.tts_viewer_voice_default)
                } else {
                    stringResource(Res.string.tts_viewer_voice_current, currentLabel)
                },
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        VoiceSearchList(
            voices = voices,
            pickedVoiceId = pickedVoiceId,
            pickedVoice = pickedVoice,
            manage = manage,
            search = searchAssignableVoices,
            onSelect = { voice ->
                pickedVoiceId = voice.id
                pickedVoice = voice
            },
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { onAssign(pickedVoiceId) },
                    enabled = enabled && pickedVoiceId.isNotBlank() && pickedVoiceId != currentVoiceId && !busy,
                ) {
                    Text(stringResource(Res.string.tts_viewer_voice_assign))
                }
            }
            if (currentVoiceId != null) {
                ManageGate(decision = manage) { enabled ->
                    TextButton(onClick = onClear, enabled = enabled && !busy) {
                        Text(
                            text = stringResource(Res.string.tts_viewer_voice_clear),
                            color = if (enabled) tokens.destructive else tokens.mutedForeground,
                        )
                    }
                }
            }
        }
    }
}

private fun TtsVoice.label(): String = "$displayName ($locale)"

// Searches the full server-side voice catalogue via [search] (the same paginated `GET /tts/voices?q=` the Voices
// tab uses), not the small cached first page in [voices], which only resolves a label for an already-saved voice.
// The matches stay on screen after a pick so the chosen row visibly stays marked.
@Composable
private fun VoiceSearchList(
    voices: List<TtsVoice>,
    pickedVoiceId: String,
    pickedVoice: TtsVoice?,
    manage: ManageDecision,
    search: suspend (query: String) -> List<TtsVoice>,
    onSelect: (TtsVoice) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var query: String by remember { mutableStateOf("") }
    var matches: List<TtsVoice> by remember { mutableStateOf(emptyList()) }
    val trimmed: String = query.trim()

    // Debounce the query and re-run the live search whenever it settles. A blank query clears the results: this
    // list is searched, not browsed (the full catalogue browser is on the Voices tab).
    LaunchedEffect(trimmed) {
        if (trimmed.isBlank()) {
            matches = emptyList()
            return@LaunchedEffect
        }
        delay(300)
        matches = search(trimmed)
    }

    val shown: List<TtsVoice> = matches.take(8)
    val pickedLabel: String? =
        pickedVoiceId
            .takeIf { it.isNotBlank() }
            ?.let { id -> (pickedVoice ?: voices.firstOrNull { it.id == id })?.label() }

    Column(modifier = Modifier.fillMaxWidth(), verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        pickedLabel?.let { label ->
            Text(
                text = stringResource(Res.string.tts_voices_default, label),
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
            Separator()
            VoiceChoiceRow(
                voice = voice,
                selected = voice.id == pickedVoiceId,
                manage = manage,
                onChoose = { onSelect(voice) },
            )
        }
        if (matches.size > shown.size) {
            Separator()
            Text(
                text = stringResource(Res.string.tts_voices_more, matches.size),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
    }
}

// One voice in the list: the whole row is the radio-style choice, and only the chosen row carries the accent
// wash and the "Selected" mark, so no row reads as chosen unless it is.
@Composable
private fun VoiceChoiceRow(voice: TtsVoice, selected: Boolean, manage: ManageDecision, onChoose: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val displayName: String =
        resolveRowLabel(voice.displayName, secondary = voice.name, typeLabel = "Voice", discriminatorSource = voice.id)
    val rowDescription: String = "$displayName, ${voice.locale}, ${voice.provider}"

    ManageGate(decision = manage) { enabled ->
        Row(
            modifier =
                Modifier.fillMaxWidth()
                    .background(if (selected) tokens.accent else tokens.card)
                    .selectable(selected = selected, enabled = enabled, role = Role.RadioButton, onClick = onChoose)
                    .clearAndSetSemantics { contentDescription = rowDescription }
                    .padding(horizontal = spacing.s3, vertical = spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(text = displayName, style = typography.sm, color = tokens.cardForeground, maxLines = 1)
                Text(
                    text = "${voice.locale} · ${voice.provider}",
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
            if (selected) {
                Text(
                    text = stringResource(Res.string.tts_viewer_voice_picked),
                    style = typography.sm,
                    color = tokens.accentForeground,
                    maxLines = 1,
                )
            }
        }
    }
}
