// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.sound.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Slider
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ChannelAudioMix
import bot.nomnomz.dashboard.feature.sound.state.MixState
import kotlin.math.roundToInt
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.sound_clips_retry
import nomnomzbot.composeapp.generated.resources.sound_mix_error
import nomnomzbot.composeapp.generated.resources.sound_mix_help
import nomnomzbot.composeapp.generated.resources.sound_mix_loading
import nomnomzbot.composeapp.generated.resources.sound_mix_master_label
import nomnomzbot.composeapp.generated.resources.sound_mix_title
import nomnomzbot.composeapp.generated.resources.sound_mix_tts_label
import org.jetbrains.compose.resources.stringResource

// The channel's master and text-to-speech volume. The bot applies both to every clip and TTS line, so the balance
// stays the same on every streaming PC that opens the overlay. No buttons: a slider saves when the drag ends.
@Composable
internal fun ChannelVolumeCard(
    mix: MixState,
    enabled: Boolean,
    onSave: suspend (master: Int, tts: Int) -> Unit,
    onRetry: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(
                    text = stringResource(Res.string.sound_mix_title),
                    style = typography.base,
                    color = tokens.cardForeground,
                )
                Text(
                    text = stringResource(Res.string.sound_mix_help),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            }
            when (mix) {
                is MixState.Loading ->
                    Text(
                        text = stringResource(Res.string.sound_mix_loading),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                is MixState.Error -> {
                    Text(
                        text = stringResource(Res.string.sound_mix_error, mix.detail),
                        style = typography.sm,
                        color = tokens.destructive,
                    )
                    TextButton(onClick = onRetry) {
                        Text(text = stringResource(Res.string.sound_clips_retry), color = tokens.mutedForeground)
                    }
                }
                is MixState.Ready -> MixSliders(mix = mix.mix, enabled = enabled, onSave = onSave)
            }
        }
    }
}

@Composable
private fun MixSliders(
    mix: ChannelAudioMix,
    enabled: Boolean,
    onSave: suspend (master: Int, tts: Int) -> Unit,
) {
    val spacing = LocalSpacing.current
    val scope = rememberCoroutineScope()

    // The dragged value stays on screen while the save runs; once it ends the stored mix shows again, so a failed
    // save snaps back to what the bot really applies.
    var masterDraft: Float? by remember { mutableStateOf(null) }
    var ttsDraft: Float? by remember { mutableStateOf(null) }
    val master: Float = masterDraft ?: mix.masterVolume.toFloat()
    val tts: Float = ttsDraft ?: mix.ttsVolume.toFloat()

    fun save() {
        val masterValue: Int = master.roundToInt()
        val ttsValue: Int = tts.roundToInt()
        scope.launch {
            onSave(masterValue, ttsValue)
            masterDraft = null
            ttsDraft = null
        }
    }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        VolumeRow(
            label = stringResource(Res.string.sound_mix_master_label, master.roundToInt()),
            value = master,
            enabled = enabled,
            onValueChange = { masterDraft = it },
            onValueChangeFinished = ::save,
        )
        VolumeRow(
            label = stringResource(Res.string.sound_mix_tts_label, tts.roundToInt()),
            value = tts,
            enabled = enabled,
            onValueChange = { ttsDraft = it },
            onValueChangeFinished = ::save,
        )
    }
}

@Composable
private fun VolumeRow(
    label: String,
    value: Float,
    enabled: Boolean,
    onValueChange: (Float) -> Unit,
    onValueChangeFinished: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        Slider(
            value = value,
            onValueChange = onValueChange,
            enabled = enabled,
            valueRange = 0f..100f,
            onValueChangeFinished = onValueChangeFinished,
        )
    }
}
