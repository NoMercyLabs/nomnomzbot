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

import bot.nomnomz.dashboard.core.connection.OAuthLauncher
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.network.ApiClient
import bot.nomnomz.dashboard.core.network.AuthApi
import bot.nomnomz.dashboard.feature.connect.state.ConnectController

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

    private val oauthLauncher: OAuthLauncher = OAuthLauncher()

    val connectController: ConnectController =
        ConnectController(
            sessionStore = sessionStore,
            authApi = authApi,
            oauthLauncher = oauthLauncher,
        )
}
