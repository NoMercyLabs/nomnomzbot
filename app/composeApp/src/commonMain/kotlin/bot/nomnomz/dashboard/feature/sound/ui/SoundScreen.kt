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

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.ButtonDefaults
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CheckboxDefaults
import androidx.compose.material3.OutlinedTextField
import androidx.compose.material3.OutlinedTextFieldDefaults
import androidx.compose.material3.Slider
import androidx.compose.material3.SliderDefaults
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.material3.TextFieldColors
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
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.TrashGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.network.SoundClip
import bot.nomnomz.dashboard.feature.shell.ui.SoundClipsGlyph
import bot.nomnomz.dashboard.feature.sound.state.SoundController
import bot.nomnomz.dashboard.feature.sound.state.SoundState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.shell_nav_sound
import nomnomzbot.composeapp.generated.resources.sound_clips_action_error
import nomnomzbot.composeapp.generated.resources.sound_clips_delete_action
import nomnomzbot.composeapp.generated.resources.sound_clips_delete_cancel
import nomnomzbot.composeapp.generated.resources.sound_clips_delete_confirm
import nomnomzbot.composeapp.generated.resources.sound_clips_delete_message
import nomnomzbot.composeapp.generated.resources.sound_clips_delete_title
import nomnomzbot.composeapp.generated.resources.sound_clips_dialog_cancel
import nomnomzbot.composeapp.generated.resources.sound_clips_dialog_display_name_label
import nomnomzbot.composeapp.generated.resources.sound_clips_dialog_edit_title
import nomnomzbot.composeapp.generated.resources.sound_clips_dialog_enabled_label
import nomnomzbot.composeapp.generated.resources.sound_clips_dialog_save
import nomnomzbot.composeapp.generated.resources.sound_clips_dialog_volume_label
import nomnomzbot.composeapp.generated.resources.sound_clips_disabled_badge
import nomnomzbot.composeapp.generated.resources.sound_clips_duration_ms
import nomnomzbot.composeapp.generated.resources.sound_clips_edit_action
import nomnomzbot.composeapp.generated.resources.sound_clips_empty
import nomnomzbot.composeapp.generated.resources.sound_clips_error
import nomnomzbot.composeapp.generated.resources.sound_clips_loading
import nomnomzbot.composeapp.generated.resources.sound_clips_preview_action
import nomnomzbot.composeapp.generated.resources.sound_clips_retry
import nomnomzbot.composeapp.generated.resources.sound_clips_size_kb
import nomnomzbot.composeapp.generated.resources.sound_clips_volume_pct
import org.jetbrains.compose.resources.stringResource

// The Sound Clips page: the channel's uploaded audio library. Lists real clips from the backend; brokers
// enable/disable, display-name rename, volume adjustment, preview, and delete back through SoundController.
// Upload is not yet surfaced here — users can upload via the REST API or Scalar docs until a file-picker
// is added to the Compose dashboard.
@Composable
fun SoundScreen(controller: SoundController, role: ManagementRole?) {
    val state: SoundState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.SoundClips)

    var editTarget: SoundClip? by remember { mutableStateOf(null) }
    var deleteTarget: SoundClip? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: SoundState = state) {
            is SoundState.Loading -> CenteredMessage(stringResource(Res.string.sound_clips_loading))
            is SoundState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is SoundState.Empty ->
                ClipList(
                    clips = emptyList(),
                    actionError = null,
                    manage = manage,
                    onEdit = { clip -> editTarget = clip },
                    onDelete = { clip -> deleteTarget = clip },
                    onPreview = { clip -> scope.launch { controller.previewClip(clip.id) } },
                )
            is SoundState.Ready ->
                ClipList(
                    clips = current.clips,
                    actionError = current.actionError,
                    manage = manage,
                    onEdit = { clip -> editTarget = clip },
                    onDelete = { clip -> deleteTarget = clip },
                    onPreview = { clip -> scope.launch { controller.previewClip(clip.id) } },
                )
        }
    }

    editTarget?.let { clip ->
        EditClipDialog(
            clip = clip,
            onDismiss = { editTarget = null },
            onSave = { displayName, volume, isEnabled ->
                scope.launch {
                    controller.updateClip(
                        id = clip.id,
                        displayName = displayName,
                        defaultVolume = volume,
                        isEnabled = isEnabled,
                    )
                    editTarget = null
                }
            },
        )
    }

    deleteTarget?.let { clip ->
        ConfirmDialog(
            title = stringResource(Res.string.sound_clips_delete_title),
            message = stringResource(Res.string.sound_clips_delete_message, clip.displayName),
            confirmLabel = stringResource(Res.string.sound_clips_delete_confirm),
            dismissLabel = stringResource(Res.string.sound_clips_delete_cancel),
            destructive = true,
            onConfirm = {
                scope.launch { controller.deleteClip(clip.id) }
                deleteTarget = null
            },
            onDismiss = { deleteTarget = null },
        )
    }
}

@Composable
private fun ClipList(
    clips: List<SoundClip>,
    actionError: String?,
    manage: ManageDecision,
    onEdit: (SoundClip) -> Unit,
    onDelete: (SoundClip) -> Unit,
    onPreview: (SoundClip) -> Unit,
) {
    val spacing = LocalSpacing.current

    Column(modifier = Modifier.fillMaxSize()) {
        PageHeader(title = stringResource(Res.string.shell_nav_sound))
        if (actionError != null) {
            ActionErrorBanner(
                message = stringResource(Res.string.sound_clips_action_error, actionError),
                modifier = Modifier.padding(bottom = spacing.s4),
            )
        }
        if (clips.isEmpty()) {
            CenteredMessage(stringResource(Res.string.sound_clips_empty))
        } else {
            LazyColumn(
                contentPadding = PaddingValues(vertical = spacing.s2),
                verticalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                items(clips, key = { it.id }) { clip ->
                    ClipRow(
                        clip = clip,
                        manage = manage,
                        onEdit = { onEdit(clip) },
                        onDelete = { onDelete(clip) },
                        onPreview = { onPreview(clip) },
                    )
                }
            }
        }
    }
}

@Composable
private fun ClipRow(
    clip: SoundClip,
    manage: ManageDecision,
    onEdit: () -> Unit,
    onDelete: () -> Unit,
    onPreview: () -> Unit,
) {
    val spacing = LocalSpacing.current
    val tokens: Tokens = LocalTokens.current
    val typography = LocalTypography.current

    val previewLabel: String = stringResource(Res.string.sound_clips_preview_action, clip.displayName)
    val editLabel: String = stringResource(Res.string.sound_clips_edit_action, clip.displayName)
    val deleteLabel: String = stringResource(Res.string.sound_clips_delete_action, clip.displayName)

    Row(
        modifier =
            Modifier.fillMaxWidth()
                .clip(RoundedCornerShape(spacing.s2))
                .background(tokens.card)
                .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = clip.displayName,
                    style = typography.base,
                    color = tokens.foreground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (!clip.isEnabled) {
                    Text(
                        text = stringResource(Res.string.sound_clips_disabled_badge),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
            }
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s3),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(
                    text = clip.name,
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                    overflow = TextOverflow.Ellipsis,
                )
                if (clip.durationMs > 0) {
                    Text(
                        text = stringResource(Res.string.sound_clips_duration_ms, clip.durationMs),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
                if (clip.sizeBytes > 0) {
                    Text(
                        text = stringResource(Res.string.sound_clips_size_kb, (clip.sizeBytes / 1024).toInt()),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                }
                Text(
                    text = stringResource(Res.string.sound_clips_volume_pct, clip.defaultVolume),
                    style = typography.xs,
                    color = tokens.mutedForeground,
                )
            }
        }

        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = SoundClipsGlyph,
                label = previewLabel,
                onClick = onPreview,
                enabled = enabled,
            )
            GlyphButton(imageVector = EditGlyph, label = editLabel, onClick = onEdit, enabled = enabled)
            GlyphButton(
                imageVector = TrashGlyph,
                label = deleteLabel,
                onClick = onDelete,
                enabled = enabled,
                tint = tokens.destructive,
            )
        }
    }
}

@Composable
private fun EditClipDialog(
    clip: SoundClip,
    onDismiss: () -> Unit,
    onSave: (String, Int, Boolean) -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    var displayName: String by remember(clip.id) { mutableStateOf(clip.displayName) }
    var volume: Float by remember(clip.id) { mutableStateOf(clip.defaultVolume.toFloat()) }
    var isEnabled: Boolean by remember(clip.id) { mutableStateOf(clip.isEnabled) }

    val fieldColors: TextFieldColors =
        OutlinedTextFieldDefaults.colors(
            focusedTextColor = tokens.foreground,
            unfocusedTextColor = tokens.foreground,
            focusedBorderColor = tokens.border,
            unfocusedBorderColor = tokens.border,
            focusedLabelColor = tokens.mutedForeground,
            unfocusedLabelColor = tokens.mutedForeground,
        )

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        title = {
            Text(
                stringResource(Res.string.sound_clips_dialog_edit_title),
                style = typography.xl,
                color = tokens.foreground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s4)) {
                OutlinedTextField(
                    value = displayName,
                    onValueChange = { displayName = it },
                    label = { Text(stringResource(Res.string.sound_clips_dialog_display_name_label)) },
                    colors = fieldColors,
                    singleLine = true,
                    modifier = Modifier.fillMaxWidth(),
                )
                Column {
                    Text(
                        text = stringResource(Res.string.sound_clips_dialog_volume_label, volume.toInt()),
                        style = typography.sm,
                        color = tokens.mutedForeground,
                    )
                    Slider(
                        value = volume,
                        onValueChange = { volume = it },
                        valueRange = 0f..100f,
                        steps = 9,
                        colors =
                            SliderDefaults.colors(
                                thumbColor = tokens.primary,
                                activeTrackColor = tokens.primary,
                                inactiveTrackColor = tokens.muted,
                            ),
                    )
                }
                Row(
                    verticalAlignment = Alignment.CenterVertically,
                    horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                ) {
                    Checkbox(
                        checked = isEnabled,
                        onCheckedChange = { isEnabled = it },
                        colors =
                            CheckboxDefaults.colors(
                                checkedColor = tokens.primary,
                                uncheckedColor = tokens.mutedForeground,
                            ),
                    )
                    Text(
                        text = stringResource(Res.string.sound_clips_dialog_enabled_label),
                        style = typography.base,
                        color = tokens.foreground,
                    )
                }
            }
        },
        confirmButton = {
            Button(
                onClick = { onSave(displayName.trim(), volume.toInt(), isEnabled) },
                enabled = displayName.isNotBlank(),
                colors =
                    ButtonDefaults.buttonColors(
                        containerColor = tokens.primary,
                        contentColor = tokens.primaryForeground,
                    ),
            ) {
                Text(stringResource(Res.string.sound_clips_dialog_save))
            }
        },
        dismissButton = {
            TextButton(onClick = onDismiss) {
                Text(
                    stringResource(Res.string.sound_clips_dialog_cancel),
                    color = tokens.mutedForeground,
                )
            }
        },
    )
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens: Tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    Column(
        modifier = Modifier.fillMaxSize(),
        horizontalAlignment = Alignment.CenterHorizontally,
        verticalArrangement = Arrangement.Center,
    ) {
        Text(
            text = stringResource(Res.string.sound_clips_error, detail),
            style = typography.base,
            color = tokens.destructive,
        )
        TextButton(onClick = onRetry, modifier = Modifier.padding(top = spacing.s2)) {
            Text(stringResource(Res.string.sound_clips_retry), color = tokens.primary)
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens: Tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
