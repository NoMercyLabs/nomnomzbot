// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.ui

import bot.nomnomz.dashboard.core.network.PlatformTemplateJson
import bot.nomnomz.dashboard.core.network.TimerTemplatePayload
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Proves the admin timer-template form writes exactly the payload the server's TimerTemplatePayload reads:
// blank message rows dropped, numbers parsed, and a saved draft reads back into the same form.
class AdminContentTimerAuthoringTest {

    @Test
    fun the_form_writes_the_server_payload_shape_and_drops_blank_messages() {
        val fields =
            TimerTemplateFields(
                name = "  Hydrate ",
                messages = listOf("Drink water", "  ", "Stretch"),
                intervalMinutes = "45",
                minChatActivity = "5",
                fireOnce = true,
                isEnabled = false,
            )

        val payload: TimerTemplatePayload =
            PlatformTemplateJson.decodeFromString(TimerTemplatePayload.serializer(), fields.toPayloadJson())

        assertEquals("Hydrate", payload.name)
        assertEquals(listOf("Drink water", "Stretch"), payload.messages)
        assertEquals(45, payload.intervalMinutes)
        assertEquals(5, payload.minChatActivity)
        assertTrue(payload.fireOnce)
        assertFalse(payload.isEnabled)
    }

    @Test
    fun a_saved_draft_reads_back_into_the_same_form() {
        val original =
            TimerTemplateFields(name = "Hydrate", messages = listOf("Drink water"), intervalMinutes = "45")

        val reread: TimerTemplateFields = TimerTemplateFields.fromPayloadJson(original.toPayloadJson())

        assertEquals(original, reread)
    }

    @Test
    fun a_timer_without_messages_is_pipeline_only_and_an_out_of_range_interval_is_incomplete() {
        val pipelineOnly = TimerTemplateFields(name = "Ad break", messages = listOf(""), intervalMinutes = "60")

        assertTrue(pipelineOnly.toPayload().runsPipelineOnly)
        assertTrue(pipelineOnly.isComplete())
        assertFalse(pipelineOnly.copy(intervalMinutes = "0").isComplete())
        assertFalse(pipelineOnly.copy(intervalMinutes = "1441").isComplete())
        assertFalse(pipelineOnly.copy(name = " ").isComplete())
    }
}
