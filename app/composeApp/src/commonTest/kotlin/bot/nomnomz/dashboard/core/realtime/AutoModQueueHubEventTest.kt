// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.realtime

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertIs

/**
 * Proves the `automod_queue_changed` hub target (S-OWN22) decodes into [HubEvent.AutoModQueueChanged]
 * with its exact fields — a mis-typed target or a renamed payload key would silently land in
 * [HubEvent.Unknown] and the attention inbox would only ever change on reload again.
 */
class AutoModQueueHubEventTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun automod_queue_changed_target_decodes_with_its_exact_fields() {
        val payload = """{"messageId":"amsg-7","userDisplayName":"Chatter","change":"held"}"""
        val event: HubEvent? =
            HubEvent.from("automod_queue_changed", JsonArray(listOf(json.parseToJsonElement(payload))))

        assertIs<HubEvent.AutoModQueueChanged>(event)
        assertEquals("amsg-7", event.change.messageId)
        assertEquals("Chatter", event.change.userDisplayName)
        assertEquals("held", event.change.change)
    }

    @Test
    fun a_resolution_keeps_its_verdict_distinct_from_a_hold() {
        val payload = """{"messageId":"amsg-8","userDisplayName":"Chatter","change":"denied"}"""
        val event: HubEvent? =
            HubEvent.from("automod_queue_changed", JsonArray(listOf(json.parseToJsonElement(payload))))

        assertIs<HubEvent.AutoModQueueChanged>(event)
        assertEquals("denied", event.change.change)
        assertEquals("amsg-8", event.change.messageId)
    }
}
