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
import kotlin.test.assertTrue

// Pins [ApiClient.Companion.failureMessage] against the real defect (2026-08-24, qtkitte's Spotify
// BYOC save): a failing request whose body carries NO problem-details detail/title — and whose HTTP
// reason phrase is blank, as browsers commonly report over HTTP/2/3 (the norm behind the Cloudflare
// front the deployed instances sit behind) — used to fall through to an empty string, rendering
// "Couldn't save the credentials:" with nothing after the colon. Every path here must resolve to a
// non-blank, actionable sentence that at minimum names the HTTP status.
class ApiClientFailureMessageTest {

    @Test
    fun `empty body and empty reason phrase on a 403 still produces real text`() {
        val message: String = ApiClient.failureMessage(status = 403, detail = null, title = null, reasonPhrase = "")

        assertTrue(message.isNotBlank(), "message must never be blank")
        assertTrue(message.contains("403"), "message should name the HTTP status: $message")
    }

    @Test
    fun `empty body and empty reason phrase on a 401 still produces real text`() {
        val message: String = ApiClient.failureMessage(status = 401, detail = null, title = null, reasonPhrase = "")

        assertTrue(message.isNotBlank())
        assertTrue(message.contains("401"), "message should name the HTTP status: $message")
    }

    @Test
    fun `empty body and empty reason phrase on a generic 5xx still produces real text`() {
        val message: String = ApiClient.failureMessage(status = 502, detail = null, title = null, reasonPhrase = "")

        assertTrue(message.isNotBlank())
        assertTrue(message.contains("502"), "message should name the HTTP status: $message")
    }

    @Test
    fun `a blank (whitespace-only) detail is treated as absent, not used verbatim`() {
        val message: String =
            ApiClient.failureMessage(status = 500, detail = "   ", title = null, reasonPhrase = null)

        assertTrue(message.isNotBlank())
        assertTrue(message.contains("500"))
    }

    @Test
    fun `a real problem-details detail always wins over the fallback`() {
        val message: String =
            ApiClient.failureMessage(
                status = 400,
                detail = "A Spotify client secret is required.",
                title = "Validation failed",
                reasonPhrase = "",
            )

        assertEquals("A Spotify client secret is required.", message)
    }

    @Test
    fun `a non-blank reason phrase is still used when there is no problem-details payload`() {
        val message: String =
            ApiClient.failureMessage(status = 418, detail = null, title = null, reasonPhrase = "I'm a Teapot")

        assertTrue(message.contains("I'm a Teapot"))
        assertTrue(message.contains("418"))
    }
}
