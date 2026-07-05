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

import kotlinx.browser.window

// Web active-channel custody — localStorage (frontend.md §6). The channel id is plain metadata (no secret) and
// must outlive a tab reload so the served-origin session restores the same channel; hence localStorage, not the
// tab-scoped sessionStorage.
actual class ActiveChannelVault : ActiveChannelStore {

    private val key: String = "nnz.active-channel"

    actual override suspend fun read(): String? = window.localStorage.getItem(key)?.ifBlank { null }

    actual override suspend fun write(channelId: String) {
        window.localStorage.setItem(key, channelId)
    }

    actual override suspend fun clear() {
        window.localStorage.removeItem(key)
    }
}
