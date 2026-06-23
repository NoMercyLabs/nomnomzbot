// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.i18n

import java.io.File

// Desktop language persistence — a tiny text file under the OS app-data dir (same base resolution as
// the token vault). The file holds the raw language tag (`en` / `nl`); its ABSENCE means System default,
// so picking System default deletes the file. Every read/write is guarded so a missing/corrupt/locked
// file degrades to System default rather than crashing the dashboard.
actual class LanguagePreferenceStore : LanguageStore {

    private val file: File by lazy {
        val base: String =
            System.getenv("LOCALAPPDATA")
                ?: System.getenv("XDG_DATA_HOME")
                ?: (System.getProperty("user.home") + File.separator + ".local" + File.separator + "share")
        val dir = File(base, "NomNomzBot").apply { mkdirs() }
        File(dir, "language")
    }

    actual override fun read(): String? =
        runCatching {
            if (!file.exists()) return@runCatching null
            file.readText().trim().ifBlank { null }
        }.getOrNull()

    actual override fun write(tag: String?) {
        runCatching {
            if (tag == null) {
                file.delete()
            } else {
                file.writeText(tag)
            }
        }
    }
}
