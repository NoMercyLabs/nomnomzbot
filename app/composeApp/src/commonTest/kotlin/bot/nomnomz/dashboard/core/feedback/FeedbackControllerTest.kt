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
import kotlin.test.assertTrue
import kotlinx.coroutines.CompletableDeferred
import kotlinx.coroutines.flow.first
import kotlinx.coroutines.launch
import kotlinx.coroutines.test.runTest
import kotlinx.coroutines.yield
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_command_saved
import nomnomzbot.composeapp.generated.resources.feedback_command_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_connect_failed
import nomnomzbot.composeapp.generated.resources.feedback_connected

// Proves the process-wide feedback bus actually carries the outcomes the app frame surfaces: a success/error
// emission reaches the stream with the right kind/label/args, AND a message emitted BEFORE a (late)
// subscriber attaches is still delivered to it — the replay that lets a banner raised mid-redirect show
// after the page rebuilds. The host is a pure projection of this stream, so this proves the frame behavior.
class FeedbackControllerTest {

    @Test
    fun a_success_emission_reaches_the_stream_with_its_kind_and_label() = runTest {
        val controller = FeedbackController()

        controller.success(Res.string.feedback_connected)

        val message: FeedbackMessage = controller.messages.first()
        assertEquals(FeedbackKind.Success, message.kind)
        assertEquals(Res.string.feedback_connected, message.label)
        assertTrue(message.formatArgs.isEmpty())
    }

    @Test
    fun an_error_emission_carries_its_detail_as_a_format_arg() = runTest {
        val controller = FeedbackController()

        controller.error(Res.string.feedback_connect_failed, "network down")

        val message: FeedbackMessage = controller.messages.first()
        assertEquals(FeedbackKind.Error, message.kind)
        assertEquals(Res.string.feedback_connect_failed, message.label)
        assertEquals(listOf<Any>("network down"), message.formatArgs)
    }

    @Test
    fun a_message_emitted_before_a_late_subscriber_is_still_delivered_via_replay() = runTest {
        // The redirect case: the message is raised while no host is listening (the page is rebuilding). A
        // subscriber that attaches AFTERWARDS must still receive it — otherwise "Connected" is lost on
        // return. The replay buffer guarantees this.
        val controller = FeedbackController()

        controller.success(Res.string.feedback_connected)

        // Subscribe only now, after the emission — the host's position post-rebuild.
        val replayed: FeedbackMessage = controller.messages.first()
        assertEquals(FeedbackKind.Success, replayed.kind)
        assertEquals(Res.string.feedback_connected, replayed.label)
    }

    @Test
    fun the_latest_outcome_supersedes_an_earlier_one_for_a_late_subscriber() = runTest {
        // A success then a failure: a host attaching after both should land on the newest (the failure), so a
        // fresh outcome always wins over a stale one.
        val controller = FeedbackController()

        controller.success(Res.string.feedback_command_saved)
        controller.error(Res.string.feedback_command_save_failed, "boom")

        val latest: FeedbackMessage = controller.messages.first()
        assertEquals(FeedbackKind.Error, latest.kind)
        assertEquals(Res.string.feedback_command_save_failed, latest.label)
    }

    @Test
    fun an_active_collector_observes_emissions_in_order() = runTest {
        val controller = FeedbackController()

        // A live collector running on the test's background scope records what it observes; the host is exactly
        // such a collector. It must see both emissions, in order, with the failure's detail intact.
        val observed: MutableList<FeedbackMessage> = mutableListOf()
        val ready: CompletableDeferred<Unit> = CompletableDeferred()
        backgroundScope.launch {
            ready.complete(Unit)
            controller.messages.collect { observed += it }
        }
        ready.await()
        yield()

        controller.success(Res.string.feedback_command_saved)
        controller.error(Res.string.feedback_command_save_failed, "denied")
        yield()

        assertEquals(2, observed.size)
        assertEquals(FeedbackKind.Success, observed[0].kind)
        assertEquals(FeedbackKind.Error, observed[1].kind)
        assertEquals(listOf<Any>("denied"), observed[1].formatArgs)
    }
}
