// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.customevents.state

import bot.nomnomz.dashboard.core.network.CustomDataSource
import kotlinx.serialization.json.Json
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNull
import kotlin.test.assertTrue

/** Proves the push-source dashboard step: the DTO carries `inboundUrl`, and the form needs a secret for push. */
class CustomEventsPushSourceTest {
    private val json: Json = Json { ignoreUnknownKeys = true }

    @Test
    fun the_source_dto_decodes_the_inbound_url() {
        val source: CustomDataSource =
            json.decodeFromString(
                """{"id":"s1","name":"hr","sourceKind":"push","hasAuthSecret":true,"inboundUrl":"https://bot.example/api/v1/webhooks/in/abc"}""",
            )

        assertEquals("https://bot.example/api/v1/webhooks/in/abc", source.inboundUrl)
    }

    @Test
    fun the_source_dto_has_no_inbound_url_when_the_server_sends_none() {
        val source: CustomDataSource = json.decodeFromString("""{"id":"s1","name":"hr","sourceKind":"poll"}""")

        assertNull(source.inboundUrl)
    }

    @Test
    fun a_push_source_cannot_save_without_a_secret() {
        assertFalse(isSourceSaveAllowed("hr", "Heart rate", "push", "", hasStoredSecret = false))
        assertFalse(isSourceSaveAllowed("hr", "Heart rate", "push", "   ", hasStoredSecret = false))
    }

    @Test
    fun a_push_source_saves_with_a_secret_or_a_stored_one() {
        assertTrue(isSourceSaveAllowed("hr", "Heart rate", "push", "s3cret", hasStoredSecret = false))
        assertTrue(isSourceSaveAllowed("hr", "Heart rate", "push", "", hasStoredSecret = true))
    }

    @Test
    fun a_poll_source_saves_without_a_secret() {
        assertTrue(isSourceSaveAllowed("hr", "Heart rate", "poll", "", hasStoredSecret = false))
    }

    @Test
    fun a_source_still_needs_a_name_and_display_name() {
        assertFalse(isSourceSaveAllowed("", "Heart rate", "poll", "", hasStoredSecret = false))
        assertFalse(isSourceSaveAllowed("hr", " ", "poll", "", hasStoredSecret = false))
    }
}
