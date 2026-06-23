// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

@file:OptIn(ExperimentalWasmJsInterop::class)

package bot.nomnomz.dashboard.core.connection

import bot.nomnomz.dashboard.core.network.ApiResult
import kotlin.js.ExperimentalWasmJsInterop
import kotlinx.browser.window
import kotlinx.coroutines.CompletableDeferred

// Web OAuth — standard same-origin redirect (frontend.md §6). The page navigates to the backend
// streamer-login; the backend completes the dance and returns the session to the served origin.
//
// Two-phase, because a redirect tears down the page:
//   1. On app load, [readReturnedSession] inspects the URL for a returned session and yields it
//      (then strips it from the address bar) — this is the post-redirect arm.
//   2. [authorize] is the pre-redirect arm: it navigates the page to the backend login and never
//      completes (the document is unloading), so it returns a never-resolving result.
//
// Backend contract this expects (see the report's "backend gap"): for the web flow the backend must
// redirect back to the served origin with the session — the access token in the URL fragment
// (`#access_token=…&expires_in=…`, kept out of the Referer/server logs) and the refresh token set as
// an HttpOnly session cookie. Today the web callback returns JSON in the response body, which a
// full-page redirect can't hand back to the app.
actual class OAuthLauncher {

    actual suspend fun authorize(baseUrl: String, flow: OAuthFlow): ApiResult<SessionTokens> {
        val path: String = if (flow == OAuthFlow.Bot) "/api/v1/auth/twitch/bot" else "/api/v1/auth/twitch"
        // Web is single-origin: the backend already knows the served origin to return to, so no
        // redirect param is sent (unlike the desktop loopback).
        window.location.assign("${baseUrl.trimEnd('/')}$path?client=web")
        // The page is unloading; this never resolves. The session arrives via readReturnedSession.
        return CompletableDeferred<ApiResult<SessionTokens>>().await()
    }
}

/**
 * Post-redirect arm: read a session the backend handed back to the served origin via the URL
 * fragment, then strip it from the address bar. Returns null when the URL carries no session.
 */
fun readReturnedSession(): SessionTokens? {
    val fragment: String = window.location.hash.removePrefix("#")
    if (fragment.isBlank()) return null

    val params: Map<String, String> = parseFragment(fragment)
    val accessToken: String = params["access_token"]?.takeIf { it.isNotBlank() } ?: return null

    val expiresAt: Long? =
        params["expires_in"]?.toLongOrNull()?.let { seconds ->
            nowEpochMillis() + seconds * 1000L
        }

    // Strip the token from the address bar so a copied URL / refresh can't replay it.
    window.history.replaceState(null, "", window.location.pathname + window.location.search)

    return SessionTokens(
        accessToken = accessToken,
        refreshToken = params["refresh_token"]?.takeIf { it.isNotBlank() },
        expiresAt = expiresAt,
    )
}

private fun parseFragment(fragment: String): Map<String, String> =
    fragment
        .split('&')
        .mapNotNull { pair ->
            val idx: Int = pair.indexOf('=')
            if (idx <= 0) return@mapNotNull null
            decodeComponent(pair.substring(0, idx)) to decodeComponent(pair.substring(idx + 1))
        }
        .toMap()

// `decodeURIComponent` is a JS global; bind it as an external function (js() bodies on wasmJs may
// not capture a Kotlin arg, so a plain external declaration is the correct seam).
private external fun decodeURIComponent(encoded: String): String

/** `Date.now()` as epoch millis — used to derive an absolute expiry from the relative `expires_in`. */
private fun jsNow(): Double = js("Date.now()")

private fun nowEpochMillis(): Long = jsNow().toLong()

private fun decodeComponent(value: String): String =
    runCatching { decodeURIComponent(value) }.getOrDefault(value)
