// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.connect.state

import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.OAuthFlow
import bot.nomnomz.dashboard.core.connection.OAuthLauncher
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.CurrentUser
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Connect screen's state-holder (frontend.md §4 — a plain holder, not a ViewModel). It owns the
// backend-URL field + connect status and runs the REAL Twitch streamer onboarding:
//
//   typed base URL → OAuthLauncher.authorize() (desktop loopback / web redirect) → SessionTokens
//   → SessionStore.connect() (which feeds the shared ApiClient its base URL + bearer)
//   → AuthApi.me() to resolve the signed-in streamer → SessionStore.setUser() → gate flips to Shell.
//
// No mock: the gate moves to Connected only after a real token is captured and /me proves it valid.
class ConnectController(
    private val sessionStore: SessionStore,
    private val authApi: AuthApi,
    private val oauthLauncher: OAuthLauncher,
    private val profileIdFactory: () -> String = ::randomProfileId,
) {
    private val _baseUrl: MutableStateFlow<String> = MutableStateFlow(DEFAULT_BASE_URL)
    private val _status: MutableStateFlow<ConnectStatus> = MutableStateFlow(ConnectStatus.Idle)

    /** The editable backend-URL field (frontend-structure.md §8 — default localhost, editable). */
    val baseUrl: StateFlow<String> = _baseUrl.asStateFlow()

    /** The current connect state the screen renders (idle / connecting / error). */
    val status: StateFlow<ConnectStatus> = _status.asStateFlow()

    fun onBaseUrlChange(value: String) {
        _baseUrl.value = value
        if (_status.value is ConnectStatus.Error) _status.value = ConnectStatus.Idle
    }

    /**
     * Drive the real streamer OAuth flow against the typed backend URL. Suspends through the browser
     * dance; on success the session is live and the gate advances. Errors surface on [status] and
     * the gate stays on Connect.
     */
    suspend fun connect() {
        val normalized: String? = normalizeBaseUrl(_baseUrl.value)
        if (normalized == null) {
            _status.value = ConnectStatus.Error(ConnectError.InvalidUrl)
            return
        }

        _status.value = ConnectStatus.Connecting

        val profile =
            ConnectionProfile(
                id = profileIdFactory(),
                displayName = normalized,
                baseUrl = normalized,
                source = ProfileSource.Manual,
            )

        when (val authResult: ApiResult<SessionTokens> = oauthLauncher.authorize(normalized, OAuthFlow.Streamer)) {
            is ApiResult.Failure ->
                _status.value = ConnectStatus.Error(ConnectError.Auth(authResult.error.message))

            is ApiResult.Ok -> establishSession(profile, authResult.value)
        }
    }

    /**
     * Complete a session from tokens captured outside the in-app launcher — the web post-redirect
     * arm hands the served-origin profile + the returned tokens here on boot (frontend.md §6).
     */
    suspend fun completeWithSession(profile: ConnectionProfile, tokens: SessionTokens) {
        _status.value = ConnectStatus.Connecting
        establishSession(profile, tokens)
    }

    /** Commit the session, point + arm the shared ApiClient, then prove the JWT via /me. */
    private suspend fun establishSession(profile: ConnectionProfile, tokens: SessionTokens) {
        sessionStore.connect(profile, tokens)

        when (val me: ApiResult<CurrentUser> = authApi.me()) {
            is ApiResult.Ok -> {
                sessionStore.setUser(me.value.toSessionUser())
                _status.value = ConnectStatus.Idle
            }

            is ApiResult.Failure -> {
                // Token didn't validate — roll the session back so the gate stays on Connect.
                sessionStore.disconnect()
                _status.value = ConnectStatus.Error(ConnectError.Auth(me.error.message))
            }
        }
    }

    private companion object {
        const val DEFAULT_BASE_URL: String = "http://localhost:5080"
    }
}

private fun CurrentUser.toSessionUser(): SessionUser =
    SessionUser(
        id = id,
        username = username,
        displayName = displayName,
        profileImageUrl = profileImageUrl,
    )

/** The Connect screen's render state. */
sealed interface ConnectStatus {
    data object Idle : ConnectStatus

    data object Connecting : ConnectStatus

    data class Error(val error: ConnectError) : ConnectStatus
}

/** Why a connect attempt failed — mapped to a localized message in the screen. */
sealed interface ConnectError {
    data object InvalidUrl : ConnectError

    data class Auth(val detail: String) : ConnectError
}

/** Accept a host with or without a scheme; reject blanks. Returns the normalized `scheme://host[:port]`. */
internal fun normalizeBaseUrl(raw: String): String? {
    val trimmed: String = raw.trim().trimEnd('/')
    if (trimmed.isEmpty()) return null
    val withScheme: String =
        if (trimmed.startsWith("http://") || trimmed.startsWith("https://")) trimmed else "http://$trimmed"
    // Require a host after the scheme.
    val afterScheme: String = withScheme.substringAfter("://")
    if (afterScheme.isBlank()) return null
    return withScheme
}

private fun randomProfileId(): String {
    val chars = "0123456789abcdef"
    return buildString { repeat(32) { append(chars.random()) } }
}
