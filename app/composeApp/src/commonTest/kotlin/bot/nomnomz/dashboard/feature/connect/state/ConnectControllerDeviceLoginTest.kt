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
import bot.nomnomz.dashboard.core.connection.OAuthLauncher
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
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.test.runTest

// Proves the no-secret Device Code Flow login the Connect screen drives: clicking "Connect with Twitch"
// confirms the backend is reachable, mints a user code, polls, and on approval establishes the real session
// (gate → Connected) — or surfaces a clean error when declined / unreachable, leaving the gate on Connect.
// The phase transition IS the observable outcome, so asserting it proves the gate renders the right screen.
class ConnectControllerDeviceLoginTest {

    private val sessionByController: MutableMap<ConnectController, SessionStore> = mutableMapOf()

    private fun controller(
        systemApi: SystemApi,
        authApi: AuthApi = FakeAuthApi(),
        lanDiscovery: LanDiscovery = FakeLanDiscovery(),
    ): ConnectController {
        val session: SessionStore = SessionStore(FakeVault())
        return ConnectController(
                sessionStore = session,
                authApi = authApi,
                systemApi = systemApi,
                oauthLauncher = OAuthLauncher(),
                lanDiscovery = lanDiscovery,
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
}

private class FakeSystemApi(
    private val ready: Boolean = false,
    private val statusError: ApiError? = null,
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
                            twitchApp = SystemCheck(ready, if (ready) "configured" else "missing"),
                            platformBot =
                                SystemCheck(ready, if (ready) "connected" else "disconnected"),
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
}

/** An in-memory [AuthApi] so the controller tests drive the device-login outcomes without any HTTP. */
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
    private val me: ApiResult<CurrentUser> =
        ApiResult.Ok(CurrentUser(id = "u1", username = "eagle", displayName = "Eagle")),
) : AuthApi {
    override suspend fun me(): ApiResult<CurrentUser> = me

    override suspend fun startDeviceLogin(): ApiResult<DeviceCodeStart> = start

    override suspend fun pollDeviceLogin(deviceCode: String): ApiResult<DeviceLoginPoll> = poll
}

private class FakeVault : SessionTokenStore {
    override suspend fun read(profileId: String): SessionTokens? = null

    override suspend fun write(profileId: String, tokens: SessionTokens) = Unit

    override suspend fun clear(profileId: String) = Unit
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
