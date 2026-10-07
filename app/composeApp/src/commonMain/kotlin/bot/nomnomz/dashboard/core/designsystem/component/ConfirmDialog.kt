// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.designsystem.component

import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.Stable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.testTag
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.network.ApiResult
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.confirm_dialog_failed
import org.jetbrains.compose.resources.stringResource

/** Test tag on the spinner a locked dialog action shows beside its label. */
const val DialogActionProgressTag: String = "dialog-action-progress"

/** The outcome of the work behind a dialog's primary action: [Done] closes the dialog, [Failed] keeps it open. */
sealed interface DialogResult {
    data object Done : DialogResult

    /** [detail] is the human reason (the server's message); null shows the generic "nothing was changed" line. */
    data class Failed(val detail: String? = null) : DialogResult
}

/** Maps a write's [ApiResult] to the dialog outcome, so a caller migration is `action = { api.call().toDialogResult() }`. */
fun ApiResult<*>.toDialogResult(): DialogResult =
    when (this) {
        is ApiResult.Ok -> DialogResult.Done
        is ApiResult.Failure -> DialogResult.Failed(error.message)
    }

/** Maps a controller's success flag to the dialog outcome (the failure reason already went to the feedback toast). */
fun Boolean.toDialogResult(): DialogResult = if (this) DialogResult.Done else DialogResult.Failed()

/**
 * The stay-open contract behind every dialog whose primary action talks to the server (psychology spec X1:
 * a dialog never closes before the result). While [pending] the action is locked and the dialog cannot be
 * dismissed; a [DialogResult.Failed] leaves it open with [failure] to show; [DialogResult.Done] calls [onDone].
 * Used by [ConfirmDialog] and by form dialogs built on [AlertDialog]/[Dialog] (see [DialogActionConfirm]).
 */
@Stable
class DialogActionState(private val scope: CoroutineScope, private val onDone: () -> Unit) {
    var pending: Boolean by mutableStateOf(false)
        private set

    var failure: DialogResult.Failed? by mutableStateOf(null)
        private set

    /** Runs [action] once; a call while one is in flight is ignored. */
    fun run(action: suspend () -> DialogResult) {
        if (pending) return
        pending = true
        failure = null
        scope.launch {
            val result: DialogResult =
                try {
                    action()
                } catch (cancelled: CancellationException) {
                    throw cancelled
                } catch (error: Exception) {
                    DialogResult.Failed(error.message)
                }
            pending = false
            when (result) {
                is DialogResult.Done -> onDone()
                is DialogResult.Failed -> failure = result
            }
        }
    }
}

@Composable
fun rememberDialogActionState(onDone: () -> Unit): DialogActionState {
    val scope: CoroutineScope = rememberCoroutineScope()
    return remember(scope) { DialogActionState(scope, onDone) }
}

/** The inline reason a dialog action failed: the server's words when it sent any, else one generic human line. */
@Composable
fun DialogActionError(failure: DialogResult.Failed, modifier: Modifier = Modifier) {
    InlineError(
        message = failure.detail?.takeIf { it.isNotBlank() } ?: stringResource(Res.string.confirm_dialog_failed),
        modifier = modifier,
    )
}

// The one confirmation dialog for the whole dashboard. Every irreversible action (delete a command, ban a
// viewer, clear a queue, disconnect an integration) routes its confirm step through here, so the wording,
// the destructive-red affirmative, and the cancel affordance are identical everywhere (design-system rule:
// destructive actions MUST confirm). Caller owns the open/closed state; this only renders when shown.
// Built on the DS [AlertDialog] (shadcn Dialog) — no raw Material3.
//
// Two forms. This one is fire-and-forget: it hands control back at once, so the caller closes it before the
// server answers and a failure is lost. Prefer the `action` form below for any server write.
@Composable
fun ConfirmDialog(
    title: String,
    message: String,
    confirmLabel: String,
    dismissLabel: String,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit,
    destructive: Boolean = false,
    // Lets a caller withhold the affirmative while a prerequisite check (e.g. a destructive action's counted
    // blast radius) is still loading, without deadlocking the user if that check fails — the caller decides
    // per-outcome, this dialog just renders the disabled state (S-CONSEQ-b).
    confirmEnabled: Boolean = true,
) {
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(text = title) },
        text = { Text(text = message) },
        confirmButton = { ConfirmButton(confirmLabel, onConfirm, destructive, confirmEnabled, pending = false) },
        dismissButton = { DismissButton(dismissLabel, onDismiss, enabled = true) },
    )
}

/**
 * Stay-open confirmation: [action] runs when the user confirms. While it runs the confirm shows progress, both
 * buttons are locked and the dialog cannot be dismissed; [DialogResult.Failed] keeps it open with the reason
 * inline (the user can retry or cancel); [DialogResult.Done] closes it through [onDismiss].
 */
@Composable
fun ConfirmDialog(
    title: String,
    message: String,
    confirmLabel: String,
    dismissLabel: String,
    onDismiss: () -> Unit,
    action: suspend () -> DialogResult,
    destructive: Boolean = false,
    confirmEnabled: Boolean = true,
) {
    val state: DialogActionState = rememberDialogActionState(onDone = onDismiss)

    AlertDialog(
        onDismissRequest = { if (!state.pending) onDismiss() },
        dismissOnBackPress = !state.pending,
        dismissOnClickOutside = !state.pending,
        title = { Text(text = title) },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(LocalSpacing.current.s2)) {
                Text(text = message)
                state.failure?.let { failure: DialogResult.Failed -> DialogActionError(failure) }
            }
        },
        confirmButton = {
            DialogActionConfirm(
                state = state,
                label = confirmLabel,
                enabled = confirmEnabled,
                action = action,
                destructive = destructive,
            )
        },
        dismissButton = { DialogActionDismiss(state = state, label = dismissLabel, onDismiss = onDismiss) },
    )
}

/**
 * The primary button of a form dialog (built on [AlertDialog]) whose action talks to the server. Shows progress
 * and locks while [state] is pending. Pair it with [DialogActionDismiss] and show [DialogActionState.failure]
 * through [DialogActionError] in the dialog body.
 */
@Composable
fun DialogActionConfirm(
    state: DialogActionState,
    label: String,
    enabled: Boolean,
    action: suspend () -> DialogResult,
    destructive: Boolean = false,
) {
    ConfirmButton(label, { state.run(action) }, destructive, enabled, state.pending)
}

/** The ghost Cancel beside [DialogActionConfirm]; locked while the action is in flight. */
@Composable
fun DialogActionDismiss(state: DialogActionState, label: String, onDismiss: () -> Unit) {
    DismissButton(label, onDismiss, enabled = !state.pending)
}

// One primary per group (sleak): the affirmative is the filled button — red when destructive, the default
// filled variant otherwise — and Cancel beside it is ghost. While [pending] the label stays (so the button does
// not change width) and a spinner leads it; the button is disabled so a second click cannot re-run the action.
@Composable
private fun ConfirmButton(
    label: String,
    onClick: () -> Unit,
    destructive: Boolean,
    enabled: Boolean,
    pending: Boolean,
) {
    Button(
        onClick = onClick,
        enabled = enabled && !pending,
        variant = if (destructive) ButtonVariant.Destructive else ButtonVariant.Default,
        size = ButtonSize.Sm,
        leftIcon =
            if (pending) {
                { Spinner(modifier = Modifier.testTag(DialogActionProgressTag), size = SpinnerSize.Sm) }
            } else {
                null
            },
    ) {
        Text(text = label, maxLines = 1)
    }
}

@Composable
private fun DismissButton(label: String, onDismiss: () -> Unit, enabled: Boolean) {
    val tokens = LocalTokens.current
    TextButton(onClick = onDismiss, enabled = enabled) {
        Text(text = label, color = tokens.mutedForeground, maxLines = 1)
    }
}
