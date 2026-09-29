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
import kotlin.test.Test
import kotlin.test.assertEquals

// The confirm dialog names exactly what a reset changes — these prove each part is detected on its own, and that
// the name and the on/off switch (which a reset never touches) never count as a change.
class PresetResetChangesTest {
    private val preset: CommandPreset =
        CommandPreset(key = "hug", description = "Give someone a hug.", templateResponse = "{{user.name}} hugs")

    private val untouched: CommandSummary =
        CommandSummary(
            name = "hug",
            description = "Give someone a hug.",
            templateResponse = "{{user.name}} hugs",
            templateResponses = emptyList(),
            isEnabled = true,
            presetKey = "hug",
        )

    @Test
    fun an_untouched_preset_command_has_nothing_to_reset_even_when_renamed_and_switched_off() {
        assertEquals(emptyList(), presetResetChanges(untouched.copy(name = "cuddle", isEnabled = false), preset))
    }

    @Test
    fun every_edited_part_is_named_in_ladder_order() {
        val edited: CommandSummary =
            untouched.copy(
                tier = "pipeline",
                pipelineId = "p1",
                templateResponse = "mine",
                description = "mine",
                minPermissionLevel = "Moderator",
                userCooldownSeconds = 30,
                aliases = listOf("cuddle"),
                matchMode = "Exact",
            )

        assertEquals(
            listOf(
                PresetResetField.Reaction,
                PresetResetField.Responses,
                PresetResetField.Description,
                PresetResetField.Permission,
                PresetResetField.Cooldown,
                PresetResetField.Aliases,
                PresetResetField.Matching,
            ),
            presetResetChanges(edited, preset),
        )
    }

    @Test
    fun a_random_response_list_alone_counts_as_a_responses_change() {
        assertEquals(
            listOf(PresetResetField.Responses),
            presetResetChanges(untouched.copy(templateResponses = listOf("extra")), preset),
        )
    }

    @Test
    fun a_custom_prefix_alone_counts_as_a_matching_change() {
        assertEquals(
            listOf(PresetResetField.Matching),
            presetResetChanges(untouched.copy(customPrefix = "?"), preset),
        )
    }
}
