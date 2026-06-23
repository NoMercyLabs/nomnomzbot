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

import kotlinx.browser.window

// Web language persistence — localStorage (persists across tab close, unlike the session-scoped token
// store), since the display-language preference is per-install UI state rather than a secret. Stores the
// raw language tag; its ABSENCE means System default, so picking System default removes the key. Guarded
// so a storage exception (e.g. private-mode quota) degrades to System default rather than crashing.
actual class LanguagePreferenceStore : LanguageStore {

    actual override fun read(): String? =
        runCatching { window.localStorage.getItem(KEY)?.takeIf { it.isNotBlank() } }.getOrNull()

    actual override fun write(tag: String?) {
        runCatching {
            if (tag == null) {
                window.localStorage.removeItem(KEY)
            } else {
                window.localStorage.setItem(KEY, tag)
            }
        }
    }

    private companion object {
        const val KEY: String = "nnz.language"
    }
}
