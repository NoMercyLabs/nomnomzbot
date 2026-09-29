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

/**
 * Where the client keeps the act-as token it must present to `/auth/refresh` itself, so a reload (web) or an
 * in-process restart (desktop) comes back as the impersonated user.
 */
interface ActAsTokenCustody {
    /** The held act-as token, or null when this client holds none. */
    fun read(): String?

    fun hold(token: String)

    fun clear()
}

/**
 * Web custody: the server keeps the act-as token in the HttpOnly `nnz_act_as` cookie, which the browser sends to
 * `/auth/refresh` on its own. JS holds it nowhere — no localStorage, no sessionStorage — so this holds nothing.
 */
object CookieActAsCustody : ActAsTokenCustody {
    override fun read(): String? = null

    override fun hold(token: String) = Unit

    override fun clear() = Unit
}

/** Native custody: memory only. The desktop keeps one instance for the process, so it outlives an app restart. */
class InMemoryActAsCustody : ActAsTokenCustody {
    private var token: String? = null

    override fun read(): String? = token

    override fun hold(token: String) {
        this.token = token
    }

    override fun clear() {
        token = null
    }
}

/** The act-as token custody of this platform: the cookie on web, the process on desktop. */
expect fun platformActAsCustody(): ActAsTokenCustody
