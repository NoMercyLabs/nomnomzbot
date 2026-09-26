// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.editor

import androidx.compose.runtime.Composable
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.runComposeUiTest
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import kotlin.test.Test
import kotlin.test.assertEquals

// The screen shown instead of an editor when the system web view cannot run: it must name the fix and make
// that fix the one primary action, and it must never offer a way to edit.
@OptIn(ExperimentalTestApi::class)
class EditorUnavailableScreenTest {
    @Composable
    private fun Pinned(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") { NomNomzTheme { content() } }
    }

    @Test
    fun missingRuntimeOffersTheEvergreenInstallAndClose() = runComposeUiTest {
        val opened: MutableList<String> = mutableListOf()
        var closed = 0
        setContent {
            Pinned {
                EditorUnavailableScreen(
                    reason = EditorUnavailableReason.MissingWebView2Runtime,
                    onOpenFix = { url -> opened += url },
                    onClose = { closed++ },
                )
            }
        }

        onNodeWithText("Editing needs the system web view").assertExists()
        onNodeWithText("Install the Evergreen WebView2 Runtime", substring = true).assertExists()
        onNodeWithText("Get WebView2 Runtime").performClick()
        assertEquals(listOf("https://developer.microsoft.com/microsoft-edge/webview2/"), opened)
        assertEquals(0, closed)

        onNodeWithText("Close").performClick()
        assertEquals(1, closed)
    }

    @Test
    fun withoutAnInstallableFixCloseIsTheOnlyAction() = runComposeUiTest {
        var closed = 0
        setContent {
            Pinned {
                EditorUnavailableScreen(
                    reason = EditorUnavailableReason.SystemWebViewFailed("libwebkit2gtk-4.1.so.0 not found"),
                    onOpenFix = { error("no fix action should exist for this reason") },
                    onClose = { closed++ },
                )
            }
        }

        onNodeWithText("libwebkit2gtk-4.1.so.0 not found", substring = true).assertExists()
        onNodeWithText("install WebKitGTK", substring = true).assertExists()
        onAllNodesWithText("Get WebView2 Runtime").assertCountEquals(0)
        onNodeWithText("Close").performClick()
        assertEquals(1, closed)
    }

    @Test
    fun pageThatNeverLoadedNamesTheAddressItTried() = runComposeUiTest {
        setContent {
            Pinned {
                EditorUnavailableScreen(
                    reason = EditorUnavailableReason.PageDidNotLoad("http://192.168.2.10:5080/editor/index.html"),
                    onOpenFix = {},
                    onClose = {},
                )
            }
        }

        onNodeWithText("The code editor could not load").assertExists()
        onNodeWithText("http://192.168.2.10:5080/editor/index.html", substring = true).assertExists()
    }
}
