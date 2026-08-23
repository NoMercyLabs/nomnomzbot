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

import kotlinx.serialization.Serializable

// A saved server connection (S111b — desktop saved connections). Distinct from [ConnectionProfile],
// which describes the CURRENTLY active backend for the running session; a [SavedConnection] is a named
// entry in the desktop app's persisted list — the profile-menu switcher (DEPLOY.md:41).
@Serializable
data class SavedConnection(
    val id: String,
    val label: String,
    val baseUrl: String,
    val lastUsedAt: Long?,
)

// Persists the desktop app's saved server connections and which one is active (frontend.md §6 —
// the native app is multi-origin, unlike the single-origin web build). Each connection's session
// token lives in the per-profile [SessionTokenStore] keyed by [SavedConnection.id]; forgetting a
// connection here must also drop its token there — [SavedConnectionsRepository] wires the two
// together so callers never forget one without the other.
interface SavedConnectionsStore {
    suspend fun list(): List<SavedConnection>

    suspend fun activeId(): String?

    suspend fun add(connection: SavedConnection)

    suspend fun setActive(id: String)

    suspend fun remove(id: String)
}
