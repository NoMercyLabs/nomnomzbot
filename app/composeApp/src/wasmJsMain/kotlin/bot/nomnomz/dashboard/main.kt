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

import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.window.ComposeViewport
import bot.nomnomz.dashboard.core.connection.ConnectReturn
import bot.nomnomz.dashboard.core.connection.ConnectionProfile
import bot.nomnomz.dashboard.core.connection.ProfileSource
import bot.nomnomz.dashboard.core.connection.SessionTokens
import bot.nomnomz.dashboard.core.connection.readReturnedConnect
import bot.nomnomz.dashboard.core.connection.readReturnedSession
import bot.nomnomz.dashboard.core.di.AppGraph
import bot.nomnomz.dashboard.core.feedback.connectReturnFeedback
import kotlinx.browser.document
import kotlinx.browser.window
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
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
            // The flow returned to the served origin in the shell — surface the outcome on the app frame so
            // the user sees "Connected" / "Couldn't connect: …" instead of a silent return. The bus replays
            // it to the FeedbackHost once the shell rebuilds after this redirect.
            graph.feedbackController.emit(connectReturnFeedback(connect.connected, connect.errorCode))
            CoroutineScope(Dispatchers.Main).launch { graph.integrationsController.refresh() }
        }
    }

    ComposeViewport(document.body!!) {
        App(graph = graph)
        // Tear down the HTML boot overlay once Compose has rendered a frame. Placed AFTER App so that if App
        // throws during composition (e.g. a string-resource bundle that 502'd mid-load), this effect never
        // commits — the overlay stays up and offers a reload instead of leaving a frozen page (see index.html).
        // The first beat tears the overlay down; the beats after it keep the boot watchdog quiet. An uncaught
        // exception in any effect kills the recomposer and cancels this loop with it, so the watchdog sees the
        // beats stop and offers a reload instead of a dashboard frozen on its last frame.
        LaunchedEffect(Unit) {
            while (true) {
                reportAppAlive()
                delay(HEARTBEAT_INTERVAL_MS)
            }
        }
    }
}

// Must stay well below the watchdog's stale threshold in nnz-boot.js.
private const val HEARTBEAT_INTERVAL_MS: Long = 1_000

// Tells the nnz-boot.js overlay the app is alive and rendering. Guarded on the JS side in case the overlay
// script did not load.
private fun reportAppAlive(): Unit = js("{ if (window.__nnzAppAlive) window.__nnzAppAlive(); }")
