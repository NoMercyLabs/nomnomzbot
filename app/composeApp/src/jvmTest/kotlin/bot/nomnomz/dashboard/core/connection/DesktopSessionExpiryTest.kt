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
import bot.nomnomz.dashboard.core.network.AuthPayload
import java.io.File
import java.nio.file.Files
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves S111c-expiry ("desktop session expiry is unhandled"): [SessionStore.refreshOrExpire] —
// the method the shared 401→refresh→retry interceptor and the SignalR refresher both call
// (AppGraph.tokenRefresher) — refreshes silently when the refresh token is still valid, and on a
// dead refresh token drops ONLY the active connection's in-memory session, leaving the REAL
// desktop custody untouched: the encrypted [TokenVault] entries for every OTHER saved connection,
// and the [FileSavedConnectionsStore] list itself, survive on disk exactly as they were. Uses the
// real jvm file-backed stores (not in-memory fakes) because the claim under test IS "the files on
// disk survive", not just "the in-memory flags look right".
class DesktopSessionExpiryTest {

    private fun tempDir(): File = Files.createTempDirectory("session-expiry-test").toFile()

    private val connectionA =
        ConnectionProfile(id = "conn-a", displayName = "Bot A", baseUrl = "https://bot-a.example", source = ProfileSource.Manual)
    private val connectionB =
        ConnectionProfile(id = "conn-b", displayName = "Bot B", baseUrl = "https://bot-b.example", source = ProfileSource.Manual)

    private val tokensA = SessionTokens(accessToken = "stale-access-a", refreshToken = "refresh-a")
    private val tokensB = SessionTokens(accessToken = "access-b", refreshToken = "refresh-b")

    @Test
    fun `an expired access token with a still-valid refresh token refreshes silently and the session continues`() = runTest {
        val vault = TokenVault(tempDir())
        val store = SessionStore(vault, InMemoryProfileStore(), InMemoryChannelStore())
        store.connect(connectionA, tokensA)

        val refreshed: Boolean =
            store.refreshOrExpire { ApiResult.Ok(AuthPayload(accessToken = "fresh-access-a", refreshToken = "refresh-a-rotated")) }

        assertTrue(refreshed, "a valid refresh must report success")
        assertEquals(SessionPhase.Connected, store.phase.value, "the session must stay connected across a silent refresh")
        assertEquals(connectionA, store.activeProfile.value, "the active connection must be unchanged")
        assertEquals("fresh-access-a", store.accessToken(), "the new access token must be live immediately")
    }

    @Test
    fun `a dead refresh token routes to login without wiping saved connections or other connections' tokens`() = runTest {
        val dir: File = tempDir()
        val vault = TokenVault(dir)
        val savedConnectionsFile = File(dir.parentFile, "${dir.name}-saved-connections.json")
        val savedConnections = FileSavedConnectionsStore(savedConnectionsFile)

        // Two saved connections, both with real vaulted tokens — the exact shape the desktop
        // multi-origin switcher persists (SavedConnection.id IS the TokenVault key).
        savedConnections.add(SavedConnection(id = connectionA.id, label = "Bot A", baseUrl = connectionA.baseUrl, lastUsedAt = 1L))
        savedConnections.add(SavedConnection(id = connectionB.id, label = "Bot B", baseUrl = connectionB.baseUrl, lastUsedAt = 2L))
        vault.write(connectionA.id, tokensA)
        vault.write(connectionB.id, tokensB)

        val store = SessionStore(vault, InMemoryProfileStore(), InMemoryChannelStore())
        store.connect(connectionA, tokensA)

        val refreshed: Boolean =
            store.refreshOrExpire { ApiResult.Failure(ApiError(401, "REFRESH_INVALID", "refresh token expired")) }

        assertEquals(false, refreshed, "a dead refresh token must report failure")
        assertEquals(SessionPhase.NotConnected, store.phase.value, "the gate must fall through to Connect (login)")
        assertNull(store.activeProfile.value, "the dead connection's in-memory profile must be cleared")
        assertNull(store.accessToken(), "no stale access token must remain live")

        // The saved-connections LIST survives untouched — both entries, in place.
        val remainingConnections: List<SavedConnection> = savedConnections.list()
        assertEquals(2, remainingConnections.size, "expiring one connection must not remove any saved connection")
        assertTrue(remainingConnections.any { it.id == connectionA.id })
        assertTrue(remainingConnections.any { it.id == connectionB.id })

        // The OTHER connection's vaulted token survives byte-for-byte — no cross-connection wipe.
        val stillThereB: SessionTokens? = vault.read(connectionB.id)
        assertNotNull(stillThereB, "connection B's vault entry must survive connection A's expiry")
        assertEquals(tokensB.accessToken, stillThereB.accessToken)
        assertEquals(tokensB.refreshToken, stillThereB.refreshToken)
    }
}

/** In-memory [ActiveProfileStore] — the profile/channel custody isn't what this test is proving; the real
 * jvm [TokenVault] and [FileSavedConnectionsStore] carry the actual file-survival assertions. */
private class InMemoryProfileStore : ActiveProfileStore {
    private var stored: ConnectionProfile? = null
    override suspend fun read(): ConnectionProfile? = stored
    override suspend fun write(profile: ConnectionProfile) { stored = profile }
    override suspend fun clear() { stored = null }
}

private class InMemoryChannelStore : ActiveChannelStore {
    private var stored: String? = null
    override suspend fun read(): String? = stored
    override suspend fun write(channelId: String) { stored = channelId }
    override suspend fun clear() { stored = null }
}
