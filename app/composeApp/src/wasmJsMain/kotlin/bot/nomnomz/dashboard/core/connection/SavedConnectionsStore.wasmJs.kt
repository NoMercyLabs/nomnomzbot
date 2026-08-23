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

/** Web factory — a deliberate no-op (single-origin, served by its own bot; S111b is desktop-only). */
actual fun savedConnectionsStore(): SavedConnectionsStore = NoOpSavedConnectionsStore

private object NoOpSavedConnectionsStore : SavedConnectionsStore {
    override suspend fun list(): List<SavedConnection> = emptyList()

    override suspend fun activeId(): String? = null

    override suspend fun add(connection: SavedConnection) = Unit

    override suspend fun setActive(id: String) = Unit

    override suspend fun remove(id: String) = Unit
}
