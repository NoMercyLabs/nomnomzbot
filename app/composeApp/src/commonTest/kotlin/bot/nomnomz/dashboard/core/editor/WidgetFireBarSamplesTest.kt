// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue

// S060: the web project-editor's fire bar used to post an empty `{}` payload for every event type it fired into the
// sandboxed preview -- a chat box, cheer alert, or poll bar had nothing to read no matter which button was pressed.
// [WidgetFireBarSamples] ports the server's per-event-type sample catalogue (WidgetTestEventController.cs's
// `WidgetTestSamples`) so the SAME representative shape the real "test this overlay" dashboard action fires is
// available client-side. These assertions prove real, event-specific field structure -- not a placeholder object,
// and not the same shape reused for every event.
class WidgetFireBarSamplesTest {
    @Test
    fun `cheer sample carries a real user and bit amount, not a placeholder`() {
        val sample: JsonObject = WidgetFireBarSamples.sampleFor("cheer")

        assertEquals("TestCheerer", sample.getValue("user").jsonPrimitive.content)
        assertEquals(500, sample.getValue("amount").jsonPrimitive.content.toInt())
    }

    @Test
    fun `chat message sample carries real fragments and badges, distinct from every other event`() {
        val chat: JsonObject = WidgetFireBarSamples.sampleFor("ChatMessage")

        assertEquals("Hey chat! Kappa this stream is amazing LUL 4Head", chat.getValue("message").jsonPrimitive.content)
        val fragments = chat.getValue("fragments").jsonArray
        assertEquals(6, fragments.size)
        val firstEmote = fragments[1].jsonObject
        assertEquals("emote", firstEmote.getValue("type").jsonPrimitive.content)
        assertEquals("Kappa", firstEmote.getValue("text").jsonPrimitive.content)
        val badges = chat.getValue("badges").jsonArray
        assertEquals("broadcaster", badges[0].jsonObject.getValue("setId").jsonPrimitive.content)

        // Distinct shape from an unrelated event -- proves the samples are per-event-type, not one generic blob.
        val cheer: JsonObject = WidgetFireBarSamples.sampleFor("cheer")
        assertTrue("fragments" !in cheer)
        assertTrue("message" !in cheer)
    }

    @Test
    fun `poll begin and poll end carry different fields, matching the real broadcast frames`() {
        val begin: JsonObject = WidgetFireBarSamples.sampleFor("poll_begin")
        val end: JsonObject = WidgetFireBarSamples.sampleFor("poll_end")

        assertTrue("winningChoiceId" !in begin)
        assertEquals("c1", end.getValue("winningChoiceId").jsonPrimitive.content)
    }

    @Test
    fun `an unknown event type falls back to the documented default sample, not an empty object`() {
        val fallback: JsonObject = WidgetFireBarSamples.sampleFor("something_nobody_declared")

        assertEquals("TestUser", fallback.getValue("user").jsonPrimitive.content)
        assertEquals(1, fallback.size)
    }

    @Test
    fun `allSamplesJson embeds every known event type plus the default key, each as real JSON`() {
        val encoded: JsonObject = Json.parseToJsonElement(WidgetFireBarSamples.allSamplesJson()).jsonObject

        assertTrue(encoded.containsKey("follow"))
        assertTrue(encoded.containsKey("reward_redeemed"))
        assertTrue(encoded.containsKey(WidgetFireBarSamples.DefaultKey))
        assertEquals("TestFollower", encoded.getValue("follow").jsonObject.getValue("user").jsonPrimitive.content)
    }
}
