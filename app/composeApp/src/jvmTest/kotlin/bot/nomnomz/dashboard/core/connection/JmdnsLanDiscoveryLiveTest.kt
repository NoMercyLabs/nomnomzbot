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

import kotlin.test.Test
import kotlin.test.assertTrue
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.runBlocking
import kotlinx.coroutines.withTimeoutOrNull

// An OPPORTUNISTIC end-to-end browse: when a real bot is advertising `_nomnomz._tcp` on the LAN, this
// proves the jmDNS engine actually resolves it into a usable Discovered profile (the deterministic
// coverage lives in LanDiscoveryMappingTest / ConnectControllerSetupRoutingTest — this is the live
// confidence check). It is SAFE in CI: a short bounded poll, and a skip (no failure) when no bot is found,
// so it can never hang or go red on a machine with nothing on the wire.
class JmdnsLanDiscoveryLiveTest {

    // Real wall-clock waiting (Dispatchers.Default) — NOT runTest, whose virtual clock would fast-forward
    // the poll past the real network round-trip and never see the live announcement.
    @Test
    fun resolves_a_real_bot_on_the_lan_when_one_is_advertising() = runBlocking(Dispatchers.Default) {
        val discovery: JmdnsLanDiscovery = JmdnsLanDiscovery()
        discovery.start()
        try {
            val found: List<ConnectionProfile>? =
                withTimeoutOrNull(BROWSE_BUDGET_MS) {
                    var current: List<ConnectionProfile> = emptyList()
                    while (current.isEmpty()) {
                        current = discovery.discovered.value
                        if (current.isNotEmpty()) break
                        delay(POLL_INTERVAL_MS)
                    }
                    current
                }

            if (found.isNullOrEmpty()) {
                // Nothing advertising on this network — skip without failing.
                println("[JmdnsLanDiscoveryLiveTest] no `_nomnomz._tcp` bot found on the LAN; skipping.")
                return@runBlocking
            }

            // A real bot resolved — every discovered entry must be a usable, well-formed Discovered profile.
            found.forEach { profile ->
                assertTrue(profile.source == ProfileSource.Discovered, "expected a Discovered profile")
                assertTrue(profile.id.isNotBlank(), "discovered profile must carry a stable id")
                assertTrue(profile.displayName.isNotBlank(), "discovered profile must carry a display name")
                assertTrue(
                    profile.baseUrl.startsWith("http://") || profile.baseUrl.startsWith("https://"),
                    "discovered baseUrl must be a real URL, was '${profile.baseUrl}'",
                )
            }
            println("[JmdnsLanDiscoveryLiveTest] resolved ${found.size} bot(s): ${found.map { it.displayName }}")
        } finally {
            discovery.stop()
        }
    }

    private companion object {
        const val BROWSE_BUDGET_MS: Long = 4_000L
        const val POLL_INTERVAL_MS: Long = 150L
    }
}
