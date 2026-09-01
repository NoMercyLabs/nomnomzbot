// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.feedback

import androidx.compose.animation.AnimatedVisibility
import androidx.compose.animation.fadeIn
import androidx.compose.animation.fadeOut
import androidx.compose.animation.slideInVertically
import androidx.compose.animation.slideOutVertically
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.padding
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import bot.nomnomz.dashboard.core.designsystem.component.Toast
import bot.nomnomz.dashboard.core.designsystem.component.ToastVariant
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import kotlinx.coroutines.delay
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_dismiss
import org.jetbrains.compose.resources.stringResource

// The single feedback host, rendered ONCE in the app frame (over the shell content). It collects the
// process-wide [FeedbackController] stream and surfaces the latest outcome as a top-of-frame banner that
// floats above whatever page is mounted — so a "Saved" / "Couldn't connect" message is visible on every
// page and survives a page navigation or a post-OAuth page rebuild (the bus replays the last message to a
// host that re-subscribes after the rebuild).
//
// Dwell (frontend.md feedback): a success/info auto-dismisses after a few seconds; an error stays until the
// user dismisses it, so a failure is never missed. a11y: the banner is an assertive liveRegion, so a screen
// reader announces the outcome the moment it appears, and the dismiss control carries its own label.
//
// This host renders the design-system `Toast` primitive (core/designsystem/component/Toast.kt) —
// modeled on shadcn's Sonner. Colors, width cap, and dp all come from that primitive's tokens.
private const val AUTO_DISMISS_MS: Long = 4_000L

@Composable
fun FeedbackHost(
    controller: FeedbackController,
    modifier: Modifier = Modifier,
) {
    val spacing = LocalSpacing.current

    // The message currently on screen, or null when the host is empty. A new emission replaces the current
    // one (latest-wins), so a fresh outcome always supersedes a lingering banner.
    var current: FeedbackMessage? by remember { mutableStateOf(null) }

    // Subscribe once for the host's lifetime. replay=1 means a message emitted while a page was redirecting
    // (the host briefly absent) is re-delivered here on (re)subscribe — that is the survive-the-redirect
    // requirement. Re-collecting also lands the replayed message, which is correct: after a rebuild the user
    // still needs to see "Connected".
    LaunchedEffect(controller) {
        controller.messages.collect { message -> current = message }
    }

    // Auto-dismiss only the non-error kinds; an error waits for an explicit dismiss. Keyed on the message
    // instance so each new success restarts its own timer and a replaced message cancels the prior timer.
    val shown: FeedbackMessage? = current
    if (shown != null && shown.kind != FeedbackKind.Error) {
        LaunchedEffect(shown) {
            delay(AUTO_DISMISS_MS)
            if (current === shown) current = null
        }
    }

    // Top-right, flowing down from off-screen — the Sonner-style toast corner, not a center-page banner
    // that competes with page content for the same horizontal band.
    Box(modifier = modifier.fillMaxSize(), contentAlignment = Alignment.TopEnd) {
        AnimatedVisibility(
            visible = shown != null,
            enter = slideInVertically { full -> -full } + fadeIn(),
            exit = slideOutVertically { full -> -full } + fadeOut(),
        ) {
            shown?.let { message ->
                FeedbackToast(
                    message = message,
                    onDismiss = { if (current === message) current = null },
                    modifier = Modifier.padding(spacing.s4),
                )
            }
        }
    }
}

@Composable
private fun FeedbackToast(
    message: FeedbackMessage,
    onDismiss: () -> Unit,
    modifier: Modifier = Modifier,
) {
    val variant: ToastVariant =
        when (message.kind) {
            FeedbackKind.Error -> ToastVariant.Destructive
            FeedbackKind.Success, FeedbackKind.Info -> ToastVariant.Default
        }

    // Resolve the localized template + args here (i18n: the bus never carries a rendered string).
    val text: String = stringResource(message.label, *message.formatArgs.toTypedArray())
    val dismissLabel: String = stringResource(Res.string.feedback_dismiss)

    Toast(
        text = text,
        dismissLabel = dismissLabel,
        onDismiss = onDismiss,
        modifier = modifier,
        variant = variant,
    )
}
