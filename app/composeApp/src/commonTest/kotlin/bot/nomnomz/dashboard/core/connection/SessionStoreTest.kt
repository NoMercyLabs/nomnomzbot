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

import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// Proves the real session state machine that drives the App gate (frontend.md §5/§6). The gate
// renders Connect while NotConnected and the Main shell while Connected, so these phase transitions
// ARE the routing decision — if they break, the gate routes wrong. A connect must:
//   (1) flip the phase to Connected, (2) pin the active profile, (3) persist tokens to the vault,
//   (4) expose the base URL + bearer the shared ApiClient reads.
// A disconnect must undo all of that and clear the vault entry.
class SessionStoreTest {

    private fun store(vault: TokenVault = TokenVault()): SessionStore = SessionStore(vault)

    private val profile =
        ConnectionProfile(
            id = "profile-1",
            displayName = "My self-host",
            baseUrl = "http://localhost:5080",
            source = ProfileSource.Manual,
        )

    private val tokens =
        SessionTokens(
            accessToken = "jwt-access-token",
            refreshToken = "refresh-token",
            expiresAt = 1_900_000_000_000L,
        )

    @Test
    fun starts_not_connected_with_no_profile_or_token() {
        val store: SessionStore = store()
        assertEquals(SessionPhase.NotConnected, store.phase.value)
        assertNull(store.activeProfile.value)
        assertNull(store.baseUrl())
        assertNull(store.accessToken())
    }

    @Test
    fun connect_flips_to_connected_and_exposes_profile_url_and_bearer() = runTest {
        val vault = FakeTokenVault()
        val store: SessionStore = SessionStore(vault)

        store.connect(profile, tokens)

        assertEquals(SessionPhase.Connected, store.phase.value)
        assertEquals(profile, store.activeProfile.value)
        // The shared ApiClient reads exactly these to target + authorize requests.
        assertEquals("http://localhost:5080", store.baseUrl())
        assertEquals("jwt-access-token", store.accessToken())
        // Tokens were persisted for restore-on-relaunch.
        assertEquals(tokens, vault.stored["profile-1"])
    }

    @Test
    fun set_user_surfaces_the_signed_in_streamer_to_the_shell() = runTest {
        val store: SessionStore = SessionStore(FakeTokenVault())
        store.connect(profile, tokens)

        store.setUser(
            SessionUser(
                id = "12345",
                username = "stoney_eagle",
                displayName = "Stoney_Eagle",
                profileImageUrl = "https://img/avatar.png",
            )
        )

        val user: SessionUser? = store.user.value
        assertEquals("12345", user?.id)
        assertEquals("Stoney_Eagle", user?.displayName)
    }

    @Test
    fun disconnect_clears_session_user_and_vault_then_returns_to_connect() = runTest {
        val vault = FakeTokenVault()
        val store: SessionStore = SessionStore(vault)
        store.connect(profile, tokens)
        store.setUser(SessionUser("1", "u", "U", null))

        store.disconnect()

        assertEquals(SessionPhase.NotConnected, store.phase.value)
        assertNull(store.activeProfile.value)
        assertNull(store.user.value)
        assertNull(store.baseUrl())
        assertNull(store.accessToken())
        // The vault entry is gone — a relaunch finds no session to restore.
        assertNull(vault.stored["profile-1"])
    }
}

/** An in-memory [SessionTokenStore] so the session tests run without touching the OS vault. */
private class FakeTokenVault : SessionTokenStore {
    val stored: MutableMap<String, SessionTokens> = mutableMapOf()

    override suspend fun read(profileId: String): SessionTokens? = stored[profileId]

    override suspend fun write(profileId: String, tokens: SessionTokens) {
        stored[profileId] = tokens
    }

    override suspend fun clear(profileId: String) {
        stored.remove(profileId)
    }
}
