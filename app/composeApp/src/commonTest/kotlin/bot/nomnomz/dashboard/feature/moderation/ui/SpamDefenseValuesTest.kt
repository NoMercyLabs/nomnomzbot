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
import bot.nomnomz.dashboard.core.network.SpamSettingDescriptor
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

/**
 * The spam-defence form renders whatever catalogue the server sends. A catalogue entry the form cannot read
 * or write (the trust-ladder thresholds arrive flagged as a toggle) used to render as a switch stuck on off.
 */
class SpamDefenseValuesTest {

    @Test
    fun the_trust_ladder_thresholds_are_not_offered_as_a_switch() {
        val thresholds = SpamSettingDescriptor(key = "TrustThresholds", group = "trust", isToggle = true)

        assertFalse(SpamDefenseValues.isEditable(thresholds))
    }

    @Test
    fun a_known_toggle_is_editable_and_flipping_it_changes_the_setting() {
        val dryRun = SpamSettingDescriptor(key = "DryRun", group = "master", isToggle = true)
        val settings = SpamDefenseSettings(dryRun = true)

        assertTrue(SpamDefenseValues.isEditable(dryRun))
        assertFalse(SpamDefenseValues.boolean(SpamDefenseValues.withBoolean(settings, "DryRun", false), "DryRun"))
    }

    @Test
    fun a_bounded_number_is_editable_and_an_unknown_one_is_not() {
        val lockdown = SpamSettingDescriptor(key = "LockdownMinutes", group = "lockdown", minimum = 1.0, maximum = 240.0)
        val unknown = SpamSettingDescriptor(key = "SomethingNew", group = "lockdown", minimum = 1.0, maximum = 5.0)

        assertTrue(SpamDefenseValues.isEditable(lockdown))
        assertFalse(SpamDefenseValues.isEditable(unknown))
        assertEquals(30, SpamDefenseValues.withText(SpamDefenseSettings(), "LockdownMinutes", "30")?.lockdownMinutes)
    }

    @Test
    fun the_newcomer_gate_limits_and_hold_switch_read_and_write_their_own_fields() {
        val accountAge = SpamSettingDescriptor(key = "AccountAgeGateDays", group = "newcomers", minimum = 0.0, maximum = 365.0)
        val followAge = SpamSettingDescriptor(key = "FollowAgeGateDays", group = "newcomers", minimum = 0.0, maximum = 90.0)
        val holds = SpamSettingDescriptor(key = "AccountGateHoldsForReview", group = "newcomers", isToggle = true)
        val settings = SpamDefenseSettings()

        assertTrue(SpamDefenseValues.isEditable(accountAge))
        assertTrue(SpamDefenseValues.isEditable(followAge))
        assertTrue(SpamDefenseValues.isEditable(holds))

        val changed = SpamDefenseValues.withText(SpamDefenseValues.withText(settings, "AccountAgeGateDays", "14")!!, "FollowAgeGateDays", "3")!!
        assertEquals(14, changed.accountAgeGateDays)
        assertEquals(3, changed.followAgeGateDays)
        assertEquals("14", SpamDefenseValues.text(changed, "AccountAgeGateDays"))
        assertEquals("3", SpamDefenseValues.text(changed, "FollowAgeGateDays"))

        assertTrue(SpamDefenseValues.boolean(settings, "AccountGateHoldsForReview"))
        assertFalse(SpamDefenseValues.boolean(SpamDefenseValues.withBoolean(settings, "AccountGateHoldsForReview", false), "AccountGateHoldsForReview"))
    }
}
