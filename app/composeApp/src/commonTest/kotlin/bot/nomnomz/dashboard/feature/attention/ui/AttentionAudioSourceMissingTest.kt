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
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_live_message
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_live_title
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_message
import nomnomzbot.composeapp.generated.resources.attention_audio_source_missing_title

// The server raises audio_source_missing when overlay pages are open but none is an Audio Source page, so
// sound and TTS play from another page without the streamer knowing.
class AttentionAudioSourceMissingTest {
    private val item =
        ActionRequiredItem(
            kind = "audio_source_missing",
            severity = "warning",
            titleKey = "attention_audio_source_missing_title",
            messageKey = "attention_audio_source_missing_message",
            deepLinkRoute = "widgets",
        )

    @Test
    fun the_title_maps_to_the_audio_source_string() {
        assertEquals(
            AttentionText(Res.string.attention_audio_source_missing_title),
            attentionTitleOf(item, Instant.parse("2026-10-03T12:00:00Z")),
        )
    }

    @Test
    fun the_message_maps_to_the_audio_source_fix() {
        assertEquals(AttentionText(Res.string.attention_audio_source_missing_message), attentionMessageOf(item))
    }

    private val liveItem =
        ActionRequiredItem(
            kind = "audio_source_missing",
            severity = "critical",
            titleKey = "attention_audio_source_missing_live_title",
            messageKey = "attention_audio_source_missing_live_message",
            deepLinkRoute = "widgets",
        )

    @Test
    fun the_live_title_and_message_map_to_the_critical_strings() {
        assertEquals(
            AttentionText(Res.string.attention_audio_source_missing_live_title),
            attentionTitleOf(liveItem, Instant.parse("2026-10-03T12:00:00Z")),
        )
        assertEquals(AttentionText(Res.string.attention_audio_source_missing_live_message), attentionMessageOf(liveItem))
    }
}
