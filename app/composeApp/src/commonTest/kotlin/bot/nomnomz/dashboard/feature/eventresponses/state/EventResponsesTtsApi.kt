// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsConfigUpdate
import bot.nomnomz.dashboard.core.network.TtsTestRequest
import bot.nomnomz.dashboard.core.network.UpsertTtsLexiconEntryBody

// The Event Responses page only reads the channel's TTS config (is TTS on?) — every other call is out of scope
// and fails loudly if a change ever starts using it here.
internal class EventResponsesTtsApi(
    private val configResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig(isEnabled = true)),
) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> = configResult
    override suspend fun updateConfig(channelId: String, update: TtsConfigUpdate) = error("stub")
    override suspend fun setByokKey(channelId: String, provider: String, apiKey: String, region: String?) = error("stub")
    override suspend fun removeByokKey(channelId: String, provider: String) = error("stub")
    override suspend fun voicesPage(channelId: String, query: String, locale: String, gender: String, provider: String, accent: String, page: Int, pageSize: Int) = error("stub")
    override suspend fun voices(channelId: String) = error("stub")
    override suspend fun testSpeak(channelId: String, request: TtsTestRequest) = error("stub")
    override suspend fun queue(channelId: String) = error("stub")
    override suspend fun approveQueueEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun rejectQueueEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun userVoice(channelId: String, userId: String) = error("stub")
    override suspend fun setUserVoice(channelId: String, userId: String, voiceId: String) = error("stub")
    override suspend fun clearUserVoice(channelId: String, userId: String) = error("stub")
    override suspend fun lexicon(channelId: String) = error("stub")
    override suspend fun createLexiconEntry(channelId: String, body: UpsertTtsLexiconEntryBody) = error("stub")
    override suspend fun updateLexiconEntry(channelId: String, entryId: String, body: UpsertTtsLexiconEntryBody) = error("stub")
    override suspend fun deleteLexiconEntry(channelId: String, entryId: String) = error("stub")
    override suspend fun myVoice(channelId: String) = error("stub")
    override suspend fun setMyVoice(channelId: String, voiceId: String) = error("stub")
    override suspend fun clearMyVoice(channelId: String) = error("stub")
    override suspend fun overlay(channelId: String) = error("stub")
    override suspend fun testOverlay(channelId: String) = error("stub")
    override suspend fun skipPlayback(channelId: String) = error("stub")
    override suspend fun clearPlayback(channelId: String) = error("stub")
    override suspend fun pausePlayback(channelId: String) = error("stub")
    override suspend fun resumePlayback(channelId: String) = error("stub")
    override suspend fun configDefaults(channelId: String) = error("stub")
    override suspend fun resetConfig(channelId: String) = error("stub")
}
