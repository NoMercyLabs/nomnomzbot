// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.state

import bot.nomnomz.dashboard.core.network.CommandPreset
import bot.nomnomz.dashboard.core.network.CommandSummary

/** One part of a command that "Reset to preset" puts back. Name and on/off state are never part of a reset. */
enum class PresetResetField {
    Reaction,
    Responses,
    Description,
    Permission,
    Cooldown,
    Aliases,
    Matching,
}

/**
 * Exactly which parts of [command] a reset to [preset] would change — the consequence the confirm dialog shows
 * before anything is written. Empty means the command already matches its preset.
 */
fun presetResetChanges(command: CommandSummary, preset: CommandPreset): List<PresetResetField> =
    buildList {
        if (command.tier != preset.tier || command.pipelineId != null) add(PresetResetField.Reaction)
        if (
            command.templateResponse.orEmpty() != preset.templateResponse.orEmpty() ||
                command.templateResponses.orEmpty() != preset.templateResponses
        ) {
            add(PresetResetField.Responses)
        }
        if (command.description.orEmpty() != preset.description.orEmpty()) add(PresetResetField.Description)
        if (command.minPermissionLevel != preset.minPermissionLevel) add(PresetResetField.Permission)
        if (
            command.cooldownSeconds != preset.cooldownSeconds ||
                command.userCooldownSeconds != preset.userCooldownSeconds ||
                command.cooldownPerUser != preset.cooldownPerUser
        ) {
            add(PresetResetField.Cooldown)
        }
        if (command.aliases != preset.aliases) add(PresetResetField.Aliases)
        if (
            command.prefixMode != preset.prefixMode ||
                !command.customPrefix.isNullOrEmpty() ||
                command.matchMode != preset.matchMode ||
                !command.matchPattern.isNullOrEmpty()
        ) {
            add(PresetResetField.Matching)
        }
    }
