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

// Web token custody — DELIBERATELY persists nothing in JS-readable storage (frontend.md §6). On the browser
// build the long-lived refresh token lives in an HttpOnly + Secure cookie the backend sets, which JS can't
// read (so XSS can't steal it — localStorage/sessionStorage would be exfiltratable); the short-lived access
// token lives only in memory (the SessionStore) for the session. A relaunch therefore holds no token: it
// restores the session by calling /auth/refresh, which the browser answers by attaching that cookie
// automatically. The non-secret active profile is what persists (localStorage, via ActiveProfileVault), not
// any token. (Native keeps a real file/keychain vault — there is no browser XSS surface there.)
actual class TokenVault : SessionTokenStore {

    actual override suspend fun read(profileId: String): SessionTokens? = null

    actual override suspend fun write(profileId: String, tokens: SessionTokens) = Unit

    actual override suspend fun clear(profileId: String) = Unit
}
