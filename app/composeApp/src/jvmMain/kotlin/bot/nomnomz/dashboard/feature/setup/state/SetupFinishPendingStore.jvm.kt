// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.setup.state

import bot.nomnomz.dashboard.core.platform.DesktopDataDir
import java.io.File
import kotlinx.serialization.json.Json

// Desktop persistence — a small JSON file under the OS app-data dir (same base resolution as the token
// vault / language preference). Every read/write is guarded so a missing/corrupt/locked file degrades to
// "no pending record" rather than crashing the dashboard.
actual class SetupFinishPendingStore : SetupFinishStore {

    private val file: File by lazy { File(DesktopDataDir.resolve(), "setup-finish-pending.json") }
    private val json: Json = Json { ignoreUnknownKeys = true }

    actual override fun read(): SetupFinishPending? =
        runCatching {
            if (!file.exists()) return@runCatching null
            json.decodeFromString(SetupFinishPending.serializer(), file.readText())
        }.getOrNull()

    actual override fun write(pending: SetupFinishPending?) {
        runCatching {
            if (pending == null) {
                file.delete()
            } else {
                file.parentFile?.mkdirs()
                file.writeText(json.encodeToString(SetupFinishPending.serializer(), pending))
            }
        }
    }
}
