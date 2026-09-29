// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.ui

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardType
import bot.nomnomz.dashboard.core.designsystem.PermissionRungs
import bot.nomnomz.dashboard.core.designsystem.component.AppSelectField
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.Dialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogDescription
import bot.nomnomz.dashboard.core.designsystem.component.DialogFooter
import bot.nomnomz.dashboard.core.designsystem.component.DialogTitle
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.InlineError
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.BuiltinCommand
import bot.nomnomz.dashboard.core.network.BuiltinReplyGroup
import bot.nomnomz.dashboard.feature.commands.state.BuiltinDetailController
import bot.nomnomz.dashboard.feature.commands.state.BuiltinDetailState
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesController
import bot.nomnomz.dashboard.feature.commands.state.BuiltinRepliesState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.builtin_detail_cooldown
import nomnomzbot.composeapp.generated.resources.builtin_detail_cooldown_hint
import nomnomzbot.composeapp.generated.resources.builtin_detail_cooldown_invalid
import nomnomzbot.composeapp.generated.resources.builtin_detail_enabled
import nomnomzbot.composeapp.generated.resources.builtin_detail_intro
import nomnomzbot.composeapp.generated.resources.builtin_detail_permission
import nomnomzbot.composeapp.generated.resources.builtin_detail_permission_default
import nomnomzbot.composeapp.generated.resources.builtin_detail_permission_floor
import nomnomzbot.composeapp.generated.resources.builtin_detail_replies
import nomnomzbot.composeapp.generated.resources.builtin_detail_reserved
import nomnomzbot.composeapp.generated.resources.builtin_detail_reset
import nomnomzbot.composeapp.generated.resources.builtin_detail_reset_confirm
import nomnomzbot.composeapp.generated.resources.builtin_detail_reset_message
import nomnomzbot.composeapp.generated.resources.builtin_detail_reset_title
import nomnomzbot.composeapp.generated.resources.builtin_detail_save
import nomnomzbot.composeapp.generated.resources.builtin_detail_settings
import nomnomzbot.composeapp.generated.resources.builtin_detail_title
import nomnomzbot.composeapp.generated.resources.builtin_detail_tts
import nomnomzbot.composeapp.generated.resources.builtin_replies_cancel
import nomnomzbot.composeapp.generated.resources.builtin_replies_close
import org.jetbrains.compose.resources.stringResource

/**
 * A built-in command's full edit surface (commands-pipelines.md §4.5 + §11): whether it answers in chat, whether
 * TTS reads it out, its cooldown, who can use it, and every reply it sends — with one reset that puts all of it
 * back. "Save settings" is the settings group's one primary action; the reply rows keep their own quiet
 * edit/save flow; "Reset to defaults" is a destructive-ghost that first shows exactly what it will undo.
 * Sections are separated, never nested in rounded cards, so no radius sits inside the dialog's own.
 */
@Composable
fun BuiltinDetailDialog(
    detail: BuiltinDetailState,
    detailController: BuiltinDetailController,
    replies: BuiltinRepliesState,
    repliesController: BuiltinRepliesController,
) {
    val openKey: String = detail.openKey ?: return
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val scope = rememberCoroutineScope()
    var confirmReset: Boolean by remember { mutableStateOf(false) }
    val close: () -> Unit = {
        detailController.close()
        repliesController.close()
    }

    Dialog(onDismissRequest = close) {
        DialogTitle(text = stringResource(Res.string.builtin_detail_title, "!$openKey"))
        DialogDescription(text = stringResource(Res.string.builtin_detail_intro))
        detail.error?.let { InlineError(message = it) }
        val builtin: BuiltinCommand? = detail.builtin
        if (builtin == null) {
            if (detail.loading) Spinner(color = tokens.mutedForeground)
        } else {
            Column(
                modifier = Modifier.weight(1f, fill = false).verticalScroll(rememberScrollState()),
                verticalArrangement = Arrangement.spacedBy(spacing.s4),
            ) {
                SectionHeading(text = stringResource(Res.string.builtin_detail_settings))
                SettingsSection(builtin = builtin, detail = detail, controller = detailController)
                Separator()
                SectionHeading(text = stringResource(Res.string.builtin_detail_replies))
                BuiltinReplyList(state = replies, controller = repliesController)
            }
        }
        DialogFooter {
            if (builtin != null && !builtin.isReserved) {
                Button(
                    onClick = { confirmReset = true },
                    variant = ButtonVariant.DestructiveGhost,
                    enabled = !detail.saving,
                    modifier = Modifier.testTag("builtin-reset"),
                ) {
                    Text(text = stringResource(Res.string.builtin_detail_reset), maxLines = 1)
                }
                Spacer(modifier = Modifier.weight(1f))
            }
            Button(onClick = close, variant = ButtonVariant.Outline) {
                Text(text = stringResource(Res.string.builtin_replies_close), maxLines = 1)
            }
        }
    }

    val builtin: BuiltinCommand = detail.builtin ?: return
    if (confirmReset) {
        // The consequence is spelled out before anything changes: every default it returns to, and how many of
        // the channel's own replies go back to their default text (counted from the live reply list).
        val rewordedReplies: Int =
            replies.visibleGroups.flatMap(BuiltinReplyGroup::replies).count { it.isOverridden }
        ConfirmDialog(
            title = stringResource(Res.string.builtin_detail_reset_title, builtin.name),
            message =
                stringResource(
                    Res.string.builtin_detail_reset_message,
                    builtin.name,
                    builtin.defaultCooldownSeconds,
                    stringResource(PermissionRungs.labelOf(builtin.defaultMinPermissionLevel)),
                    rewordedReplies,
                ),
            confirmLabel = stringResource(Res.string.builtin_detail_reset_confirm),
            dismissLabel = stringResource(Res.string.builtin_replies_cancel),
            destructive = true,
            onConfirm = {
                confirmReset = false
                scope.launch {
                    detailController.reset()
                    repliesController.open(builtin.replyGroup.ifBlank { builtin.builtinKey })
                }
            },
            onDismiss = { confirmReset = false },
        )
    }
}

@Composable
private fun SectionHeading(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Text(text = text, style = typography.sm.copy(fontWeight = FontWeight.SemiBold), color = tokens.popoverForeground)
}

@Composable
private fun SettingsSection(builtin: BuiltinCommand, detail: BuiltinDetailState, controller: BuiltinDetailController) {
    val spacing = LocalSpacing.current
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val scope = rememberCoroutineScope()

    if (builtin.isReserved) {
        Text(
            text = stringResource(Res.string.builtin_detail_reserved),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        return
    }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        SettingSwitch(
            label = stringResource(Res.string.builtin_detail_enabled),
            checked = builtin.isEnabled,
            enabled = !detail.saving,
            onCheckedChange = { on -> scope.launch { controller.setEnabled(on) } },
        )
        SettingSwitch(
            label = stringResource(Res.string.builtin_detail_tts),
            checked = builtin.speakWithTts,
            enabled = !detail.saving,
            onCheckedChange = { on -> scope.launch { controller.setSpeakWithTts(on) } },
        )
        AppTextField(
            value = detail.cooldownText,
            onValueChange = controller::editCooldown,
            label = stringResource(Res.string.builtin_detail_cooldown),
            placeholder = builtin.defaultCooldownSeconds.toString(),
            supportingText = stringResource(Res.string.builtin_detail_cooldown_hint, builtin.defaultCooldownSeconds),
            isError = !detail.cooldownValid,
            errorText = if (detail.cooldownValid) null else stringResource(Res.string.builtin_detail_cooldown_invalid),
            enabled = !detail.saving,
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            modifier = Modifier.fillMaxWidth().testTag("builtin-cooldown"),
        )
        PermissionPicker(builtin = builtin, detail = detail, controller = controller)
        Row(modifier = Modifier.fillMaxWidth(), horizontalArrangement = Arrangement.End) {
            Button(
                onClick = { scope.launch { controller.saveSettings() } },
                enabled = detail.settingsDirty && detail.cooldownValid,
                loading = detail.saving,
                modifier = Modifier.testTag("builtin-save"),
            ) {
                Text(text = stringResource(Res.string.builtin_detail_save), maxLines = 1)
            }
        }
    }
}

@Composable
private fun PermissionPicker(builtin: BuiltinCommand, detail: BuiltinDetailState, controller: BuiltinDetailController) {
    val tokens = LocalTokens.current
    var open: Boolean by remember { mutableStateOf(false) }
    val defaultLabel: String =
        stringResource(
            Res.string.builtin_detail_permission_default,
            stringResource(PermissionRungs.labelOf(builtin.defaultMinPermissionLevel)),
        )
    val floorNote: String? =
        if (builtin.defaultMinPermissionLevel.equals(PermissionRungs.Everyone, ignoreCase = true)) {
            null
        } else {
            stringResource(
                Res.string.builtin_detail_permission_floor,
                stringResource(PermissionRungs.labelOf(builtin.defaultMinPermissionLevel)),
            )
        }

    AppSelectField(
        label = stringResource(Res.string.builtin_detail_permission),
        value = detail.permission?.let { stringResource(PermissionRungs.labelOf(it)) } ?: defaultLabel,
        expanded = open,
        onExpandedChange = { open = it },
        enabled = !detail.saving,
        supportingText = floorNote,
        modifier = Modifier.fillMaxWidth().testTag("builtin-permission"),
    ) {
        DropdownMenuItem(
            text = { Text(defaultLabel, color = tokens.cardForeground) },
            onClick = {
                controller.editPermission(null)
                open = false
            },
        )
        detail.permissionChoices.forEach { rung ->
            DropdownMenuItem(
                text = { Text(stringResource(PermissionRungs.labelOf(rung)), color = tokens.cardForeground) },
                onClick = {
                    controller.editPermission(rung)
                    open = false
                },
            )
        }
    }
}

@Composable
private fun SettingSwitch(label: String, checked: Boolean, enabled: Boolean, onCheckedChange: (Boolean) -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    Row(
        modifier = Modifier.fillMaxWidth(),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.SpaceBetween,
    ) {
        Text(text = label, style = typography.sm, color = tokens.popoverForeground)
        Switch(
            checked = checked,
            onCheckedChange = onCheckedChange,
            enabled = enabled,
            modifier = Modifier.semantics { contentDescription = label },
        )
    }
}
