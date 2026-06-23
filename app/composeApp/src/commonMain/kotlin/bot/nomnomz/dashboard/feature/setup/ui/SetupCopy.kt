// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.ui

import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.setup_field_discord_clientId_label
import nomnomzbot.composeapp.generated.resources.setup_field_discord_clientSecret_label
import nomnomzbot.composeapp.generated.resources.setup_field_spotify_clientId_label
import nomnomzbot.composeapp.generated.resources.setup_field_spotify_clientSecret_label
import nomnomzbot.composeapp.generated.resources.setup_field_twitch_app_clientId_help
import nomnomzbot.composeapp.generated.resources.setup_field_twitch_app_clientId_label
import nomnomzbot.composeapp.generated.resources.setup_field_twitch_app_clientSecret_help
import nomnomzbot.composeapp.generated.resources.setup_field_twitch_app_clientSecret_label
import nomnomzbot.composeapp.generated.resources.setup_field_youtube_clientId_label
import nomnomzbot.composeapp.generated.resources.setup_field_youtube_clientSecret_label
import nomnomzbot.composeapp.generated.resources.setup_step_discord_description
import nomnomzbot.composeapp.generated.resources.setup_step_discord_instruction_1
import nomnomzbot.composeapp.generated.resources.setup_step_discord_instruction_2
import nomnomzbot.composeapp.generated.resources.setup_step_discord_instruction_3
import nomnomzbot.composeapp.generated.resources.setup_step_discord_title
import nomnomzbot.composeapp.generated.resources.setup_step_platform_bot_description
import nomnomzbot.composeapp.generated.resources.setup_step_platform_bot_instruction_1
import nomnomzbot.composeapp.generated.resources.setup_step_platform_bot_instruction_2
import nomnomzbot.composeapp.generated.resources.setup_step_platform_bot_title
import nomnomzbot.composeapp.generated.resources.setup_step_spotify_description
import nomnomzbot.composeapp.generated.resources.setup_step_spotify_instruction_1
import nomnomzbot.composeapp.generated.resources.setup_step_spotify_instruction_2
import nomnomzbot.composeapp.generated.resources.setup_step_spotify_instruction_3
import nomnomzbot.composeapp.generated.resources.setup_step_spotify_title
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_description
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_instruction_1
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_instruction_2
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_instruction_3
import nomnomzbot.composeapp.generated.resources.setup_step_twitch_app_title
import nomnomzbot.composeapp.generated.resources.setup_step_youtube_description
import nomnomzbot.composeapp.generated.resources.setup_step_youtube_instruction_1
import nomnomzbot.composeapp.generated.resources.setup_step_youtube_instruction_2
import nomnomzbot.composeapp.generated.resources.setup_step_youtube_instruction_3
import nomnomzbot.composeapp.generated.resources.setup_step_youtube_title
import org.jetbrains.compose.resources.StringResource

// Frontend-side localization for the self-describing setup wizard's COPY, keyed on the backend's STABLE
// step/field keys (the backend authors all wizard text in English by design; user-facing i18n is the
// frontend's job — frontend.md). The wizard stays structurally self-describing: only the copy is localized
// here, never the step ORDER, fields, actions, or completion state — those still come from the backend
// contract verbatim.
//
// Key scheme:
//   step  title:        setup_step_<stepKey>_title
//   step  description:  setup_step_<stepKey>_description
//   step  instructions: setup_step_<stepKey>_instruction_<1-based index>
//   field label:        setup_field_<stepKey>_<fieldKey>_label
//   field help:         setup_field_<stepKey>_<fieldKey>_help
//
// Field keys are scoped by step (the same `clientId`/`clientSecret` keys recur across providers with
// DIFFERENT help text — Twitch's secret is "shown only once", the generic providers' have none), so a
// per-step lookup is the only correct mapping.
//
// FALLBACK: every lookup returns a nullable [StringResource]; the screen renders the backend-provided
// English when null. So a future backend step the frontend hasn't translated yet still SHOWS (untranslated)
// rather than blank — the wizard never loses content by adding a step ahead of its i18n keys.
internal object SetupCopy {

    /** The localized title for [stepKey], or null to fall back to the backend's [SetupStep.title]. */
    fun stepTitle(stepKey: String): StringResource? =
        when (stepKey) {
            STEP_TWITCH -> Res.string.setup_step_twitch_app_title
            STEP_PLATFORM_BOT -> Res.string.setup_step_platform_bot_title
            STEP_SPOTIFY -> Res.string.setup_step_spotify_title
            STEP_DISCORD -> Res.string.setup_step_discord_title
            STEP_YOUTUBE -> Res.string.setup_step_youtube_title
            else -> null
        }

    /** The localized description for [stepKey], or null to fall back to the backend's [SetupStep.description]. */
    fun stepDescription(stepKey: String): StringResource? =
        when (stepKey) {
            STEP_TWITCH -> Res.string.setup_step_twitch_app_description
            STEP_PLATFORM_BOT -> Res.string.setup_step_platform_bot_description
            STEP_SPOTIFY -> Res.string.setup_step_spotify_description
            STEP_DISCORD -> Res.string.setup_step_discord_description
            STEP_YOUTUBE -> Res.string.setup_step_youtube_description
            else -> null
        }

    /**
     * The localized instruction line at [index] (0-based, matching the backend list order) for [stepKey],
     * or null to fall back to the backend's raw instruction at that position. Keyed by the 1-based index so
     * a step with N translated lines maps the same N positions the backend emits; any extra backend lines
     * (a future addition) fall back untranslated.
     */
    fun instruction(stepKey: String, index: Int): StringResource? =
        when (stepKey) {
            STEP_TWITCH ->
                when (index) {
                    0 -> Res.string.setup_step_twitch_app_instruction_1
                    1 -> Res.string.setup_step_twitch_app_instruction_2
                    2 -> Res.string.setup_step_twitch_app_instruction_3
                    else -> null
                }
            STEP_PLATFORM_BOT ->
                when (index) {
                    0 -> Res.string.setup_step_platform_bot_instruction_1
                    1 -> Res.string.setup_step_platform_bot_instruction_2
                    else -> null
                }
            STEP_SPOTIFY ->
                when (index) {
                    0 -> Res.string.setup_step_spotify_instruction_1
                    1 -> Res.string.setup_step_spotify_instruction_2
                    2 -> Res.string.setup_step_spotify_instruction_3
                    else -> null
                }
            STEP_DISCORD ->
                when (index) {
                    0 -> Res.string.setup_step_discord_instruction_1
                    1 -> Res.string.setup_step_discord_instruction_2
                    2 -> Res.string.setup_step_discord_instruction_3
                    else -> null
                }
            STEP_YOUTUBE ->
                when (index) {
                    0 -> Res.string.setup_step_youtube_instruction_1
                    1 -> Res.string.setup_step_youtube_instruction_2
                    2 -> Res.string.setup_step_youtube_instruction_3
                    else -> null
                }
            else -> null
        }

    /** The localized label for [fieldKey] under [stepKey], or null to fall back to the backend's [SetupField.label]. */
    fun fieldLabel(stepKey: String, fieldKey: String): StringResource? =
        when (stepKey to fieldKey) {
            STEP_TWITCH to FIELD_CLIENT_ID -> Res.string.setup_field_twitch_app_clientId_label
            STEP_TWITCH to FIELD_CLIENT_SECRET -> Res.string.setup_field_twitch_app_clientSecret_label
            STEP_SPOTIFY to FIELD_CLIENT_ID -> Res.string.setup_field_spotify_clientId_label
            STEP_SPOTIFY to FIELD_CLIENT_SECRET -> Res.string.setup_field_spotify_clientSecret_label
            STEP_DISCORD to FIELD_CLIENT_ID -> Res.string.setup_field_discord_clientId_label
            STEP_DISCORD to FIELD_CLIENT_SECRET -> Res.string.setup_field_discord_clientSecret_label
            STEP_YOUTUBE to FIELD_CLIENT_ID -> Res.string.setup_field_youtube_clientId_label
            STEP_YOUTUBE to FIELD_CLIENT_SECRET -> Res.string.setup_field_youtube_clientSecret_label
            else -> null
        }

    /**
     * The localized help for [fieldKey] under [stepKey], or null to fall back to the backend's
     * [SetupField.help] (which is itself nullable — only the Twitch fields carry help server-side).
     */
    fun fieldHelp(stepKey: String, fieldKey: String): StringResource? =
        when (stepKey to fieldKey) {
            STEP_TWITCH to FIELD_CLIENT_ID -> Res.string.setup_field_twitch_app_clientId_help
            STEP_TWITCH to FIELD_CLIENT_SECRET -> Res.string.setup_field_twitch_app_clientSecret_help
            else -> null
        }

    // The backend's stable step + field keys (SystemController's SetupWizard.Build), mirrored here so the
    // lookups read against named constants rather than scattered string literals.
    private const val STEP_TWITCH: String = "twitch_app"
    private const val STEP_PLATFORM_BOT: String = "platform_bot"
    private const val STEP_SPOTIFY: String = "spotify"
    private const val STEP_DISCORD: String = "discord"
    private const val STEP_YOUTUBE: String = "youtube"

    private const val FIELD_CLIENT_ID: String = "clientId"
    private const val FIELD_CLIENT_SECRET: String = "clientSecret"
}
