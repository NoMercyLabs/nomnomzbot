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
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.application
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme

// Desktop (jvm) entry point — launches the Compose window. Referenced by
// composeApp/build.gradle.kts `mainClass = "bot.nomnomz.dashboard.MainKt"`.
fun main() = application {
    Window(
        onCloseRequest = ::exitApplication,
        title = "NomNomzBot",
    ) {
        // A Surface painted with the theme background so the window has no white flash
        // before App()'s own backgrounds draw.
        NomNomzTheme {
            Surface(color = LocalTokens.current.background) {
                App()
            }
        }
    }
}
