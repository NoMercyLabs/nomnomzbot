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

    /** The TTS voices available to the channel (across the configured providers). */
    suspend fun voices(channelId: String): ApiResult<List<TtsVoice>>

    /**
     * Synthesise [text] with [voiceId] and return the result (backend `POST /tts/test`). The result carries the
     * base64-encoded audio and the provider's reported duration so the dashboard can play it back inline.
     */
    suspend fun testSpeak(channelId: String, request: TtsTestRequest): ApiResult<TtsTestResult>
}

class RestTtsApi(private val client: ApiClient) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> =
        client.getEnvelope("api/v1/channels/$channelId/tts/config")

    override suspend fun updateConfig(
        channelId: String,
        update: TtsConfigUpdate,
    ): ApiResult<TtsConfig> =
        client.putEnvelope("api/v1/channels/$channelId/tts/config", update)

    // StatusResponseDto envelope wrapping the voice list — getEnvelope reads the `data` list directly.
    override suspend fun voices(channelId: String): ApiResult<List<TtsVoice>> =
        client.getEnvelope("api/v1/channels/$channelId/tts/voices")

    // The test response is a StatusResponseDto<TtsTestResultDto> envelope — getEnvelope unwraps `data`.
    override suspend fun testSpeak(channelId: String, request: TtsTestRequest): ApiResult<TtsTestResult> =
        client.postEnvelope("api/v1/channels/$channelId/tts/test", request)
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
    // Opt-OUT: masks mild swears before a message is read aloud. Default ON, so a channel with no stored value
    // (or an older backend that omits it) reads as ON.
    val profanityCensorEnabled: Boolean = true,
    // Opt-IN: hold every TTS utterance in the moderator approval queue until a mod approves it. Default OFF.
    val modApprovalRequired: Boolean = false,
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
    val profanityCensorEnabled: Boolean? = null,
    val modApprovalRequired: Boolean? = null,
)

/** The test-speak request body (backend `TtsTestRequestDto`). camelCase; [voiceId] is the full provider voice id. */
@Serializable
data class TtsTestRequest(val text: String, val voiceId: String)

/**
 * The test-speak result (backend `TtsTestResultDto`). [audioBase64] is the synthesised audio encoded as base64
 * (the dashboard uses it as a data URI for inline playback); [durationMs] is the provider's reported length;
 * [provider] identifies which engine rendered it.
 */
@Serializable
data class TtsTestResult(
    val voiceId: String = "",
    val provider: String = "",
    val durationMs: Int = 0,
    val audioBase64: String = "",
)

/**
 * One available TTS voice (backend `TtsVoiceDto`). camelCase mirror; the TTS page lists these so the operator
 * sees the valid [id]s for `defaultVoiceId` alongside each voice's [displayName] / [locale] / [gender] /
 * [provider]. [isDefault] marks a provider's default voice.
 */
@Serializable
data class TtsVoice(
    val id: String = "",
    val name: String = "",
    val displayName: String = "",
    val locale: String = "",
    val gender: String = "",
    val provider: String = "",
    val isDefault: Boolean = false,
)
