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

import androidx.compose.runtime.Composable
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DialogResult
import bot.nomnomz.dashboard.core.network.CommandPreset
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.feature.commands.state.PresetResetField
import bot.nomnomz.dashboard.feature.commands.state.presetResetChanges
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.commands_delete_cancel
import nomnomzbot.composeapp.generated.resources.commands_preset_field_aliases
import nomnomzbot.composeapp.generated.resources.commands_preset_field_cooldown
import nomnomzbot.composeapp.generated.resources.commands_preset_field_description
import nomnomzbot.composeapp.generated.resources.commands_preset_field_matching
import nomnomzbot.composeapp.generated.resources.commands_preset_field_permission
import nomnomzbot.composeapp.generated.resources.commands_preset_field_reaction
import nomnomzbot.composeapp.generated.resources.commands_preset_field_responses
import nomnomzbot.composeapp.generated.resources.commands_preset_reset_confirm
import nomnomzbot.composeapp.generated.resources.commands_preset_reset_message
import nomnomzbot.composeapp.generated.resources.commands_preset_reset_nothing
import nomnomzbot.composeapp.generated.resources.commands_preset_reset_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

/**
 * Confirms "Reset to preset" for one seeded fun command. The consequence is named before anything is written:
 * every part of the command the reset puts back, computed from the live command and the server's preset. A
 * command that already matches its preset cannot be confirmed — there is nothing to reset.
 */
@Composable
internal fun PresetResetDialog(
    command: CommandSummary,
    preset: CommandPreset,
    action: suspend () -> DialogResult,
    onDismiss: () -> Unit,
) {
    val changes: List<PresetResetField> = presetResetChanges(command, preset)
    val changedLabels: List<String> = changes.map { stringResource(it.label()) }
    ConfirmDialog(
        title = stringResource(Res.string.commands_preset_reset_title, command.name),
        message =
            if (changes.isEmpty()) {
                stringResource(Res.string.commands_preset_reset_nothing, command.name, preset.key)
            } else {
                stringResource(Res.string.commands_preset_reset_message, preset.key, changedLabels.joinToString(", "))
            },
        confirmLabel = stringResource(Res.string.commands_preset_reset_confirm),
        dismissLabel = stringResource(Res.string.commands_delete_cancel),
        destructive = true,
        confirmEnabled = changes.isNotEmpty(),
        action = action,
        onDismiss = onDismiss,
    )
}

private fun PresetResetField.label(): StringResource =
    when (this) {
        PresetResetField.Reaction -> Res.string.commands_preset_field_reaction
        PresetResetField.Responses -> Res.string.commands_preset_field_responses
        PresetResetField.Description -> Res.string.commands_preset_field_description
        PresetResetField.Permission -> Res.string.commands_preset_field_permission
        PresetResetField.Cooldown -> Res.string.commands_preset_field_cooldown
        PresetResetField.Aliases -> Res.string.commands_preset_field_aliases
        PresetResetField.Matching -> Res.string.commands_preset_field_matching
    }
