// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.attention.ui

import bot.nomnomz.dashboard.core.network.ActionRequiredItem
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.attention_source_name_unban_requests
import nomnomzbot.composeapp.generated.resources.attention_source_name_unknown
import nomnomzbot.composeapp.generated.resources.attention_source_unavailable_message
import nomnomzbot.composeapp.generated.resources.attention_source_unavailable_title

// The server raises source_unavailable when one of the inbox checks fails to read, so the streamer sees what
// could not be checked and why instead of a silently shorter list.
class AttentionSourceUnavailableTest {
    private fun item(source: String): ActionRequiredItem =
        ActionRequiredItem(
            kind = "source_unavailable",
            severity = "warning",
            titleKey = "attention_source_unavailable_title",
            messageKey = "attention_source_unavailable_message",
            parameters = mapOf("source" to source, "reason" to "Missing scope moderator:read:unban_requests"),
            deepLinkRoute = "integrations",
        )

    @Test
    fun the_title_names_the_check_that_failed() {
        assertEquals(
            AttentionText(
                Res.string.attention_source_unavailable_title,
                listOf(AttentionArg.Resource(Res.string.attention_source_name_unban_requests)),
            ),
            attentionTitleOf(item("unban_requests"), Instant.parse("2026-10-08T12:00:00Z")),
        )
    }

    @Test
    fun an_unnamed_check_still_gets_an_honest_title() {
        assertEquals(
            AttentionText(
                Res.string.attention_source_unavailable_title,
                listOf(AttentionArg.Resource(Res.string.attention_source_name_unknown)),
            ),
            attentionTitleOf(item("a_future_check"), Instant.parse("2026-10-08T12:00:00Z")),
        )
    }

    @Test
    fun the_message_carries_the_reason() {
        assertEquals(
            AttentionText(
                Res.string.attention_source_unavailable_message,
                listOf(AttentionArg.Literal("Missing scope moderator:read:unban_requests")),
            ),
            attentionMessageOf(item("unban_requests")),
        )
    }
}
