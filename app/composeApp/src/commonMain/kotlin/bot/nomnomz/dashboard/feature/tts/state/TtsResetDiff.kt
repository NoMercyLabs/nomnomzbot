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

/** The channel TTS settings "Reset to defaults" writes — exactly the ones the server's reset restores. */
enum class TtsResetField {
    Enabled,
    Mode,
    DefaultProvider,
    MaxCharacters,
    MinPermission,
    SkipBotMessages,
    ReadUsernames,
    ProfanityCensor,
    ModApproval,
    MinBits,
    ViewerVoiceSelfService,
}

/** One side of a reset change, kept as data so the screen (not this file) owns every translated label. */
sealed interface TtsResetValue {
    /** A switch. */
    data class Flag(val on: Boolean) : TtsResetValue

    /** A number; null = no value (the bits gate is off). */
    data class Number(val value: Int?) : TtsResetValue

    /** One of a fixed wire-value set (dispatch mode, provider, minimum permission). */
    data class Choice(val wire: String) : TtsResetValue
}

/** One setting the reset would change: what it is now and what it goes back to. */
data class TtsResetChange(val field: TtsResetField, val current: TtsResetValue, val default: TtsResetValue)

/**
 * The settings that differ between the [current] saved config and the server's [defaults], in the order the
 * form shows them. Empty means the channel is already at its defaults, so a reset would write nothing.
 * Deliberately not compared: the default voice, and the BYOK / lexicon / per-viewer data — a reset leaves
 * them alone, so listing them here would promise a change that never happens.
 */
fun ttsResetChanges(current: TtsConfig, defaults: TtsConfig): List<TtsResetChange> =
    listOfNotNull(
        change(TtsResetField.Enabled, TtsResetValue.Flag(current.isEnabled), TtsResetValue.Flag(defaults.isEnabled)),
        change(TtsResetField.Mode, TtsResetValue.Choice(current.mode), TtsResetValue.Choice(defaults.mode)),
        change(
            TtsResetField.DefaultProvider,
            TtsResetValue.Choice(current.defaultProvider),
            TtsResetValue.Choice(defaults.defaultProvider),
        ),
        change(
            TtsResetField.MaxCharacters,
            TtsResetValue.Number(current.maxCharacters),
            TtsResetValue.Number(defaults.maxCharacters),
        ),
        change(
            TtsResetField.MinPermission,
            TtsResetValue.Choice(current.minPermission),
            TtsResetValue.Choice(defaults.minPermission),
        ),
        change(
            TtsResetField.SkipBotMessages,
            TtsResetValue.Flag(current.skipBotMessages),
            TtsResetValue.Flag(defaults.skipBotMessages),
        ),
        change(
            TtsResetField.ReadUsernames,
            TtsResetValue.Flag(current.readUsernames),
            TtsResetValue.Flag(defaults.readUsernames),
        ),
        change(
            TtsResetField.ProfanityCensor,
            TtsResetValue.Flag(current.profanityCensorEnabled),
            TtsResetValue.Flag(defaults.profanityCensorEnabled),
        ),
        change(
            TtsResetField.ModApproval,
            TtsResetValue.Flag(current.modApprovalRequired),
            TtsResetValue.Flag(defaults.modApprovalRequired),
        ),
        change(
            TtsResetField.MinBits,
            TtsResetValue.Number(current.minBitsToTts),
            TtsResetValue.Number(defaults.minBitsToTts),
        ),
        change(
            TtsResetField.ViewerVoiceSelfService,
            TtsResetValue.Flag(current.viewerVoiceSelfServiceEnabled),
            TtsResetValue.Flag(defaults.viewerVoiceSelfServiceEnabled),
        ),
    )

private fun change(field: TtsResetField, current: TtsResetValue, default: TtsResetValue): TtsResetChange? =
    if (current == default) null else TtsResetChange(field, current, default)
