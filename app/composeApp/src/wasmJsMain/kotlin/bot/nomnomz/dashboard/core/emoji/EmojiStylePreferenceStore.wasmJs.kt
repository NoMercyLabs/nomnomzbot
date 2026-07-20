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

import kotlinx.browser.window

// Web emoji-style persistence — localStorage (persists across tab close, unlike the session-scoped token
// store), since the emoji style is per-install UI state rather than a secret. Stores the raw style token;
// its ABSENCE means the default color style, so picking Color removes the key. Guarded so a storage
// exception (e.g. private-mode quota) degrades to the default rather than crashing.
actual class EmojiStylePreferenceStore : EmojiStyleStore {

    actual override fun read(): String? =
        runCatching { window.localStorage.getItem(KEY)?.takeIf { it.isNotBlank() } }.getOrNull()

    actual override fun write(token: String?) {
        runCatching {
            if (token == null) {
                window.localStorage.removeItem(KEY)
            } else {
                window.localStorage.setItem(KEY, token)
            }
        }
    }

    private companion object {
        const val KEY: String = "nnz.emoji-style"
    }
}
