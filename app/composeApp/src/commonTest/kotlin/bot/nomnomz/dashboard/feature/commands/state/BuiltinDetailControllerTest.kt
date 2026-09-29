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

import bot.nomnomz.dashboard.core.network.BuiltinCommand
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// The built-in detail editor against a backend-shaped store: what the form sends, what the editor shows back,
// that the Commands list is told to refetch, and that refused or out-of-range values never change anything.
class BuiltinDetailControllerTest {
    private class Harness {
        val api: InMemoryReplyCatalogue = InMemoryReplyCatalogue()
        var refreshes: Int = 0
        val controller: BuiltinDetailController =
            BuiltinDetailController(PrimaryChannelOnly(), api, onChanged = { refreshes++ })
    }

    @Test
    fun saving_a_cooldown_and_permission_stores_them_and_refreshes_the_list() = runTest {
        val h = Harness()
        h.controller.open("sr")
        assertEquals("", h.controller.state.value.cooldownText, "a default cooldown shows as an empty field")
        assertFalse(h.controller.state.value.settingsDirty)

        h.controller.editCooldown("30s")
        h.controller.editPermission("Subscriber")
        assertEquals("30", h.controller.state.value.cooldownText, "only digits are kept")
        assertTrue(h.controller.state.value.settingsDirty)
        h.controller.saveSettings()

        assertEquals(listOf("settings sr cooldown=30 permission=Subscriber"), h.api.writes)
        val saved: BuiltinCommand = assertNotNull(h.controller.state.value.builtin)
        assertEquals(30, saved.cooldownSeconds)
        assertEquals("Subscriber", saved.minPermissionLevel)
        assertTrue(saved.isCustomized)
        assertFalse(h.controller.state.value.settingsDirty, "the form now matches what is saved")
        assertEquals(1, h.refreshes)
    }

    @Test
    fun an_out_of_range_cooldown_never_reaches_the_backend() = runTest {
        val h = Harness()
        h.controller.open("sr")

        h.controller.editCooldown("5000")
        assertFalse(h.controller.state.value.cooldownValid)
        h.controller.saveSettings()

        assertTrue(h.api.writes.isEmpty())
        assertEquals(0, h.refreshes)
    }

    @Test
    fun a_safety_floor_is_never_offered_below_itself_and_a_refusal_changes_nothing() = runTest {
        val h = Harness()
        h.controller.open("whisper")

        assertEquals(
            listOf("Moderator", "LeadModerator", "Editor", "Broadcaster"),
            h.controller.state.value.permissionChoices,
        )

        // Even a crafted value is refused by the backend, and the refusal is shown, not swallowed.
        h.controller.editPermission("Everyone")
        h.controller.saveSettings()

        assertNotNull(h.controller.state.value.error)
        assertTrue(h.api.writes.isEmpty())
        assertNull(h.controller.state.value.builtin?.minPermissionLevelOverride)
        assertEquals(0, h.refreshes)
    }

    @Test
    fun switching_it_off_and_tts_on_reads_the_backend_back() = runTest {
        val h = Harness()
        h.controller.open("sr")

        h.controller.setEnabled(false)
        h.controller.setSpeakWithTts(true)

        assertEquals(listOf("enabled sr=false", "tts sr=true"), h.api.writes)
        val shown: BuiltinCommand = assertNotNull(h.controller.state.value.builtin)
        assertFalse(shown.isEnabled)
        assertTrue(shown.speakWithTts)
        assertEquals(2, h.refreshes)
    }

    @Test
    fun reset_puts_every_setting_and_reply_back_on_its_default() = runTest {
        val h = Harness()
        h.api.setReply("ch1", "sr", "added", "Queued {track.name}!")
        h.controller.open("sr")
        h.controller.setEnabled(false)
        h.controller.editCooldown("60")
        h.controller.saveSettings()
        assertEquals(1, h.controller.state.value.builtin?.replyOverrideCount)

        h.controller.reset()

        assertEquals("reset sr", h.api.writes.last())
        val shown: BuiltinCommand = assertNotNull(h.controller.state.value.builtin)
        assertTrue(shown.isEnabled)
        assertNull(shown.cooldownSecondsOverride)
        assertEquals(0, shown.replyOverrideCount)
        assertFalse(shown.isCustomized)
        assertEquals("", h.controller.state.value.cooldownText)
    }
}
