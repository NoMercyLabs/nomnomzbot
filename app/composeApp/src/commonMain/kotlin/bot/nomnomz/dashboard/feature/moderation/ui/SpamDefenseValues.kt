// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.ui

import bot.nomnomz.dashboard.core.network.SpamDefenseSettings

/**
 * Read and write one spam-defence setting by the key the backend uses for it.
 *
 * The editor is generic — it renders whatever catalogue the server sends — but Kotlin has no reflection
 * on wasmJs, so the key-to-field mapping has to be written out. These `when` blocks are that mapping and
 * nothing more: irreducible data, not a pattern that wants refactoring.
 *
 * Every unknown key falls through to leaving the settings untouched. A backend that gains a knob before
 * the dashboard is rebuilt therefore renders nothing for it rather than corrupting a neighbouring field.
 */
internal object SpamDefenseValues {

    fun boolean(settings: SpamDefenseSettings, key: String): Boolean =
        when (key) {
            "IsEnabled" -> settings.isEnabled
            "DryRun" -> settings.dryRun
            "NonLatinScriptGate" -> settings.nonLatinScriptGate
            "AutoReverseOnDequalify" -> settings.autoReverseOnDequalify
            "LockdownAutoExtend" -> settings.lockdownAutoExtend
            "NetworkSubscribe" -> settings.networkSubscribe
            "NetworkContribute" -> settings.networkContribute
            else -> false
        }

    fun withBoolean(
        settings: SpamDefenseSettings,
        key: String,
        value: Boolean,
    ): SpamDefenseSettings =
        when (key) {
            "IsEnabled" -> settings.copy(isEnabled = value)
            "DryRun" -> settings.copy(dryRun = value)
            "NonLatinScriptGate" -> settings.copy(nonLatinScriptGate = value)
            "AutoReverseOnDequalify" -> settings.copy(autoReverseOnDequalify = value)
            "LockdownAutoExtend" -> settings.copy(lockdownAutoExtend = value)
            "NetworkSubscribe" -> settings.copy(networkSubscribe = value)
            "NetworkContribute" -> settings.copy(networkContribute = value)
            else -> settings
        }

    fun text(settings: SpamDefenseSettings, key: String): String =
        when (key) {
            "SemiTrustedWatchHoursHere" -> trim(settings.semiTrustedWatchHoursHere)
            "SemiTrustedWatchHoursInstance" -> trim(settings.semiTrustedWatchHoursInstance)
            "NearDuplicateSimilarity" -> settings.nearDuplicateSimilarity.toString()
            "MinimumSkeletonLength" -> settings.minimumSkeletonLength.toString()
            "QualifyNoStandingShare" -> settings.qualifyNoStandingShare.toString()
            "DequalifyNoStandingShare" -> settings.dequalifyNoStandingShare.toString()
            "MinimumCohortSize" -> settings.minimumCohortSize.toString()
            "WindowSeconds" -> settings.windowSeconds.toString()
            "MaxWindowSeconds" -> settings.maxWindowSeconds.toString()
            "ActionDelaySeconds" -> settings.actionDelaySeconds.toString()
            "FollowSpikeFactor" -> trim(settings.followSpikeFactor)
            "JoinBurstFactor" -> trim(settings.joinBurstFactor)
            "LockdownMinutes" -> settings.lockdownMinutes.toString()
            "LockdownMaxMinutes" -> settings.lockdownMaxMinutes.toString()
            "RequiredCorroborations" -> settings.requiredCorroborations.toString()
            else -> ""
        }

    /**
     * Apply typed text, or null when it is not a valid number yet. Returning null rather than a
     * substituted value is deliberate: it leaves the field as the operator typed it while they are
     * mid-edit, instead of snapping "0." back to "0" under the cursor.
     */
    fun withText(
        settings: SpamDefenseSettings,
        key: String,
        text: String,
    ): SpamDefenseSettings? {
        val trimmed: String = text.trim()
        val number: Double = trimmed.toDoubleOrNull() ?: return null
        val whole: Int = number.toInt()

        return when (key) {
            "SemiTrustedWatchHoursHere" -> settings.copy(semiTrustedWatchHoursHere = number)
            "SemiTrustedWatchHoursInstance" -> settings.copy(semiTrustedWatchHoursInstance = number)
            "NearDuplicateSimilarity" -> settings.copy(nearDuplicateSimilarity = number)
            "MinimumSkeletonLength" -> settings.copy(minimumSkeletonLength = whole)
            "QualifyNoStandingShare" -> settings.copy(qualifyNoStandingShare = number)
            "DequalifyNoStandingShare" -> settings.copy(dequalifyNoStandingShare = number)
            "MinimumCohortSize" -> settings.copy(minimumCohortSize = whole)
            "WindowSeconds" -> settings.copy(windowSeconds = whole)
            "MaxWindowSeconds" -> settings.copy(maxWindowSeconds = whole)
            "ActionDelaySeconds" -> settings.copy(actionDelaySeconds = whole)
            "FollowSpikeFactor" -> settings.copy(followSpikeFactor = number)
            "JoinBurstFactor" -> settings.copy(joinBurstFactor = number)
            "LockdownMinutes" -> settings.copy(lockdownMinutes = whole)
            "LockdownMaxMinutes" -> settings.copy(lockdownMaxMinutes = whole)
            "RequiredCorroborations" -> settings.copy(requiredCorroborations = whole)
            else -> null
        }
    }

    /** Whole doubles read better without the trailing ".0" in a form field. */
    private fun trim(value: Double): String =
        if (value == value.toInt().toDouble()) value.toInt().toString() else value.toString()
}
