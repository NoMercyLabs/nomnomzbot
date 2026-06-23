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
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import bot.nomnomz.dashboard.core.network.ApiClient
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.BotOAuthUrl
import bot.nomnomz.dashboard.core.network.BotStatus
import bot.nomnomz.dashboard.core.network.SetupWizard
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.core.network.SystemCheck
import bot.nomnomz.dashboard.core.network.SystemChecks
import bot.nomnomz.dashboard.core.network.SystemStatus
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.test.runTest

// Proves the routing decision that closes the gap: a fresh self-host bot has no Twitch app credentials, so
// its OAuth can't start — the connect flow must probe readiness FIRST and route the gate to the Setup
// wizard (NeedsSetup) instead of straight to Twitch OAuth. The phase transition IS the routing decision,
// so asserting it proves the gate renders the wizard. The OAuth launcher is never invoked on these paths
// (not-ready / unreachable), so a real launcher is safe and uncalled.
class ConnectControllerSetupRoutingTest {

    private fun controller(
        systemApi: SystemApi,
        lanDiscovery: LanDiscovery = FakeLanDiscovery(),
    ): ConnectController {
        val session = SessionStore(FakeVault())
        val client = ApiClient(baseUrlProvider = session::baseUrl, tokenProvider = session::accessToken)
        return ConnectController(
            sessionStore = session,
            authApi = AuthApi(client),
            systemApi = systemApi,
            oauthLauncher = OAuthLauncher(),
            lanDiscovery = lanDiscovery,
            profileIdFactory = { "test-profile" },
        ).also { sessionByController[it] = session }
    }

    private val sessionByController: MutableMap<ConnectController, SessionStore> = mutableMapOf()

    private fun sessionOf(controller: ConnectController): SessionStore = sessionByController.getValue(controller)

    @Test
    fun not_ready_routes_the_gate_to_setup_and_pins_the_chosen_backend() = runTest {
        val controller = controller(FakeSystemApi(ready = false))
        controller.onBaseUrlChange("http://localhost:5080")

        controller.connect()

        val session: SessionStore = sessionOf(controller)
        // The gate is on NeedsSetup ⇒ the App renders the Setup wizard, not the Twitch OAuth.
        assertEquals(SessionPhase.NeedsSetup, session.phase.value)
        // The chosen backend is pinned so the wizard's anonymous setup calls reach it.
        assertEquals("http://localhost:5080", session.baseUrl())
        // No tokens yet — the session isn't established until the wizard finishes the streamer OAuth.
        assertEquals(null, session.accessToken())
        // The connect screen is back to idle (it handed off to the wizard, no error).
        assertEquals(ConnectStatus.Idle, controller.status.value)
    }

    @Test
    fun an_unreachable_backend_rolls_back_to_connect_with_an_error() = runTest {
        val controller = controller(FakeSystemApi(statusError = ApiError(0, "NETWORK", "Connection refused.")))
        controller.onBaseUrlChange("http://localhost:5080")

        controller.connect()

        val session: SessionStore = sessionOf(controller)
        // Couldn't read status ⇒ the gate is back on Connect, profile cleared, with the failure surfaced.
        assertEquals(SessionPhase.NotConnected, session.phase.value)
        assertEquals(null, session.baseUrl())
        val status: ConnectStatus = controller.status.value
        assertEquals(true, status is ConnectStatus.Error)
    }

    @Test
    fun exposes_the_lan_discovered_list_and_drives_its_start_stop_lifecycle() = runTest {
        val discovery = FakeLanDiscovery()
        val controller = controller(FakeSystemApi(ready = true), discovery)

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
    fun connect_to_a_discovered_backend_runs_the_same_onboarding_and_routes_to_setup() = runTest {
        // A discovered bot that isn't configured yet must route to Setup EXACTLY like the typed path —
        // proving connectTo reuses beginOnboarding (probe → setup-or-OAuth), not a separate flow.
        val controller = controller(FakeSystemApi(ready = false))
        val bot =
            ConnectionProfile(
                id = "eagle-id",
                displayName = "EAGLE",
                baseUrl = "http://192.168.1.42:5080",
                source = ProfileSource.Discovered,
            )

        controller.connectTo(bot)

        val session: SessionStore = sessionOf(controller)
        // Same routing as the typed not-ready path: gate on NeedsSetup, backend pinned, no tokens, idle.
        assertEquals(SessionPhase.NeedsSetup, session.phase.value)
        assertEquals("http://192.168.1.42:5080", session.baseUrl())
        // The pinned profile is the DISCOVERED one (its id/baseUrl), not a freshly minted manual profile.
        assertEquals(bot, session.activeProfile.value)
        assertEquals(null, session.accessToken())
        assertEquals(ConnectStatus.Idle, controller.status.value)
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
                            platformBot = SystemCheck(ready, if (ready) "connected" else "disconnected"),
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

    override suspend fun saveSpotifyCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun saveYouTubeCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun saveDiscordCredentials(clientId: String, clientSecret: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun botOAuthUrl(): ApiResult<BotOAuthUrl> = ApiResult.Ok(BotOAuthUrl("https://id.twitch.tv/authorize?bot"))

    override suspend fun botStatus(): ApiResult<BotStatus> = ApiResult.Ok(BotStatus(connected = ready))

    override suspend fun completeSetup(): ApiResult<Unit> = ApiResult.Ok(Unit)
}

private class FakeVault : SessionTokenStore {
    override suspend fun read(profileId: String): SessionTokens? = null

    override suspend fun write(profileId: String, tokens: SessionTokens) = Unit

    override suspend fun clear(profileId: String) = Unit
}

/** An in-memory [LanDiscovery] so the controller tests drive the discovered feed without any mDNS. */
private class FakeLanDiscovery : LanDiscovery {
    private val _discovered: MutableStateFlow<List<ConnectionProfile>> = MutableStateFlow(emptyList())
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
