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

import androidx.compose.runtime.Composable
import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import bot.nomnomz.dashboard.core.network.TtsConfig

// The editable TTS settings, shared by the four TTS tabs. The Live, Voices, Rules and Provider tabs each edit a
// slice of one [TtsConfig] and every Save sends the whole config, so the edits live here, above the tab bodies:
// a change made on one tab survives a switch to another tab before Save. A new [loaded] config (first load or a
// successful save) builds a fresh form, so "differs from loaded" is always measured against what is saved.
@Stable
internal class TtsForm(val loaded: TtsConfig) {
    var isEnabled: Boolean by mutableStateOf(loaded.isEnabled)
    var mode: String by mutableStateOf(loaded.mode)
    var defaultProvider: String by mutableStateOf(loaded.defaultProvider)
    var defaultVoiceId: String by mutableStateOf(loaded.defaultVoiceId ?: "")
    var maxLengthText: String by mutableStateOf(loaded.maxCharacters.toString())
    var minPermission: String by mutableStateOf(loaded.minPermission)
    var skipBotMessages: Boolean by mutableStateOf(loaded.skipBotMessages)
    var readUsernames: Boolean by mutableStateOf(loaded.readUsernames)
    var profanityCensorEnabled: Boolean by mutableStateOf(loaded.profanityCensorEnabled)
    var modApprovalRequired: Boolean by mutableStateOf(loaded.modApprovalRequired)
    // Empty text = no bits gate (minBitsToTts null). A blank field is valid; a non-numeric one is not.
    var minBitsText: String by mutableStateOf(loaded.minBitsToTts?.toString() ?: "")
    var viewerVoiceSelfService: Boolean by mutableStateOf(loaded.viewerVoiceSelfServiceEnabled)

    val maxLengthValid: Boolean
        get() = maxLengthText.toIntOrNull()?.let { it in 1..500 } == true

    // Blank clears the gate (null); otherwise it must parse to a non-negative int.
    val minBitsValid: Boolean
        get() = minBitsText.isBlank() || (minBitsText.toIntOrNull()?.let { it >= 0 } == true)

    // copy() keeps the fields the form does not edit (BYOK flags) at their loaded values, so the
    // differs-from-loaded check and the full-config save never clobber them.
    val edited: TtsConfig
        get() =
            loaded.copy(
                isEnabled = isEnabled,
                mode = mode,
                defaultProvider = defaultProvider,
                defaultVoiceId = defaultVoiceId.ifBlank { null },
                maxCharacters = maxLengthText.toIntOrNull() ?: loaded.maxCharacters,
                minPermission = minPermission,
                skipBotMessages = skipBotMessages,
                readUsernames = readUsernames,
                profanityCensorEnabled = profanityCensorEnabled,
                modApprovalRequired = modApprovalRequired,
                minBitsToTts = if (minBitsText.isBlank()) null else (minBitsText.toIntOrNull() ?: loaded.minBitsToTts),
                viewerVoiceSelfServiceEnabled = viewerVoiceSelfService,
            )

    // Save is offered only when the form is valid AND differs from the saved baseline: saving an unchanged
    // config is a no-op the user should not be invited to make.
    fun canSave(saving: Boolean): Boolean = maxLengthValid && minBitsValid && edited != loaded && !saving
}

// A stored `client_edge` (the retired browser voice) is shown as the standard plane. Normalizing the baseline
// keeps Save disabled with no edit, and makes any save send `self_host`.
internal fun TtsConfig.withRetiredModeReplaced(): TtsConfig =
    if (mode == TTS_MODE_RETIRED_BROWSER_VOICE) copy(mode = TTS_MODE_SELF_HOST) else this

@Composable
internal fun rememberTtsForm(loaded: TtsConfig): TtsForm = remember(loaded) { TtsForm(loaded) }
