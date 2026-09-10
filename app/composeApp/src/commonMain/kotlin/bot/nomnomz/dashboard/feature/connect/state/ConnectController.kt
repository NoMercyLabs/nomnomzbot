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

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.LanDiscovery
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.RestorableSession
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.servedOriginProfile
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.connection.SavedConnection
import bot.nomnomz.dashboard.core.connection.SavedConnectionsRepository
import bot.nomnomz.dashboard.core.connection.TokenVault
import bot.nomnomz.dashboard.core.connection.savedConnectionsStore
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.LoginProvider
import bot.nomnomz.dashboard.core.network.MissingScopes
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemStatus
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import kotlinx.coroutines.CompletableJob
import kotlinx.coroutines.CoroutineStart
import kotlinx.coroutines.Deferred
import kotlinx.coroutines.Job
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import kotlinx.coroutines.selects.select

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
    private val connectLauncher: ConnectLauncher,
    private val lanDiscovery: LanDiscovery,
    private val diagnosticsApi: TwitchDiagnosticsApi,
    private val profileIdFactory: () -> String = ::randomProfileId,
    // Desktop saved-connections switcher (S111b) — a file-backed list on desktop, a no-op on web.
    // Shares the SAME on-disk token vault [SessionStore] arms/reads, so a connection's token survives
    // across the SavedConnectionsRepository's own TokenVault instance (both are stateless file I/O
    // keyed by profile id — no in-memory cache to fall out of sync).
    private val savedConnectionsRepository: SavedConnectionsRepository =
        SavedConnectionsRepository(savedConnectionsStore(), TokenVault()),
) {
    // The web build is single-origin: default the backend URL to the SERVED ORIGIN so it matches wherever the
    // dashboard is opened (localhost, the LAN, or the public tunnel) instead of a hardcoded localhost. Native
    // (multi-origin) keeps the editable localhost default.
    private val _baseUrl: MutableStateFlow<String> =
        MutableStateFlow(servedOriginProfile()?.baseUrl ?: DEFAULT_BASE_URL)
    private val _status: MutableStateFlow<ConnectStatus> = MutableStateFlow(ConnectStatus.Idle)

    // Proactive dead-token recovery: the shell probes Twitch health on load and raises this when the backend
    // reports the operator's token needs re-auth (needs_reauth), so the reconnect prompt surfaces WITHOUT the
    // operator hunting a menu — one tap, no logout. Cleared once a reconnect re-establishes the session.
    private val _reauthRequired: MutableStateFlow<Boolean> = MutableStateFlow(false)

    // S050 — "remembered-session vs unreachable distinction": true when [restoreSession] found a REMEMBERED
    // session (a persisted profile + either a stored token or a refresh cookie to try) but could not confirm it
    // because the backend never answered (a transient network/proxy/cold-start blip) — as opposed to the
    // backend answering and definitively rejecting it (a dead/revoked token, a real "you are logged out"). The
    // App gate reads this to render a distinct "can't reach your bot, retrying" surface instead of silently
    // falling through to the same Landing/Connect screen a NEVER-signed-in visitor sees.
    private val _restoreUnreachable: MutableStateFlow<Boolean> = MutableStateFlow(false)

    /** True when a remembered session exists but the backend could not be reached to confirm it (see above). */
    val restoreUnreachable: StateFlow<Boolean> = _restoreUnreachable.asStateFlow()

    // The ENABLED login providers the backend offers (GET /api/v1/auth/providers). Seeded with a synthetic
    // Twitch entry so the login screen always offers Twitch — even before [loadProviders] resolves, and even
    // if that probe fails (fail-open: the screen must never be a blank card).
    private val _providers: MutableStateFlow<List<LoginProvider>> = MutableStateFlow(listOf(TWITCH_FALLBACK))

    // The profile the user is onboarding against, pinned when the flow routes to setup so [signInStreamer]
    // can run the streamer OAuth against the same backend once the wizard finishes.
    private var pendingProfile: ConnectionProfile? = null

    // The cancel signal for a pending web-redirect wait (the browser-back trap): "Continue with Twitch"
    // navigates the page away, and on web the launcher call never resolves in-page — the session arrives on
    // reload instead (see [ConnectLauncher.authorizeStreamer]). If the operator comes BACK via the browser's
    // Back button, the page is NOT reloaded (bfcache), so that suspended call is still pinned in
    // [ConnectStatus.AwaitingRedirect] forever unless something releases it. [awaitRedirectSession] races the
    // launcher call against this signal (an explicit [cancelPendingLogin], or the operator starting the
    // device-code flow instead — [connect]'s `forceDevice` path) and a hard timeout, so the wait can never
    // become an unrecoverable dead end.
    private var redirectCancelSignal: CompletableJob? = null

    /** The editable backend-URL field (frontend-structure.md §8 — default localhost, editable). */
    val baseUrl: StateFlow<String> = _baseUrl.asStateFlow()

    /** The current connect state the screen renders (idle / connecting / error). */
    val status: StateFlow<ConnectStatus> = _status.asStateFlow()

    /** True when the operator's Twitch token needs re-auth (dead/expired) — the shell auto-shows the reconnect prompt on load. */
    val reauthRequired: StateFlow<Boolean> = _reauthRequired.asStateFlow()

    /** The ENABLED login providers to render one button each for (Twitch always present). Refined by [loadProviders]. */
    val providers: StateFlow<List<LoginProvider>> = _providers.asStateFlow()

    /** The live set of bots mDNS-discovered on the LAN, surfaced as click-to-connect rows (empty on web). */
    val discovered: StateFlow<List<ConnectionProfile>> = lanDiscovery.discovered

    /** Whether LAN discovery works on this platform — false on web, where the Connect screen hides that section. */
    val discoverySupported: Boolean = lanDiscovery.isSupported

    /** True when the last browse attempt found no usable interface — the screen renders this as a calm inline message. */
    val discoveryFailed: StateFlow<Boolean> = lanDiscovery.discoveryFailed

    /** Begin browsing the LAN — called when the Connect screen appears. */
    fun startDiscovery() = lanDiscovery.start()

    /** Stop browsing the LAN — called when the Connect screen leaves composition. */
    fun stopDiscovery() = lanDiscovery.stop()

    /** Explicit re-browse (S111b) — stop then start the browser fresh, e.g. after the operator plugs in a NIC. */
    fun rescanLan() {
        lanDiscovery.stop()
        lanDiscovery.start()
    }

    private val _savedConnections: MutableStateFlow<List<SavedConnection>> = MutableStateFlow(emptyList())

    /** The desktop saved-connections list (S111b) — empty (permanently) on web. */
    val savedConnections: StateFlow<List<SavedConnection>> = _savedConnections.asStateFlow()

    private val _activeSavedConnectionId: MutableStateFlow<String?> = MutableStateFlow(null)

    /** Which saved connection is currently active, if any. */
    val activeSavedConnectionId: StateFlow<String?> = _activeSavedConnectionId.asStateFlow()

    /** Load the saved-connections list + active id from custody — call once when the Connect screen appears. */
    suspend fun loadSavedConnections() {
        _savedConnections.value = savedConnectionsRepository.list()
        _activeSavedConnectionId.value = savedConnectionsRepository.activeId()
    }

    /**
     * Save the CURRENTLY TYPED backend URL as a named connection (manual add, S111b). Validates the URL the
     * same way [connect] does; a blank/invalid URL is silently ignored (the field's own error path covers
     * feedback for the connect flow, and an add button is disabled with nothing typed).
     */
    suspend fun addSavedConnection(label: String) {
        val normalized: String = normalizeBaseUrl(_baseUrl.value) ?: return
        val trimmedLabel: String = label.trim().ifEmpty { normalized }
        savedConnectionsRepository.add(
            SavedConnection(id = profileIdFactory(), label = trimmedLabel, baseUrl = normalized, lastUsedAt = null),
        )
        loadSavedConnections()
    }

    /**
     * Switch the active backend to a saved connection (S111b) — swaps in its base URL + stored token and
     * reconnects REST + SignalR via [establishSession] (both read the shared [SessionStore]'s baseUrl/token).
     * When no token was ever stored for this connection (added but never signed in), falls back to the full
     * onboarding dance ([connectTo]) against the same id, so a first-time switch still lands the operator in.
     */
    suspend fun switchToSavedConnection(connection: SavedConnection) {
        if (loginInProgress()) return
        savedConnectionsRepository.switchTo(connection.id)
        _activeSavedConnectionId.value = connection.id

        val profile =
            ConnectionProfile(
                id = connection.id,
                displayName = connection.label,
                baseUrl = connection.baseUrl,
                source = ProfileSource.Manual,
            )
        val storedTokens: SessionTokens? = savedConnectionsRepository.tokenFor(connection.id)
        _status.value = ConnectStatus.Connecting
        val reconnected: Boolean = storedTokens != null && establishSession(profile, storedTokens)
        if (!reconnected) {
            connectTo(profile)
        }
        loadSavedConnections()
    }

    /** Forget a saved connection AND its stored token (S111b) — never touches any other connection's token. */
    suspend fun forgetSavedConnection(id: String) {
        savedConnectionsRepository.forget(id)
        loadSavedConnections()
    }

    fun onBaseUrlChange(value: String) {
        _baseUrl.value = value
        if (_status.value is ConnectStatus.Error) _status.value = ConnectStatus.Idle
    }

    /**
     * Point the dashboard at the TYPED backend and start onboarding. Validates the URL, then runs the
     * shared [beginOnboarding]: probe readiness, and either run the streamer OAuth (configured) or route to
     * the Setup wizard (fresh self-host). Errors surface on [status] and the gate stays on Connect.
     */
    suspend fun connect(forceDevice: Boolean = false) {
        // The device-code escape hatch always wins: it must be able to preempt a stuck web-redirect wait
        // (the browser-back trap) rather than being refused by the single-flight guard below.
        if (forceDevice) {
            cancelPendingLogin()
        } else if (loginInProgress()) {
            // Single-flight: never start a second device login while one is already in flight — a second poll
            // loop would double the rate we hit the backend (and Twitch). The button is also disabled while busy.
            return
        }

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
        beginOnboarding(profile, forceDevice)
    }

    /**
     * Load the ENABLED login providers the backend offers, so the screen renders one button per provider
     * (never a dead button). Keeps only `enabled == true`. Fail-open: a probe failure — or a backend that
     * somehow reports no enabled provider — falls back to the synthetic Twitch entry, so the screen always
     * offers Twitch. Anonymous; safe to call on first composition before any session exists.
     */
    suspend fun loadProviders() {
        when (val result: ApiResult<List<LoginProvider>> = authApi.providers()) {
            is ApiResult.Ok -> {
                val enabled: List<LoginProvider> = result.value.filter { it.enabled }
                _providers.value = enabled.ifEmpty { listOf(TWITCH_FALLBACK) }
            }

            is ApiResult.Failure -> _providers.value = listOf(TWITCH_FALLBACK)
        }
    }

    /**
     * Start the login for a specific [provider] chosen from the endpoint-driven button list. Twitch keeps its
     * full onboarding (readiness probe → redirect or device — [connect]); every other provider runs the
     * generic per-provider flow: a full-page authorize redirect when it supports an `auth_code`(+PKCE) flow,
     * otherwise the generic device-code login. [forceDevice] only applies to Twitch (its redirect-vs-device
     * choice); non-Twitch providers pick their path from the advertised [LoginProvider.flows].
     */
    suspend fun connect(provider: LoginProvider, forceDevice: Boolean = false) {
        if (provider.key == TWITCH_PROVIDER) {
            connect(forceDevice)
            return
        }

        if (loginInProgress()) return

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
        _status.value = ConnectStatus.Connecting
        sessionStore.pin(profile)
        pendingProfile = profile

        val supportsRedirect: Boolean =
            provider.flows.any { it == FLOW_AUTH_CODE || it == FLOW_AUTH_CODE_PKCE }
        if (supportsRedirect) {
            runProviderOAuth(profile, provider.key)
        } else {
            runDeviceLogin(profile, provider = provider.key)
        }
    }

    /**
     * Onboard against a backend CLICKED from the mDNS-discovered list (frontend.md §6) — the zero-friction
     * LAN path. Runs the identical [beginOnboarding] as the typed flow: the discovered profile already
     * carries its base URL, so no URL validation is needed.
     */
    suspend fun connectTo(profile: ConnectionProfile) {
        if (loginInProgress()) return
        beginOnboarding(profile)
    }

    /**
     * Explicit sign-out. Tell the backend to revoke the session and (web) delete the HttpOnly refresh cookie
     * BEFORE dropping local custody — otherwise the cookie survives and a reload silently re-authenticates via
     * [restoreSession]'s refresh, landing the operator straight back on the dashboard. Best-effort on the
     * network call: local custody is dropped regardless, so even an offline logout returns the gate to Connect.
     */
    suspend fun logout() {
        authApi.logout()
        sessionStore.disconnect()
    }

    /**
     * True while a login is connecting, awaiting device approval, or waiting on a web redirect — used to
     * refuse a second concurrent login. [connect]'s `forceDevice` path bypasses this via [cancelPendingLogin]
     * so the device-code escape hatch is never blocked by a stuck redirect wait.
     */
    private fun loginInProgress(): Boolean =
        _status.value is ConnectStatus.Connecting ||
            _status.value is ConnectStatus.AwaitingApproval ||
            _status.value is ConnectStatus.AwaitingRedirect

    /**
     * Explicit escape hatch for a pending web-redirect wait — the browser-back trap: the operator hit
     * browser Back after "Continue with Twitch" (or just gives up waiting) and needs a way out that isn't a
     * permanent spinner. Releases [awaitRedirectSession]'s race, which returns the card to [ConnectStatus.Idle]
     * with both sign-in options available again. A no-op when no redirect wait is pending (safe to wire to a
     * Cancel affordance that only renders during [ConnectStatus.AwaitingRedirect]).
     */
    fun cancelPendingLogin() {
        redirectCancelSignal?.complete()
    }

    /**
     * The single onboarding flow shared by the typed ([connect]) and discovered ([connectTo]) paths: the
     * no-secret Device Code Flow login. Pins [profile] (gate stays on Connect), confirms the backend is
     * reachable, then mints a user code and polls until the operator approves at twitch.tv/activate — at
     * which point the session is established and the gate advances to the shell. A failed probe rolls back.
     */
    private suspend fun beginOnboarding(profile: ConnectionProfile, forceDevice: Boolean = false) {
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

            is ApiResult.Ok -> {
                // A fresh self-host bot with NO platform app configured yet can't start ANY Twitch OAuth —
                // `onboardingComplete == false` is the single gate that routes to the first-run Setup wizard
                // instead (SystemStatusDto.OnboardingComplete — deployment config only, never gated on the
                // platform bot). `pendingProfile` is already pinned above, so the wizard's finish() →
                // [signInStreamer] runs this same streamer OAuth once the backend reports onboarding done.
                if (!statusResult.value.onboardingComplete) {
                    _status.value = ConnectStatus.Idle
                    sessionStore.enterSetup(profile)
                    return
                }
                // Redirect (Authorization Code) login when the operator has a client SECRET configured
                // (twitchApp.ok) — a clean tap → Twitch → redirect-back, far better on mobile, and it sets the
                // HttpOnly cookie that remember-me rides. Without a secret (the shared public client) only the
                // Device Code Flow can mint a refresh token, so fall back to it. The operator can also force the
                // device path ([forceDevice]) — it needs no registered redirect URL on the Twitch app, the
                // resilient way in when the redirect callback isn't registered yet.
                if (!forceDevice && statusResult.value.checks.twitchApp.ok) {
                    runStreamerOAuth(profile)
                } else {
                    runDeviceLogin(profile)
                }
            }
        }
    }

    /**
     * Re-authorize Twitch for the ALREADY-signed-in operator WITHOUT a logout — the dead-token recovery, not a
     * re-onboard. The broadcaster's re-auth is the REDIRECT (Authorization Code) flow, exactly like login: a
     * tap → Twitch → redirect-back that re-vaults a fresh token carrying the full streamer scope set in place,
     * with no code to type. On web the page navigates to Twitch and the session re-establishes on return
     * (readReturnedSession → [completeWithSession] on boot); on desktop the loopback resolves with the fresh
     * tokens and [runStreamerOAuth] re-establishes in place. The redirect needs a client secret to exchange the
     * code, so ONLY the secret-less shared public client (which can't mint a refresh token any other way) falls
     * back to the Device Code Flow. A failed or declined attempt KEEPS the existing session intact (unlike
     * onboarding, which rolls back). On self-host the bot falls back to the streamer token, so this restores
     * chat send + read once the fresh token is vaulted.
     */
    suspend fun reconnect() {
        if (loginInProgress()) return
        val profile: ConnectionProfile =
            sessionStore.activeProfile.value ?: servedOriginProfile() ?: return
        _status.value = ConnectStatus.Connecting

        // Broadcaster re-auth = the redirect flow (never a device-code banner) whenever a client secret is
        // configured; only the secret-less public client falls back to device. A failed probe surfaces an
        // error but KEEPS the session — this is a reconnect, not an onboard.
        when (val statusResult: ApiResult<SystemStatus> = systemApi.status()) {
            is ApiResult.Failure ->
                _status.value = ConnectStatus.Error(ConnectError.Auth(statusResult.error.message))

            is ApiResult.Ok ->
                if (statusResult.value.checks.twitchApp.ok) runStreamerOAuth(profile)
                else runDeviceLogin(profile, keepSession = true)
        }
    }

    /** Return the status to Idle so the reconnect bar hides when the operator dismisses it (cancel the job separately). */
    fun clearReconnectStatus() {
        _status.value = ConnectStatus.Idle
    }

    /**
     * Probe the backend for the operator's Twitch connection health and set the reconnect prompt AUTHORITATIVELY
     * from the answer: raise it when the token is dead (`needs_reauth`), and CLEAR it on any healthy status. The
     * clear-on-healthy half is what dismisses the prompt once a reconnect restores the token — the shell re-polls
     * this while the prompt is up, so it self-heals without a manual reload. Fail-open: a 404 (no Twitch connection
     * yet) or any transient network failure leaves the current state untouched — a blip must neither nag a healthy
     * operator nor wrongly clear a real prompt — and it never throws (the page can never freeze on a probe).
     */
    suspend fun checkTwitchHealth() {
        when (val health: ApiResult<MissingScopes> = diagnosticsApi.missingScopes()) {
            is ApiResult.Ok ->
                _reauthRequired.value = health.value.connectionStatus == TWITCH_NEEDS_REAUTH

            is ApiResult.Failure -> Unit
        }
    }

    /** Dismiss the proactive reconnect prompt (the operator closed it); it re-raises on the next load if still dead. */
    fun dismissReauthPrompt() {
        _reauthRequired.value = false
    }

    /** Start the device authorization for [provider], then poll it to completion (or surface the failure). */
    private suspend fun runDeviceLogin(
        profile: ConnectionProfile,
        keepSession: Boolean = false,
        provider: String = TWITCH_PROVIDER,
    ) {
        when (val start: ApiResult<DeviceCodeStart> = authApi.startDeviceLogin(provider)) {
            is ApiResult.Failure -> {
                // A RECONNECT (keepSession) must NOT drop the operator's still-valid app session on a failed
                // start — only onboarding rolls back. The error surfaces on [status]; the session stays put.
                if (!keepSession) {
                    sessionStore.disconnect()
                    pendingProfile = null
                }
                _status.value = ConnectStatus.Error(ConnectError.Auth(start.error.message))
            }

            is ApiResult.Ok -> pollDeviceLogin(profile, start.value, provider)
        }
    }

    /**
     * Show the user code + verification link and poll the backend on the device interval until the operator
     * approves (→ establish the session), declines, or the code expires. A transient poll failure is tolerated
     * until the code's deadline so a network blip mid-approval doesn't abort the login. The delay is a
     * coroutine suspend (never a thread block), so the Connect screen stays responsive.
     */
    private suspend fun pollDeviceLogin(
        profile: ConnectionProfile,
        start: DeviceCodeStart,
        provider: String = TWITCH_PROVIDER,
    ) {
        _status.value = ConnectStatus.AwaitingApproval(start.userCode, start.verificationUri)

        var intervalMs: Long = start.interval.toLong().coerceAtLeast(1) * 1_000
        val deadlineSeconds: Int = start.expiresIn.coerceAtLeast(60)
        var elapsedSeconds: Int = 0

        while (elapsedSeconds <= deadlineSeconds) {
            delay(intervalMs)
            elapsedSeconds += (intervalMs / 1_000).toInt()

            when (val poll: ApiResult<DeviceLoginPoll> = authApi.pollDeviceLogin(provider, start.deviceCode)) {
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
        awaitRedirectSession(profile) { connectLauncher.authorizeStreamer(profile.baseUrl) }

    /**
     * Run the generic per-provider login redirect for [providerKey] against [profile] and establish the
     * session on success — the non-Twitch equivalent of [runStreamerOAuth], hitting the backend's
     * `/api/v1/auth/{providerKey}/authorize` route. On web the page navigates away and the session returns on
     * reload; on desktop the loopback resolves with the tokens.
     */
    private suspend fun runProviderOAuth(profile: ConnectionProfile, providerKey: String): Boolean =
        awaitRedirectSession(profile) { connectLauncher.authorizeProvider(profile.baseUrl, providerKey) }

    /**
     * Run [startRedirect] (a desktop-loopback OR web-redirect authorize call) as a bounded, cancellable wait:
     * shows [ConnectStatus.AwaitingRedirect] and races the call against a hard timeout and
     * [redirectCancelSignal] (armed for the duration of the wait). On web the call itself never resolves after
     * a browser Back (the page is frozen, not reloaded — the real trap this guards against), so EITHER
     * external release is what keeps the card from spinning forever:
     *   - timeout: the operator never came back at all (a dead redirect — unregistered URI, a Twitch 502, ...)
     *     → an honest [ConnectError.RedirectTimedOut], never an indefinite spinner.
     *   - cancel: [cancelPendingLogin] (explicit "never mind") or [connect]'s `forceDevice` escape hatch
     *     (start the device-code flow instead) → back to [ConnectStatus.Idle].
     * The status guard at the end never clobbers a status a concurrent flow (the forceDevice escape hatch)
     * already advanced past this wait.
     */
    private suspend fun awaitRedirectSession(
        profile: ConnectionProfile,
        startRedirect: suspend () -> ApiResult<SessionTokens>,
    ): Boolean {
        val cancelSignal: CompletableJob = Job()
        redirectCancelSignal = cancelSignal
        _status.value = ConnectStatus.AwaitingRedirect

        val outcome: RedirectOutcome =
            coroutineScope {
                // UNDISPATCHED: on web, [startRedirect] runs window.location.assign synchronously before its
                // first suspension point (the never-resolving CompletableDeferred().await() in
                // OAuthLauncher.wasmJs.kt). Plain `async` (CoroutineStart.DEFAULT) instead schedules the whole
                // body onto the dispatcher and only starts running it once this coroutine yields — which
                // happens inside `select` below — so the navigation could sit queued behind whatever the
                // dispatcher runs first instead of firing in the same tick as the click. UNDISPATCHED runs the
                // body immediately, in the calling coroutine, up to that first suspension, so the browser
                // navigates the instant the button is pressed while the wait/cancel/timeout race is unaffected.
                val redirectResult: Deferred<ApiResult<SessionTokens>> =
                    async(start = CoroutineStart.UNDISPATCHED) { startRedirect() }
                val timeoutJob: Job = launch { delay(REDIRECT_WAIT_TIMEOUT_MS) }
                select<RedirectOutcome> {
                    redirectResult.onAwait { RedirectOutcome.Completed(it) }
                    timeoutJob.onJoin { RedirectOutcome.TimedOut }
                    cancelSignal.onJoin { RedirectOutcome.Cancelled }
                }.also {
                    redirectResult.cancel()
                    timeoutJob.cancel()
                }
            }

        if (redirectCancelSignal === cancelSignal) redirectCancelSignal = null

        // A concurrent flow already resolved [status] past this wait (the forceDevice escape hatch runs its
        // own login and sets AwaitingApproval/Idle before this coroutine gets to run again) — never clobber it.
        if (_status.value !is ConnectStatus.AwaitingRedirect) return false

        return when (outcome) {
            is RedirectOutcome.Completed ->
                when (val result: ApiResult<SessionTokens> = outcome.result) {
                    is ApiResult.Ok -> establishSession(profile, result.value)
                    is ApiResult.Failure -> {
                        _status.value = ConnectStatus.Error(ConnectError.Auth(result.error.message))
                        false
                    }
                }

            RedirectOutcome.TimedOut -> {
                _status.value = ConnectStatus.Error(ConnectError.RedirectTimedOut)
                false
            }

            RedirectOutcome.Cancelled -> {
                _status.value = ConnectStatus.Idle
                false
            }
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

    /**
     * Restore a remembered session on boot (frontend.md §6 — the "remembered" tier): read the persisted
     * profile + tokens, arm the shared client, and prove the access token via `/me`. If that token has
     * expired, exchange the stored refresh token once for a fresh pair and re-prove. On success the gate
     * advances straight to the shell — no device-code dance for a returning operator. On any failure the
     * stale session is purged and the gate stays on Connect. Deliberately never sets an error on [status]:
     * a failed restore is a silent fall-through to sign-in, not a visible error on the Connect screen.
     */
    suspend fun restoreSession(): Boolean {
        _restoreUnreachable.value = false
        val remembered: RestorableSession? = sessionStore.loadPersisted()
        // Web's backend is always the serving origin, so it restores from the HttpOnly cookie even with no
        // persisted profile (e.g. localStorage was cleared); native relies on the saved profile.
        val profile: ConnectionProfile = remembered?.profile ?: servedOriginProfile() ?: return false
        val stored: SessionTokens? = remembered?.tokens
        // A profile was actually REMEMBERED (as opposed to just the web build's served-origin fallback with no
        // prior session at all) — only then does an unreachable backend mean "you have a session, we just can't
        // reach it" rather than "there was never anything to restore".
        val hadRememberedSession: Boolean = remembered != null

        // Point the shared client at the backend BEFORE any call: both the stored-token probe and the cookie
        // refresh need its base URL, and on a fresh boot the store has no active profile yet — so without this
        // the ApiClient short-circuits to "no connection" and refresh never reaches the network.
        sessionStore.pin(profile)

        // 1. A stored access token (the native vault, or a same-tab web reload) — prove it first.
        if (stored != null && attachSession(profile, stored)) return true

        // 2. Renew — native sends the refresh token it holds; web sends null and the backend reads its
        //    HttpOnly cookie. Either way this gets a fresh access token without another device-code dance.
        //    A transient failure (status 0/network, or a 5xx — a proxy/backend still warming up on boot, the
        //    exact 504 observed live behind the dev webpack proxy) is retried a few times before giving up: it
        //    is NOT proof the cookie is dead, and treating it as one bounced a perfectly valid session to the
        //    login screen for what was really a momentary blip.
        val refreshed: ApiResult<AuthPayload> = refreshWithTransientRetry(stored?.refreshToken)
        when (refreshed) {
            is ApiResult.Ok -> {
                val renewed: SessionTokens =
                    SessionTokens(
                        accessToken = refreshed.value.accessToken,
                        // The backend rotates the refresh token (web keeps it in the cookie, so the body
                        // carries none); keep the prior one only if nothing came back.
                        refreshToken = refreshed.value.refreshToken ?: stored?.refreshToken,
                    )
                if (attachSession(profile, renewed)) return true
            }

            is ApiResult.Failure -> Unit
        }

        // The retries above are STILL failing on a transient status even after being exhausted — the backend
        // never gave a real (non-transient) answer at all. That is materially different from a definitive
        // rejection (401/403 — the session really is dead): a remembered session exists, it just could not be
        // confirmed. Surface that distinctly so the gate never shows the same "you are not signed in" screen a
        // brand-new visitor sees for what might just be the bot still booting or a dropped LAN link.
        if (hadRememberedSession && refreshed is ApiResult.Failure && isTransientFailure(refreshed.error)) {
            _restoreUnreachable.value = true
        }

        // Couldn't restore (expired/absent token or an unreachable backend) — drop only the in-memory session
        // but KEEP the remembered backend + cookie, so a transient failure never forces a re-login. The gate
        // falls to Connect; an explicit logout is what clears custody.
        sessionStore.clearActiveSession()
        return false
    }

    /** Clear the "remembered session unreachable" flag — the App gate calls this once it has re-tried the restore. */
    fun clearRestoreUnreachable() {
        _restoreUnreachable.value = false
    }

    /**
     * [AuthApi.refresh], retried when the failure looks transient (status 0 — no response reached the client
     * at all, e.g. a network/proxy blip — or a 5xx — the backend itself errored, e.g. still starting up). A
     * definitive rejection (401/403 — the cookie/refresh token really is dead) is never retried: retrying that
     * only delays the correct, immediate fall-through to the login screen.
     */
    private suspend fun refreshWithTransientRetry(
        refreshToken: String?,
    ): ApiResult<AuthPayload> {
        var attempt = 0
        var result: ApiResult<AuthPayload> = authApi.refresh(refreshToken)
        while (result is ApiResult.Failure && isTransientFailure(result.error) && attempt < RESTORE_REFRESH_MAX_RETRIES) {
            delay(RESTORE_REFRESH_RETRY_DELAY_MS)
            attempt++
            result = authApi.refresh(refreshToken)
        }
        return result
    }

    private fun isTransientFailure(error: ApiError): Boolean = error.status == 0 || error.status >= 500

    /**
     * Arm [tokens] onto the session (so the shared client sends them), prove them via `/me`, and commit the
     * session (gate → Connected, tokens persisted) on success. Returns false WITHOUT touching [status] when
     * the token doesn't validate, leaving the caller to try a refresh or fall through to Connect.
     */
    private suspend fun attachSession(profile: ConnectionProfile, tokens: SessionTokens): Boolean {
        sessionStore.arm(profile, tokens)
        return when (val me: ApiResult<CurrentUser> = authApi.me()) {
            is ApiResult.Ok -> {
                sessionStore.connect(profile, tokens)
                sessionStore.setUser(me.value.toSessionUser())
                true
            }

            is ApiResult.Failure -> false
        }
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
                // A fresh OAuth re-vaulted the token — clear any proactive "needs re-auth" prompt.
                _reauthRequired.value = false
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

        // Boot-restore cookie-refresh retry (a transient network/proxy/cold-start blip, not a dead session):
        // a couple of quick retries covers a backend/reverse-proxy still warming up without stalling the
        // splash for long.
        const val RESTORE_REFRESH_MAX_RETRIES: Int = 2
        const val RESTORE_REFRESH_RETRY_DELAY_MS: Long = 300L

        // The backend's advertised login-handshake tokens (LoginProviderDto.Flows) — a redirect flow means a
        // full-page authorize; device-code means the poll loop.
        const val FLOW_DEVICE_CODE: String = "device_code"
        const val FLOW_AUTH_CODE: String = "auth_code"
        const val FLOW_AUTH_CODE_PKCE: String = "auth_code_pkce"

        // The Twitch provider key + the always-available fallback descriptor the screen offers when the
        // provider probe hasn't landed or failed (fail-open — never a blank login card). Twitch supports both
        // the device-code and the redirect (auth_code) login handshakes.
        const val TWITCH_PROVIDER: String = "twitch"

        val TWITCH_FALLBACK: LoginProvider =
            LoginProvider(
                key = TWITCH_PROVIDER,
                displayName = "Twitch",
                flows = listOf(FLOW_DEVICE_CODE, FLOW_AUTH_CODE),
                enabled = true,
            )

        // The device-login poll statuses the backend returns (server-side DeviceLoginStatus).
        const val STATUS_AUTHORIZED: String = "authorized"
        const val STATUS_PENDING: String = "pending"
        const val STATUS_SLOW_DOWN: String = "slow_down"
        const val STATUS_EXPIRED: String = "expired"
        const val STATUS_DENIED: String = "denied"

        // The backend's IntegrationConnection.Status string when the Twitch token is dead/expired
        // (server-side AuthEnums.NeedsReauth) — the proactive reconnect prompt's trigger.
        const val TWITCH_NEEDS_REAUTH: String = "needs_reauth"

        // The bound on [awaitRedirectSession]'s wait — long enough for a real Twitch approval (including
        // typing 2FA), short enough that a dead redirect (unregistered URI, a Twitch 502, ...) resolves to an
        // honest error within a session rather than spinning until the operator gives up and reloads.
        const val REDIRECT_WAIT_TIMEOUT_MS: Long = 3 * 60 * 1_000
    }
}

/** How [ConnectController.awaitRedirectSession]'s race between the launcher call, a timeout, and an explicit cancel resolved. */
private sealed interface RedirectOutcome {
    data class Completed(val result: ApiResult<SessionTokens>) : RedirectOutcome

    data object TimedOut : RedirectOutcome

    data object Cancelled : RedirectOutcome
}

private fun CurrentUser.toSessionUser(): SessionUser =
    SessionUser(
        id = id,
        username = username,
        displayName = displayName,
        profileImageUrl = profileImageUrl,
        isAdmin = isAdmin,
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

    /**
     * Waiting on a redirect (Authorization Code) login: desktop resolves via the loopback; web navigates the
     * page to Twitch and returns on reload — a browser Back resumes the frozen page WITHOUT that resolving
     * (the trap this status exists to make escapable, not hide). The screen must keep the device-code option
     * usable and offer an explicit cancel while in this state — see [ConnectController.cancelPendingLogin].
     */
    data object AwaitingRedirect : ConnectStatus

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

    /** A redirect (Authorization Code) login wasn't completed within [ConnectController]'s wait bound. */
    data object RedirectTimedOut : ConnectError
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
