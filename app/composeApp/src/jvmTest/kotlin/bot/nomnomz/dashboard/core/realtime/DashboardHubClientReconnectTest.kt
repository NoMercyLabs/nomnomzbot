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

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertTrue

// ASP.NET Core SignalR JSON Hub Protocol record separator — mirrors the private constant in
// DashboardHubClient.kt (0x1E), needed here only to strip it off frames the client sent us.
private const val RECORD_SEPARATOR: Char = ''

// S035b — the backend (861caccf) turned on WithStatefulReconnect() on every hub, but this hand-rolled
// JSON-hub-protocol client (Ktor speaks no SignalR; see HubSocket.kt) does not implement the SignalR
// stateful-reconnect resume handshake (the "useStatefulReconnect" handshake opt-in plus the Sequence/Ack
// message pair, protocol types 8/9). That resume protocol cannot be verified at the wire level against a
// live server from this sandbox, and shipping an unverified hand-rolled implementation of it would risk
// silently mis-claiming a resumed connection that never actually resumed — exactly what the project's
// "never show state not enforced" rule forbids. So today every drop — a clean SignalR TYPE_CLOSE frame or
// the socket simply dying — degrades to a full clean reconnect: no stuck "looks connected but isn't"
// state, and isConnected always tracks the REAL live handshake state. This test proves that fallback
// against a real accepted TCP connection with a genuine WS opening handshake, driving the production
// DashboardHubClient over the production jvm HubSocket (Ktor CIO).
class DashboardHubClientReconnectTest {

    @Test
    fun a_server_close_flips_isConnected_false_and_the_client_reconnects_cleanly() = runBlocking {
        MiniWsServer().use { server ->
            val client = DashboardHubClient()
            client.connect(
                baseUrl = "http://127.0.0.1:${server.port}",
                tokenProvider = { "test-token" },
                channelId = "channel-under-test",
            )

            // First attempt: complete the handshake, observe the JoinChannel for the primary channel, then the
            // server drops the connection with a clean SignalR close frame (type 7) — the "recoverable server
            // close" case WithStatefulReconnect produces.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { first ->
                    first.receiveHandshake()
                    assertEquals(
                        """{"type":1,"target":"JoinChannel","arguments":["channel-under-test"]}""",
                        first.receiveText()?.trimEnd(RECORD_SEPARATOR),
                    )
                    first.sendText("""{"type":7}""")
                    first.sendClose()
                }
            }

            // isConnected must flip false — never stay stuck reporting a live connection the server just tore
            // down.
            withContext(Dispatchers.Default) {
                withTimeout(5_000) {
                    while (client.isConnected) delay(20)
                }
            }
            assertFalse(client.isConnected, "must not report connected after the server closed the socket")

            // The reconnect loop must bring up a SECOND connection on its own — not stall — and it re-joins the
            // same channel, then isConnected flips true again once that fresh handshake completes: a clean
            // full reconnect, not a silent dead client.
            val second: MiniWsServer.Connection =
                withContext(Dispatchers.IO) { server.acceptConnection() }
            withContext(Dispatchers.IO) {
                second.receiveHandshake()
                assertEquals(
                    """{"type":1,"target":"JoinChannel","arguments":["channel-under-test"]}""",
                    second.receiveText()?.trimEnd(RECORD_SEPARATOR),
                )
            }

            withContext(Dispatchers.Default) {
                withTimeout(5_000) {
                    while (!client.isConnected) delay(20)
                }
            }
            assertTrue(client.isConnected, "must reconnect and report connected again after a fresh handshake")

            client.disconnect()
            second.close()
        }
    }

    // Reads the client's handshake request frame and answers with the JSON hub protocol success response `{}`.
    private fun MiniWsServer.Connection.receiveHandshake() {
        receiveText() // `{"protocol":"json","version":1}` + record separator
        sendText("{}")
    }
}
