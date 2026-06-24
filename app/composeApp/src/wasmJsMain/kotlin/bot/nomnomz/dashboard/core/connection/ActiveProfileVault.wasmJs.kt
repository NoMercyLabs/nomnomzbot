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

// Web active-profile custody — localStorage (frontend.md §6). The profile is plain connection metadata
// (no secret), and it must outlive the tab so a relaunch on the served origin restores the session; hence
// localStorage, not the tab-scoped sessionStorage. The single-origin web build only ever has one such
// profile (its own serving origin).
actual class ActiveProfileVault : ActiveProfileStore {

    private val json: Json = Json { ignoreUnknownKeys = true }

    private val key: String = "nnz.active-profile"

    actual override suspend fun read(): ConnectionProfile? {
        val raw: String = window.localStorage.getItem(key) ?: return null
        return runCatching { json.decodeFromString(ConnectionProfile.serializer(), raw) }.getOrNull()
    }

    actual override suspend fun write(profile: ConnectionProfile) {
        window.localStorage.setItem(key, json.encodeToString(ConnectionProfile.serializer(), profile))
    }

    actual override suspend fun clear() {
        window.localStorage.removeItem(key)
    }
}
