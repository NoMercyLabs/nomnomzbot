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

// Combines the saved-connections list ([SavedConnectionsStore]) with the per-profile token vault
// ([SessionTokenStore]) so a "forget" always removes both halves together — a connection's entry
// AND its stored token — while leaving every other connection's token untouched (S111b).
class SavedConnectionsRepository(
    private val connections: SavedConnectionsStore,
    private val tokens: SessionTokenStore,
) {
    suspend fun list(): List<SavedConnection> = connections.list()

    suspend fun activeId(): String? = connections.activeId()

    suspend fun add(connection: SavedConnection) {
        connections.add(connection)
    }

    suspend fun switchTo(id: String) {
        connections.setActive(id)
    }

    /** The stored session token for a connection, if it was ever signed into — null for a freshly added one. */
    suspend fun tokenFor(id: String): SessionTokens? = tokens.read(id)

    suspend fun forget(id: String) {
        connections.remove(id)
        tokens.clear(id)
    }
}
