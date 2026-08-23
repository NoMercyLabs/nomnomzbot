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

import java.io.File
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json

// Desktop persistence for the saved-connections list (S111b) — a single plain JSON file beside the
// active-profile file (frontend.md §6). Connection metadata (label + base URL) is not a secret, so
// it stays plain JSON; the token for each connection lives separately in the encrypted [TokenVault].
class FileSavedConnectionsStore internal constructor(private val file: File) : SavedConnectionsStore {

    constructor() : this(defaultFile())

    private val json: Json = Json { ignoreUnknownKeys = true }

    @Serializable
    private data class OnDiskState(
        val connections: List<SavedConnection> = emptyList(),
        val activeId: String? = null,
    )

    private fun readState(): OnDiskState {
        if (!file.exists()) return OnDiskState()
        return runCatching { json.decodeFromString(OnDiskState.serializer(), file.readText()) }
            .getOrDefault(OnDiskState())
    }

    private fun writeState(state: OnDiskState) {
        file.parentFile?.mkdirs()
        file.writeText(json.encodeToString(OnDiskState.serializer(), state))
    }

    override suspend fun list(): List<SavedConnection> =
        withContext(Dispatchers.IO) { readState().connections }

    override suspend fun activeId(): String? =
        withContext(Dispatchers.IO) { readState().activeId }

    override suspend fun add(connection: SavedConnection) {
        withContext(Dispatchers.IO) {
            val state: OnDiskState = readState()
            val withoutExisting: List<SavedConnection> = state.connections.filterNot { it.id == connection.id }
            writeState(state.copy(connections = withoutExisting + connection, activeId = connection.id))
        }
    }

    override suspend fun setActive(id: String) {
        withContext(Dispatchers.IO) {
            val state: OnDiskState = readState()
            if (state.connections.none { it.id == id }) return@withContext
            val touched: List<SavedConnection> =
                state.connections.map { if (it.id == id) it.copy(lastUsedAt = System.currentTimeMillis()) else it }
            writeState(state.copy(connections = touched, activeId = id))
        }
    }

    override suspend fun remove(id: String) {
        withContext(Dispatchers.IO) {
            val state: OnDiskState = readState()
            val remaining: List<SavedConnection> = state.connections.filterNot { it.id == id }
            val nextActiveId: String? =
                if (state.activeId == id) remaining.maxByOrNull { it.lastUsedAt ?: 0L }?.id else state.activeId
            writeState(state.copy(connections = remaining, activeId = nextActiveId))
        }
    }

    private companion object {
        fun defaultFile(): File {
            val base: String =
                System.getenv("LOCALAPPDATA")
                    ?: System.getenv("XDG_DATA_HOME")
                    ?: (System.getProperty("user.home") + File.separator + ".local" + File.separator + "share")
            return File(base, "NomNomzBot${File.separator}saved-connections.json")
        }
    }
}
