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
class SessionStore(
    private val tokenVault: SessionTokenStore = TokenVault(),
    private val profileStore: ActiveProfileStore = ActiveProfileVault(),
) {

    private val _phase: MutableStateFlow<SessionPhase> = MutableStateFlow(SessionPhase.NotConnected)
    private val _activeProfile: MutableStateFlow<ConnectionProfile?> = MutableStateFlow(null)
    private val _user: MutableStateFlow<SessionUser?> = MutableStateFlow(null)

    // The operator-selected active channel. Null until the first channel list resolves on login.
    // Controllers read this instead of resolving the "first" channel every load, enabling the multi-
    // channel switcher to propagate across all pages without per-controller changes.
    private val _activeChannelId: MutableStateFlow<String?> = MutableStateFlow(null)

    private var tokens: SessionTokens? = null

    /** The current session phase the gate observes. */
    val phase: StateFlow<SessionPhase> = _phase.asStateFlow()

    /** The active backend connection, or null when none is selected. */
    val activeProfile: StateFlow<ConnectionProfile?> = _activeProfile.asStateFlow()

    /** The signed-in streamer, surfaced to the shell once /me resolves. */
    val user: StateFlow<SessionUser?> = _user.asStateFlow()

    /** The currently-selected managed channel, or null while loading. */
    val activeChannelId: StateFlow<String?> = _activeChannelId.asStateFlow()

    /** Switch the active managed channel. Each page controller picks this up on its next load. */
    fun switchChannel(channelId: String) {
        _activeChannelId.value = channelId
    }

    /** Set the active channel on first login (the user's own channel). */
    fun setDefaultChannel(channelId: String) {
        if (_activeChannelId.value == null) _activeChannelId.value = channelId
    }

    /** The active backend base URL the [ApiClient] targets; null when not connected. */
    fun baseUrl(): String? = _activeProfile.value?.baseUrl

    /** The bearer token the [ApiClient] attaches; null when signed out. */
    fun accessToken(): String? = tokens?.accessToken

    /**
     * Silently replace the stored access token after a background /auth/refresh — leaves the profile
     * and refresh token intact. Called by [ApiClient.tokenRefresher] on 401→refresh→retry.
     */
    fun updateAccessToken(newToken: String) {
        tokens = tokens?.copy(accessToken = newToken) ?: SessionTokens(accessToken = newToken)
    }

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
     * Pin the active profile WITHOUT changing the gate phase or holding tokens — so the shared
     * [ApiClient] can reach the chosen backend for the anonymous device-login calls while the gate
     * stays on Connect (the user-code + verification link render there, not the setup wizard). On
     * approval [connect] flips to Connected; on cancel/failure [disconnect] clears it.
     */
    fun pin(profile: ConnectionProfile) {
        _activeProfile.value = profile
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
        // Remember the profile too, so a relaunch knows which backend + vault key to restore from.
        profileStore.write(profile)
        _phase.value = SessionPhase.Connected
    }

    /**
     * Stage a candidate [profile] + [sessionTokens] onto the store WITHOUT persisting them or flipping the
     * gate to Connected — so restore-on-boot can arm the shared [ApiClient] (base URL + bearer) and prove
     * the token via `/me` before committing. On success the caller follows with [connect] (which persists +
     * advances the gate); on failure with [disconnect] (which clears the stale session).
     */
    fun arm(profile: ConnectionProfile, sessionTokens: SessionTokens) {
        _activeProfile.value = profile
        tokens = sessionTokens
    }

    /**
     * Read the remembered session — the persisted active profile and (if any) its vaulted tokens — for
     * restore on boot. Returns null only when NO profile was remembered (fresh install / after a logout). The
     * tokens may be null even with a profile present: the web build keeps no token in JS (its refresh token
     * rides an HttpOnly cookie), so restore proves the session by refreshing against that cookie.
     */
    suspend fun loadPersisted(): RestorableSession? {
        val profile: ConnectionProfile = profileStore.read() ?: return null
        val saved: SessionTokens? = tokenVault.read(profile.id)
        return RestorableSession(profile, saved)
    }

    /** Attach the signed-in identity resolved from `/me` after [connect]. */
    fun setUser(sessionUser: SessionUser) {
        _user.value = sessionUser
    }

    /**
     * Drop ONLY the in-memory session — active profile, tokens, signed-in user — and return the gate to
     * Connect, WITHOUT clearing persistent custody (the vault + remembered profile + the HttpOnly cookie stay).
     * Used when a boot restore can't complete (e.g. an expired token, an unreachable backend): a transient
     * failure must not forget the remembered backend and force a fresh sign-in. An explicit [disconnect]
     * (logout) is what wipes custody.
     */
    fun clearActiveSession() {
        _activeProfile.value = null
        tokens = null
        _user.value = null
        _activeChannelId.value = null
        _phase.value = SessionPhase.NotConnected
    }

    /** Drop the session — clear the vault entry, forget the profile, return the gate to Connect. */
    suspend fun disconnect() {
        _activeProfile.value?.let { tokenVault.clear(it.id) }
        profileStore.clear()
        tokens = null
        _user.value = null
        _activeProfile.value = null
        _activeChannelId.value = null
        _phase.value = SessionPhase.NotConnected
    }
}

/**
 * A remembered session read back from custody on boot — the active profile and its persisted tokens, if any.
 * [tokens] is null on the web build (no JS-readable token store; the refresh token is an HttpOnly cookie),
 * so restore refreshes against the cookie rather than a stored token.
 */
data class RestorableSession(val profile: ConnectionProfile, val tokens: SessionTokens?)

/** The signed-in streamer identity surfaced to the shell (frontend.md §6). */
data class SessionUser(
    val id: String,
    val username: String,
    val displayName: String,
    val profileImageUrl: String?,
    val isAdmin: Boolean = false,
)
