// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.serialization.json.Json

// The viewer's Twitch suspicious-user flag rides on the user-context DTO as `lowTrustStatus`. The three wire
// values must reach the app unchanged, and an older server that omits the field must read as "none".
class LowTrustStatusDtoTest {
    private val json: Json = Json { ignoreUnknownKeys = true }

    private fun statusOf(body: String): String =
        json.decodeFromString(UserModerationContext.serializer(), body).lowTrustStatus

    @Test
    fun all_three_statuses_parse_unchanged() {
        assertEquals("none", statusOf("""{"userId":"u1","lowTrustStatus":"none"}"""))
        assertEquals("active_monitoring", statusOf("""{"userId":"u1","lowTrustStatus":"active_monitoring"}"""))
        assertEquals("restricted", statusOf("""{"userId":"u1","lowTrustStatus":"restricted"}"""))
    }

    @Test
    fun a_server_without_the_field_reads_as_none() {
        assertEquals("none", statusOf("""{"userId":"u1"}"""))
    }
}
