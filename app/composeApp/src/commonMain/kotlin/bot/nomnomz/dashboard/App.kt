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

import androidx.compose.animation.Crossfade
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.connection.SessionPhase
import bot.nomnomz.dashboard.core.connection.SessionUser
import bot.nomnomz.dashboard.core.di.AppGraph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.designsystem.theme.Scheme
import bot.nomnomz.dashboard.core.feedback.FeedbackHost
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.navigation.Destination
import bot.nomnomz.dashboard.core.navigation.RouteStore
import bot.nomnomz.dashboard.feature.connect.ui.ConnectScreen
import bot.nomnomz.dashboard.feature.language.state.AppLanguage
import bot.nomnomz.dashboard.feature.language.ui.LanguagePicker
import bot.nomnomz.dashboard.feature.setup.ui.SetupWizardScreen
import bot.nomnomz.dashboard.feature.shell.state.ShellAccess
import bot.nomnomz.dashboard.feature.shell.ui.ShellScreen
import bot.nomnomz.dashboard.feature.splash.ui.SplashScreen
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch

private const val SPLASH_HOLD_MS: Long = 1_200L

// Root composable: theme + connection gate (frontend.md §5). The gate resolves the active
// Destination from a one-shot boot splash and the session phase:
//   Splash (booting) -> Connect (no session) -> Shell (session established).
//
// The Connect screen drives the REAL Twitch streamer onboarding through the injected AppGraph
// (ConnectController → status probe → OAuthLauncher → SessionStore → AuthApi.me). A fresh self-host bot
// has no Twitch app credentials yet, so the probe routes the gate to the first-run Setup wizard
// (NeedsSetup → Setup) where the user enters every secret through proper inputs — never a config file —
// before the streamer OAuth runs. Next slice swaps this state-driven gate for the Navigation Compose
// NavHost and injects the graph via Koin instead of remember.
@Composable
fun App(graph: AppGraph = remember { AppGraph() }) {
    // The display-language override (frontend.md i18n) — resolve the operator's chosen language to a
    // locale tag and wrap ONLY the string-reading UI in AppEnvironment, so every `stringResource`
    // re-renders live in the chosen language regardless of the OS/browser locale. `null` (System default)
    // follows the platform locale.
    val language: AppLanguage by graph.languageController.current.collectAsStateWithLifecycle()

    // The theme, the boot/session gate, and the resolved Destination read NO `stringResource`, so they
    // live OUTSIDE AppEnvironment. A language switch then disposes only the keyed string-reading content
    // below — not the theme tokens, the splash-hold state, or the session phase — which avoids rebuilding
    // (and GC-churning) the whole subtree on every locale flip while still re-rendering every visible
    // string live. `LocalSpacing` (from NomNomzTheme) stays in scope for the content beneath it.
    NomNomzTheme(scheme = Scheme.Dark) {
        val phase: SessionPhase by graph.sessionStore.phase.collectAsStateWithLifecycle()
        val spacing = LocalSpacing.current
        val scope = rememberCoroutineScope()
        val routeStore: RouteStore = remember { RouteStore() }

        var booting: Boolean by remember { mutableStateOf(true) }
        LaunchedEffect(Unit) {
            // Restore a remembered session (frontend.md §6) under the splash, so a returning operator lands
            // straight on the shell instead of re-running the device-code login. Run it concurrently with the
            // splash hold and join before lifting the gate, so the destination resolves once (no Connect→Shell
            // flash). Skip when a session is already in flight (e.g. the web post-OAuth redirect arm in main()).
            val restore: Job? =
                if (graph.sessionStore.phase.value == SessionPhase.NotConnected) {
                    launch { graph.connectController.restoreSession() }
                } else {
                    null
                }
            delay(SPLASH_HOLD_MS)
            restore?.join()
            booting = false
        }

        val destination: Destination = when {
            booting -> Destination.Splash
            phase == SessionPhase.Connected -> Destination.Shell
            phase == SessionPhase.NeedsSetup -> Destination.Setup
            else -> Destination.Connect
        }

        // When the user actively signs in (Connect → Shell), push a `#/` history entry BEFORE the shell
        // writes its first page route. This gives the browser Back button a meaningful "connect screen"
        // entry to return to, so the operator can press Back from any shell page and land on the sign-in
        // state instead of leaving the app entirely. Skipped on session-restore (the gate goes
        // Splash → Shell without the operator ever seeing Connect — adding a history entry there would
        // make Back misleadingly appear to "go back to the sign-in screen" from a session the app silently
        // restored, which is confusing). We track whether the operator came through Connect explicitly
        // with [enteredViaConnect].
        var enteredViaConnect: Boolean by remember { mutableStateOf(false) }
        LaunchedEffect(destination) {
            when (destination) {
                Destination.Connect, Destination.Setup -> enteredViaConnect = true
                Destination.Shell -> if (enteredViaConnect) routeStore.pushConnectEntry()
                else -> Unit
            }
        }

        // When in the shell, collect disconnect signals from the route store (web Back pressing the `#/` entry
        // we pushed on connect) and sign the operator out — returning them to the Connect screen.
        LaunchedEffect(destination) {
            if (destination == Destination.Shell) {
                routeStore.disconnectRequests.collect { graph.sessionStore.disconnect() }
            }
        }

        AppEnvironment(tag = language.tag) {
            // Stack the destination under a persistent top-end language picker — one placement that's
            // reachable across the whole app: during onboarding (splash/connect/setup, so a Dutch-system
            // streamer can pin English before signing in) AND in the authenticated shell.
            Box(modifier = Modifier.fillMaxSize()) {
                Crossfade(targetState = destination) { target ->
                    when (target) {
                        Destination.Splash -> SplashScreen()
                        Destination.Connect -> ConnectScreen(controller = graph.connectController)
                        Destination.Setup -> SetupWizardScreen(controller = graph.setupController)
                        Destination.Shell -> {
                            val user: SessionUser? by
                                graph.sessionStore.user.collectAsStateWithLifecycle()
                            // Resolve the caller's REAL Plane-B role from the backend (/effective/me) once per
                            // shell entry, then gate the sidebar + write affordances off it (frontend-ia.md §7).
                            // This replaces the old `role = Broadcaster` hardcode: a delegated Mod/Editor sees
                            // only their surface, and a viewer (null role) gets the participation-only surface.
                            val access: ShellAccess by
                                graph.shellAccessController.state.collectAsStateWithLifecycle()
                            LaunchedEffect(Unit) { graph.shellAccessController.load() }
                            when (val resolved: ShellAccess = access) {
                                // Hold the splash under the one-shot role probe so the shell never flashes the
                                // wrong (over-granted) surface before the real role lands.
                                ShellAccess.Loading -> SplashScreen()
                                is ShellAccess.Resolved ->
                                    ShellScreen(
                                        graph = graph,
                                        languageController = graph.languageController,
                                        routeStore = routeStore,
                                        user = user,
                                        access = resolved,
                                        onLogout = { scope.launch { graph.sessionStore.disconnect() } },
                                    )
                            }
                        }
                    }
                }

                // The single app-frame feedback host: one instance over the whole frame, so success/error
                // messages persist across page navigation and show on every page (the app frame hosts the message).
                FeedbackHost(controller = graph.feedbackController)

                // The picker is a global top-end affordance ONLY during onboarding (splash/connect/setup),
                // so a Dutch-system streamer can pin English before signing in. In the shell it lives in the
                // profile menu (frontend-ia.md §4), so it must not overlay there — that caused the top-bar
                // collision with the old Disconnect button.
                if (destination != Destination.Shell) {
                    LanguagePicker(
                        controller = graph.languageController,
                        modifier = Modifier
                            .align(Alignment.TopEnd)
                            .padding(spacing.s2),
                    )
                }
            }
        }
    }
}
