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

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.awt.ComposeWindow
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.ButtonVariant
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.designsystem.theme.Scheme
import bot.nomnomz.dashboard.core.designsystem.theme.Spacing
import bot.nomnomz.dashboard.core.designsystem.theme.Tokens
import bot.nomnomz.dashboard.core.designsystem.theme.Typography
import java.awt.Desktop
import java.awt.Dimension
import java.awt.event.WindowAdapter
import java.awt.event.WindowEvent
import java.net.URI
import javax.swing.WindowConstants
import kotlin.coroutines.resume
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.suspendCancellableCoroutine
import kotlinx.coroutines.swing.Swing
import kotlinx.coroutines.withContext
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_bot_title
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_close
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_get_webview2
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_no_bot
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_page_failed
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_system
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_title
import nomnomzbot.composeapp.generated.resources.code_editor_unavailable_webview2
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.getString
import org.jetbrains.compose.resources.stringResource

/** Why the desktop cannot open the code editor. There is deliberately no lesser substitute editor. */
sealed interface EditorUnavailableReason {
    /** Windows without the Microsoft Edge WebView2 Runtime — fixed by installing the Evergreen runtime. */
    data object MissingWebView2Runtime : EditorUnavailableReason

    /** The system web view could not start here (e.g. Linux without WebKitGTK). */
    data class SystemWebViewFailed(val detail: String) : EditorUnavailableReason

    /** No bot connection, so there is no editor page to load. */
    data object NoBotConnected : EditorUnavailableReason

    /** The web view started but the editor page never announced itself. */
    data class PageDidNotLoad(val pageUrl: String) : EditorUnavailableReason
}

// A small design-system window that says why editing cannot start and the one step that fixes it. Suspends
// until the operator closes it, so the caller's editAndCompile contract ("returns when the editor closes") holds.
internal object EditorUnavailableWindow {
    // Window geometry in AWT pixels — sized to the message, not a design token.
    private val windowSize: Dimension = Dimension(600, 340)

    suspend fun show(reason: EditorUnavailableReason) {
        val windowTitle: String = getString(reasonTitle(reason))
        val window: ComposeWindow = withContext(Dispatchers.Swing) { ComposeWindow().apply { title = windowTitle } }
        try {
            withContext(Dispatchers.Swing) {
                suspendCancellableCoroutine { continuation ->
                    window.defaultCloseOperation = WindowConstants.DISPOSE_ON_CLOSE
                    window.addWindowListener(
                        object : WindowAdapter() {
                            override fun windowClosed(event: WindowEvent?) {
                                if (continuation.isActive) continuation.resume(Unit)
                            }
                        }
                    )
                    window.setContent {
                        NomNomzTheme(scheme = Scheme.Dark) {
                            EditorUnavailableScreen(
                                reason = reason,
                                onOpenFix = { url -> runCatching { Desktop.getDesktop().browse(URI(url)) } },
                                onClose = { window.dispose() },
                            )
                        }
                    }
                    window.size = windowSize
                    window.setLocationRelativeTo(null)
                    window.isVisible = true
                }
            }
        } finally {
            withContext(Dispatchers.Swing) { if (window.isDisplayable) window.dispose() }
        }
    }
}

// One group, one primary action: installing the runtime when that is the fix, otherwise closing. Close is
// the quiet sibling whenever a fix action takes the primary slot.
@Composable
internal fun EditorUnavailableScreen(
    reason: EditorUnavailableReason,
    onOpenFix: (String) -> Unit,
    onClose: () -> Unit,
) {
    val tokens: Tokens = LocalTokens.current
    val spacing: Spacing = LocalSpacing.current
    val typography: Typography = LocalTypography.current
    val fixUrl: String? =
        if (reason == EditorUnavailableReason.MissingWebView2Runtime) NativeWebViewProbe.WEBVIEW2_DOWNLOAD_URL else null

    Column(
        modifier = Modifier.fillMaxSize().background(tokens.background).padding(spacing.s8),
        verticalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Text(
            text = stringResource(reasonTitle(reason)),
            style = typography.xl2,
            color = tokens.foreground,
        )
        Text(text = reasonMessage(reason), style = typography.sm, color = tokens.mutedForeground)
        Spacer(Modifier.weight(1f))
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2, Alignment.End),
        ) {
            if (fixUrl != null) {
                Button(onClick = onClose, variant = ButtonVariant.Ghost) {
                    Text(stringResource(Res.string.code_editor_unavailable_close))
                }
                Button(onClick = { onOpenFix(fixUrl) }) {
                    Text(stringResource(Res.string.code_editor_unavailable_get_webview2))
                }
            } else {
                Button(onClick = onClose) { Text(stringResource(Res.string.code_editor_unavailable_close)) }
            }
        }
    }
}

// The web view reasons are about this computer; the bot reasons are about the connection.
private fun reasonTitle(reason: EditorUnavailableReason): StringResource =
    when (reason) {
        EditorUnavailableReason.MissingWebView2Runtime,
        is EditorUnavailableReason.SystemWebViewFailed -> Res.string.code_editor_unavailable_title
        EditorUnavailableReason.NoBotConnected,
        is EditorUnavailableReason.PageDidNotLoad -> Res.string.code_editor_unavailable_bot_title
    }

@Composable
private fun reasonMessage(reason: EditorUnavailableReason): String =
    when (reason) {
        EditorUnavailableReason.MissingWebView2Runtime -> stringResource(Res.string.code_editor_unavailable_webview2)
        is EditorUnavailableReason.SystemWebViewFailed ->
            stringResource(Res.string.code_editor_unavailable_system, reason.detail)
        EditorUnavailableReason.NoBotConnected -> stringResource(Res.string.code_editor_unavailable_no_bot)
        is EditorUnavailableReason.PageDidNotLoad ->
            stringResource(Res.string.code_editor_unavailable_page_failed, reason.pageUrl)
    }
