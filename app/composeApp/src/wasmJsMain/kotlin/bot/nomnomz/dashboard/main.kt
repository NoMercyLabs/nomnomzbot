// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard

import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.window.ComposeViewport
import bot.nomnomz.dashboard.core.connection.ConnectReturn
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.readReturnedConnect
import bot.nomnomz.dashboard.core.connection.readReturnedSession
import bot.nomnomz.dashboard.core.di.AppGraph
import kotlinx.browser.document
import kotlinx.browser.window
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

// Web (wasmJs) entry point. Single-origin (frontend.md §6): the served origin IS the backend, so the
// profile is synthesized from window.location.origin. On a post-OAuth redirect the backend returns
// the session in the URL fragment; readReturnedSession() picks it up and completeWithSession() runs
// the same establish→/me path the desktop flow uses, so the gate lands on the shell.
@OptIn(ExperimentalComposeUiApi::class)
fun main() {
    val graph = AppGraph()

    val returned: SessionTokens? = readReturnedSession()
    if (returned != null) {
        val origin: String = window.location.origin
        val profile =
            ConnectionProfile(
                id = "served-origin",
                displayName = origin,
                baseUrl = origin,
                source = ProfileSource.ServedOrigin,
            )
        CoroutineScope(Dispatchers.Main).launch {
            graph.connectController.completeWithSession(profile, returned)
        }
    } else {
        // A bot/integration connect that completed on web redirects back here with a marker query. The
        // session was already restored from the vault, so the shell is shown; consume the marker (it
        // strips it from the address bar) — the integrations screen re-reads the authoritative status
        // when it mounts. The full nav slice will route straight to the integrations section.
        val connect: ConnectReturn? = readReturnedConnect()
        if (connect != null) {
            CoroutineScope(Dispatchers.Main).launch { graph.integrationsController.refresh() }
        }
    }

    ComposeViewport(document.body!!) {
        App(graph = graph)
    }
}
