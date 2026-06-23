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

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The active-connection + session Store (frontend.md §4/§6). Global, long-lived, injected; exposes
// StateFlow the App gate observes. This is the real direct-connect store — it holds the active
// ConnectionProfile + its SessionTokens, derives the SessionPhase the gate routes on, and feeds the
// shared ApiClient its base URL + bearer token via [baseUrl]/[accessToken].
//
// This slice is single-profile (the Connect screen sets one active profile and signs in against it);
// the multi-origin saved-list switcher (native) layers on this same store in the connection slice.
class SessionStore(private val tokenVault: SessionTokenStore = TokenVault()) {

    private val _phase: MutableStateFlow<SessionPhase> = MutableStateFlow(SessionPhase.NotConnected)
    private val _activeProfile: MutableStateFlow<ConnectionProfile?> = MutableStateFlow(null)
    private val _user: MutableStateFlow<SessionUser?> = MutableStateFlow(null)

    private var tokens: SessionTokens? = null

    /** The current session phase the gate observes. */
    val phase: StateFlow<SessionPhase> = _phase.asStateFlow()

    /** The active backend connection, or null when none is selected. */
    val activeProfile: StateFlow<ConnectionProfile?> = _activeProfile.asStateFlow()

    /** The signed-in streamer, surfaced to the shell once /me resolves. */
    val user: StateFlow<SessionUser?> = _user.asStateFlow()

    /** The active backend base URL the [ApiClient] targets; null when not connected. */
    fun baseUrl(): String? = _activeProfile.value?.baseUrl

    /** The bearer token the [ApiClient] attaches; null when signed out. */
    fun accessToken(): String? = tokens?.accessToken

    /**
     * Enter first-run setup against [profile]: pin the active profile (so the shared [ApiClient] can
     * reach the chosen backend for the anonymous setup calls) but hold NO tokens, and flip the gate to
     * [SessionPhase.NeedsSetup] so it shows the wizard. The wizard runs the anonymous credential saves,
     * then drives the streamer OAuth — which calls [connect] and advances to the shell.
     */
    fun enterSetup(profile: ConnectionProfile) {
        _activeProfile.value = profile
        tokens = null
        _phase.value = SessionPhase.NeedsSetup
    }

    /**
     * Establish the real session: pin the active profile, hold its tokens, persist them to the
     * vault, and flip the gate to Connected. The signed-in [user] is attached separately once the
     * `/me` probe resolves (so the gate can advance immediately and the shell fills in the identity).
     */
    suspend fun connect(profile: ConnectionProfile, sessionTokens: SessionTokens) {
        _activeProfile.value = profile
        tokens = sessionTokens
        tokenVault.write(profile.id, sessionTokens)
        _phase.value = SessionPhase.Connected
    }

    /** Attach the signed-in identity resolved from `/me` after [connect]. */
    fun setUser(sessionUser: SessionUser) {
        _user.value = sessionUser
    }

    /** Drop the session — clear the vault entry, forget the profile, return the gate to Connect. */
    suspend fun disconnect() {
        _activeProfile.value?.let { tokenVault.clear(it.id) }
        tokens = null
        _user.value = null
        _activeProfile.value = null
        _phase.value = SessionPhase.NotConnected
    }
}

/** The signed-in streamer identity surfaced to the shell (frontend.md §6). */
data class SessionUser(
    val id: String,
    val username: String,
    val displayName: String,
    val profileImageUrl: String?,
)
