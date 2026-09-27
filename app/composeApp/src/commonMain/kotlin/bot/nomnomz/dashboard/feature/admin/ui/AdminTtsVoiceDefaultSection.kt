// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonSize
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.Select
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TtsVoiceCandidate
import bot.nomnomz.dashboard.core.network.TtsVoiceDefault
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsController
import bot.nomnomz.dashboard.feature.admin.state.PlatformDefaultsState
import bot.nomnomz.dashboard.feature.admin.state.TtsVoiceDefaultEdit
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.platform_defaults_apply
import nomnomzbot.composeapp.generated.resources.platform_defaults_cancel
import nomnomzbot.composeapp.generated.resources.platform_defaults_change
import nomnomzbot.composeapp.generated.resources.platform_defaults_check_first
import nomnomzbot.composeapp.generated.resources.platform_defaults_check_impact
import nomnomzbot.composeapp.generated.resources.platform_defaults_voice_counts
import nomnomzbot.composeapp.generated.resources.platform_defaults_voice_current
import nomnomzbot.composeapp.generated.resources.platform_defaults_voice_edit_title
import nomnomzbot.composeapp.generated.resources.platform_defaults_voice_missing
import nomnomzbot.composeapp.generated.resources.platform_defaults_voice_pick
import org.jetbrains.compose.resources.stringResource

/**
 * The platform default TTS voice: the one catalogue voice every channel that never picked its own speaks with.
 * One neutral card with a quiet "Change"; the editor's apply is the single primary action of the flow.
 */
@Composable
internal fun TtsVoiceDefaultSection(state: PlatformDefaultsState, controller: PlatformDefaultsController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val current: TtsVoiceDefault? = state.voiceDefault

    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
                Text(
                    text = if (current != null) {
                        stringResource(Res.string.platform_defaults_voice_current, current.displayName, current.locale)
                    } else {
                        stringResource(Res.string.platform_defaults_voice_missing)
                    },
                    style = typography.sm,
                    color = tokens.cardForeground,
                )
                if (current != null) {
                    Text(
                        text = stringResource(
                            Res.string.platform_defaults_voice_counts,
                            current.channelsFollowing,
                            current.channelsWithOwnVoice,
                        ),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
            Button(
                onClick = controller::openVoiceEdit,
                variant = ButtonVariant.Outline,
                size = ButtonSize.Sm,
                enabled = state.voiceCandidates.isNotEmpty(),
            ) {
                Text(text = stringResource(Res.string.platform_defaults_change), maxLines = 1)
            }
        }
    }
    state.voiceEdit?.let { edit -> TtsVoiceDefaultEditDialog(state = state, edit = edit, controller = controller) }
}

// The picker line for one candidate: name, locale and gender. The separators are typographic, not language-bound.
private fun candidateLabel(candidate: TtsVoiceCandidate): String =
    "${candidate.displayName} · ${candidate.locale} · ${candidate.gender}"

/**
 * The editor for the platform voice. A different pick drops the previous count, so "Check impact" (outline)
 * must run for exactly this voice before the single primary "Apply to N channels" arms.
 */
@Composable
internal fun TtsVoiceDefaultEditDialog(
    state: PlatformDefaultsState,
    edit: TtsVoiceDefaultEdit,
    controller: PlatformDefaultsController,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()
    val busy: Boolean = edit.saving || edit.previewing
    var expanded: Boolean by remember { mutableStateOf(false) }
    val picked: TtsVoiceCandidate? = state.voiceCandidates.firstOrNull { it.voiceId == edit.voiceId }

    Dialog(onDismissRequest = controller::dismissVoiceEdit) {
        DialogTitle(text = stringResource(Res.string.platform_defaults_voice_edit_title))
        Select(
            value = picked,
            options = state.voiceCandidates,
            onValueChange = { controller.pickVoice(it.voiceId) },
            label = stringResource(Res.string.platform_defaults_voice_pick),
            optionLabel = ::candidateLabel,
            modifier = Modifier.fillMaxWidth(),
            expanded = expanded,
            onExpandedChange = { expanded = it },
            enabled = !busy,
        )
        if (edit.preview != null || edit.previewing) {
            PlatformDefaultBlastRadiusText(preview = edit.preview)
        } else {
            Text(
                text = stringResource(Res.string.platform_defaults_check_first),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
        DialogFooter {
            Button(onClick = controller::dismissVoiceEdit, variant = ButtonVariant.Ghost, enabled = !edit.saving) {
                Text(text = stringResource(Res.string.platform_defaults_cancel), maxLines = 1)
            }
            Button(
                onClick = { scope.launch { controller.previewVoiceEdit() } },
                variant = ButtonVariant.Outline,
                enabled = !busy && picked != null && edit.preview == null,
                loading = edit.previewing,
            ) {
                Text(text = stringResource(Res.string.platform_defaults_check_impact), maxLines = 1)
            }
            Button(
                onClick = { scope.launch { controller.saveVoiceEdit() } },
                enabled = edit.canSave,
                loading = edit.saving,
            ) {
                Text(
                    text = stringResource(Res.string.platform_defaults_apply, edit.preview?.channelsAffected ?: 0),
                    maxLines = 1,
                )
            }
        }
    }
}
