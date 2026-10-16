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

/**
 * The server pushes `sr_queue_changed` to the dashboard on every song-request queue change (a request added,
 * removed, promoted, or played). Landing in [HubEvent.Unknown] left the Song Requests page stale until the
 * playing track changed.
 */
class SrQueueHubEventTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun sr_queue_changed_target_decodes_to_the_queue_changed_event() {
        val payload = """{"items":[{"title":"Never Gonna","requestedBy":"viewer1","durationSec":213}]}"""
        val event: HubEvent? =
            HubEvent.from("sr_queue_changed", JsonArray(listOf(json.parseToJsonElement(payload))))

        assertEquals(HubEvent.SrQueueChanged, event)
    }
}
