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

import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_connect_failed
import nomnomzbot.composeapp.generated.resources.feedback_connected

// Maps an OAuth/connect RETURN to a frame-level [FeedbackMessage] so the user lands back in the shell with a
// clear "Connected" / "Couldn't connect: …" instead of a silent return (the owner's explicit requirement —
// "the app frame should host the message with a page redirect"). The web launcher already decodes the
// backend's return marker (provider connected / error code) on reload; this turns that outcome into the one
// message the app frame surfaces.

/**
 * Translate a connect/OAuth return into the message the frame should show.
 *  - success → a green "Connected" banner.
 *  - failure → a persistent error banner carrying the backend's error code as detail.
 */
fun connectReturnFeedback(connected: Boolean, errorCode: String?): FeedbackMessage =
    if (connected) {
        FeedbackMessage(FeedbackKind.Success, Res.string.feedback_connected)
    } else {
        FeedbackMessage(
            kind = FeedbackKind.Error,
            label = Res.string.feedback_connect_failed,
            formatArgs = listOf(errorCode ?: ""),
        )
    }
