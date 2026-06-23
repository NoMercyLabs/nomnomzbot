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
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.connection.SessionPhase
import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.designsystem.theme.Scheme
import bot.nomnomz.dashboard.core.navigation.Destination
import bot.nomnomz.dashboard.feature.connect.ui.ConnectScreen
import bot.nomnomz.dashboard.feature.shell.ui.ShellScreen
import bot.nomnomz.dashboard.feature.splash.ui.SplashScreen
import kotlinx.coroutines.delay

private const val SPLASH_HOLD_MS: Long = 1_200L

// Root composable: theme + connection gate (frontend.md §5). The gate resolves the active
// Destination from a one-shot boot splash and the session phase:
//   Splash (booting) -> Connect (no session) -> Shell (session established).
//
// Next slice swaps this state-driven gate for the Navigation Compose NavHost + the
// Setup-wizard rung, and injects the SessionStore via Koin instead of remember.
@Composable
fun App() {
    NomNomzTheme(scheme = Scheme.Dark) {
        val sessionStore: SessionStore = remember { SessionStore() }
        val phase: SessionPhase by sessionStore.phase.collectAsStateWithLifecycle()

        var booting: Boolean by remember { mutableStateOf(true) }
        LaunchedEffect(Unit) {
            delay(SPLASH_HOLD_MS)
            booting = false
        }

        val destination: Destination = when {
            booting -> Destination.Splash
            phase == SessionPhase.Connected -> Destination.Shell
            else -> Destination.Connect
        }

        Crossfade(targetState = destination) { target ->
            when (target) {
                Destination.Splash -> SplashScreen()
                Destination.Connect -> ConnectScreen(onConnect = sessionStore::connect)
                Destination.Shell -> ShellScreen(onDisconnect = sessionStore::disconnect)
            }
        }
    }
}
