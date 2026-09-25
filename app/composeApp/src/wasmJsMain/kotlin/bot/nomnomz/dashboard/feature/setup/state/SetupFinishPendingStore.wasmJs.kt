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

import kotlinx.browser.window
import kotlinx.serialization.json.Json

// Web persistence — localStorage, since it survives the full-page redirect the streamer OAuth performs
// (unlike in-memory controller state, which is torn down the instant the page navigates away). Guarded so a
// storage exception (e.g. private-mode quota) degrades to "no pending record" rather than crashing.
actual class SetupFinishPendingStore : SetupFinishStore {

    private val json: Json = Json { ignoreUnknownKeys = true }

    actual override fun read(): SetupFinishPending? =
        runCatching {
            window.localStorage.getItem(KEY)?.takeIf { it.isNotBlank() }
                ?.let { json.decodeFromString(SetupFinishPending.serializer(), it) }
        }.getOrNull()

    actual override fun write(pending: SetupFinishPending?) {
        runCatching {
            if (pending == null) {
                window.localStorage.removeItem(KEY)
            } else {
                window.localStorage.setItem(KEY, json.encodeToString(SetupFinishPending.serializer(), pending))
            }
        }
    }

    private companion object {
        const val KEY: String = "nnz.setup_finish_pending"
    }
}
