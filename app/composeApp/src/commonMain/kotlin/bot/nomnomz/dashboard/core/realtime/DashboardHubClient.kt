// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.realtime

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.cancel
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow
import kotlinx.coroutines.launch
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.int
import kotlinx.serialization.json.jsonArray
import kotlinx.serialization.json.jsonObject
import kotlinx.serialization.json.jsonPrimitive

// ASP.NET Core SignalR JSON Hub Protocol — record separator byte between frames.
private const val RECORD_SEPARATOR: Char = ''

// SignalR message types (hub protocol spec).
private const val TYPE_INVOCATION: Int = 1
private const val TYPE_PING: Int = 6
private const val TYPE_CLOSE: Int = 7

// Client keep-alive cadence. The SignalR server evicts a connection it hasn't heard from within its
// ClientTimeoutInterval (default 30 s) — and server→client frames do NOT reset that timer, only client→server
// traffic does — so the client must send its own protocol ping well under the timeout to hold the socket open.
private const val PingIntervalMillis: Long = 15_000

/**
 * Thin SignalR hub client targeting the backend `DashboardHub` at `/hubs/dashboard`.
 *
 * The raw text transport is a [HubSocket] (expect/actual): the browser's native WebSocket on wasmJs — where
 * Ktor's WebSockets plugin never opens a socket on the Fetch engine, which is why the web dashboard's live
 * push previously never connected — and Ktor's CIO WebSocket on jvm/desktop. Everything here (handshake,
 * JoinChannel, keep-alive ping, `\x1e`-framing, dispatch, reconnect) is shared over that transport.
 *
 * Lifecycle:
 * - Call [connect] to open the WebSocket, complete the handshake, and join a channel group.
 * - Collect [events] to receive hub invocations dispatched by the server.
 * - Call [disconnect] to close gracefully (or let the scope cancel).
 *
 * Reconnection: the client reconnects with exponential back-off (cap 30 s) whenever the socket
 * closes unexpectedly. Only a deliberate [disconnect] stops the loop.
 *
 * Thread safety: all mutations are confined to the internal [scope] launched on [Dispatchers.Default].
 */
class DashboardHubClient {

    private val scope: CoroutineScope = CoroutineScope(SupervisorJob() + Dispatchers.Default)
    private var connectJob: Job? = null
    private var currentChannelId: String? = null
    private var socket: HubSocket? = null

    private val _events: MutableSharedFlow<HubEvent> = MutableSharedFlow(extraBufferCapacity = 64)

    /** Hub invocations received from the server after a successful [connect] + `JoinChannel`. */
    val events: SharedFlow<HubEvent> = _events.asSharedFlow()

    /** True while the WebSocket is connected and the handshake is complete. */
    var isConnected: Boolean = false
        private set

    /**
     * Open the WebSocket to `{baseUrl}/hubs/dashboard`, complete the SignalR handshake, invoke
     * `JoinChannel({channelId})`, then stream incoming hub invocations into [events].
     *
     * Re-entrant per channel: a repeat call for the SAME channel while connected is a no-op; a call for a
     * DIFFERENT channel tears down the current connection and rejoins the new channel's group (so the feed
     * follows the operator's active channel instead of staying stuck on the first one it ever joined).
     *
     * [tokenProvider] is read on EVERY (re)connect, never captured once: the REST layer rotates the JWT on a
     * 401, so a reconnect must send the CURRENT token or the socket strands on a stale one and every retry 401s.
     *
     * [refreshToken] refreshes the JWT (POST /auth/refresh) and returns true when a fresh one was stored. A raw
     * WebSocket has no HTTP interceptor, so when the handshake fails on an expired token — the common case for
     * an idle chat page where no REST call has fired to refresh — the client would otherwise retry with the same
     * expired token forever. We call it before each retry that failed to establish, so an expired token self-heals.
     */
    fun connect(
        baseUrl: String,
        tokenProvider: () -> String?,
        channelId: String,
        refreshToken: (suspend () -> Boolean)? = null,
    ) {
        if (connectJob?.isActive == true && currentChannelId == channelId) return
        connectJob?.cancel()
        currentChannelId = channelId
        connectJob =
            scope.launch {
                var backoffMs: Long = 1_000
                while (true) {
                    var established = false
                    // Reset the back-off once a session actually establishes, so a long-lived socket that later
                    // drops reconnects promptly instead of inheriting a grown delay from an earlier failure run.
                    runCatching {
                            openSession(baseUrl, tokenProvider, channelId) {
                                established = true
                                backoffMs = 1_000
                            }
                        }
                        .onFailure { /* swallowed — the reconnect loop below handles it */ }
                    isConnected = false
                    // The session never established this attempt — overwhelmingly an expired/absent JWT (the
                    // handshake upgrade 401s). Refresh the token before the next attempt so an idle session
                    // recovers on the next reconnect. A network failure also trips this; the refresh is a cheap
                    // idempotent no-op there (it just fails and we retry on back-off anyway).
                    if (!established) refreshToken?.invoke()
                    // Reconnect loop — honour back-off so we don't spam the server on flaky networks.
                    delay(backoffMs)
                    backoffMs = (backoffMs * 2).coerceAtMost(30_000)
                }
            }
    }

    /** Close the WebSocket and stop the reconnect loop. */
    fun disconnect() {
        connectJob?.cancel()
        connectJob = null
        currentChannelId = null
        socket?.close()
        socket = null
        isConnected = false
    }

    /** Release all resources. After this the client cannot be reused. */
    fun dispose() {
        scope.cancel()
    }

    // ─── Internals ───────────────────────────────────────────────────────────

    private suspend fun openSession(
        baseUrl: String,
        tokenProvider: () -> String?,
        channelId: String,
        onConnected: () -> Unit,
    ) {
        // Read the CURRENT token for this attempt (see [connect]); bail and let the caller's back-off retry
        // when none is available yet, instead of opening the socket with an empty token (a guaranteed 401).
        val accessToken: String = tokenProvider() ?: return

        // Strip trailing slash and derive ws:// from http:// (or wss:// from https://).
        val base: String = baseUrl.trimEnd('/')
        val wsBase: String =
            when {
                base.startsWith("https://") -> "wss://" + base.removePrefix("https://")
                base.startsWith("http://") -> "ws://" + base.removePrefix("http://")
                else -> base
            }

        val hubSocket: HubSocket = HubSocket()
        socket = hubSocket
        try {
            // Opens and suspends until the socket is OPEN; throws (caught by the reconnect loop) if it fails.
            hubSocket.open("$wsBase/hubs/dashboard?access_token=$accessToken")

            // ── Handshake ──────────────────────────────────────────────
            // Send the JSON hub protocol handshake request, terminated with the record separator.
            hubSocket.send("""{"protocol":"json","version":1}$RECORD_SEPARATOR""")

            // The first frame back is the handshake response. In the SignalR JSON hub protocol a SUCCESS
            // response is the EMPTY OBJECT `{}`; a rejection carries `{"error":"…"}`. Bail ONLY when an "error"
            // field is actually present — treating the empty `{}` success as a rejection would close the socket
            // the instant every handshake succeeded.
            val handshakeFrame: String = hubSocket.receive() ?: return
            val handshakeMsg: String = handshakeFrame.trimEnd(RECORD_SEPARATOR)
            val handshake: JsonObject? =
                runCatching { Json.parseToJsonElement(handshakeMsg).jsonObject }.getOrNull()
            if (handshake?.containsKey("error") == true) return

            // ── JoinChannel invocation ─────────────────────────────────
            // Tell the hub which channel group we want to subscribe to.
            hubSocket.send(
                """{"type":1,"invocationId":"join","target":"JoinChannel","arguments":["$channelId"]}$RECORD_SEPARATOR"""
            )

            isConnected = true
            onConnected()

            coroutineScope {
                // ── Keep-alive ping ────────────────────────────────────────
                // Send our own SignalR ping under the server's ClientTimeoutInterval; without it the hub evicts
                // us every ~30 s (server→client chat frames don't reset that timer) and the feed goes silent
                // until the next reconnect. Cancelled when the receive loop ends (the finally below).
                val pingJob: Job =
                    launch {
                        while (true) {
                            delay(PingIntervalMillis)
                            hubSocket.send("""{"type":$TYPE_PING}$RECORD_SEPARATOR""")
                        }
                    }

                // ── Event loop ────────────────────────────────────────────
                try {
                    while (true) {
                        // A single frame may carry multiple SignalR messages, each separated by \x1e.
                        val raw: String = hubSocket.receive() ?: break
                        for (segment: String in raw.split(RECORD_SEPARATOR)) {
                            if (segment.isBlank()) continue
                            dispatchSegment(segment)
                        }
                    }
                } finally {
                    pingJob.cancel()
                }
            }
        } finally {
            isConnected = false
            hubSocket.close()
            if (socket === hubSocket) socket = null
        }
    }

    private fun dispatchSegment(segment: String) {
        val json: JsonElement =
            runCatching { Json.parseToJsonElement(segment) }.getOrNull() ?: return
        val obj: JsonObject = json.jsonObject
        val type: Int = obj["type"]?.jsonPrimitive?.int ?: return

        when (type) {
            TYPE_INVOCATION -> {
                val target: String = obj["target"]?.jsonPrimitive?.content ?: return
                val args: JsonArray = obj["arguments"]?.jsonArray ?: return
                val event: HubEvent? = HubEvent.from(target, args)
                if (event != null) {
                    _events.tryEmit(event)
                }
            }
            TYPE_PING -> Unit // pong is automatic — nothing to do on an inbound ping
            TYPE_CLOSE -> {
                isConnected = false
            }
        }
    }
}
