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

import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import com.sun.net.httpserver.HttpExchange
import com.sun.net.httpserver.HttpServer
import java.awt.Desktop
import java.net.InetSocketAddress
import java.net.URI
import java.net.URLDecoder
import kotlin.coroutines.resume
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull

// Desktop OAuth — RFC-8252 loopback (frontend.md §6). Bind a transient listener on an OS-assigned
// ephemeral port on 127.0.0.1, open the system browser to the authorize URL with its redirect pointed
// at the loopback, and capture whatever the backend/provider delivers there. No app-side secret, no
// fixed port, no inbound firewall hole beyond the lifetime of the dance.
//
// Two shapes share the one loopback:
//   authorize()    — the streamer login: the callback carries `access_token` / `refresh_token` /
//                    `expires_in` (the same shape the mobile nomnomzbot:// deep-link redirect emits),
//                    which becomes a SessionTokens.
//   awaitConnect() — the bot-account + integration connects: the token is vaulted SERVER-SIDE, so the
//                    callback carries only a success marker (`bot_connected=true`, or a provider redirect
//                    with no error) / an `error=…`. No tokens are captured; the caller confirms the
//                    connection by re-polling its status endpoint.
private const val LOOPBACK_HOST: String = "127.0.0.1"
private const val CALLBACK_PATH: String = "/cb"
private const val AUTH_TIMEOUT_MS: Long = 5 * 60 * 1000L

actual class OAuthLauncher {

    actual suspend fun authorize(baseUrl: String, flow: OAuthFlow): ApiResult<SessionTokens> =
        runLoopback { redirect ->
            ApiResult.Ok(buildStreamerStartUrl(baseUrl, flow, redirect))
        } resolveWith { exchange ->
            parseTokenCallback(exchange)
        }

    actual suspend fun authorizeProvider(
        baseUrl: String,
        providerKey: String,
    ): ApiResult<SessionTokens> =
        runLoopback { redirect ->
            ApiResult.Ok(buildProviderStartUrl(baseUrl, providerKey, redirect))
        } resolveWith { exchange ->
            parseTokenCallback(exchange)
        }

    actual suspend fun awaitConnect(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ): ApiResult<Unit> =
        runLoopback(authorizeUrlFor) resolveWith { exchange ->
            parseSignalCallback(exchange)
        }

    // ── Loopback core ────────────────────────────────────────────────────────────

    /**
     * Bind the loopback, resolve the authorize URL (with the loopback redirect injected), open the
     * browser, and capture the callback exchange. The captured [HttpExchange] is mapped to the caller's
     * result by the [resolveWith] continuation, so the same listener serves both the token-returning and
     * the signal-only shapes.
     */
    private suspend fun <T> runLoopbackRaw(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>,
        map: (HttpExchange) -> ApiResult<T>,
    ): ApiResult<T> =
        withContext(Dispatchers.IO) {
            val server: HttpServer =
                try {
                    HttpServer.create(InetSocketAddress(LOOPBACK_HOST, 0), 0)
                } catch (cause: Throwable) {
                    return@withContext failure("LOOPBACK_BIND", cause.message ?: "Could not bind a loopback listener.")
                }

            val port: Int = server.address.port
            val redirect = "http://$LOOPBACK_HOST:$port$CALLBACK_PATH"

            val authorizeUrl: String =
                when (val url: ApiResult<String> = authorizeUrlFor(redirect)) {
                    is ApiResult.Failure -> {
                        server.stop(0)
                        return@withContext ApiResult.Failure(url.error)
                    }
                    is ApiResult.Ok -> url.value
                }

            try {
                val captured: ApiResult<T>? =
                    withTimeoutOrNull(AUTH_TIMEOUT_MS) {
                        suspendCancellableCoroutine { continuation ->
                            server.createContext(CALLBACK_PATH) { exchange ->
                                val result: ApiResult<T> = map(exchange)
                                respond(exchange, result is ApiResult.Ok)
                                if (continuation.isActive) continuation.resume(result)
                            }
                            server.start()
                            continuation.invokeOnCancellation { server.stop(0) }

                            if (!openBrowser(authorizeUrl)) {
                                if (continuation.isActive) {
                                    continuation.resume(
                                        failure(
                                            "BROWSER_LAUNCH",
                                            "Could not open the system browser. Open this URL manually: $authorizeUrl",
                                        )
                                    )
                                }
                            }
                        }
                    }

                captured
                    ?: failure("OAUTH_TIMEOUT", "Timed out waiting for the authorization to complete.")
            } finally {
                server.stop(0)
            }
        }

    // A tiny two-step builder so the two public flows read as one expression each: runLoopback { url }
    // resolveWith { exchange -> result }. It only carries the authorize-URL provider until the mapper
    // is supplied.
    private fun runLoopback(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ): LoopbackRun = LoopbackRun(authorizeUrlFor)

    private inner class LoopbackRun(
        private val authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ) {
        suspend infix fun <T> resolveWith(map: (HttpExchange) -> ApiResult<T>): ApiResult<T> =
            runLoopbackRaw(authorizeUrlFor, map)
    }

    // ── Authorize-URL builders ─────────────────────────────────────────────────────

    private fun buildStreamerStartUrl(baseUrl: String, flow: OAuthFlow, redirect: String): String {
        val base: String = baseUrl.trimEnd('/')
        // Streamer login is the token-returning user flow. The bot flow is signal-only and goes through
        // awaitConnect (it cannot return tokens), so it never reaches this builder.
        val path: String = if (flow == OAuthFlow.Bot) "/api/v1/auth/twitch/bot" else "/api/v1/auth/twitch"
        return "$base$path?client=desktop&redirect_uri=${encode(redirect)}"
    }

    // The generic per-provider login redirect (non-Twitch): the backend's `/auth/{key}/authorize` route,
    // token-returning to the loopback exactly like the streamer flow.
    private fun buildProviderStartUrl(baseUrl: String, providerKey: String, redirect: String): String {
        val base: String = baseUrl.trimEnd('/')
        return "$base/api/v1/auth/$providerKey/authorize?client=desktop&redirect_uri=${encode(redirect)}"
    }

    // ── Callback parsers ───────────────────────────────────────────────────────────

    private fun parseTokenCallback(exchange: HttpExchange): ApiResult<SessionTokens> {
        val params: Map<String, String> = parseQuery(exchange.requestURI.rawQuery)

        params["error"]?.let { error ->
            return failure(error, params["error_description"] ?: error)
        }

        val accessToken: String? = params["access_token"]
        if (accessToken.isNullOrBlank()) {
            return failure("NO_TOKEN", "The sign-in callback did not carry an access token.")
        }

        val expiresAt: Long? =
            params["expires_in"]?.toLongOrNull()?.let { seconds ->
                System.currentTimeMillis() + seconds * 1000L
            }

        return ApiResult.Ok(
            SessionTokens(
                accessToken = accessToken,
                refreshToken = params["refresh_token"]?.takeIf { it.isNotBlank() },
                expiresAt = expiresAt,
            )
        )
    }

    private fun parseSignalCallback(exchange: HttpExchange): ApiResult<Unit> {
        val params: Map<String, String> = parseQuery(exchange.requestURI.rawQuery)
        params["error"]?.let { error ->
            return failure(error, params["error_description"] ?: error)
        }
        // Any non-error return to the loopback means the backend completed and vaulted the token
        // server-side (`bot_connected=true` for the bot; the provider→backend→loopback hop for an
        // integration). The caller re-polls status to surface the authoritative connected state.
        return ApiResult.Ok(Unit)
    }

    // ── Browser + response ─────────────────────────────────────────────────────────

    private fun respond(exchange: HttpExchange, ok: Boolean) {
        val title: String = if (ok) "Done" else "Didn't complete"
        val body: String =
            if (ok) {
                "You're all set. You can close this tab and return to NomNomzBot."
            } else {
                "That didn't complete. Close this tab and try again from the app."
            }
        val html: String =
            "<!DOCTYPE html><html><head><title>$title</title>" +
                "<style>body{background:#141125;color:#f4f5fa;font-family:system-ui,sans-serif;" +
                "display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0}" +
                ".card{background:#1A1530;border-radius:16px;padding:48px;text-align:center;max-width:420px}" +
                "h1{font-size:22px;margin:0 0 8px}p{color:#8889a0;font-size:14px;margin:0}</style></head>" +
                "<body><div class='card'><h1>$title</h1><p>$body</p></div></body></html>"
        val bytes: ByteArray = html.encodeToByteArray()
        exchange.responseHeaders.add("Content-Type", "text/html; charset=utf-8")
        exchange.sendResponseHeaders(200, bytes.size.toLong())
        exchange.responseBody.use { it.write(bytes) }
    }

    private fun openBrowser(url: String): Boolean =
        try {
            if (Desktop.isDesktopSupported() && Desktop.getDesktop().isSupported(Desktop.Action.BROWSE)) {
                Desktop.getDesktop().browse(URI(url))
                true
            } else {
                false
            }
        } catch (_: Throwable) {
            false
        }

    private fun parseQuery(query: String?): Map<String, String> {
        if (query.isNullOrBlank()) return emptyMap()
        return query
            .split('&')
            .mapNotNull { pair ->
                val idx: Int = pair.indexOf('=')
                if (idx <= 0) return@mapNotNull null
                val key: String = decode(pair.substring(0, idx))
                val value: String = decode(pair.substring(idx + 1))
                key to value
            }
            .toMap()
    }

    private fun encode(value: String): String =
        java.net.URLEncoder.encode(value, Charsets.UTF_8.name())

    private fun decode(value: String): String = URLDecoder.decode(value, Charsets.UTF_8.name())

    private fun failure(code: String, message: String): ApiResult.Failure =
        ApiResult.Failure(ApiError(status = 0, code = code, message = message))
}
