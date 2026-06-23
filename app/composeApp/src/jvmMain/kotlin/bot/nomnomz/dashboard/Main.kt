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

import androidx.compose.material3.Surface
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.ui.Alignment
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.WindowPosition
import androidx.compose.ui.window.application
import androidx.compose.ui.window.rememberWindowState
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme

// Desktop (jvm) entry point — launches the Compose window. Referenced by
// composeApp/build.gradle.kts `mainClass = "bot.nomnomz.dashboard.MainKt"`.
fun main() = application {
    // Open centered at a size that fits the onboarding wizard without overflow (the window dimensions are an OS
    // concern, not design-system spacing).
    val windowState =
        rememberWindowState(
            position = WindowPosition(Alignment.Center),
            size = DpSize(1320.dp, 920.dp),
        )

    Window(
        onCloseRequest = ::exitApplication,
        state = windowState,
        title = "NomNomzBot",
    ) {
        // Raise the window so a launcher-started app isn't left behind the active window. Deliberately ONLY
        // toFront() — calling window.requestFocus()/isAlwaysOnTop here manipulates the AWT focus owner and left
        // Compose's text fields unable to receive keyboard input (a regression), while toFront() alone is enough
        // to surface the window.
        LaunchedEffect(Unit) {
            window.toFront()
        }

        // A Surface painted with the theme background so the window has no white flash
        // before App()'s own backgrounds draw.
        NomNomzTheme {
            Surface(color = LocalTokens.current.background) {
                App()
            }
        }
    }
}
