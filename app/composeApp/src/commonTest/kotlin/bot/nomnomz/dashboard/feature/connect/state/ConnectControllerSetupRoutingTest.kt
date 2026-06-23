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

import bot.nomnomz.dashboard.core.connection.OAuthLauncher
import bot.nomnomz.dashboard.core.connection.SessionPhase
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.SessionTokenStore
import bot.nomnomz.dashboard.core.connection.SessionTokens
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

    private fun controller(systemApi: SystemApi): ConnectController {
        val session = SessionStore(FakeVault())
        val client = ApiClient(baseUrlProvider = session::baseUrl, tokenProvider = session::accessToken)
        return ConnectController(
            sessionStore = session,
            authApi = AuthApi(client),
            systemApi = systemApi,
            oauthLauncher = OAuthLauncher(),
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
