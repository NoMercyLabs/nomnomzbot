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

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.UnconfinedTestDispatcher
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull

// Proves the real session state machine that drives the App gate (frontend.md §5/§6). The gate renders
// Connect while NotConnected and the Main shell while Connected, so these phase transitions ARE the routing
// decision — if they break, the gate routes wrong. A connect must: (1) flip the phase to Connected,
// (2) pin the active profile, (3) persist BOTH the tokens (vault) and the profile (so a relaunch knows the
// vault key + base URL), (4) expose the base URL + bearer the shared ApiClient reads. A disconnect undoes
// all of that. And the remembered session must read back exactly through [loadPersisted] / [arm] so the
// boot restore path can validate it before committing the gate.
@OptIn(ExperimentalCoroutinesApi::class)
class SessionStoreTest {

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
        val store: SessionStore = SessionStore(FakeTokenVault(), FakeProfileStore(), FakeChannelStore())
        assertEquals(SessionPhase.NotConnected, store.phase.value)
        assertNull(store.activeProfile.value)
        assertNull(store.baseUrl())
        assertNull(store.accessToken())
    }

    @Test
    fun connect_flips_to_connected_and_persists_profile_and_tokens() = runTest {
        val vault = FakeTokenVault()
        val profiles = FakeProfileStore()
        val store: SessionStore = SessionStore(vault, profiles, FakeChannelStore())

        store.connect(profile, tokens)

        assertEquals(SessionPhase.Connected, store.phase.value)
        assertEquals(profile, store.activeProfile.value)
        // The shared ApiClient reads exactly these to target + authorize requests.
        assertEquals("http://localhost:5080", store.baseUrl())
        assertEquals("jwt-access-token", store.accessToken())
        // Both halves of the remembered session are persisted for restore-on-relaunch:
        assertEquals(tokens, vault.stored["profile-1"]) // the secret tokens, keyed by profile id
        assertEquals(profile, profiles.stored) // the profile itself (the vault key + base URL)
    }

    @Test
    fun set_user_surfaces_the_signed_in_streamer_to_the_shell() = runTest {
        val store: SessionStore = SessionStore(FakeTokenVault(), FakeProfileStore(), FakeChannelStore())
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
    fun impersonation_swaps_the_active_token_then_restores_it_without_touching_custody() = runTest {
        val vault = FakeTokenVault()
        val store: SessionStore = SessionStore(vault, FakeProfileStore(), FakeChannelStore())
        store.connect(profile, tokens)

        // Enter act-as: the active bearer becomes the target's, the flag names them — and NOTHING persists
        // (the vault still holds the operator's original token, so a relaunch never restores an act-as session).
        store.beginImpersonation(targetAccessToken = "target-jwt", targetDisplayName = "Target User")

        assertEquals("target-jwt", store.accessToken())
        assertEquals("Target User", store.impersonating.value?.displayName)
        assertEquals(tokens, vault.stored["profile-1"]) // custody untouched — only the in-memory token swapped

        // Exit act-as: the operator's own token is restored and the flag clears.
        store.endImpersonation()

        assertEquals("jwt-access-token", store.accessToken())
        assertNull(store.impersonating.value)
    }

    @Test
    fun ending_impersonation_when_not_impersonating_is_a_no_op() = runTest {
        val store: SessionStore = SessionStore(FakeTokenVault(), FakeProfileStore(), FakeChannelStore())
        store.connect(profile, tokens)

        // A stray exit must never blank a real token (nothing was stashed to restore).
        store.endImpersonation()

        assertEquals("jwt-access-token", store.accessToken())
        assertNull(store.impersonating.value)
    }

    @Test
    fun disconnect_clears_user_profile_and_vault_then_returns_to_connect() = runTest {
        val vault = FakeTokenVault()
        val profiles = FakeProfileStore()
        val store: SessionStore = SessionStore(vault, profiles, FakeChannelStore())
        store.connect(profile, tokens)
        store.setUser(SessionUser("1", "u", "U", null))

        store.disconnect()

        assertEquals(SessionPhase.NotConnected, store.phase.value)
        assertNull(store.activeProfile.value)
        assertNull(store.user.value)
        assertNull(store.baseUrl())
        assertNull(store.accessToken())
        // Nothing remains for a relaunch to restore — both halves are gone.
        assertNull(vault.stored["profile-1"])
        assertNull(profiles.stored)
    }

    @Test
    fun arm_exposes_profile_and_bearer_without_committing_the_gate_or_persisting() = runTest {
        val vault = FakeTokenVault()
        val profiles = FakeProfileStore()
        val store: SessionStore = SessionStore(vault, profiles, FakeChannelStore())

        store.arm(profile, tokens)

        // The shared ApiClient can now reach + authorize (so restore can call /me), but the gate has NOT
        // advanced and nothing was persisted — a rejected /me must be able to roll back cleanly.
        assertEquals("http://localhost:5080", store.baseUrl())
        assertEquals("jwt-access-token", store.accessToken())
        assertEquals(SessionPhase.NotConnected, store.phase.value)
        assertNull(vault.stored["profile-1"])
        assertNull(profiles.stored)
    }

    @Test
    fun load_persisted_returns_the_remembered_profile_and_tokens_after_connect() = runTest {
        val vault = FakeTokenVault()
        val profiles = FakeProfileStore()
        SessionStore(vault, profiles, FakeChannelStore()).connect(profile, tokens)

        // A FRESH store over the same custody (i.e. a relaunch) reads back exactly what connect persisted.
        val remembered: RestorableSession? = SessionStore(vault, profiles, FakeChannelStore()).loadPersisted()

        assertEquals(profile, remembered?.profile)
        assertEquals(tokens, remembered?.tokens)
    }

    @Test
    fun load_persisted_is_null_when_nothing_was_remembered() = runTest {
        val store: SessionStore = SessionStore(FakeTokenVault(), FakeProfileStore(), FakeChannelStore())
        assertNull(store.loadPersisted())
    }

    @Test
    fun load_persisted_returns_the_profile_with_null_tokens_when_no_token_is_stored() = runTest {
        // The web build persists the profile (localStorage) but NO token — its refresh token is an HttpOnly
        // cookie. loadPersisted must still surface the profile (with null tokens) so restore can refresh
        // against that cookie, rather than reporting "nothing remembered".
        val profiles = FakeProfileStore()
        profiles.stored = profile

        val store: SessionStore = SessionStore(FakeTokenVault(), profiles, FakeChannelStore())
        val remembered: RestorableSession? = store.loadPersisted()

        assertEquals(profile, remembered?.profile)
        assertNull(remembered?.tokens)
    }

    @Test
    fun switching_channel_persists_the_choice_so_a_reload_restores_it() = runTest {
        val channels = FakeChannelStore()
        // An unconfined test dispatcher runs the fire-and-forget persistence write EAGERLY, so the assertion
        // observes it deterministically without advancing the scheduler by hand.
        val store: SessionStore =
            SessionStore(
                FakeTokenVault(),
                FakeProfileStore(),
                channels,
                CoroutineScope(UnconfinedTestDispatcher(testScheduler)),
            )

        store.setDefaultChannel("owned-channel") // the owned default seeded on first channel-list load
        store.switchChannel("moderated-channel") // the operator then switches to one they moderate

        // The EXPLICIT switch — not the owned default — is what's remembered (the default must never clobber it).
        assertEquals("moderated-channel", channels.stored)
        assertEquals("moderated-channel", store.activeChannelId.value)
        // A FRESH store over the same custody (i.e. a web reload) reads that channel back to restore it.
        assertEquals(
            "moderated-channel",
            SessionStore(FakeTokenVault(), FakeProfileStore(), channels).persistedActiveChannel(),
        )
    }

    @Test
    fun disconnect_forgets_the_remembered_channel() = runTest {
        val channels = FakeChannelStore()
        channels.stored = "some-channel"
        val store: SessionStore =
            SessionStore(FakeTokenVault(), FakeProfileStore(), channels, backgroundScope)
        store.connect(profile, tokens)

        store.disconnect()

        // A real logout wipes ALL custody — including the remembered channel — so a relaunch starts clean.
        assertNull(channels.stored)
        assertNull(store.persistedActiveChannel())
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

/** An in-memory [ActiveProfileStore] so the session tests run without touching the OS file/localStorage. */
private class FakeProfileStore : ActiveProfileStore {
    var stored: ConnectionProfile? = null

    override suspend fun read(): ConnectionProfile? = stored

    override suspend fun write(profile: ConnectionProfile) {
        stored = profile
    }

    override suspend fun clear() {
        stored = null
    }
}

/** An in-memory [ActiveChannelStore] so the session tests run without touching the OS file/localStorage. */
private class FakeChannelStore : ActiveChannelStore {
    var stored: String? = null

    override suspend fun read(): String? = stored

    override suspend fun write(channelId: String) {
        stored = channelId
    }

    override suspend fun clear() {
        stored = null
    }
}
