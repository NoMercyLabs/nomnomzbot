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

import bot.nomnomz.dashboard.core.network.EventResponseDefault
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The event-response family of the platform-defaults tab (plan item A4): any edit drops the old count, the
 * save arms only after a preview for exactly the edited values, it echoes that count, the row becomes the
 * server read-back, and an enabled default with an empty message can never be saved.
 */
class PlatformDefaultsEventControllerTest {
    private val follow = EventResponseDefault(
        eventType = "channel.follow",
        isEnabled = true,
        message = "Welcome {user}!",
        variables = listOf("user"),
        channelsFollowing = 4,
        channelsWithOwnResponse = 2,
    )

    private suspend fun opened(api: FakePlatformDefaultsApi): PlatformDefaultsController {
        val controller = PlatformDefaultsController(api)
        controller.loadEventResponseDefaults()
        controller.openEventEdit("channel.follow")
        return controller
    }

    @Test
    fun editing_drops_the_count_and_the_save_arms_only_after_a_preview_of_the_edited_values() = runTest {
        val api = FakePlatformDefaultsApi(emptyList(), events = listOf(follow))
        val controller = opened(api)

        controller.editEventMessage("Thanks {user}!")
        assertFalse(assertNotNull(controller.state.value.eventEdit).canSave, "no count for these values yet")

        controller.previewEventEdit()
        val edit: EventResponseDefaultEdit = assertNotNull(controller.state.value.eventEdit)
        assertEquals("Thanks {user}!", api.eventPreviews.single().second.message)
        assertEquals(4, edit.preview?.channelsAffected)
        assertTrue(edit.canSave)

        controller.editEventEnabled(false)
        assertNull(controller.state.value.eventEdit?.preview, "a later edit invalidates the count")
    }

    @Test
    fun saving_echoes_the_count_and_the_row_becomes_the_read_back() = runTest {
        val api = FakePlatformDefaultsApi(emptyList(), events = listOf(follow))
        val controller = opened(api)
        controller.editEventMessage("Thanks {user}!")
        controller.previewEventEdit()

        controller.saveEventEdit()

        val body = api.eventSaves.single().second
        assertEquals(4, body.confirmedChannelsAffected)
        assertEquals("Thanks {user}!", body.message)
        assertTrue(body.isEnabled)
        assertEquals("Thanks {user}!", controller.state.value.eventDefaults.single().message)
        assertNull(controller.state.value.eventEdit)
    }

    @Test
    fun an_enabled_default_without_a_message_can_never_be_saved() = runTest {
        val api = FakePlatformDefaultsApi(emptyList(), events = listOf(follow))
        val controller = opened(api)
        controller.editEventMessage("   ")
        controller.previewEventEdit()

        controller.saveEventEdit()

        assertTrue(api.eventSaves.isEmpty())
        assertFalse(assertNotNull(controller.state.value.eventEdit).canSave)
    }
}
