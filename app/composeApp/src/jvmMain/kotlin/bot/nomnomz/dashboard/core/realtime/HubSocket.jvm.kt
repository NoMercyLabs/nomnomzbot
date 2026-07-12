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

import io.ktor.client.HttpClient
import io.ktor.client.engine.cio.CIO
import io.ktor.client.plugins.websocket.DefaultClientWebSocketSession
import io.ktor.client.plugins.websocket.WebSockets
import io.ktor.client.plugins.websocket.webSocketSession
import io.ktor.websocket.Frame
import io.ktor.websocket.readText
import kotlinx.coroutines.cancel

// JVM/desktop hub transport — Ktor's CIO engine speaks WebSockets natively (unlike the wasmJs Fetch engine,
// see HubSocket.wasmJs.kt), so desktop live push works over Ktor. One client per socket, disposed on close,
// matching the per-attempt reconnect lifecycle DashboardHubClient drives.
actual class HubSocket {
    private val client: HttpClient = HttpClient(CIO) { install(WebSockets) }
    private var session: DefaultClientWebSocketSession? = null

    actual suspend fun open(url: String) {
        session = client.webSocketSession(url)
    }

    actual fun send(text: String) {
        session?.outgoing?.trySend(Frame.Text(text))
    }

    actual suspend fun receive(): String? {
        val active: DefaultClientWebSocketSession = session ?: return null
        // Iterate until a text frame arrives; the loop ends (returns null) when the channel closes.
        for (frame in active.incoming) {
            if (frame is Frame.Text) return frame.readText()
        }
        return null
    }

    actual fun close() {
        session?.cancel()
        session = null
        client.close()
    }
}
