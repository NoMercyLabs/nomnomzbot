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
import bot.nomnomz.dashboard.core.di.AppGraph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.designsystem.theme.Scheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.navigation.Destination
import bot.nomnomz.dashboard.feature.connect.ui.ConnectScreen
import bot.nomnomz.dashboard.feature.language.state.AppLanguage
import bot.nomnomz.dashboard.feature.language.ui.LanguagePicker
import bot.nomnomz.dashboard.feature.setup.ui.SetupWizardScreen
import bot.nomnomz.dashboard.feature.shell.ui.ShellScreen
import bot.nomnomz.dashboard.feature.splash.ui.SplashScreen
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
    // locale tag and wrap the whole app in AppEnvironment, so every `stringResource` re-renders live in
    // the chosen language regardless of the OS/browser locale. `null` (System default) follows the
    // platform locale.
    val language: AppLanguage by graph.languageController.current.collectAsStateWithLifecycle()

    AppEnvironment(tag = language.tag) {
        NomNomzTheme(scheme = Scheme.Dark) {
            val phase: SessionPhase by graph.sessionStore.phase.collectAsStateWithLifecycle()
            val spacing = LocalSpacing.current
            val scope = rememberCoroutineScope()

            var booting: Boolean by remember { mutableStateOf(true) }
            LaunchedEffect(Unit) {
                delay(SPLASH_HOLD_MS)
                booting = false
            }

            val destination: Destination = when {
                booting -> Destination.Splash
                phase == SessionPhase.Connected -> Destination.Shell
                phase == SessionPhase.NeedsSetup -> Destination.Setup
                else -> Destination.Connect
            }

            // Stack the destination under a persistent top-end language picker — one placement that's
            // reachable across the whole app: during onboarding (splash/connect/setup, so a Dutch-system
            // streamer can pin English before signing in) AND in the authenticated shell.
            Box(modifier = Modifier.fillMaxSize()) {
                Crossfade(targetState = destination) { target ->
                    when (target) {
                        Destination.Splash -> SplashScreen()
                        Destination.Connect -> ConnectScreen(controller = graph.connectController)
                        Destination.Setup -> SetupWizardScreen(controller = graph.setupController)
                        Destination.Shell ->
                            ShellScreen(
                                integrationsController = graph.integrationsController,
                                onDisconnect = { scope.launch { graph.sessionStore.disconnect() } },
                            )
                    }
                }

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
