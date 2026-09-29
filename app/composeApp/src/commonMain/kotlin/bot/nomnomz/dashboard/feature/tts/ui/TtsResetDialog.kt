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

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.feature.tts.state.TtsResetChange
import bot.nomnomz.dashboard.feature.tts.state.TtsResetField
import bot.nomnomz.dashboard.feature.tts.state.TtsResetValue
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.tts_label_default_provider
import nomnomzbot.composeapp.generated.resources.tts_label_filter_profanity
import nomnomzbot.composeapp.generated.resources.tts_label_max_length
import nomnomzbot.composeapp.generated.resources.tts_label_min_bits
import nomnomzbot.composeapp.generated.resources.tts_label_min_permission
import nomnomzbot.composeapp.generated.resources.tts_label_mod_approval
import nomnomzbot.composeapp.generated.resources.tts_label_mode
import nomnomzbot.composeapp.generated.resources.tts_label_read_usernames
import nomnomzbot.composeapp.generated.resources.tts_label_skip_bot_messages
import nomnomzbot.composeapp.generated.resources.tts_label_viewer_self_service
import nomnomzbot.composeapp.generated.resources.tts_reset_cancel
import nomnomzbot.composeapp.generated.resources.tts_reset_change
import nomnomzbot.composeapp.generated.resources.tts_reset_confirm
import nomnomzbot.composeapp.generated.resources.tts_reset_intro
import nomnomzbot.composeapp.generated.resources.tts_reset_none
import nomnomzbot.composeapp.generated.resources.tts_reset_off
import nomnomzbot.composeapp.generated.resources.tts_reset_on
import nomnomzbot.composeapp.generated.resources.tts_reset_preserved
import nomnomzbot.composeapp.generated.resources.tts_reset_title
import nomnomzbot.composeapp.generated.resources.tts_reset_unchanged
import nomnomzbot.composeapp.generated.resources.tts_toggle_enabled
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The "Reset to defaults" confirm: lists every setting the reset would change as `current → default` BEFORE
// anything is written, and states what it leaves alone. The confirm is the dialog's one primary action and is
// disabled when nothing differs (an empty [changes] means the channel is already at its defaults).
@Composable
internal fun TtsResetDialog(
    changes: List<TtsResetChange>,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current
    val canConfirm: Boolean = changes.isNotEmpty()

    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = stringResource(Res.string.tts_reset_title)) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                if (canConfirm) {
                    Text(text = stringResource(Res.string.tts_reset_intro))
                    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
                        for (change: TtsResetChange in changes) {
                            ResetChangeRow(change)
                        }
                    }
                } else {
                    Text(text = stringResource(Res.string.tts_reset_unchanged))
                }
                Text(text = stringResource(Res.string.tts_reset_preserved), style = typography.sm)
            }
        },
        confirmButton = {
            TextButton(onClick = onConfirm, enabled = canConfirm) {
                Text(
                    text = stringResource(Res.string.tts_reset_confirm),
                    color = if (canConfirm) tokens.primary else tokens.mutedForeground,
                    maxLines = 1,
                )
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(text = stringResource(Res.string.tts_reset_cancel), color = tokens.mutedForeground)
            }
        },
    )
}

// One change: the setting's name on the left, `current → default` on the right.
@Composable
private fun ResetChangeRow(change: TtsResetChange) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = stringResource(resetFieldLabel(change.field)),
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.weight(1f),
        )
        Text(
            text =
                stringResource(
                    Res.string.tts_reset_change,
                    resetValueLabel(change.field, change.current),
                    resetValueLabel(change.field, change.default),
                ),
            style = typography.sm,
            color = tokens.popoverForeground,
        )
    }
}

// Each setting's name reuses the label the General form already shows, so the dialog reads as the same fields.
private fun resetFieldLabel(field: TtsResetField): StringResource =
    when (field) {
        TtsResetField.Enabled -> Res.string.tts_toggle_enabled
        TtsResetField.Mode -> Res.string.tts_label_mode
        TtsResetField.DefaultProvider -> Res.string.tts_label_default_provider
        TtsResetField.MaxCharacters -> Res.string.tts_label_max_length
        TtsResetField.MinPermission -> Res.string.tts_label_min_permission
        TtsResetField.SkipBotMessages -> Res.string.tts_label_skip_bot_messages
        TtsResetField.ReadUsernames -> Res.string.tts_label_read_usernames
        TtsResetField.ProfanityCensor -> Res.string.tts_label_filter_profanity
        TtsResetField.ModApproval -> Res.string.tts_label_mod_approval
        TtsResetField.MinBits -> Res.string.tts_label_min_bits
        TtsResetField.ViewerVoiceSelfService -> Res.string.tts_label_viewer_self_service
    }

// The display text for one side of a change: On/Off, the number (or "None"), or the fixed-set option's own label.
@Composable
private fun resetValueLabel(field: TtsResetField, value: TtsResetValue): String =
    when (value) {
        is TtsResetValue.Flag ->
            stringResource(if (value.on) Res.string.tts_reset_on else Res.string.tts_reset_off)
        is TtsResetValue.Number ->
            value.value?.toString() ?: stringResource(Res.string.tts_reset_none)
        is TtsResetValue.Choice -> {
            val options: List<Pair<String, StringResource>> =
                when (field) {
                    TtsResetField.Mode -> TTS_MODES
                    TtsResetField.DefaultProvider -> TTS_PROVIDERS
                    TtsResetField.MinPermission -> PERMISSIONS
                    else -> emptyList()
                }
            options.firstOrNull { it.first == value.wire }?.let { stringResource(it.second) } ?: value.wire
        }
    }
