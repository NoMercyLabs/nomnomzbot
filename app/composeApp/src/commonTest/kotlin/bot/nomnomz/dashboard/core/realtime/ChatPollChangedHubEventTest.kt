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
import kotlin.test.assertNull

// Proves the `ChatPollChanged` hub target decodes into [HubEvent.ChatPollChanged] with the server's payload
// shape (backend ChatPollChangedAlertDto) — a renamed target or key would land in [HubEvent.Unknown] and the
// poll card would silently stop following the poll.
class ChatPollChangedHubEventTest {
    private val json = Json { ignoreUnknownKeys = true }

    @Test
    fun chat_poll_changed_decodes_with_the_whole_poll() {
        val payload =
            """{"pollId":"0b1c","change":"vote","question":"Best game?","status":"open","totalVotes":3,""" +
                """"options":[{"index":1,"label":"A","votes":2},{"index":2,"label":"B","votes":1}],""" +
                """"openedAt":"2026-07-20T00:00:00Z","closesAt":null,"closedAt":null}"""
        val event: HubEvent? = HubEvent.from("ChatPollChanged", JsonArray(listOf(json.parseToJsonElement(payload))))

        assertIs<HubEvent.ChatPollChanged>(event)
        assertEquals("0b1c", event.poll.pollId)
        assertEquals("vote", event.poll.change)
        assertEquals("open", event.poll.status)
        assertEquals(3, event.poll.totalVotes)
        assertEquals(listOf(2, 1), event.poll.options.map { it.votes })
        assertEquals(listOf(1, 2), event.poll.options.map { it.index })
        assertNull(event.poll.closedAt)
    }
}
