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

import org.jetbrains.compose.resources.StringResource
import kotlinx.coroutines.flow.MutableSharedFlow
import kotlinx.coroutines.flow.SharedFlow
import kotlinx.coroutines.flow.asSharedFlow

// The process-wide feedback bus (one instance in AppGraph). Feature controllers emit a [FeedbackMessage]
// the moment a write/connect resolves; the single shell-level FeedbackHost collects this stream and shows
// the message on whatever page is mounted, so an outcome is visible from one place across the whole frame.
//
// The seam every emitter depends on is [Feedback] (the narrow emit-only interface) — controllers take a
// [Feedback], not the concrete bus, so a test can substitute a recording fake and assert exactly what an
// action emitted. The host consumes [messages].
//
// Replay matters here: an OAuth/connect flow tears the page down and rebuilds it on return, and the host
// re-subscribes only after that rebuild. A plain SharedFlow with zero replay would drop a message emitted
// during the gap. A small replay buffer holds the most recent emissions so a message raised mid-redirect
// still reaches the freshly-mounted host — the requirement that "a message emitted during a redirect still
// shows after the page rebuilds".

/** The narrow emit-only seam every feature controller depends on (so writes can announce their outcome). */
interface Feedback {
    fun success(label: StringResource, vararg formatArgs: Any)

    fun error(label: StringResource, vararg formatArgs: Any)

    fun info(label: StringResource, vararg formatArgs: Any)

    /** Emit a fully-formed message (used by callers that already hold a [FeedbackMessage], e.g. OAuth return). */
    fun emit(message: FeedbackMessage)
}

class FeedbackController : Feedback {
    // extraBufferCapacity gives a non-suspending tryEmit even with a slow/absent collector; replay re-delivers
    // the recent messages to a host that subscribes after a redirect rebuild. DROP_OLDEST keeps the newest
    // outcome rather than blocking an emitter when the buffer is momentarily full.
    private val _messages: MutableSharedFlow<FeedbackMessage> =
        MutableSharedFlow(
            replay = REPLAY,
            extraBufferCapacity = EXTRA_BUFFER,
            onBufferOverflow = kotlinx.coroutines.channels.BufferOverflow.DROP_OLDEST,
        )

    /** The stream the shell-level host collects to render outcomes. */
    val messages: SharedFlow<FeedbackMessage> = _messages.asSharedFlow()

    override fun success(label: StringResource, vararg formatArgs: Any) =
        emit(FeedbackMessage(FeedbackKind.Success, label, formatArgs.toList()))

    override fun error(label: StringResource, vararg formatArgs: Any) =
        emit(FeedbackMessage(FeedbackKind.Error, label, formatArgs.toList()))

    override fun info(label: StringResource, vararg formatArgs: Any) =
        emit(FeedbackMessage(FeedbackKind.Info, label, formatArgs.toList()))

    override fun emit(message: FeedbackMessage) {
        _messages.tryEmit(message)
    }

    private companion object {
        const val REPLAY: Int = 1
        const val EXTRA_BUFFER: Int = 8
    }
}

/**
 * A no-op [Feedback] (Null Object). The default for a controller's feedback seam so a unit test that only
 * asserts state transitions can construct the controller without wiring a bus; production injects the real
 * [FeedbackController]. A test that asserts the EMITTED message substitutes a recording fake instead.
 */
object NoOpFeedback : Feedback {
    override fun success(label: StringResource, vararg formatArgs: Any) = Unit

    override fun error(label: StringResource, vararg formatArgs: Any) = Unit

    override fun info(label: StringResource, vararg formatArgs: Any) = Unit

    override fun emit(message: FeedbackMessage) = Unit
}
