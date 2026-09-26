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

import androidx.compose.ui.graphics.toArgb
import bot.nomnomz.dashboard.core.designsystem.theme.DarkTokens
import ca.weblite.webview.swing.WebViewComponent
import java.awt.Color
import java.awt.BorderLayout
import java.awt.Dimension
import java.awt.event.WindowAdapter
import java.awt.event.WindowEvent
import javax.swing.JFrame
import javax.swing.SwingUtilities
import javax.swing.WindowConstants
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.NonCancellable
import kotlinx.coroutines.channels.Channel
import kotlinx.coroutines.swing.Swing
import kotlinx.coroutines.withContext
import kotlinx.coroutines.withTimeoutOrNull
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.code_editor_window_title
import org.jetbrains.compose.resources.getString

/** How a native web view editor session ended. */
internal sealed interface WebViewEditorOutcome {
    /** The operator closed the editor. */
    data object Closed : WebViewEditorOutcome

    /** The page never announced itself, so the operator was never shown a working editor. */
    data class PageDidNotLoad(val pageUrl: String) : WebViewEditorOutcome
}

// The desktop editor: the SAME served editor page the web build shows (Monaco with the SDK types, live
// diagnostics, multi-file tree, live preview, fire bar, history and test run), hosted in the operating system's
// own web view — WebView2 on Windows, WKWebView on macOS, WebKitGTK on Linux — inside a normal desktop window.
// Kotlin only drives the bridge; every page message goes through the shared [EditorBridgeSession], so a save
// here reaches the caller's `compile` exactly as it does on the web.
internal object WebViewProjectEditor {
    // How long the page gets to post `ready`. It posts at the end of its own module, before Monaco or any CDN
    // asset is fetched, so a healthy load is well under a second; silence this long means it never loaded.
    private const val READY_TIMEOUT_MS: Long = 20_000

    private val closeMessage: String = """{"type":"${EditorBridgeProtocol.CLOSE}"}"""

    suspend fun editAndCompile(pageUrl: String, title: String, session: (post: (String) -> Unit) -> EditorBridgeSession): WebViewEditorOutcome {
        val inbox: Channel<String> = Channel(Channel.UNLIMITED)
        val windowTitle: String = getString(Res.string.code_editor_window_title, title)
        val window: EditorWebViewWindow = withContext(Dispatchers.Swing) { EditorWebViewWindow.open(pageUrl, windowTitle, inbox) }
        val bridge: EditorBridgeSession = session(window::deliver)
        try {
            val first: EditorInboundMessage =
                withTimeoutOrNull(READY_TIMEOUT_MS) { nextMessage(inbox) } ?: return WebViewEditorOutcome.PageDidNotLoad(pageUrl)
            var open: Boolean = bridge.handle(first)
            while (open) {
                open = bridge.handle(nextMessage(inbox))
            }
            return WebViewEditorOutcome.Closed
        } finally {
            inbox.close()
            withContext(NonCancellable + Dispatchers.Swing) { window.dispose() }
        }
    }

    private suspend fun nextMessage(inbox: Channel<String>): EditorInboundMessage {
        while (true) {
            val raw: String = inbox.receiveCatching().getOrNull() ?: return EditorInboundMessage(EditorBridgeProtocol.CLOSE)
            EditorBridgeProtocol.decode(raw)?.let { message -> return message }
        }
    }

    // The window + web view pair. Built and disposed on the EDT; [deliver] may be called from any thread.
    private class EditorWebViewWindow(private val frame: JFrame, private val webView: WebViewComponent) {
        fun deliver(messageJson: String) {
            val script: String = EditorWebViewBridge.deliverScript(messageJson)
            SwingUtilities.invokeLater { if (frame.isDisplayable) webView.eval(script) }
        }

        fun dispose() {
            webView.dispose()
            if (frame.isDisplayable) frame.dispose()
        }

        companion object {
            fun open(pageUrl: String, windowTitle: String, inbox: Channel<String>): EditorWebViewWindow {
                val webView: WebViewComponent = WebViewComponent.create()
                // Both before navigation: the page posts `ready` as its module finishes, and a bridge installed
                // after that point would miss it.
                webView.addOnBeforeLoad(EditorWebViewBridge.initScript())
                webView.addJavascriptCallback(EditorWebViewBridge.HOST_FUNCTION) { raw: String ->
                    EditorWebViewBridge.unwrapHostCall(raw)?.let { message: String -> inbox.trySend(message) }
                }
                webView.setUrl(pageUrl)

                val frame = JFrame(windowTitle)
                frame.defaultCloseOperation = WindowConstants.DISPOSE_ON_CLOSE
                frame.addWindowListener(
                    object : WindowAdapter() {
                        override fun windowClosing(event: WindowEvent?) {
                            inbox.trySend(closeMessage)
                        }
                    }
                )
                // The dark theme's own background token behind the web view, so the window never flashes a
                // default toolkit colour before the page paints.
                frame.contentPane.background = Color(DarkTokens.background.toArgb(), true)
                frame.contentPane.add(webView, BorderLayout.CENTER)
                frame.size = Dimension(1360, 860)
                frame.setLocationRelativeTo(null)
                frame.isVisible = true
                frame.toFront()
                return EditorWebViewWindow(frame, webView)
            }
        }
    }
}
