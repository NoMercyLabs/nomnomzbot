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
import bot.nomnomz.dashboard.core.connection.LanDiscovery
import bot.nomnomz.dashboard.core.connection.OAuthFlow
import bot.nomnomz.dashboard.core.connection.OAuthLauncher
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemStatus
import kotlinx.coroutines.delay
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
//
// Before the OAuth dance, the controller probes system readiness (SystemApi.status). A fresh self-host bot
// has no Twitch app credentials yet, so its OAuth can't even start — when the backend reports it isn't
// ready, the controller pins the chosen profile and routes the gate to the first-run Setup wizard instead.
// The wizard collects the credentials and, once ready, calls back into [signInStreamer] to run this same
// streamer OAuth — which now works.
//
// Two ways into the same onboarding (frontend.md §6): the user TYPES a backend URL ([connect]), or CLICKS
// a backend mDNS [LanDiscovery] surfaced on the LAN ([connectTo]). Both build a [ConnectionProfile] and
// run the identical [beginOnboarding] — readiness probe → setup-or-OAuth. The only difference is where the
// profile came from (Manual vs Discovered).
class ConnectController(
    private val sessionStore: SessionStore,
    private val authApi: AuthApi,
    private val systemApi: SystemApi,
    private val oauthLauncher: OAuthLauncher,
    private val lanDiscovery: LanDiscovery,
    private val profileIdFactory: () -> String = ::randomProfileId,
) {
    private val _baseUrl: MutableStateFlow<String> = MutableStateFlow(DEFAULT_BASE_URL)
    private val _status: MutableStateFlow<ConnectStatus> = MutableStateFlow(ConnectStatus.Idle)

    // The profile the user is onboarding against, pinned when the flow routes to setup so [signInStreamer]
    // can run the streamer OAuth against the same backend once the wizard finishes.
    private var pendingProfile: ConnectionProfile? = null

    /** The editable backend-URL field (frontend-structure.md §8 — default localhost, editable). */
    val baseUrl: StateFlow<String> = _baseUrl.asStateFlow()

    /** The current connect state the screen renders (idle / connecting / error). */
    val status: StateFlow<ConnectStatus> = _status.asStateFlow()

    /** The live set of bots mDNS-discovered on the LAN, surfaced as click-to-connect rows (empty on web). */
    val discovered: StateFlow<List<ConnectionProfile>> = lanDiscovery.discovered

    /** Whether LAN discovery works on this platform — false on web, where the Connect screen hides that section. */
    val discoverySupported: Boolean = lanDiscovery.isSupported

    /** Begin browsing the LAN — called when the Connect screen appears. */
    fun startDiscovery() = lanDiscovery.start()

    /** Stop browsing the LAN — called when the Connect screen leaves composition. */
    fun stopDiscovery() = lanDiscovery.stop()

    fun onBaseUrlChange(value: String) {
        _baseUrl.value = value
        if (_status.value is ConnectStatus.Error) _status.value = ConnectStatus.Idle
    }

    /**
     * Point the dashboard at the TYPED backend and start onboarding. Validates the URL, then runs the
     * shared [beginOnboarding]: probe readiness, and either run the streamer OAuth (configured) or route to
     * the Setup wizard (fresh self-host). Errors surface on [status] and the gate stays on Connect.
     */
    suspend fun connect() {
        val normalized: String? = normalizeBaseUrl(_baseUrl.value)
        if (normalized == null) {
            _status.value = ConnectStatus.Error(ConnectError.InvalidUrl)
            return
        }

        val profile =
            ConnectionProfile(
                id = profileIdFactory(),
                displayName = normalized,
                baseUrl = normalized,
                source = ProfileSource.Manual,
            )
        beginOnboarding(profile)
    }

    /**
     * Onboard against a backend CLICKED from the mDNS-discovered list (frontend.md §6) — the zero-friction
     * LAN path. Runs the identical [beginOnboarding] as the typed flow: the discovered profile already
     * carries its base URL, so no URL validation is needed.
     */
    suspend fun connectTo(profile: ConnectionProfile) {
        beginOnboarding(profile)
    }

    /**
     * The single onboarding flow shared by the typed ([connect]) and discovered ([connectTo]) paths: the
     * no-secret Device Code Flow login. Pins [profile] (gate stays on Connect), confirms the backend is
     * reachable, then mints a user code and polls until the operator approves at twitch.tv/activate — at
     * which point the session is established and the gate advances to the shell. A failed probe rolls back.
     */
    private suspend fun beginOnboarding(profile: ConnectionProfile) {
        _status.value = ConnectStatus.Connecting

        // Pin the profile so the shared ApiClient targets the chosen backend for the anonymous device
        // calls, but DON'T flip the gate to setup — the user code renders here on Connect.
        sessionStore.pin(profile)
        pendingProfile = profile

        // Confirm the backend is reachable before showing a code (a clean "can't reach" beats a dead code).
        when (val statusResult: ApiResult<SystemStatus> = systemApi.status()) {
            is ApiResult.Failure -> {
                sessionStore.disconnect()
                pendingProfile = null
                _status.value = ConnectStatus.Error(ConnectError.Auth(statusResult.error.message))
            }

            is ApiResult.Ok -> runDeviceLogin(profile)
        }
    }

    /** Start the device authorization, then poll it to completion (or surface the failure). */
    private suspend fun runDeviceLogin(profile: ConnectionProfile) {
        when (val start: ApiResult<DeviceCodeStart> = authApi.startDeviceLogin()) {
            is ApiResult.Failure -> {
                sessionStore.disconnect()
                pendingProfile = null
                _status.value = ConnectStatus.Error(ConnectError.Auth(start.error.message))
            }

            is ApiResult.Ok -> pollDeviceLogin(profile, start.value)
        }
    }

    /**
     * Show the user code + verification link and poll the backend on the device interval until the operator
     * approves (→ establish the session), declines, or the code expires. A transient poll failure is tolerated
     * until the code's deadline so a network blip mid-approval doesn't abort the login. The delay is a
     * coroutine suspend (never a thread block), so the Connect screen stays responsive.
     */
    private suspend fun pollDeviceLogin(profile: ConnectionProfile, start: DeviceCodeStart) {
        _status.value = ConnectStatus.AwaitingApproval(start.userCode, start.verificationUri)

        var intervalMs: Long = start.interval.toLong().coerceAtLeast(1) * 1_000
        val deadlineSeconds: Int = start.expiresIn.coerceAtLeast(60)
        var elapsedSeconds: Int = 0

        while (elapsedSeconds <= deadlineSeconds) {
            delay(intervalMs)
            elapsedSeconds += (intervalMs / 1_000).toInt()

            when (val poll: ApiResult<DeviceLoginPoll> = authApi.pollDeviceLogin(start.deviceCode)) {
                is ApiResult.Failure -> {
                    if (elapsedSeconds > deadlineSeconds) {
                        _status.value = ConnectStatus.Error(ConnectError.Auth(poll.error.message))
                        return
                    }
                }

                is ApiResult.Ok ->
                    when (poll.value.status) {
                        STATUS_AUTHORIZED -> {
                            val auth: AuthPayload? = poll.value.auth
                            if (auth == null) {
                                _status.value = ConnectStatus.Error(ConnectError.LoginFailed)
                                return
                            }
                            establishSession(
                                profile,
                                SessionTokens(
                                    accessToken = auth.accessToken,
                                    refreshToken = auth.refreshToken,
                                ),
                            )
                            return
                        }

                        STATUS_SLOW_DOWN -> intervalMs += 5_000
                        STATUS_PENDING -> Unit
                        STATUS_EXPIRED -> {
                            _status.value = ConnectStatus.Error(ConnectError.LoginExpired)
                            return
                        }
                        STATUS_DENIED -> {
                            _status.value = ConnectStatus.Error(ConnectError.LoginDenied)
                            return
                        }
                        else -> {
                            _status.value = ConnectStatus.Error(ConnectError.LoginFailed)
                            return
                        }
                    }
            }
        }

        // Fell through the deadline without an authorize/deny — the code is dead.
        _status.value = ConnectStatus.Error(ConnectError.LoginExpired)
    }

    /**
     * Run the streamer OAuth for the pinned onboarding profile once the setup wizard reports the bot is
     * ready. Returns true when the session is established (the gate advances to the shell). Called by the
     * [SetupController] from the wizard's "continue to Twitch sign-in" action.
     */
    suspend fun signInStreamer(): Boolean {
        val profile: ConnectionProfile = pendingProfile ?: return false
        return runStreamerOAuth(profile)
    }

    /** Run the streamer OAuth dance against [profile] and establish the session on success. */
    private suspend fun runStreamerOAuth(profile: ConnectionProfile): Boolean =
        when (val authResult: ApiResult<SessionTokens> = oauthLauncher.authorize(profile.baseUrl, OAuthFlow.Streamer)) {
            is ApiResult.Failure -> {
                _status.value = ConnectStatus.Error(ConnectError.Auth(authResult.error.message))
                false
            }

            is ApiResult.Ok -> establishSession(profile, authResult.value)
        }

    /**
     * Complete a session from tokens captured outside the in-app launcher — the web post-redirect
     * arm hands the served-origin profile + the returned tokens here on boot (frontend.md §6).
     */
    suspend fun completeWithSession(profile: ConnectionProfile, tokens: SessionTokens) {
        _status.value = ConnectStatus.Connecting
        establishSession(profile, tokens)
    }

    /**
     * Commit the session, point + arm the shared ApiClient, then prove the JWT via /me. Returns true when
     * the session is live (the gate advances to the shell); false when the token didn't validate.
     */
    private suspend fun establishSession(profile: ConnectionProfile, tokens: SessionTokens): Boolean {
        sessionStore.connect(profile, tokens)

        return when (val me: ApiResult<CurrentUser> = authApi.me()) {
            is ApiResult.Ok -> {
                sessionStore.setUser(me.value.toSessionUser())
                _status.value = ConnectStatus.Idle
                pendingProfile = null
                true
            }

            is ApiResult.Failure -> {
                // Token didn't validate — roll the session back so the gate stays on Connect.
                sessionStore.disconnect()
                _status.value = ConnectStatus.Error(ConnectError.Auth(me.error.message))
                false
            }
        }
    }

    private companion object {
        const val DEFAULT_BASE_URL: String = "http://localhost:5080"

        // The device-login poll statuses the backend returns (server-side DeviceLoginStatus).
        const val STATUS_AUTHORIZED: String = "authorized"
        const val STATUS_PENDING: String = "pending"
        const val STATUS_SLOW_DOWN: String = "slow_down"
        const val STATUS_EXPIRED: String = "expired"
        const val STATUS_DENIED: String = "denied"
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

    /**
     * The device login is live: show [userCode] for the operator to enter at [verificationUri]
     * (twitch.tv/activate) while the controller polls for approval in the background.
     */
    data class AwaitingApproval(val userCode: String, val verificationUri: String) : ConnectStatus

    data class Error(val error: ConnectError) : ConnectStatus
}

/** Why a connect attempt failed — mapped to a localized message in the screen. */
sealed interface ConnectError {
    data object InvalidUrl : ConnectError

    data class Auth(val detail: String) : ConnectError

    /** The user code expired before it was approved. */
    data object LoginExpired : ConnectError

    /** The operator declined the authorization at Twitch. */
    data object LoginDenied : ConnectError

    /** The login failed for an unexpected reason (malformed/authorized-without-tokens). */
    data object LoginFailed : ConnectError
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
