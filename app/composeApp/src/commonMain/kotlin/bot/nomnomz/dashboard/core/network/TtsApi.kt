// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.core.network

import kotlinx.serialization.Serializable

// The typed TTS facade — the channel's text-to-speech configuration the TTS page renders read-only.
// TTS config is a single object (backend `StatusResponseDto<TtsConfigDto>`), so it is read with
// getEnvelope's `data: T` unwrap. State holders depend on this interface and fake it in tests without HTTP.
//
// Backend route (TtsConfigController):
//   GET /api/v1/channels/{channelId}/tts/config  →  StatusResponseDto<TtsConfigDto>
interface TtsApi {
    /** The channel's current TTS configuration. */
    suspend fun config(channelId: String): ApiResult<TtsConfig>
}

class RestTtsApi(private val client: ApiClient) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> =
        client.getEnvelope("api/v1/channels/$channelId/tts/config")
}

/** The channel's TTS configuration (backend `TtsConfigDto`). Field names mirror the DTO camelCase exactly. */
@Serializable
data class TtsConfig(
    val isEnabled: Boolean = false,
    val defaultVoiceId: String = "",
    val maxLength: Int = 0,
    val minPermission: String = "",
    val skipBotMessages: Boolean = false,
    val readUsernames: Boolean = false,
)
