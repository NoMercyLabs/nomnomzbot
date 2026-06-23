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
// ephemeral port on 127.0.0.1, open the system browser to the backend streamer-login URL with
// redirect pointed at the loopback, and capture the tokens the backend delivers there. No app-side
// secret, no fixed port, no inbound firewall hole beyond the lifetime of the dance.
//
// Backend contract this expects (see the report's "backend gap"): for a desktop client the backend
// must (1) accept an http://127.0.0.1:<port>/cb loopback redirect (RFC-8252 §7.3 — today the
// redirect predicate only allows the nomnomzbot:// scheme), and (2) deliver the session to that
// redirect as query params `access_token` / `refresh_token` / `expires_in` — exactly the shape it
// ALREADY emits for the mobile nomnomzbot:// deep-link redirect.
private const val LOOPBACK_HOST: String = "127.0.0.1"
private const val CALLBACK_PATH: String = "/cb"
private const val AUTH_TIMEOUT_MS: Long = 5 * 60 * 1000L

actual class OAuthLauncher {

    actual suspend fun authorize(baseUrl: String, flow: OAuthFlow): ApiResult<SessionTokens> =
        withContext(Dispatchers.IO) {
            val server: HttpServer =
                try {
                    HttpServer.create(InetSocketAddress(LOOPBACK_HOST, 0), 0)
                } catch (cause: Throwable) {
                    return@withContext failure("LOOPBACK_BIND", cause.message ?: "Could not bind a loopback listener.")
                }

            val port: Int = server.address.port
            val redirect = "http://$LOOPBACK_HOST:$port$CALLBACK_PATH"
            val authorizeUrl: String = buildStartUrl(baseUrl, flow, redirect)

            try {
                val captured: ApiResult<SessionTokens>? =
                    withTimeoutOrNull(AUTH_TIMEOUT_MS) {
                        suspendCancellableCoroutine { continuation ->
                            server.createContext(CALLBACK_PATH) { exchange ->
                                val result: ApiResult<SessionTokens> = parseCallback(exchange)
                                respond(exchange, result)
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
                    ?: failure("OAUTH_TIMEOUT", "Timed out waiting for the Twitch sign-in to complete.")
            } finally {
                server.stop(0)
            }
        }

    private fun buildStartUrl(baseUrl: String, flow: OAuthFlow, redirect: String): String {
        val base: String = baseUrl.trimEnd('/')
        // Streamer login is the user flow; bot reuses the same launcher in the next slice.
        val path: String = if (flow == OAuthFlow.Bot) "/api/v1/auth/twitch/bot" else "/api/v1/auth/twitch"
        val encodedRedirect: String = URI(redirect).toString()
        return "$base$path?client=desktop&redirect_uri=${encode(encodedRedirect)}"
    }

    private fun parseCallback(exchange: HttpExchange): ApiResult<SessionTokens> {
        val query: String? = exchange.requestURI.rawQuery
        val params: Map<String, String> = parseQuery(query)

        params["error"]?.let { error ->
            val description: String = params["error_description"] ?: error
            return failure(error, description)
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

    private fun respond(exchange: HttpExchange, result: ApiResult<SessionTokens>) {
        val ok: Boolean = result is ApiResult.Ok
        val title: String = if (ok) "Signed in" else "Sign-in failed"
        val body: String =
            if (ok) {
                "You're signed in to NomNomzBot. You can close this tab and return to the app."
            } else {
                "Sign-in didn't complete. Close this tab and try again from the app."
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
