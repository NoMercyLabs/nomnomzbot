// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.di

import bot.nomnomz.dashboard.core.connection.ConnectLauncher
import bot.nomnomz.dashboard.core.connection.LanDiscovery
import bot.nomnomz.dashboard.core.connection.OAuthConnectLauncher
import bot.nomnomz.dashboard.core.connection.OAuthLauncher
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.connection.lanDiscovery
import bot.nomnomz.dashboard.core.network.ApiClient
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.core.network.BotAuthApi
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.IntegrationsApi
import bot.nomnomz.dashboard.core.network.RestBotAuthApi
import bot.nomnomz.dashboard.core.network.RestChannelsApi
import bot.nomnomz.dashboard.core.network.RestIntegrationsApi
import bot.nomnomz.dashboard.core.network.RestSystemApi
import bot.nomnomz.dashboard.core.network.SystemApi
import bot.nomnomz.dashboard.feature.connect.state.ConnectController
import bot.nomnomz.dashboard.feature.integrations.state.IntegrationsController
import bot.nomnomz.dashboard.feature.setup.state.SetupController

// The composition root for this slice — one instance of each engine singleton (frontend-structure.md
// F7: one HttpClient, one ConnectionStore), wired by explicit constructor injection. Koin replaces
// this hand graph 1:1 in the DI slice (frontend.md §11); the wiring shape is identical, so features
// don't change. App.kt holds one AppGraph for the app lifetime.
class AppGraph {
    val sessionStore: SessionStore = SessionStore()

    // The single shared client reads base URL + token from the session on every request, so a
    // sign-in / connection switch re-targets the live client (frontend.md §3.1).
    val apiClient: ApiClient =
        ApiClient(
            baseUrlProvider = sessionStore::baseUrl,
            tokenProvider = sessionStore::accessToken,
        )

    val authApi: AuthApi = AuthApi(apiClient)
    val channelsApi: ChannelsApi = RestChannelsApi(apiClient)
    val botAuthApi: BotAuthApi = RestBotAuthApi(apiClient)
    val integrationsApi: IntegrationsApi = RestIntegrationsApi(apiClient)
    val systemApi: SystemApi = RestSystemApi(apiClient)

    private val oauthLauncher: OAuthLauncher = OAuthLauncher()
    private val connectLauncher: ConnectLauncher = OAuthConnectLauncher(oauthLauncher)

    // The per-target mDNS browse seam — jmDNS on desktop, a no-op on web (single-origin). Built via the
    // platform [lanDiscovery] factory like the other per-target engines (OAuthLauncher / TokenVault) and
    // handed to the Connect controller, which owns its start/stop lifecycle.
    private val lanDiscovery: LanDiscovery = lanDiscovery()

    val connectController: ConnectController =
        ConnectController(
            sessionStore = sessionStore,
            authApi = authApi,
            systemApi = systemApi,
            oauthLauncher = oauthLauncher,
            lanDiscovery = lanDiscovery,
        )

    // The first-run setup wizard's holder. On "continue to sign-in" it hands back to the connect
    // controller's streamer OAuth (signInStreamer), which establishes the session and advances the gate.
    val setupController: SetupController =
        SetupController(
            systemApi = systemApi,
            connectLauncher = connectLauncher,
            onReadyToSignIn = connectController::signInStreamer,
        )

    val integrationsController: IntegrationsController =
        IntegrationsController(
            sessionStore = sessionStore,
            channelsApi = channelsApi,
            botAuthApi = botAuthApi,
            integrationsApi = integrationsApi,
            connectLauncher = connectLauncher,
        )
}
