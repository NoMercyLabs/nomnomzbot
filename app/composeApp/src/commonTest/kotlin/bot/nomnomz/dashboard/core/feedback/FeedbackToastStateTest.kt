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

import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertSame
import kotlin.test.assertTrue
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.ExperimentalCoroutinesApi
import kotlinx.coroutines.test.advanceTimeBy
import kotlinx.coroutines.test.runCurrent
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_action_retry
import nomnomzbot.composeapp.generated.resources.feedback_action_undo
import nomnomzbot.composeapp.generated.resources.feedback_connect_failed
import nomnomzbot.composeapp.generated.resources.feedback_connected

// Proves the toast's dwell and action slot against the test scheduler's virtual clock: a success with an
// action stays 10 s, one without 4 s, an error stays until dismissed, the action runs exactly once, and a
// second activation while it runs is ignored.
@OptIn(ExperimentalCoroutinesApi::class)
class FeedbackToastStateTest {

    private fun success(action: FeedbackAction? = null): FeedbackMessage =
        FeedbackMessage(FeedbackKind.Success, Res.string.feedback_connected, action = action)

    private fun error(action: FeedbackAction? = null): FeedbackMessage =
        FeedbackMessage(FeedbackKind.Error, Res.string.feedback_connect_failed, action = action)

    @Test
    fun a_success_without_an_action_dwells_four_seconds() = runTest {
        val state = FeedbackToastState(backgroundScope)
        state.show(success())

        advanceTimeBy(3_999)
        runCurrent()
        assertNotNull(state.current.value)
        advanceTimeBy(2)
        runCurrent()
        assertNull(state.current.value)
    }

    @Test
    fun a_success_with_an_action_dwells_ten_seconds() = runTest {
        val state = FeedbackToastState(backgroundScope)
        state.show(success(FeedbackAction(Res.string.feedback_action_undo) {}))

        advanceTimeBy(9_999)
        runCurrent()
        assertNotNull(state.current.value)
        advanceTimeBy(2)
        runCurrent()
        assertNull(state.current.value)
    }

    @Test
    fun an_error_stays_until_dismissed_even_with_a_retry() = runTest {
        val state = FeedbackToastState(backgroundScope)
        val message: FeedbackMessage = error(FeedbackAction(Res.string.feedback_action_retry) {})
        state.show(message)

        advanceTimeBy(60_000)
        runCurrent()
        assertSame(message, state.current.value)
        state.dismiss(message)
        assertNull(state.current.value)
    }

    @Test
    fun retry_re_invokes_the_failed_call_exactly_once() = runTest {
        val state = FeedbackToastState(backgroundScope)
        var calls = 0
        val message: FeedbackMessage =
            error(FeedbackAction(Res.string.feedback_action_retry) { calls++ })
        state.show(message)

        state.runAction(message)
        runCurrent()
        state.runAction(message)
        runCurrent()

        assertEquals(1, calls)
        assertNull(state.current.value)
    }

    @Test
    fun a_second_click_is_ignored_while_the_action_runs() = runTest {
        val state = FeedbackToastState(backgroundScope)
        val gate = CompletableDeferred<Unit>()
        var calls = 0
        val message: FeedbackMessage =
            error(FeedbackAction(Res.string.feedback_action_retry) { calls++; gate.await() })
        state.show(message)

        state.runAction(message)
        runCurrent()
        assertTrue(state.running.value)
        state.runAction(message)
        runCurrent()
        assertEquals(1, calls)

        gate.complete(Unit)
        runCurrent()
        assertTrue(!state.running.value)
        assertEquals(1, calls)
    }

    @Test
    fun the_toast_shows_the_actions_own_outcome_not_the_cleared_original() = runTest {
        val state = FeedbackToastState(backgroundScope)
        val outcome: FeedbackMessage = success()
        val message: FeedbackMessage =
            error(FeedbackAction(Res.string.feedback_action_retry) { state.show(outcome) })
        state.show(message)

        state.runAction(message)
        runCurrent()

        assertSame(outcome, state.current.value)
    }
}
