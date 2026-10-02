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
import bot.nomnomz.dashboard.core.time.ClockTime
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.datetime.Instant
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.attention_spotify_blocked_message
import nomnomzbot.composeapp.generated.resources.attention_spotify_blocked_title

// The server raises spotify_app_blocked when Spotify blocks the channel's own app for hours. The title must
// name when the block ends on the streamer's clock, and the message must carry the fix.
class AttentionSpotifyBlockedTest {
    private val until: String = "2026-10-02T20:47:03.0000000+00:00"
    private val now: Instant = Instant.parse("2026-10-02T14:09:00Z")
    private val item =
        ActionRequiredItem(
            kind = "spotify_app_blocked",
            severity = "warning",
            titleKey = "attention_spotify_blocked_title",
            messageKey = "attention_spotify_blocked_message",
            parameters = mapOf("until" to until),
            deepLinkRoute = "integrations",
        )

    @Test
    fun the_title_names_the_end_of_the_block_in_local_time() {
        assertEquals(
            AttentionText(
                Res.string.attention_spotify_blocked_title,
                listOf(AttentionArg.Literal(ClockTime.of(until, now)!!)),
            ),
            attentionTitleOf(item, now),
        )
    }

    @Test
    fun the_message_maps_to_the_new_app_fix() {
        assertEquals(AttentionText(Res.string.attention_spotify_blocked_message), attentionMessageOf(item))
    }
}
