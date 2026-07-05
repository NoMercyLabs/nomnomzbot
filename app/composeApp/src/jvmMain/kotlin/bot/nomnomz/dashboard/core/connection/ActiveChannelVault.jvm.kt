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

// Desktop active-channel custody — a single plain-text file under the OS app-data dir, beside the token and
// profile vaults (frontend.md §6). The channel id is not a secret (a tenant GUID), so it stays plain text.
actual class ActiveChannelVault : ActiveChannelStore {

    private val dir: File by lazy {
        val base: String =
            System.getenv("LOCALAPPDATA")
                ?: System.getenv("XDG_DATA_HOME")
                ?: (System.getProperty("user.home") + File.separator + ".local" + File.separator + "share")
        File(base, "NomNomzBot").apply { mkdirs() }
    }

    private val file: File by lazy { File(dir, "active-channel.txt") }

    actual override suspend fun read(): String? =
        withContext(Dispatchers.IO) {
            if (!file.exists()) return@withContext null
            runCatching { file.readText().trim().ifBlank { null } }.getOrNull()
        }

    actual override suspend fun write(channelId: String) {
        withContext(Dispatchers.IO) { file.writeText(channelId) }
    }

    actual override suspend fun clear() {
        withContext(Dispatchers.IO) { file.delete() }
    }
}
