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
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
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

// S035b — the backend (861caccf) runs WithStatefulReconnect() on every hub. This hand-rolled JSON hub
// protocol client (Ktor speaks no SignalR; see HubSocket.kt) implements the resume handshake by hand per
// the official spec (github.com/dotnet/aspnetcore SignalR/docs/specs/HubProtocol.md): the handshake opts
// in with "useStatefulReconnect": true, a reconnect sends a type-9 Sequence message FIRST, unacked outbound
// invocations are buffered and resent (acked ones are dropped from the buffer), and inbound invocations are
// numbered so a message the server resends after a resume — it doesn't know precisely what got through — is
// recognised as a duplicate and dropped rather than redelivered. A resume that never proves itself alive
// falls back to a plain fresh connect on the next attempt. Every test here drives the PRODUCTION
// DashboardHubClient over the PRODUCTION jvm HubSocket (Ktor CIO) against a real accepted TCP connection
// with a genuine WS opening handshake (MiniWsServer), asserting on the actual wire frames — not on internals.
class DashboardHubClientReconnectTest {

    @Test
    fun a_reconnect_sends_sequence_first_and_resends_the_still_unacked_invocation() = runBlocking {
        MiniWsServer().use { server ->
            val client = DashboardHubClient()
            client.connect(
                baseUrl = "http://127.0.0.1:${server.port}",
                tokenProvider = { "test-token" },
                channelId = "channel-under-test",
            )

            // First connection: fresh connect, plain JoinChannel (sequence id 1), never acked. Server then
            // drops the connection without acking it.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { first ->
                    first.receiveHandshake()
                    assertEquals(
                        """{"type":1,"target":"JoinChannel","arguments":["channel-under-test"]}""",
                        first.receiveText()?.trimEnd(RECORD_SEPARATOR),
                    )
                }
            }
            awaitDisconnected(client)

            // Second connection: MUST be a resume — Sequence(1) first (nothing was ever acked, so the client
            // still starts numbering from 1), THEN the resent JoinChannel — not a fresh rejoin.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { second ->
                    second.receiveHandshake()
                    assertEquals(
                        """{"type":9,"sequenceId":2}""",
                        second.receiveText()?.trimEnd(RECORD_SEPARATOR),
                        "a reconnect after a prior successful connection must send Sequence first, " +
                            "declaring the NEXT id it will use (2 — id 1 was already used by the JoinChannel)",
                    )
                    assertEquals(
                        """{"type":1,"target":"JoinChannel","arguments":["channel-under-test"]}""",
                        second.receiveText()?.trimEnd(RECORD_SEPARATOR),
                        "the still-unacked JoinChannel must be resent on resume",
                    )
                    // Confirm the resume so the client settles into steady state — checked while this
                    // connection is STILL OPEN, so the check can't race the connection closing right after.
                    second.sendText("""{"type":6}""")
                    awaitConnected(client)
                }
            }

            client.disconnect()
        }
    }

    @Test
    fun an_acked_invocation_is_not_resent_but_a_later_unacked_one_is() = runBlocking {
        MiniWsServer().use { server ->
            val client = DashboardHubClient()
            client.connect(
                baseUrl = "http://127.0.0.1:${server.port}",
                tokenProvider = { "test-token" },
                channelId = "channel-under-test",
            )

            // First connection: JoinChannel (seq 1) is acked; a SECOND tracked invocation — [join] on another
            // channel, over this SAME live socket — goes out as seq 2 and is never acked before the drop.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { first ->
                    first.receiveHandshake()
                    assertEquals(
                        """{"type":1,"target":"JoinChannel","arguments":["channel-under-test"]}""",
                        first.receiveText()?.trimEnd(RECORD_SEPARATOR),
                    )
                    // Ack seq 1 — the client must drop it from its resend buffer.
                    first.sendText("""{"type":8,"sequenceId":1}""")

                    client.join("second-channel")
                    assertEquals(
                        """{"type":1,"target":"JoinChannel","arguments":["second-channel"]}""",
                        first.receiveText()?.trimEnd(RECORD_SEPARATOR),
                        "the extra join must arrive on the live socket",
                    )
                    // Connection dies here (server.use{} closes) WITHOUT acking seq 2.
                }
            }
            awaitDisconnected(client)

            // Resume: only the UNACKED seq 2 (second-channel) is resent — the acked seq 1 (primary channel)
            // must not reappear.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { second ->
                    second.receiveHandshake()
                    assertEquals(
                        """{"type":9,"sequenceId":3}""",
                        second.receiveText()?.trimEnd(RECORD_SEPARATOR),
                        "Sequence must declare the next id to be used (3), covering both prior sends",
                    )
                    assertEquals(
                        """{"type":1,"target":"JoinChannel","arguments":["second-channel"]}""",
                        second.receiveText()?.trimEnd(RECORD_SEPARATOR),
                        "only the still-unacked second-channel join may be resent, never the acked primary one",
                    )
                    // Checked while this connection is STILL OPEN — see the first test's comment.
                    second.sendText("""{"type":6}""")
                    awaitConnected(client)
                }
            }

            client.disconnect()
        }
    }

    @Test
    fun an_inbound_duplicate_after_resume_is_dropped_not_redelivered() = runBlocking {
        MiniWsServer().use { server ->
            val client = DashboardHubClient()
            val received = mutableListOf<HubEvent>()
            val collectScope = CoroutineScope(Dispatchers.Default)
            val collectJob = collectScope.launch { client.events.collect { received.add(it) } }
            client.connect(
                baseUrl = "http://127.0.0.1:${server.port}",
                tokenProvider = { "test-token" },
                channelId = "channel-under-test",
            )

            val pushFrame = """{"type":1,"target":"SomethingNotYetModelled","arguments":["payload"]}"""

            withContext(Dispatchers.IO) {
                server.acceptConnection().use { first ->
                    first.receiveHandshake()
                    first.receiveText() // JoinChannel
                    // Ack the outbound JoinChannel so the resend buffer is empty going into the resume below
                    // — this test is only about INBOUND dedup, not outbound resend (covered by the other tests).
                    first.sendText("""{"type":8,"sequenceId":1}""")
                    // Push ONE invocation — the server's first ever push, inbound sequence id 1.
                    first.sendText(pushFrame)
                    // Consume the client's Ack for it before dropping the connection.
                    assertEquals("""{"type":8,"sequenceId":1}""", first.receiveText()?.trimEnd(RECORD_SEPARATOR))
                }
            }
            awaitDisconnected(client)
            withTimeout(LIVENESS_TIMEOUT_MS) { while (received.size < 1) delay(20) }
            assertEquals(1, received.size, "the original push must be delivered exactly once")

            withContext(Dispatchers.IO) {
                server.acceptConnection().use { second ->
                    second.receiveHandshake()
                    second.receiveText() // client's Sequence(2) — nothing to resend, no buffer entries left
                    // Server declares it will resend starting from id 1 — i.e. it's replaying the SAME push
                    // the client already processed, because it doesn't know precisely what got through.
                    second.sendText("""{"type":9,"sequenceId":1}""")
                    second.sendText(pushFrame)
                    // The client must still Ack it (it did receive the frame) even though it drops it.
                    assertEquals("""{"type":8,"sequenceId":1}""", second.receiveText()?.trimEnd(RECORD_SEPARATOR))
                }
            }
            awaitConnected(client)

            // The duplicate must NOT have been redelivered to [events] — still exactly one.
            delay(200)
            assertEquals(1, received.size, "a resent duplicate below the processed sequence id must be dropped")

            client.disconnect()
            collectJob.cancel()
        }
    }

    @Test
    fun a_refused_resume_falls_back_to_a_fresh_connect_on_the_next_attempt() = runBlocking {
        MiniWsServer().use { server ->
            val client = DashboardHubClient()
            client.connect(
                baseUrl = "http://127.0.0.1:${server.port}",
                tokenProvider = { "test-token" },
                channelId = "channel-under-test",
            )

            // First connection establishes normally.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { first ->
                    first.receiveHandshake()
                    first.receiveText() // JoinChannel
                }
            }
            awaitDisconnected(client)

            // Second connection: the client attempts a resume (Sequence first) — the server refuses it by
            // closing immediately instead of confirming.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { second ->
                    second.receiveHandshake()
                    assertEquals(
                        """{"type":9,"sequenceId":2}""",
                        second.receiveText()?.trimEnd(RECORD_SEPARATOR),
                        "the client must still attempt resume first",
                    )
                    second.receiveText() // resent JoinChannel — server ignores it and closes
                }
            }
            awaitDisconnected(client)

            // Third connection: the failed resume must have reset resume bookkeeping — this MUST be a plain
            // fresh connect (a raw JoinChannel, no Sequence message), never another broken resume attempt.
            withContext(Dispatchers.IO) {
                server.acceptConnection().use { third ->
                    third.receiveHandshake()
                    assertEquals(
                        """{"type":1,"target":"JoinChannel","arguments":["channel-under-test"]}""",
                        third.receiveText()?.trimEnd(RECORD_SEPARATOR),
                        "a refused resume must fall back to a fresh JoinChannel connect, not retry resume",
                    )
                }
            }
            awaitConnected(client)

            client.disconnect()
        }
    }

    // Reads the client's handshake request frame and answers with the JSON hub protocol success response `{}`.
    private fun MiniWsServer.Connection.receiveHandshake() {
        receiveText() // `{"protocol":"json","version":1,"useStatefulReconnect":true}` + record separator
        sendText("{}")
    }

    // These are LIVENESS waits — how long to let a real socket handshake/reconnect happen — not the thing
    // under assertion. 5s was enough locally and expired on a loaded CI runner, turning a slow machine into
    // a red build. Raising the ceiling changes no assertion; a genuine hang still fails, just later.
    private companion object {
        const val LIVENESS_TIMEOUT_MS = 30_000L
    }

    private suspend fun awaitDisconnected(client: DashboardHubClient) {
        withContext(Dispatchers.Default) {
            withTimeout(LIVENESS_TIMEOUT_MS) { while (client.isConnected) delay(20) }
        }
        assertFalse(client.isConnected)
    }

    private suspend fun awaitConnected(client: DashboardHubClient) {
        withContext(Dispatchers.Default) {
            withTimeout(LIVENESS_TIMEOUT_MS) { while (!client.isConnected) delay(20) }
        }
        assertTrue(client.isConnected)
    }
}
