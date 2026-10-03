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

import java.util.Locale
import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import bot.nomnomz.dashboard.core.network.GalleryItemSummary
import org.jetbrains.compose.resources.ExperimentalResourceApi
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.getString
import org.jetbrains.compose.resources.getSystemResourceEnvironment
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull

// S-EDITOR-I18N (gallery half): a gallery item that carries a translation key renders through the key lookup;
// one without a key (a community item) or with an unknown key falls back to the name/description as sent.
@OptIn(ExperimentalResourceApi::class)
class GalleryTextTest {
    private val json: Json = Json { ignoreUnknownKeys = true }

    @Test
    fun wirePayload_withKeys_decodesThem_andAnOldPayloadWithoutKeysStillDecodes() {
        val withKeys: GalleryItemSummary =
            json.decodeFromString("""{"id":"1","name":"Alerts","nameKey":"widget.gallery.alerts.name","descriptionKey":"widget.gallery.alerts.description"}""")
        val oldShape: GalleryItemSummary = json.decodeFromString("""{"id":"2","name":"Community"}""")

        assertEquals("widget.gallery.alerts.name", withKeys.nameKey)
        assertEquals("widget.gallery.alerts.description", withKeys.descriptionKey)
        assertNull(oldShape.nameKey)
        assertNull(oldShape.descriptionKey)
    }

    @Test
    fun aGalleryKey_resolvesToDutchText() = runBlocking {
        val original: Locale = Locale.getDefault()
        try {
            Locale.setDefault(Locale.forLanguageTag("nl"))
            val resource: StringResource =
                assertNotNull(galleryTextResource("widget.gallery.alerts.name"), "the alerts name key must resolve")
            val resolved: String = getString(getSystemResourceEnvironment(), resource)
            assertEquals("Meldingen", resolved)
        } finally {
            Locale.setDefault(original)
        }
    }

    @Test
    fun noKey_orAnUnknownKey_hasNoResource_soTheSentTextIsShown() {
        assertNull(galleryTextResource(null))
        assertNull(galleryTextResource(""))
        assertNull(galleryTextResource("widget.gallery.not_a_real_widget.name"))
    }
}
