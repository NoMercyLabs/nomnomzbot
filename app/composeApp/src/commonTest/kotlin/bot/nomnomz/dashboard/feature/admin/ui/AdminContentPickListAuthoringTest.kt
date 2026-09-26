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

import bot.nomnomz.dashboard.core.network.PickListTemplatePayload
import bot.nomnomz.dashboard.core.network.PlatformTemplateJson
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

// Proves the admin pick-list-template form writes exactly the payload the server's PickListTemplatePayload reads,
// and only accepts a name that works as a {list.pick.<name>} key.
class AdminContentPickListAuthoringTest {

    @Test
    fun the_form_writes_the_server_payload_and_drops_blank_entries() {
        val fields = PickListTemplateFields(name = " greetings ", description = "  ", items = listOf("Hey!", " ", "Hi!"))

        val payload: PickListTemplatePayload =
            PlatformTemplateJson.decodeFromString(PickListTemplatePayload.serializer(), fields.toPayloadJson())

        assertEquals("greetings", payload.name)
        assertNull(payload.description)
        assertEquals(listOf("Hey!", "Hi!"), payload.items)
    }

    @Test
    fun a_saved_draft_reads_back_into_the_same_form() {
        val original = PickListTemplateFields(name = "greetings", description = "Warm", items = listOf("Hey!"))

        assertEquals(original, PickListTemplateFields.fromPayloadJson(original.toPayloadJson()))
    }

    @Test
    fun only_a_template_key_safe_name_with_at_least_one_entry_is_complete() {
        assertTrue(PickListTemplateFields(name = "fight_moves-2", items = listOf("kick")).isComplete())
        assertFalse(PickListTemplateFields(name = "fight moves", items = listOf("kick")).isComplete())
        assertFalse(PickListTemplateFields(name = "{x}", items = listOf("kick")).isComplete())
        assertFalse(PickListTemplateFields(name = "fight", items = listOf(" ")).isComplete())
    }
}
