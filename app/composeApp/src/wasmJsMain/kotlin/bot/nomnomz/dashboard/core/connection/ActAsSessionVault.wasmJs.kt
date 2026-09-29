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
import kotlinx.serialization.KSerializer
import kotlinx.serialization.json.Json

// Web act-as custody — sessionStorage, not localStorage: the act-as session belongs to the one tab it was started
// in, survives that tab's reload (F5 stays impersonated), and dies with the tab. Only the act-as ACCESS token is
// kept: access-only (no refresh), clamped to the open support session and revoked server-side on Exit. That is a
// deliberate, narrow exception to TokenVault's "no token in JS storage" rule — script on this page can already mint
// an access token by calling /auth/refresh with the cookie, so this adds no stronger credential. The refresh token
// (the long-lived one) never leaves its HttpOnly cookie.
actual class ActAsSessionVault : ActAsSessionStore {

    private val json: Json = Json { ignoreUnknownKeys = true }

    actual override fun read(): PersistedActAs? = decode(SESSION_KEY, PersistedActAs.serializer())

    actual override fun write(session: PersistedActAs) {
        window.sessionStorage.setItem(SESSION_KEY, json.encodeToString(PersistedActAs.serializer(), session))
    }

    actual override fun clear() {
        window.sessionStorage.removeItem(SESSION_KEY)
    }

    actual override fun putNotice(notice: ActAsEndNotice) {
        window.sessionStorage.setItem(NOTICE_KEY, json.encodeToString(ActAsEndNotice.serializer(), notice))
    }

    actual override fun takeNotice(): ActAsEndNotice? {
        val notice: ActAsEndNotice? = decode(NOTICE_KEY, ActAsEndNotice.serializer())
        window.sessionStorage.removeItem(NOTICE_KEY)
        return notice
    }

    private fun <T> decode(key: String, serializer: KSerializer<T>): T? {
        val raw: String = window.sessionStorage.getItem(key) ?: return null
        return runCatching { json.decodeFromString(serializer, raw) }.getOrNull()
    }

    private companion object {
        const val SESSION_KEY: String = "nnz.act-as"
        const val NOTICE_KEY: String = "nnz.act-as-notice"
    }
}
