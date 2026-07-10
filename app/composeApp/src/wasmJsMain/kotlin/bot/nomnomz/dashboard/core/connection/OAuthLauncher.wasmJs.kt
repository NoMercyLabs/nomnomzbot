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
import kotlinx.coroutines.delay

// Web OAuth — standard same-origin redirect (frontend.md §6). The page navigates to the backend
// authorize URL; the backend completes the dance and returns to the served origin.
//
// Two-phase, because a redirect tears down the page:
//   1. On app load, [readReturnedSession] / [readReturnedConnect] inspect the URL for a returned
//      session (streamer login — fragment tokens) or a connect marker (bot/integration — query flags)
//      and yield it, then strip it from the address bar.
//   2. [authorize] / [awaitConnect] are the pre-redirect arms: they navigate the page to the backend
//      and never complete (the document is unloading).
//
// Backend contract this expects (see the report's "backend gap"):
//   - Streamer login (token-returning): the web callback must redirect back to the served origin with
//     the access token in the URL fragment (`#access_token=…&expires_in=…`) and the refresh token as an
//     HttpOnly cookie. Today the web callback returns JSON in the body, which a full-page redirect can't
//     hand back to the app.
//   - Bot/integration connects (signal-only): the backend already redirects back to the frontend with a
//     marker query (`?bot_connected=true`, `?discord_connected=true`, or `?provider=…&error=…`), which
//     [readReturnedConnect] reads on reload.
actual class OAuthLauncher {

    actual suspend fun authorize(baseUrl: String, flow: OAuthFlow): ApiResult<SessionTokens> {
        val path: String = if (flow == OAuthFlow.Bot) "/api/v1/auth/twitch/bot" else "/api/v1/auth/twitch"
        // Web is single-origin: the backend already knows the served origin to return to, so no
        // redirect param is sent (unlike the desktop loopback).
        window.location.assign("${baseUrl.trimEnd('/')}$path?client=web")
        // The page is unloading; this never resolves. The session arrives via readReturnedSession.
        return CompletableDeferred<ApiResult<SessionTokens>>().await()
    }

    actual suspend fun authorizeProvider(
        baseUrl: String,
        providerKey: String,
    ): ApiResult<SessionTokens> {
        // Web is single-origin: the backend returns to the served origin itself, so no redirect param is
        // sent (unlike the desktop loopback). Same shape as [authorize] — the session rides back in the URL
        // fragment on reload, read by readReturnedSession.
        window.location.assign("${baseUrl.trimEnd('/')}/api/v1/auth/$providerKey/authorize?client=web")
        return CompletableDeferred<ApiResult<SessionTokens>>().await()
    }

    actual suspend fun awaitConnect(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ): ApiResult<Unit> {
        // Web is single-origin: the backend returns to the served origin itself.
        return when (val url: ApiResult<String> = authorizeUrlFor("")) {
            is ApiResult.Failure -> ApiResult.Failure(url.error)
            is ApiResult.Ok -> {
                val popup = window.open(url.value, "_blank", "width=620,height=760,popup=yes")
                if (popup == null) {
                    // Popup was blocked — fall back to full-page redirect (original behaviour).
                    window.location.assign(url.value)
                    return CompletableDeferred<ApiResult<Unit>>().await()
                }
                // Spin until the /oauth-relay page closes the popup (it auto-closes after
                // postMessage-ing the result back). The calling controller re-polls status on return.
                while (!popup.closed) {
                    delay(300)
                }
                ApiResult.Ok(Unit)
            }
        }
    }
}

/**
 * Post-redirect arm for streamer login: read a session the backend handed back to the served origin via
 * the URL fragment, then strip it from the address bar. Returns null when the URL carries no session.
 */
fun readReturnedSession(): SessionTokens? {
    val fragment: String = window.location.hash.removePrefix("#")
    if (fragment.isBlank()) return null

    val params: Map<String, String> = parsePairs(fragment)
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

/**
 * Post-redirect arm for the bot/integration connects: read the marker the backend appended to the
 * served-origin query (`bot_connected`, `custom_bot_connected`, `discord_connected`, or `provider` +
 * `error`), then strip it from the address bar. Returns the outcome, or null when no marker is present.
 */
fun readReturnedConnect(): ConnectReturn? {
    val query: String = window.location.search.removePrefix("?")
    if (query.isBlank()) return null

    val params: Map<String, String> = parsePairs(query)
    val provider: String? = params["provider"]
    val error: String? = params["error"]

    val outcome: ConnectReturn? =
        when {
            error != null -> ConnectReturn(provider, connected = false, errorCode = error)
            params["bot_connected"] == "true" || params["custom_bot_connected"] == "true" ->
                ConnectReturn(provider = "bot", connected = true, errorCode = null)
            params["discord_connected"] == "true" ->
                ConnectReturn(provider = "discord", connected = true, errorCode = null)
            else -> null
        }

    if (outcome != null) {
        window.history.replaceState(null, "", window.location.pathname)
    }
    return outcome
}

/** A connect outcome the backend handed back to the served origin after a bot/integration connect. */
data class ConnectReturn(val provider: String?, val connected: Boolean, val errorCode: String?)

private fun parsePairs(raw: String): Map<String, String> =
    raw
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
