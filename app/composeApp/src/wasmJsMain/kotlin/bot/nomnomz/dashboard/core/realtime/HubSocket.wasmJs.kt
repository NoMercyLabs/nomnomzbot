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

package bot.nomnomz.dashboard.core.realtime

import kotlin.js.ExperimentalWasmJsInterop
import kotlin.js.JsAny
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.channels.Channel
import org.w3c.dom.MessageEvent
import org.w3c.dom.WebSocket
import org.w3c.dom.events.Event

// wasmJs hub transport — the browser's native WebSocket (kotlinx.browser). Ktor's WebSockets plugin does
// NOT establish a socket on the wasmJs Fetch engine, so `HttpClient.webSocket` silently never connected and
// the web dashboard's live chat push never opened (only a page reload's REST fetch showed new messages). A
// browser WebSocket connects, joins, and receives exactly like every other SignalR client — verified: the
// same origin/token opens the socket and receives the handshake + JoinChannel completion frames.
actual class HubSocket {
    private var ws: WebSocket? = null

    // Inbound text frames, buffered so an onmessage callback never blocks and no frame is lost between
    // receive() calls. Closed when the socket closes/errors, which turns receive() into a null (EOF).
    private val inbox: Channel<String> = Channel(Channel.UNLIMITED)

    actual suspend fun open(url: String) {
        val socket = WebSocket(url)
        ws = socket
        val ready: CompletableDeferred<Unit> = CompletableDeferred()
        socket.onopen = { _: Event ->
            if (!ready.isCompleted) ready.complete(Unit)
        }
        socket.onmessage = { event: MessageEvent ->
            val text: String? = messageText(event.data)
            if (text != null) inbox.trySend(text)
        }
        socket.onerror = { _: Event ->
            if (!ready.isCompleted) ready.completeExceptionally(HubSocketException("websocket error"))
            inbox.close()
        }
        socket.onclose = { _: Event ->
            if (!ready.isCompleted) ready.completeExceptionally(HubSocketException("closed before open"))
            inbox.close()
        }
        // Suspend until the socket is OPEN (or fails to open) — so the caller only sends after connect.
        ready.await()
    }

    actual fun send(text: String) {
        runCatching { ws?.send(text) }
    }

    actual suspend fun receive(): String? = inbox.receiveCatching().getOrNull()

    actual fun close() {
        runCatching { ws?.close() }
        inbox.close()
        ws = null
    }
}

private class HubSocketException(message: String) : Exception(message)

// The browser hands a text WebSocket frame's payload back as a JS string; surface it as a Kotlin String
// (null for a binary/non-string frame, which the SignalR JSON hub protocol never sends).
private fun messageText(data: JsAny?): String? = js("(typeof data === 'string') ? data : null")
