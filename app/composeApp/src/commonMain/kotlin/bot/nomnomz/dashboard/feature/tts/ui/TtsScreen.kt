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
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
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
import nomnomzbot.composeapp.generated.resources.tts_off
import nomnomzbot.composeapp.generated.resources.tts_on
import nomnomzbot.composeapp.generated.resources.tts_retry
import nomnomzbot.composeapp.generated.resources.tts_status_disabled
import nomnomzbot.composeapp.generated.resources.tts_status_enabled
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The TTS page: a read-only summary of the channel's text-to-speech configuration — the enabled status plus the
// labelled config values, all real data from [TtsController]. The screen is a pure projection of the controller's
// state; it loads on first composition and offers a retry on failure. No edit/save/preview actions in this slice.
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
            is TtsState.Ready -> ReadyContent(config = current.config)
        }
    }
}

@Composable
private fun ReadyContent(config: TtsConfig) {
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        StatusBanner(isEnabled = config.isEnabled)
        ConfigCard(config = config)
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
private fun ConfigCard(config: TtsConfig) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(tokens.radius.lg))
            .background(tokens.card)
            .padding(spacing.s4),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        ConfigRow(Res.string.tts_label_voice, config.defaultVoiceId)
        ConfigRow(Res.string.tts_label_min_permission, config.minPermission)
        ConfigRow(Res.string.tts_label_max_length, config.maxLength.toString())
        ConfigRow(Res.string.tts_label_skip_bot_messages, onOffLabel(config.skipBotMessages))
        ConfigRow(Res.string.tts_label_read_usernames, onOffLabel(config.readUsernames))
    }
}

@Composable
private fun ConfigRow(labelRes: StringResource, value: String) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = stringResource(labelRes)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            // One node for screen readers: "Voice: Brian" rather than two disconnected texts.
            .clearAndSetSemantics { contentDescription = "$label: $value" },
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = label,
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.weight(1f),
        )
        Text(text = value, style = typography.sm, color = tokens.cardForeground)
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

/** Render a boolean flag as the localized on/off label (never raw "true"/"false"). */
@Composable
private fun onOffLabel(value: Boolean): String =
    stringResource(if (value) Res.string.tts_on else Res.string.tts_off)
