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

// Desktop token custody — a per-profile JSON file under the OS app-data dir (frontend.md §6).
// The secret-custody slice swaps this for the OS keychain (Windows DPAPI / macOS Keychain /
// Linux libsecret) behind the same expect interface; the file store proves the direct-connect
// persistence loop in this slice.
actual class TokenVault : SessionTokenStore {

    private val json: Json = Json { ignoreUnknownKeys = true }

    private val dir: File by lazy {
        val base: String =
            System.getenv("LOCALAPPDATA")
                ?: System.getenv("XDG_DATA_HOME")
                ?: (System.getProperty("user.home") + File.separator + ".local" + File.separator + "share")
        File(base, "NomNomzBot${File.separator}tokens").apply { mkdirs() }
    }

    private fun fileFor(profileId: String): File = File(dir, "$profileId.json")

    actual override suspend fun read(profileId: String): SessionTokens? =
        withContext(Dispatchers.IO) {
            val file: File = fileFor(profileId)
            if (!file.exists()) return@withContext null
            runCatching { json.decodeFromString(SessionTokens.serializer(), file.readText()) }
                .getOrNull()
        }

    actual override suspend fun write(profileId: String, tokens: SessionTokens) {
        withContext(Dispatchers.IO) {
            fileFor(profileId).writeText(json.encodeToString(SessionTokens.serializer(), tokens))
        }
    }

    actual override suspend fun clear(profileId: String) {
        withContext(Dispatchers.IO) { fileFor(profileId).delete() }
    }
}
