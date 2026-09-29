// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.widgets.state

import bot.nomnomz.dashboard.core.network.WidgetSummary
import kotlinx.serialization.json.Json
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// Which catalogue action an overlay row offers decides whether a streamer's edited code can be replaced without a
// confirm. These pin the rule: an edited widget only ever offers the confirmed Reset, never the direct Update.
class CatalogueRowActionTest {
    private val systemWidget: WidgetSummary =
        WidgetSummary(id = "w1", name = "Alerts", source = "first_party", galleryItemId = "g1")

    @Test
    fun an_edited_system_widget_offers_reset_even_when_a_catalogue_update_is_pending() {
        val edited: WidgetSummary = systemWidget.copy(isCustomized = true, galleryUpdateAvailable = true)

        assertEquals(CatalogueRowAction.Reset, edited.catalogueRowAction())
    }

    @Test
    fun an_unedited_widget_with_a_pending_catalogue_update_offers_the_direct_update() {
        assertEquals(
            CatalogueRowAction.Update,
            systemWidget.copy(galleryUpdateAvailable = true).catalogueRowAction(),
        )
    }

    @Test
    fun an_unedited_up_to_date_widget_and_a_custom_widget_offer_nothing() {
        assertEquals(CatalogueRowAction.None, systemWidget.catalogueRowAction())
        val custom: WidgetSummary = WidgetSummary(id = "w2", name = "Mine", source = "custom", galleryItemId = null)
        assertEquals(CatalogueRowAction.None, custom.copy(isCustomized = true).catalogueRowAction())
    }

    @Test
    fun only_first_party_widgets_are_marked_system() {
        assertTrue(systemWidget.isSystem)
        assertFalse(systemWidget.copy(source = "verified_gallery").isSystem)
        assertFalse(systemWidget.copy(source = "custom").isSystem)
    }

    @Test
    fun the_backend_customized_flag_decodes_onto_the_row() {
        val wire: String =
            """{"id":"w1","name":"Alerts","source":"first_party","galleryItemId":"g1","isCustomized":true}"""

        val decoded: WidgetSummary = Json { ignoreUnknownKeys = true }.decodeFromString(WidgetSummary.serializer(), wire)

        assertTrue(decoded.isCustomized)
        assertEquals(CatalogueRowAction.Reset, decoded.catalogueRowAction())
    }
}
