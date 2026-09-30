// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.ui

import androidx.compose.material3.Text
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import java.io.File
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlin.test.fail

// The Event Responses list showed raw keys ("channel.ad_break.begin", "channel.ban") as row titles because only
// 18 of the backend catalogue's event types had a friendly name. This reads the backend catalogue itself, so a
// new event type without a label fails here instead of reaching the list as a raw key.
@OptIn(ExperimentalTestApi::class)
class EventTypeLabelsTest {
    private val services = File("../../server/src/NomNomzBot.Application/Commands/Services")

    private fun backendEventTypes(): Set<String> {
        val presets = File(services, "EventResponsePresetCatalog.cs")
        val tones = File(services, "EventResponseToneCatalog.cs")
        if (!presets.isFile || !tones.isFile) fail("backend catalogues not found under ${services.absolutePath}")
        val fromPresets: Set<String> =
            Regex("""\bPreset\(\s*"([^"]+)"""").findAll(presets.readText()).map { it.groupValues[1] }.toSet()
        val fromTones: Set<String> =
            Regex("""\bAdd\(\s*catalog,\s*"([^"]+)"""").findAll(tones.readText()).map { it.groupValues[1] }.toSet()
        return fromPresets + fromTones
    }

    private fun stringNames(path: String): Set<String> =
        Regex("""<string name="([^"]+)">""").findAll(File(path).readText()).map { it.groupValues[1] }.toSet()

    @Test
    fun every_event_type_the_backend_catalogue_serves_has_a_friendly_label() {
        val types: Set<String> = backendEventTypes()
        assertTrue(types.size >= 35, "expected the whole backend catalogue, parsed only ${types.size} event types")

        assertEquals(emptySet(), types - EventTypeLabels.keys, "backend event types with no friendly label")
    }

    @Test
    fun every_label_resolves_to_a_string_in_english_and_dutch() {
        val english: Set<String> = stringNames("src/commonMain/composeResources/values/strings.xml")
        val dutch: Set<String> = stringNames("src/commonMain/composeResources/values-nl/strings.xml")
        val keys: Set<String> = EventTypeLabels.values.map { it.key }.toSet()

        assertEquals(emptySet(), keys - english, "labels missing from English")
        assertEquals(emptySet(), keys - dutch, "labels missing from Dutch")
    }

    private fun renders(tag: String, eventType: String, expected: String) = runComposeUiTest {
        setContent { AppEnvironment(tag = tag) { NomNomzTheme { Text(eventType.toEventLabel()) } } }
        waitForIdle()
        onNodeWithText(expected).assertExists()
    }

    @Test
    fun the_two_keys_seen_raw_on_the_live_list_render_as_names() {
        renders("en", "channel.ad_break.begin", "Ad Break Started")
        renders("en", "channel.ban", "Ban or Timeout")
        renders("nl", "channel.ad_break.begin", "Advertentiepauze gestart")
        renders("nl", "channel.ban", "Ban of timeout")
    }

    @Test
    fun an_unknown_event_type_still_shows_its_key_rather_than_nothing() {
        renders("en", "future.event", "future.event")
    }
}
