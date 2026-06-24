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
import kotlinx.serialization.json.Json

// Desktop active-profile custody — a single JSON file under the OS app-data dir, beside the token vault
// (frontend.md §6). The profile is not a secret (connection metadata only), so it stays plain JSON; the
// token vault, not this file, is what the secret-custody slice moves into the OS keychain.
actual class ActiveProfileVault : ActiveProfileStore {

    private val json: Json = Json { ignoreUnknownKeys = true }

    private val dir: File by lazy {
        val base: String =
            System.getenv("LOCALAPPDATA")
                ?: System.getenv("XDG_DATA_HOME")
                ?: (System.getProperty("user.home") + File.separator + ".local" + File.separator + "share")
        File(base, "NomNomzBot").apply { mkdirs() }
    }

    private val file: File by lazy { File(dir, "active-profile.json") }

    actual override suspend fun read(): ConnectionProfile? =
        withContext(Dispatchers.IO) {
            if (!file.exists()) return@withContext null
            runCatching { json.decodeFromString(ConnectionProfile.serializer(), file.readText()) }
                .getOrNull()
        }

    actual override suspend fun write(profile: ConnectionProfile) {
        withContext(Dispatchers.IO) {
            file.writeText(json.encodeToString(ConnectionProfile.serializer(), profile))
        }
    }

    actual override suspend fun clear() {
        withContext(Dispatchers.IO) { file.delete() }
    }
}
