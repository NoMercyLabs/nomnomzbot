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

// The typed TTS facade — the channel's text-to-speech configuration the TTS page reads and edits.
// TTS config is a single object (backend `StatusResponseDto<TtsConfigDto>`), so both the read and the
// update unwrap getEnvelope/putEnvelope's `data: T`. State holders depend on this interface and fake it
// in tests without HTTP.
//
// Backend routes (TtsConfigController):
//   GET /api/v1/channels/{channelId}/tts/config  →  StatusResponseDto<TtsConfigDto>
//   PUT /api/v1/channels/{channelId}/tts/config  ←  UpdateTtsConfigDto  →  StatusResponseDto<TtsConfigDto>
interface TtsApi {
    /** The channel's current TTS configuration. */
    suspend fun config(channelId: String): ApiResult<TtsConfig>

    /** Persist [update]; the backend echoes the saved configuration back. */
    suspend fun updateConfig(channelId: String, update: TtsConfigUpdate): ApiResult<TtsConfig>
}

class RestTtsApi(private val client: ApiClient) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> =
        client.getEnvelope("api/v1/channels/$channelId/tts/config")

    override suspend fun updateConfig(
        channelId: String,
        update: TtsConfigUpdate,
    ): ApiResult<TtsConfig> =
        client.putEnvelope("api/v1/channels/$channelId/tts/config", update)
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

// The TTS config update request (backend `UpdateTtsConfigDto`). Every field is nullable: the backend
// treats null as "leave unchanged", so a partial edit only sends the fields that moved. `explicitNulls =
// false` on the shared Json means absent fields are omitted from the wire body, not sent as JSON null.
// Field names mirror the DTO camelCase exactly (ApiClient never renames). `minPermission` must be one of
// everyone|subscribers|vip|moderators|broadcaster; `maxLength` is 1..500 — both validated server-side.
@Serializable
data class TtsConfigUpdate(
    val isEnabled: Boolean? = null,
    val defaultVoiceId: String? = null,
    val maxLength: Int? = null,
    val minPermission: String? = null,
    val skipBotMessages: Boolean? = null,
    val readUsernames: Boolean? = null,
)
