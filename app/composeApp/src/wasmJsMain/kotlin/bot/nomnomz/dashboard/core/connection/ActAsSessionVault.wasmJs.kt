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
import kotlinx.serialization.json.Json

// Web act-as state — sessionStorage, not localStorage: it belongs to the one tab the operator started acting in and
// dies with it. It holds only the operator's return route and a one-shot notice. No token ever lands here: the
// act-as token rides the HttpOnly nnz_act_as cookie (CookieActAsCustody), exactly like the refresh token.
actual class ActAsSessionVault : ActAsSessionStore {

    private val json: Json = Json { ignoreUnknownKeys = true }

    actual override fun readReturnLocation(): String? = window.sessionStorage.getItem(RETURN_KEY)

    actual override fun writeReturnLocation(location: String) {
        window.sessionStorage.setItem(RETURN_KEY, location)
    }

    actual override fun clear() {
        window.sessionStorage.removeItem(RETURN_KEY)
    }

    actual override fun putNotice(notice: ActAsEndNotice) {
        window.sessionStorage.setItem(NOTICE_KEY, json.encodeToString(ActAsEndNotice.serializer(), notice))
    }

    actual override fun takeNotice(): ActAsEndNotice? {
        val raw: String = window.sessionStorage.getItem(NOTICE_KEY) ?: return null
        window.sessionStorage.removeItem(NOTICE_KEY)
        return runCatching { json.decodeFromString(ActAsEndNotice.serializer(), raw) }.getOrNull()
    }

    private companion object {
        const val RETURN_KEY: String = "nnz.act-as-return"
        const val NOTICE_KEY: String = "nnz.act-as-notice"
    }
}

actual fun platformActAsCustody(): ActAsTokenCustody = CookieActAsCustody
