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

import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeout
import kotlin.test.Test
import kotlin.test.assertTrue

class DashboardHubClientTest {

    // Regression: an idle chat feed used to 401-storm the SignalR handshake forever once its JWT expired —
    // the raw WebSocket has no HTTP 401→refresh interceptor, and on an idle page no REST call fires to rotate
    // the token. connect() must now refresh the JWT itself whenever a handshake attempt fails to establish, so
    // the next reconnect carries a fresh token instead of replaying the expired one.
    @Test
    fun refreshes_the_jwt_when_a_handshake_attempt_never_establishes() = runTest {
        val refreshed: CompletableDeferred<Unit> = CompletableDeferred()
        val client = DashboardHubClient()

        // A null token makes openSession bail before any network I/O — the same "never handshook" outcome an
        // expired-token 401 produces — so the client must invoke the refresher before backing off to retry.
        client.connect(
            baseUrl = "http://127.0.0.1:1",
            tokenProvider = { null },
            channelId = "channel-under-test",
            refreshToken = {
                refreshed.complete(Unit)
                false
            },
        )

        // The client drives its reconnect loop on its own Dispatchers.Default scope, so wait in REAL time (not
        // the test scheduler's virtual clock) for the first refresh; withTimeout fails the test if it never comes.
        withContext(Dispatchers.Default) { withTimeout(5_000) { refreshed.await() } }
        client.disconnect()

        assertTrue(refreshed.isCompleted, "hub client should refresh the JWT after a failed handshake")
    }
}
