// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.ActionDangerTier
import bot.nomnomz.dashboard.core.network.ActionDefault
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The platform-defaults tab state (plan item A4): a pick fetches the counted blast radius for exactly that
 * pick, the save stays disarmed until it is on screen (and, for a dangerous action, acknowledged), the save
 * echoes the previewed count, the row is replaced by the server's read-back, and a stale count re-previews
 * instead of applying.
 */
class PlatformDefaultsControllerTest {
    private val commandsWrite = ActionDefault(
        actionKey = "commands:write",
        description = "Manage custom commands",
        shippedDefaultLevel = 10,
        effectiveDefaultLevel = 10,
        floorLevel = 10,
    )
    private val nuke = ActionDefault(
        actionKey = "moderation:nuke",
        shippedDefaultLevel = 20,
        effectiveDefaultLevel = 20,
        floorLevel = 20,
        floorTier = ActionDangerTier.CRITICAL,
    )

    private fun controllerWith(api: FakePlatformDefaultsApi) = PlatformDefaultsController(api = api)

    @Test
    fun picking_a_level_previews_exactly_that_level_and_arms_the_save_only_after_the_count_arrives() = runTest {
        val api = FakePlatformDefaultsApi(listOf(commandsWrite))
        val controller = controllerWith(api)
        controller.loadActionDefaults()

        controller.pickActionLevel("commands:write", 30)

        assertEquals(listOf<Pair<String, Int?>>("commands:write" to 30), api.previews.toList())
        val edit: ActionDefaultEdit = assertNotNull(controller.state.value.actionEdit)
        assertEquals(3, edit.preview?.channelsAffected)
        assertTrue(edit.canSave)
        assertFalse(ActionDefaultEdit(actionKey = "commands:write", level = 30).canSave, "no count yet = no save")
    }

    @Test
    fun saving_echoes_the_previewed_count_and_replaces_the_row_with_the_read_back() = runTest {
        val api = FakePlatformDefaultsApi(listOf(commandsWrite))
        val controller = controllerWith(api)
        controller.loadActionDefaults()
        controller.pickActionLevel("commands:write", 30)

        controller.saveActionEdit()

        val (key, body) = api.saves.single()
        assertEquals("commands:write", key)
        assertEquals(30, body.level)
        assertEquals(3, body.confirmedChannelsAffected)
        assertFalse(body.confirmDanger)
        val row: ActionDefault = controller.state.value.actionDefaults.single()
        assertEquals(30, row.platformDefaultLevel)
        assertEquals(30, row.effectiveDefaultLevel)
        assertNull(controller.state.value.actionEdit, "the editor closes after a successful save")
    }

    @Test
    fun a_stale_count_is_not_applied_and_the_preview_is_refreshed() = runTest {
        val api = FakePlatformDefaultsApi(listOf(commandsWrite))
        val controller = controllerWith(api)
        controller.loadActionDefaults()
        controller.pickActionLevel("commands:write", 30)
        api.followers = 5 // another channel started following the default meanwhile

        controller.saveActionEdit()

        assertEquals(10, controller.state.value.actionDefaults.single().effectiveDefaultLevel)
        val edit: ActionDefaultEdit = assertNotNull(controller.state.value.actionEdit)
        assertEquals(5, edit.preview?.channelsAffected, "the operator now sees the new count")
        assertEquals(2, api.previews.size)
    }

    @Test
    fun a_dangerous_action_needs_the_acknowledgement_before_the_save_arms() = runTest {
        val api = FakePlatformDefaultsApi(listOf(nuke))
        val controller = controllerWith(api)
        controller.loadActionDefaults()
        controller.pickActionLevel("moderation:nuke", 40)

        assertFalse(assertNotNull(controller.state.value.actionEdit).canSave)
        controller.saveActionEdit()
        assertTrue(api.saves.isEmpty(), "an unacknowledged dangerous change never reaches the server")

        controller.acknowledgeDanger(true)
        controller.saveActionEdit()
        assertTrue(api.saves.single().second.confirmDanger)
    }

    @Test
    fun choosing_the_shipped_default_sends_a_null_level() = runTest {
        val api = FakePlatformDefaultsApi(
            listOf(commandsWrite.copy(platformDefaultLevel = 30, effectiveDefaultLevel = 30)),
        )
        val controller = controllerWith(api)
        controller.loadActionDefaults()
        controller.openActionEdit("commands:write")
        assertEquals(30, controller.state.value.actionEdit?.level, "the editor opens on the current platform default")

        controller.pickActionLevel("commands:write", null)
        controller.saveActionEdit()

        assertNull(api.saves.single().second.level)
        assertEquals(10, controller.state.value.actionDefaults.single().effectiveDefaultLevel)
    }

    @Test
    fun the_filter_matches_key_or_description() = runTest {
        val controller = controllerWith(FakePlatformDefaultsApi(listOf(commandsWrite, nuke)))
        controller.loadActionDefaults()

        controller.setActionFilter("custom")
        assertEquals(listOf("commands:write"), controller.state.value.visibleActionDefaults.map { it.actionKey })
        controller.setActionFilter("NUKE")
        assertEquals(listOf("moderation:nuke"), controller.state.value.visibleActionDefaults.map { it.actionKey })
    }
}
