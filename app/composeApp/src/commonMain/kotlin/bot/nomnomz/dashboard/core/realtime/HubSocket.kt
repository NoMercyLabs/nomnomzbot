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

// The raw text WebSocket transport under [DashboardHubClient] — the ONE per-target piece of the SignalR
// hub client, mirroring how the REST client abstracts only its engine (HttpClientFactory). It exists
// because the transport is the only thing that differs by platform: on jvm/desktop Ktor's CIO engine
// speaks WebSockets natively, but on wasmJs the Fetch engine does NOT — `HttpClient.webSocket` silently
// never opens a socket there, so the web dashboard's live push never connected. The wasmJs actual uses the
// browser's native WebSocket instead; the jvm actual keeps Ktor. Everything above this (handshake, join,
// keep-alive ping, framing, reconnect) is shared SignalR logic in [DashboardHubClient].
expect class HubSocket() {
    /** Opens the socket to [url] and suspends until it is OPEN; throws if it closes/errors before opening. */
    suspend fun open(url: String)

    /** Sends one text frame. Non-suspending and failure-swallowing — a send on a dead socket is a no-op. */
    fun send(text: String)

    /** Suspends for the next inbound text frame; returns null once the socket has closed. */
    suspend fun receive(): String?

    /** Closes the socket and releases its resources. Idempotent. */
    fun close()
}
