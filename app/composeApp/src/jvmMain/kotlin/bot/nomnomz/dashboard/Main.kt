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
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.snapshotFlow
import androidx.compose.ui.Alignment
import androidx.compose.ui.unit.DpSize
import androidx.compose.ui.unit.dp
import androidx.compose.ui.window.Window
import androidx.compose.ui.window.WindowPlacement
import androidx.compose.ui.window.WindowPosition
import androidx.compose.ui.window.WindowState
import androidx.compose.ui.window.application
import androidx.compose.ui.window.rememberWindowState
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.platform.DesktopLogFile
import bot.nomnomz.dashboard.core.platform.ScreenBounds
import bot.nomnomz.dashboard.core.platform.WindowGeometry
import bot.nomnomz.dashboard.core.platform.WindowStateStore
import java.awt.GraphicsEnvironment
import kotlinx.coroutines.flow.collectLatest

// Default window size — fits the onboarding wizard without overflow (the window dimensions are
// an OS concern, not design-system spacing). Used both as the initial size and as the fallback
// when no persisted [WindowGeometry] exists yet or the persisted one is unusable.
private val DEFAULT_WINDOW_SIZE = DpSize(1320.dp, 920.dp)

// Desktop (jvm) entry point — launches the Compose window. Referenced by
// composeApp/build.gradle.kts `mainClass = "bot.nomnomz.dashboard.MainKt"`.
fun main() = application {
    val log = remember { DesktopLogFile() }
    val windowStateStore =
        remember {
            WindowStateStore(
                defaultGeometry = WindowGeometry(x = -1f, y = -1f, width = 1320f, height = 920f, maximized = false),
            )
        }

    // Restore the persisted window geometry (S111c), validated against the monitors actually
    // connected right now — an external monitor unplugged since the last run must not strand the
    // window off-screen. x/y = -1 (the "no saved position" sentinel from the store's own default)
    // means center it instead of pinning a literal (-1, -1).
    val restored: WindowGeometry =
        remember {
            val screens: List<ScreenBounds> =
                GraphicsEnvironment.getLocalGraphicsEnvironment().screenDevices.map { device ->
                    val bounds = device.defaultConfiguration.bounds
                    ScreenBounds(bounds.x.toFloat(), bounds.y.toFloat(), bounds.width.toFloat(), bounds.height.toFloat())
                }
            windowStateStore.loadSanitized(screens)
        }

    val windowState: WindowState =
        rememberWindowState(
            position =
                if (restored.x < 0f && restored.y < 0f) {
                    WindowPosition(Alignment.Center)
                } else {
                    WindowPosition(restored.x.dp, restored.y.dp)
                },
            size = if (restored.width > 0f && restored.height > 0f) DpSize(restored.width.dp, restored.height.dp) else DEFAULT_WINDOW_SIZE,
            placement = if (restored.maximized) WindowPlacement.Maximized else WindowPlacement.Floating,
        )

    Window(
        onCloseRequest = {
            windowStateStore.save(
                WindowGeometry(
                    x = windowState.position.x.value,
                    y = windowState.position.y.value,
                    width = windowState.size.width.value,
                    height = windowState.size.height.value,
                    maximized = windowState.placement == WindowPlacement.Maximized,
                ),
            )
            log.append("shell", "window closed")
            exitApplication()
        },
        state = windowState,
        title = "NomNomzBot",
    ) {
        // Raise the window so a launcher-started app isn't left behind the active window. Deliberately ONLY
        // toFront() — calling window.requestFocus()/isAlwaysOnTop here manipulates the AWT focus owner and left
        // Compose's text fields unable to receive keyboard input (a regression), while toFront() alone is enough
        // to surface the window.
        LaunchedEffect(Unit) {
            window.toFront()
            log.append("shell", "window opened")
        }

        // Persist geometry as the operator moves/resizes/(un)maximizes the window, not only on close — a
        // crash or force-quit must not lose the last few seconds of window arrangement.
        val scope = rememberCoroutineScope()
        LaunchedEffect(windowState) {
            snapshotFlow { Triple(windowState.position, windowState.size, windowState.placement) }
                .collectLatest { (position, size, placement) ->
                    if (position !is WindowPosition.Absolute) return@collectLatest
                    windowStateStore.save(
                        WindowGeometry(
                            x = position.x.value,
                            y = position.y.value,
                            width = size.width.value,
                            height = size.height.value,
                            maximized = placement == WindowPlacement.Maximized,
                        ),
                    )
                }
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
