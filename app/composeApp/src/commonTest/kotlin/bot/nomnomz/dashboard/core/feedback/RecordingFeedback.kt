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

// A recording [Feedback] for controller tests: it captures every message a controller emits so a test can
// assert the EXACT outcome an action announced on the frame (kind + label + detail), not merely that some
// emit happened. Shared across feature-controller tests so each proves its writes announce success/error.
class RecordingFeedback : Feedback {
    val messages: MutableList<FeedbackMessage> = mutableListOf()

    /** The single message, when a test expects exactly one emission. */
    val only: FeedbackMessage
        get() = messages.single()

    override fun success(label: StringResource, vararg formatArgs: Any) {
        messages += FeedbackMessage(FeedbackKind.Success, label, formatArgs.toList())
    }

    override fun error(label: StringResource, vararg formatArgs: Any) {
        messages += FeedbackMessage(FeedbackKind.Error, label, formatArgs.toList())
    }

    override fun info(label: StringResource, vararg formatArgs: Any) {
        messages += FeedbackMessage(FeedbackKind.Info, label, formatArgs.toList())
    }

    override fun emit(message: FeedbackMessage) {
        messages += message
    }
}
