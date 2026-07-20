// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.emoji

import java.io.File

// Desktop emoji-style persistence — a tiny text file under the OS app-data dir (same base resolution as
// the token vault and the language file). The file holds the raw style token (`monochrome`); its ABSENCE
// means the default color style, so picking Color deletes the file. Every read/write is guarded so a
// missing/corrupt/locked file degrades to the default rather than crashing the dashboard.
actual class EmojiStylePreferenceStore : EmojiStyleStore {

    private val file: File by lazy {
        val base: String =
            System.getenv("LOCALAPPDATA")
                ?: System.getenv("XDG_DATA_HOME")
                ?: (System.getProperty("user.home") + File.separator + ".local" + File.separator + "share")
        val dir = File(base, "NomNomzBot").apply { mkdirs() }
        File(dir, "emoji-style")
    }

    actual override fun read(): String? =
        runCatching {
            if (!file.exists()) return@runCatching null
            file.readText().trim().ifBlank { null }
        }.getOrNull()

    actual override fun write(token: String?) {
        runCatching {
            if (token == null) {
                file.delete()
            } else {
                file.writeText(token)
            }
        }
    }
}
