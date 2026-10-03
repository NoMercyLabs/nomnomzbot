// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.i18n

import bot.nomnomz.dashboard.core.network.LocalizedTextDto
import kotlinx.serialization.json.Json
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

class SchemaLocalizedStringTest {
    private val json: Json = Json { ignoreUnknownKeys = true }

    @Test
    fun verbatimText_isReturnedAsWritten_evenWhenTheKeyWouldMatchAResource() {
        // `pipelines_field_volume` is a real resource name; the author's own text must still win.
        val dto = LocalizedTextDto(key = "pipelines.field.volume", text = "Loudness")

        assertEquals("Loudness", verbatimSchemaText(dto))
    }

    @Test
    fun keyOnly_hasNoVerbatimText_soTheLookupStillApplies() {
        assertNull(verbatimSchemaText(LocalizedTextDto(key = "widget.alerts.events.label")))
        assertNull(verbatimSchemaText(null))
    }

    @Test
    fun wirePayload_withText_decodesTheVerbatimText_andAnOldPayloadStillDecodes() {
        val withText = json.decodeFromString<LocalizedTextDto>("""{"key":"","text":"My label"}""")
        val oldShape = json.decodeFromString<LocalizedTextDto>("""{"key":"widget.alerts.events.label"}""")

        assertEquals("My label", withText.text)
        assertEquals("", withText.key)
        assertNull(oldShape.text)
        assertEquals("widget.alerts.events.label", oldShape.key)
    }
}
