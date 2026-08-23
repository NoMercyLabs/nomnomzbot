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

import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonPrimitive
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

/**
 * The dashboard used to show whatever it fetched when a page opened: a command added by another moderator,
 * or by the bot itself, only appeared after a manual reload. The backend had always broadcast
 * `ConfigChanged` on the dashboard hub — the client decoded it to [HubEvent.Unknown] and dropped it.
 * These pin both halves: the wire payload decodes into a real event, and a page only refetches for ITS
 * domain (so a quote edit does not make every open page re-query the backend).
 */
class ConfigChangeRefreshTest {

    private val hubJson: Json = Json { ignoreUnknownKeys = true }

    @Test
    fun the_wire_payload_decodes_into_a_config_changed_event_not_unknown() {
        val payload: String =
            """{"broadcasterId":"chan-1","domain":"commands","entityId":"cmd-7","action":"created"}"""

        val event: HubEvent? =
            HubEvent.from("ConfigChanged", JsonArray(listOf(hubJson.parseToJsonElement(payload))))

        val changed: HubEvent.ConfigChanged =
            event as? HubEvent.ConfigChanged
                ?: throw AssertionError("expected ConfigChanged, got $event")
        // Assert the DISTINCTIONS a refresh decision is made on — domain, entity and action — not merely
        // that something decoded: a payload that silently defaulted every field would still be non-null.
        assertEquals("commands", changed.change.domain)
        assertEquals("cmd-7", changed.change.entityId)
        assertEquals("created", changed.change.action)
        assertEquals("chan-1", changed.change.broadcasterId)
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun only_the_watched_domains_trigger_a_reload() = runTest {
        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        val reloadedFor: MutableList<String> = mutableListOf()

        val subscription = launch {
            events.onConfigChange("commands", "builtins") { change ->
                reloadedFor += change.domain
            }
        }
        testScheduler.advanceUntilIdle()

        events.emit(configChange("quotes"))
        events.emit(configChange("commands"))
        events.emit(configChange("builtins"))
        events.emit(configChange("timers"))
        testScheduler.advanceUntilIdle()
        subscription.cancel()

        // A page refetches for its own domains only — and in the order the events arrived, so a later
        // change cannot be overtaken by an earlier one.
        assertEquals(listOf("commands", "builtins"), reloadedFor)
    }

    @OptIn(ExperimentalCoroutinesApi::class)
    @Test
    fun a_non_config_event_never_triggers_a_reload() = runTest {
        val events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 16)
        var reloads = 0

        val subscription = launch { events.onConfigChange("commands") { reloads++ } }
        testScheduler.advanceUntilIdle()

        events.emit(HubEvent.Unknown("SomethingElse", "{}"))
        testScheduler.advanceUntilIdle()
        subscription.cancel()

        assertTrue(reloads == 0, "an unrelated hub event must not refetch the page")
    }

    private fun configChange(domain: String): HubEvent.ConfigChanged =
        HubEvent.ConfigChanged(
            HubConfigChanged(
                broadcasterId = "chan-1",
                domain = domain,
                entityId = null,
                action = "updated",
            )
        )
}
