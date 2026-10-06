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

import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch

// How long each kind of message stays on screen. A success/info with an action (Undo) needs time to be
// reached, so it dwells longer; an error never times out so a failure is never missed.
private const val DWELL_MS: Long = 4_000L
private const val DWELL_WITH_ACTION_MS: Long = 10_000L

/** The auto-dismiss delay for [message] in ms, or null when it stays until the user dismisses it. */
fun feedbackDwellMillis(message: FeedbackMessage): Long? =
    when {
        message.kind == FeedbackKind.Error -> null
        message.action != null -> DWELL_WITH_ACTION_MS
        else -> DWELL_MS
    }

/**
 * The state behind the single FeedbackHost: the message on screen, its dwell timer, and the action button.
 * Kept free of Compose so the dwell and the run-once rule are tested on a virtual clock.
 */
class FeedbackToastState(private val scope: CoroutineScope) {
    private val _current: MutableStateFlow<FeedbackMessage?> = MutableStateFlow(null)
    private val _running: MutableStateFlow<Boolean> = MutableStateFlow(false)
    private var dwell: Job? = null

    /** The message on screen, or null. A new message replaces it (latest wins). */
    val current: StateFlow<FeedbackMessage?> = _current.asStateFlow()

    /** True while an action handler runs; the button is disabled and further activations are ignored. */
    val running: StateFlow<Boolean> = _running.asStateFlow()

    fun show(message: FeedbackMessage) {
        dwell?.cancel()
        _current.value = message
        val ms: Long = feedbackDwellMillis(message) ?: return
        dwell =
            scope.launch {
                delay(ms)
                dismiss(message)
            }
    }

    fun dismiss(message: FeedbackMessage) {
        if (_current.value === message) {
            dwell?.cancel()
            _current.value = null
        }
    }

    /**
     * Run [message]'s action once. Ignored when the message is no longer on screen or an action is already
     * running. The toast stays (button disabled) while it runs, then clears unless the handler showed its own
     * outcome in the meantime.
     */
    fun runAction(message: FeedbackMessage) {
        val action: FeedbackAction = message.action ?: return
        if (_current.value !== message || _running.value) return
        _running.value = true
        dwell?.cancel()
        scope.launch {
            try {
                action.handler()
            } finally {
                _running.value = false
                dismiss(message)
            }
        }
    }
}
