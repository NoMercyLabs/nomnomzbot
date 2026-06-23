// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.connection

import kotlinx.browser.window
import kotlinx.serialization.json.Json

// Web token custody — the short-lived access token in sessionStorage, cleared on tab close
// (frontend.md §6). The build is served first-party by its own bot (single origin), and the
// refresh token rides the backend session/HttpOnly cookie set on callback, so the app never
// persists a long-lived secret in JS. Documented XSS caveat, acceptable for a first-party origin.
actual class TokenVault : SessionTokenStore {

    private val json: Json = Json { ignoreUnknownKeys = true }

    private fun keyFor(profileId: String): String = "nnz.tokens.$profileId"

    actual override suspend fun read(profileId: String): SessionTokens? {
        val raw: String = window.sessionStorage.getItem(keyFor(profileId)) ?: return null
        return runCatching { json.decodeFromString(SessionTokens.serializer(), raw) }.getOrNull()
    }

    actual override suspend fun write(profileId: String, tokens: SessionTokens) {
        window.sessionStorage.setItem(
            keyFor(profileId),
            json.encodeToString(SessionTokens.serializer(), tokens),
        )
    }

    actual override suspend fun clear(profileId: String) {
        window.sessionStorage.removeItem(keyFor(profileId))
    }
}
