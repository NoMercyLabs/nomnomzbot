// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.state

import bot.nomnomz.dashboard.core.network.TtsConfig
import kotlin.test.Test
import kotlin.test.assertEquals

// The reset preview's diff: which settings a "Reset to defaults" would actually change, and from what to what.
class TtsResetDiffTest {

    private val defaults: TtsConfig =
        TtsConfig(
            isEnabled = true,
            mode = "client_edge",
            defaultProvider = "edge",
            maxCharacters = 500,
            minPermission = "everyone",
            skipBotMessages = true,
            readUsernames = true,
            profanityCensorEnabled = true,
            modApprovalRequired = false,
            minBitsToTts = null,
            viewerVoiceSelfServiceEnabled = true,
        )

    @Test
    fun a_config_equal_to_the_defaults_has_nothing_to_reset() {
        assertEquals(emptyList(), ttsResetChanges(defaults, defaults))
    }

    @Test
    fun every_setting_that_differs_is_listed_current_to_default_in_form_order() {
        val current: TtsConfig =
            TtsConfig(
                isEnabled = false,
                mode = "byok",
                defaultProvider = "azure",
                maxCharacters = 120,
                minPermission = "moderators",
                skipBotMessages = false,
                readUsernames = false,
                profanityCensorEnabled = false,
                modApprovalRequired = true,
                minBitsToTts = 100,
                viewerVoiceSelfServiceEnabled = false,
            )

        val changes: List<TtsResetChange> = ttsResetChanges(current, defaults)

        assertEquals(
            listOf(
                TtsResetChange(TtsResetField.Enabled, TtsResetValue.Flag(false), TtsResetValue.Flag(true)),
                TtsResetChange(TtsResetField.Mode, TtsResetValue.Choice("byok"), TtsResetValue.Choice("client_edge")),
                TtsResetChange(
                    TtsResetField.DefaultProvider,
                    TtsResetValue.Choice("azure"),
                    TtsResetValue.Choice("edge"),
                ),
                TtsResetChange(TtsResetField.MaxCharacters, TtsResetValue.Number(120), TtsResetValue.Number(500)),
                TtsResetChange(
                    TtsResetField.MinPermission,
                    TtsResetValue.Choice("moderators"),
                    TtsResetValue.Choice("everyone"),
                ),
                TtsResetChange(TtsResetField.SkipBotMessages, TtsResetValue.Flag(false), TtsResetValue.Flag(true)),
                TtsResetChange(TtsResetField.ReadUsernames, TtsResetValue.Flag(false), TtsResetValue.Flag(true)),
                TtsResetChange(TtsResetField.ProfanityCensor, TtsResetValue.Flag(false), TtsResetValue.Flag(true)),
                TtsResetChange(TtsResetField.ModApproval, TtsResetValue.Flag(true), TtsResetValue.Flag(false)),
                TtsResetChange(TtsResetField.MinBits, TtsResetValue.Number(100), TtsResetValue.Number(null)),
                TtsResetChange(
                    TtsResetField.ViewerVoiceSelfService,
                    TtsResetValue.Flag(false),
                    TtsResetValue.Flag(true),
                ),
            ),
            changes,
        )
    }

    @Test
    fun only_the_differing_settings_are_listed() {
        val changes: List<TtsResetChange> = ttsResetChanges(defaults.copy(maxCharacters = 200), defaults)

        assertEquals(listOf(TtsResetField.MaxCharacters), changes.map { it.field })
    }

    @Test
    fun what_a_reset_leaves_alone_never_shows_up_as_a_change() {
        // The default voice, BYOK flags and region are not reset, so differing there must not promise a change.
        val current: TtsConfig =
            defaults.copy(
                defaultVoiceId = "en-GB-SoniaNeural",
                hasAzureByokKey = true,
                hasElevenLabsByokKey = true,
                azureRegion = "westus2",
            )

        assertEquals(emptyList(), ttsResetChanges(current, defaults))
    }
}
