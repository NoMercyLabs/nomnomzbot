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

import bot.nomnomz.dashboard.core.connection.ActiveProfileStore
import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.LanDiscovery
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionPhase
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.AuthPayload
import bot.nomnomz.dashboard.core.network.BotOAuthUrl
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.CurrentUser
import bot.nomnomz.dashboard.core.network.DeviceCodeStart
import bot.nomnomz.dashboard.core.network.DeviceLoginPoll
import bot.nomnomz.dashboard.core.network.EventSubReconcileReport
import bot.nomnomz.dashboard.core.network.EventSubSubscription
import bot.nomnomz.dashboard.core.network.MissingScopes
import bot.nomnomz.dashboard.core.network.ScopeRegrantStart
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import bot.nomnomz.dashboard.core.network.TwitchDiagnosticsApi
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.test.runTest

// Proves the no-secret Device Code Flow login the Connect screen drives: clicking "Connect with Twitch"
// confirms the backend is reachable, mints a user code, polls, and on approval establishes the real session
// (gate → Connected) — or surfaces a clean error when declined / unreachable, leaving the gate on Connect.
// It also proves the "remembered" tier (frontend.md §6): restoreSession() signs a returning operator
// straight in from custody (refreshing an expired token), and purges a session it can't restore.
// The phase transition IS the observable outcome, so asserting it proves the gate renders the right screen.
class ConnectControllerDeviceLoginTest {

    private val sessionByController: MutableMap<ConnectController, SessionStore> = mutableMapOf()

    private fun controller(
        systemApi: SystemApi,
        authApi: AuthApi = FakeAuthApi(),
        lanDiscovery: LanDiscovery = FakeLanDiscovery(),
        vault: SessionTokenStore = InMemoryVault(),
        profiles: ActiveProfileStore = InMemoryProfileStore(),
        connectLauncher: ConnectLauncher = FakeConnectLauncher(),
        diagnostics: TwitchDiagnosticsApi = FakeTwitchDiagnosticsApi(),
    ): ConnectController {
        val session: SessionStore = SessionStore(vault, profiles)
        return ConnectController(
                sessionStore = session,
                authApi = authApi,
                systemApi = systemApi,
                connectLauncher = connectLauncher,
                lanDiscovery = lanDiscovery,
                diagnosticsApi = diagnostics,
                profileIdFactory = { "test-profile" },
            )
            .also { sessionByController[it] = session }
    }

    private fun sessionOf(controller: ConnectController): SessionStore =
        sessionByController.getValue(controller)

    @Test
    fun connect_establishes_the_session_when_the_login_is_approved() = runTest {
        val authApi =
            FakeAuthApi(
                poll =
                    ApiResult.Ok(
                        DeviceLoginPoll(
                            "authorized",
                            AuthPayload(accessToken = "acc", refreshToken = "ref"),
                        )
                    )
            )
        val controller = controller(FakeSystemApi(ready = true), authApi)
        controller.onBaseUrlChange("http://localhost:5080")

        controller.connect()

        val session: SessionStore = sessionOf(controller)
        // Approval establishes the real session: tokens held, gate on Connected, /me identity attached.
        assertEquals(SessionPhase.Connected, session.phase.value)
        assertEquals("acc", session.accessToken())
        assertEquals("eagle", session.user.value?.username)
        assertEquals(ConnectStatus.Idle, controller.status.value)
    }

    @Test
    fun connect_surfaces_an_error_when_the_login_is_declined() = runTest {
        val authApi = FakeAuthApi(poll = ApiResult.Ok(DeviceLoginPoll("denied")))
        val controller = controller(FakeSystemApi(ready = true), authApi)
        controller.onBaseUrlChange("http://localhost:5080")

        controller.connect()

        // Declined ⇒ no session; the gate stays on Connect with the decline surfaced.
        val session: SessionStore = sessionOf(controller)
        assertEquals(SessionPhase.NotConnected, session.phase.value)
        assertEquals(null, session.accessToken())
        val status: ConnectStatus = controller.status.value
        assertEquals(true, status is ConnectStatus.Error)
        assertEquals(ConnectError.LoginDenied, (status as ConnectStatus.Error).error)
    }

    @Test
    fun an_unreachable_backend_rolls_back_to_connect_with_an_error() = runTest {
        val controller =
            controller(FakeSystemApi(statusError = ApiError(0, "NETWORK", "Connection refused.")))
        controller.onBaseUrlChange("http://localhost:5080")

        controller.connect()

        val session: SessionStore = sessionOf(controller)
        // Couldn't read status ⇒ the gate is back on Connect, profile cleared, with the failure surfaced.
        assertEquals(SessionPhase.NotConnected, session.phase.value)
        assertEquals(null, session.baseUrl())
        assertEquals(true, controller.status.value is ConnectStatus.Error)
    }

    @Test
    fun exposes_the_lan_discovered_list_and_drives_its_start_stop_lifecycle() = runTest {
        val discovery = FakeLanDiscovery()
        val controller = controller(FakeSystemApi(ready = true), lanDiscovery = discovery)

        // The controller re-publishes the discovery feed verbatim, so the Connect screen renders it.
        val bot =
            ConnectionProfile(
                id = "eagle-id",
                displayName = "EAGLE",
                baseUrl = "http://192.168.1.42:5080",
                source = ProfileSource.Discovered,
            )
        discovery.emit(listOf(bot))
        assertEquals(listOf(bot), controller.discovered.value)

        // Lifecycle: the screen starts browsing on show and stops on dispose.
        controller.startDiscovery()
        assertEquals(1, discovery.startCount)
        controller.stopDiscovery()
        assertEquals(1, discovery.stopCount)
    }

    @Test
    fun connect_to_a_discovered_backend_establishes_the_session_on_approval() = runTest {
        // A discovered bot runs the SAME device login as the typed path — proving connectTo reuses
        // beginOnboarding (probe → device login), not a separate flow, and binds the discovered profile.
        val authApi =
            FakeAuthApi(
                poll =
                    ApiResult.Ok(
                        DeviceLoginPoll("authorized", AuthPayload(accessToken = "acc", refreshToken = "ref"))
                    )
            )
        val controller = controller(FakeSystemApi(ready = true), authApi)
        val bot =
            ConnectionProfile(
                id = "eagle-id",
                displayName = "EAGLE",
                baseUrl = "http://192.168.1.42:5080",
                source = ProfileSource.Discovered,
            )

        controller.connectTo(bot)

        val session: SessionStore = sessionOf(controller)
        assertEquals(SessionPhase.Connected, session.phase.value)
        // The session binds the DISCOVERED profile (its id/baseUrl), not a freshly minted manual profile.
        assertEquals(bot, session.activeProfile.value)
        assertEquals("acc", session.accessToken())
    }

    @Test
    fun connect_to_an_unreachable_discovered_backend_rolls_back_with_an_error() = runTest {
        val controller =
            controller(FakeSystemApi(statusError = ApiError(0, "NETWORK", "Connection refused.")))
        val bot =
            ConnectionProfile(
                id = "eagle-id",
                displayName = "EAGLE",
                baseUrl = "http://192.168.1.42:5080",
                source = ProfileSource.Discovered,
            )

        controller.connectTo(bot)

        val session: SessionStore = sessionOf(controller)
        assertEquals(SessionPhase.NotConnected, session.phase.value)
        assertEquals(null, session.baseUrl())
        assertEquals(true, controller.status.value is ConnectStatus.Error)
    }

    // ── Login method: redirect (secret) vs device (no secret) ─────────────────

    @Test
    fun connect_uses_the_redirect_login_when_the_twitch_app_has_a_secret_configured() = runTest {
        // Backend reports the Twitch app configured (client id + secret) ⇒ the better redirect login runs, not
        // the device-code dance. The fake launcher returns the session the OAuth redirect would yield.
        val launcher =
            FakeConnectLauncher(
                streamerResult =
                    ApiResult.Ok(SessionTokens(accessToken = "redir-acc", refreshToken = "redir-ref")),
            )
        val controller =
            controller(
                FakeSystemApi(ready = true, twitchConfigured = true),
                FakeAuthApi(meResults = listOf(ApiResult.Ok(CurrentUser("u1", "eagle", "Eagle")))),
                connectLauncher = launcher,
            )
        controller.onBaseUrlChange("http://localhost:5080")

        controller.connect()

        val session: SessionStore = sessionOf(controller)
        // The redirect path ran (not the device poll), and its tokens established the session.
        assertEquals(true, launcher.authorizeStreamerCalled)
        assertEquals(SessionPhase.Connected, session.phase.value)
        assertEquals("redir-acc", session.accessToken())
        assertEquals("eagle", session.user.value?.username)
    }

    @Test
    fun connect_uses_the_device_flow_when_no_secret_is_configured() = runTest {
        // No secret ⇒ only the shared-public-client device flow can mint a refresh token, so it is used.
        val launcher = FakeConnectLauncher()
        val authApi =
            FakeAuthApi(
                poll =
                    ApiResult.Ok(
                        DeviceLoginPoll("authorized", AuthPayload(accessToken = "acc", refreshToken = "ref"))
                    )
            )
        val controller =
            controller(
                FakeSystemApi(ready = true, twitchConfigured = false),
                authApi,
                connectLauncher = launcher,
            )
        controller.onBaseUrlChange("http://localhost:5080")

        controller.connect()

        val session: SessionStore = sessionOf(controller)
        assertEquals(false, launcher.authorizeStreamerCalled) // redirect NOT used
        assertEquals(SessionPhase.Connected, session.phase.value)
        assertEquals("acc", session.accessToken())
    }

    // ── Reconnect: broadcaster re-auth uses the redirect flow, never a device banner ──

    @Test
    fun reconnect_uses_the_redirect_flow_for_the_broadcaster_when_a_secret_is_configured() = runTest {
        // The dead-token recovery for a signed-in operator: with a client secret present it MUST re-auth via the
        // redirect (Authorization Code) flow — a tap → Twitch → redirect-back — not a device-code banner.
        val launcher =
            FakeConnectLauncher(
                streamerResult =
                    ApiResult.Ok(SessionTokens(accessToken = "fresh-acc", refreshToken = "fresh-ref")),
            )
        val controller =
            controller(
                FakeSystemApi(ready = true, twitchConfigured = true),
                FakeAuthApi(meResults = listOf(ApiResult.Ok(CurrentUser("u1", "eagle", "Eagle")))),
                connectLauncher = launcher,
            )
        // Seed an already-signed-in session — reconnect re-auths in place, it does not onboard.
        val session: SessionStore = sessionOf(controller)
        session.connect(rememberedProfile, SessionTokens(accessToken = "dead-acc", refreshToken = "dead-ref"))

        controller.reconnect()

        // The redirect launcher ran (no device-code banner) and re-vaulted the fresh token in place.
        assertEquals(true, launcher.authorizeStreamerCalled)
        assertEquals(SessionPhase.Connected, session.phase.value)
        assertEquals("fresh-acc", session.accessToken())
    }

    @Test
    fun reconnect_falls_back_to_device_flow_only_when_no_secret_is_configured() = runTest {
        // The secret-less shared public client can't exchange a redirect code, so its ONLY way to mint a fresh
        // refresh token is the Device Code Flow — the one place a broadcaster re-auth still shows a user code.
        val launcher = FakeConnectLauncher()
        val authApi =
            FakeAuthApi(
                poll =
                    ApiResult.Ok(
                        DeviceLoginPoll(
                            "authorized",
                            AuthPayload(accessToken = "dev-acc", refreshToken = "dev-ref"),
                        )
                    ),
                meResults = listOf(ApiResult.Ok(CurrentUser("u1", "eagle", "Eagle"))),
            )
        val controller =
            controller(
                FakeSystemApi(ready = true, twitchConfigured = false),
                authApi,
                connectLauncher = launcher,
            )
        val session: SessionStore = sessionOf(controller)
        session.connect(rememberedProfile, SessionTokens(accessToken = "dead-acc", refreshToken = "dead-ref"))

        controller.reconnect()

        assertEquals(false, launcher.authorizeStreamerCalled) // redirect NOT used — no secret
        assertEquals(SessionPhase.Connected, session.phase.value)
        assertEquals("dev-acc", session.accessToken())
    }

    // ── Proactive dead-token detection (prompt on load, no menu hunt) ─────────

    @Test
    fun check_twitch_health_raises_the_reconnect_prompt_when_the_backend_reports_needs_reauth() = runTest {
        val controller =
            controller(
                FakeSystemApi(ready = true, twitchConfigured = true),
                diagnostics = FakeTwitchDiagnosticsApi(connectionStatus = "needs_reauth"),
            )
        assertEquals(false, controller.reauthRequired.value)

        controller.checkTwitchHealth()

        // A dead token proactively raises the prompt so the shell shows "reconnect" on load — no menu hunt.
        assertEquals(true, controller.reauthRequired.value)
    }

    @Test
    fun check_twitch_health_stays_quiet_for_a_healthy_connection() = runTest {
        val controller =
            controller(
                FakeSystemApi(ready = true, twitchConfigured = true),
                diagnostics = FakeTwitchDiagnosticsApi(connectionStatus = "connected"),
            )

        controller.checkTwitchHealth()

        assertEquals(false, controller.reauthRequired.value) // a healthy token never nags for a reconnect
    }

    @Test
    fun check_twitch_health_fails_open_when_the_probe_errors() = runTest {
        val controller =
            controller(
                FakeSystemApi(ready = true, twitchConfigured = true),
                diagnostics = FakeTwitchDiagnosticsApi(fail = true),
            )

        controller.checkTwitchHealth()

        // A 404 / transient probe failure must NOT raise a false prompt (fail-open, never freeze).
        assertEquals(false, controller.reauthRequired.value)
    }

    // ── Remembered tier (restore-on-boot) ─────────────────────────────────────

    private val rememberedProfile =
        ConnectionProfile(
            id = "p1",
            displayName = "self-host",
            baseUrl = "http://localhost:5080",
            source = ProfileSource.Manual,
        )

    @Test
    fun restore_session_signs_a_returning_operator_straight_in_when_the_stored_token_is_valid() = runTest {
        val vault = InMemoryVault()
        val profiles = InMemoryProfileStore()
        vault.write("p1", SessionTokens(accessToken = "stored-acc", refreshToken = "stored-ref"))
        profiles.write(rememberedProfile)
        val controller =
            controller(
                FakeSystemApi(ready = true),
                FakeAuthApi(meResults = listOf(ApiResult.Ok(CurrentUser("u1", "eagle", "Eagle")))),
                vault = vault,
                profiles = profiles,
            )

        val restored: Boolean = controller.restoreSession()

        val session: SessionStore = sessionOf(controller)
        // The remembered session is live: gate on the shell, riding the stored token, identity attached.
        assertEquals(true, restored)
        assertEquals(SessionPhase.Connected, session.phase.value)
        assertEquals("stored-acc", session.accessToken())
        assertEquals("eagle", session.user.value?.username)
        // No device-code dance, and no error flashed on the Connect screen.
        assertEquals(ConnectStatus.Idle, controller.status.value)
    }

    @Test
    fun restore_session_refreshes_an_expired_access_token_then_signs_in() = runTest {
        val vault = InMemoryVault()
        val profiles = InMemoryProfileStore()
        vault.write("p1", SessionTokens(accessToken = "stale-acc", refreshToken = "good-ref"))
        profiles.write(rememberedProfile)
        val authApi =
            FakeAuthApi(
                // /me rejects the stored token, then accepts the refreshed one.
                meResults =
                    listOf(
                        ApiResult.Failure(ApiError(401, "EXPIRED", "token expired")),
                        ApiResult.Ok(CurrentUser("u1", "eagle", "Eagle")),
                    ),
                refreshResult =
                    ApiResult.Ok(AuthPayload(accessToken = "fresh-acc", refreshToken = "rotated-ref")),
            )
        val controller =
            controller(FakeSystemApi(ready = true), authApi, vault = vault, profiles = profiles)

        val restored: Boolean = controller.restoreSession()

        val session: SessionStore = sessionOf(controller)
        assertEquals(true, restored)
        assertEquals(SessionPhase.Connected, session.phase.value)
        // The session now rides the REFRESHED access token, and the rotated refresh token was re-persisted.
        assertEquals("fresh-acc", session.accessToken())
        assertEquals(
            SessionTokens(accessToken = "fresh-acc", refreshToken = "rotated-ref"),
            vault.stored["p1"],
        )
    }

    @Test
    fun restore_session_refreshes_against_the_cookie_when_no_token_is_stored_in_js() = runTest {
        // The web build holds no JS token — only the profile (its refresh token is an HttpOnly cookie). restore
        // must still recover the session by refreshing (the browser attaches the cookie), with the controller
        // passing a null token to the refresh call.
        val vault = InMemoryVault()
        val profiles = InMemoryProfileStore()
        profiles.write(rememberedProfile) // profile only; vault deliberately empty (no JS token on web)
        val authApi =
            FakeAuthApi(
                meResults = listOf(ApiResult.Ok(CurrentUser("u1", "eagle", "Eagle"))),
                refreshResult =
                    ApiResult.Ok(AuthPayload(accessToken = "cookie-acc", refreshToken = null)),
            )
        val controller =
            controller(FakeSystemApi(ready = true), authApi, vault = vault, profiles = profiles)
        // Watch the base URL the shared client would target the instant refresh runs: on a fresh boot the
        // store has no active profile, so restore MUST pin it first or the real client short-circuits to
        // "no connection" and the cookie refresh never reaches the network.
        authApi.baseUrlProbe = sessionOf(controller)::baseUrl

        val restored: Boolean = controller.restoreSession()

        val session: SessionStore = sessionOf(controller)
        assertEquals(true, restored)
        assertEquals(SessionPhase.Connected, session.phase.value)
        assertEquals("cookie-acc", session.accessToken())
        assertEquals("eagle", session.user.value?.username)
        assertEquals(ConnectStatus.Idle, controller.status.value)
        // The client was aimed at the remembered backend before the cookie refresh — the regression guard.
        assertEquals("http://localhost:5080", authApi.baseUrlAtRefresh)
    }

    @Test
    fun restore_session_falls_to_connect_without_wiping_custody_when_it_cant_restore() = runTest {
        val vault = InMemoryVault()
        val profiles = InMemoryProfileStore()
        vault.write("p1", SessionTokens(accessToken = "stale-acc", refreshToken = "dead-ref"))
        profiles.write(rememberedProfile)
        val authApi =
            FakeAuthApi(
                meResults = listOf(ApiResult.Failure(ApiError(401, "EXPIRED", "token expired"))),
                refreshResult = ApiResult.Failure(ApiError(401, "REFRESH", "refresh rejected")),
            )
        val controller =
            controller(FakeSystemApi(ready = true), authApi, vault = vault, profiles = profiles)

        val restored: Boolean = controller.restoreSession()

        val session: SessionStore = sessionOf(controller)
        assertEquals(false, restored)
        // The gate falls to Connect — the in-memory session is dropped...
        assertEquals(SessionPhase.NotConnected, session.phase.value)
        assertEquals(null, session.accessToken())
        // ...but the remembered backend is KEPT. A transient failure (or a momentarily-down backend) must not
        // forget the user's connection and force a re-login; only an explicit logout wipes custody.
        assertEquals(
            SessionTokens(accessToken = "stale-acc", refreshToken = "dead-ref"),
            vault.stored["p1"],
        )
        assertEquals(rememberedProfile, profiles.stored)
        // And nothing is flashed on the Connect screen; it simply shows the sign-in.
        assertEquals(ConnectStatus.Idle, controller.status.value)
    }

    @Test
    fun restore_session_is_a_no_op_when_no_session_was_remembered() = runTest {
        val controller = controller(FakeSystemApi(ready = true))

        val restored: Boolean = controller.restoreSession()

        assertEquals(false, restored)
        assertEquals(SessionPhase.NotConnected, sessionOf(controller).phase.value)
    }
}

private class FakeSystemApi(
    private val ready: Boolean = false,
    private val statusError: ApiError? = null,
    // Whether the Twitch app has a client SECRET configured (so the redirect login is available). Separate
    // from [ready]; defaults false so the device-flow tests keep taking the device path.
    private val twitchConfigured: Boolean = false,
) : SystemApi {
    override suspend fun status(): ApiResult<SystemStatus> =
        if (statusError != null) {
            ApiResult.Failure(statusError)
        } else {
            ApiResult.Ok(
                SystemStatus(
                    ready = ready,
                    checks =
                        SystemChecks(
                            twitchApp =
                                SystemCheck(
                                    ok = twitchConfigured,
                                    ready = true,
                                    status = if (twitchConfigured) "ready_redirect" else "ready_device",
                                ),
                            platformBot =
                                SystemCheck(
                                    ok = ready,
                                    ready = ready,
                                    status = if (ready) "connected" else "disconnected",
                                ),
                        ),
                )
            )
        }

    override suspend fun wizard(): ApiResult<SetupWizard> = ApiResult.Ok(SetupWizard(complete = ready))

    override suspend fun saveTwitchCredentials(
        clientId: String,
        clientSecret: String,
        botUsername: String?,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun saveSpotifyCredentials(
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun saveYouTubeCredentials(
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun saveDiscordCredentials(
        clientId: String,
        clientSecret: String,
    ): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> =
        ApiResult.Ok(BotOAuthUrl("https://id.twitch.tv/authorize?bot"))

    override suspend fun botStatus(): ApiResult<BotStatus> =
        ApiResult.Ok(BotStatus(connected = ready))

    override suspend fun completeSetup(): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun pronouns(): ApiResult<List<bot.nomnomz.dashboard.core.network.PronounOption>> = ApiResult.Ok(emptyList())
}

/** An in-memory [AuthApi] so the controller tests drive the device-login + restore outcomes without HTTP. */
private class FakeAuthApi(
    private val start: ApiResult<DeviceCodeStart> =
        ApiResult.Ok(
            DeviceCodeStart(
                deviceCode = "device-code",
                userCode = "WXYZ-7890",
                verificationUri = "https://www.twitch.tv/activate",
                interval = 1,
                expiresIn = 60,
            )
        ),
    private val poll: ApiResult<DeviceLoginPoll> = ApiResult.Ok(DeviceLoginPoll("pending")),
    // Successive /me results — restore calls /me once per token it tries (stored, then refreshed); the last
    // entry repeats. Defaults to a single signed-in streamer, which the device-login tests read once.
    private val meResults: List<ApiResult<CurrentUser>> =
        listOf(ApiResult.Ok(CurrentUser(id = "u1", username = "eagle", displayName = "Eagle"))),
    private val refreshResult: ApiResult<AuthPayload> =
        ApiResult.Failure(ApiError(401, "REFRESH", "no refresh configured")),
) : AuthApi {
    private var meCall: Int = 0

    /** Wired by a test to the session's base-URL provider, to prove the client was aimed before [refresh]. */
    var baseUrlProbe: (() -> String?)? = null

    /** The base URL the (real) shared client WOULD target at the moment [refresh] ran — null ⇒ "no connection". */
    var baseUrlAtRefresh: String? = null
        private set

    override suspend fun me(): ApiResult<CurrentUser> {
        val index: Int = minOf(meCall, meResults.lastIndex)
        meCall++
        return meResults[index]
    }

    override suspend fun startDeviceLogin(): ApiResult<DeviceCodeStart> = start

    override suspend fun pollDeviceLogin(deviceCode: String): ApiResult<DeviceLoginPoll> = poll

    override suspend fun refresh(refreshToken: String?): ApiResult<AuthPayload> {
        baseUrlAtRefresh = baseUrlProbe?.invoke()
        return refreshResult
    }
}

/** A fake [ConnectLauncher] — records whether the streamer redirect login ran, and returns a canned result. */
private class FakeConnectLauncher(
    private val streamerResult: ApiResult<SessionTokens> =
        ApiResult.Failure(ApiError(0, "NO_LAUNCH", "redirect login not expected in this test")),
) : ConnectLauncher {
    var authorizeStreamerCalled: Boolean = false
        private set

    override suspend fun authorizeStreamer(baseUrl: String): ApiResult<SessionTokens> {
        authorizeStreamerCalled = true
        return streamerResult
    }

    override suspend fun awaitConnect(
        authorizeUrlFor: suspend (redirect: String) -> ApiResult<String>
    ): ApiResult<Unit> = ApiResult.Ok(Unit)
}

/** A fake [TwitchDiagnosticsApi] — drives the proactive dead-token probe with a canned Twitch connection status. */
private class FakeTwitchDiagnosticsApi(
    private val connectionStatus: String = "connected",
    private val fail: Boolean = false,
) : TwitchDiagnosticsApi {
    override suspend fun missingScopes(): ApiResult<MissingScopes> =
        if (fail) ApiResult.Failure(ApiError(404, "NOT_FOUND", "no twitch connection"))
        else ApiResult.Ok(MissingScopes(connectionStatus = connectionStatus))

    override suspend fun startRegrant(): ApiResult<ScopeRegrantStart> =
        ApiResult.Failure(ApiError(0, "UNUSED", "re-grant not exercised in these tests"))

    override suspend fun subscriptions(channelId: String): ApiResult<List<EventSubSubscription>> =
        ApiResult.Ok(emptyList())

    override suspend fun reconcile(channelId: String): ApiResult<EventSubReconcileReport> =
        ApiResult.Ok(EventSubReconcileReport())
}

/** An in-memory [SessionTokenStore] the restore tests seed + assert against, without any OS vault. */
private class InMemoryVault : SessionTokenStore {
    val stored: MutableMap<String, SessionTokens> = mutableMapOf()

    override suspend fun read(profileId: String): SessionTokens? = stored[profileId]

    override suspend fun write(profileId: String, tokens: SessionTokens) {
        stored[profileId] = tokens
    }

    override suspend fun clear(profileId: String) {
        stored.remove(profileId)
    }
}

/** An in-memory [ActiveProfileStore] the restore tests seed + assert against, without any OS file. */
private class InMemoryProfileStore : ActiveProfileStore {
    var stored: ConnectionProfile? = null

    override suspend fun read(): ConnectionProfile? = stored

    override suspend fun write(profile: ConnectionProfile) {
        stored = profile
    }

    override suspend fun clear() {
        stored = null
    }
}

/** An in-memory [LanDiscovery] so the controller tests drive the discovered feed without any mDNS. */
private class FakeLanDiscovery : LanDiscovery {
    override val isSupported: Boolean = true

    private val _discovered: MutableStateFlow<List<ConnectionProfile>> =
        MutableStateFlow(emptyList())
    override val discovered: StateFlow<List<ConnectionProfile>> = _discovered.asStateFlow()

    var startCount: Int = 0
        private set

    var stopCount: Int = 0
        private set

    fun emit(profiles: List<ConnectionProfile>) {
        _discovered.value = profiles
    }

    override fun start() {
        startCount++
    }

    override fun stop() {
        stopCount++
    }
}
