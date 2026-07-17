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

    /** Pending TTS utterances awaiting moderator approval (newest-first). Empty when approval is off / none held. */
    suspend fun queue(channelId: String): ApiResult<List<TtsQueueEntry>>

    /** Approve a pending utterance ([entryId]) — the backend then synthesises and plays it on the overlay. */
    suspend fun approveQueueEntry(channelId: String, entryId: String): ApiResult<Unit>

    /** Reject a pending utterance ([entryId]) — it is discarded and nothing plays. */
    suspend fun rejectQueueEntry(channelId: String, entryId: String): ApiResult<Unit>

    /**
     * The per-viewer voice override for [userId], or `null` when the viewer has none (uses the channel default).
     * The backend answers 404 for "no override" — this maps that to `Ok(null)`, so only a real error is a Failure.
     */
    suspend fun userVoice(channelId: String, userId: String): ApiResult<UserTtsVoice?>

    /** Assign [voiceId] as [userId]'s voice (must be one the channel can synthesise). */
    suspend fun setUserVoice(channelId: String, userId: String, voiceId: String): ApiResult<Unit>

    /** Clear [userId]'s voice override (they fall back to the channel default). A 404 (nothing set) is a success. */
    suspend fun clearUserVoice(channelId: String, userId: String): ApiResult<Unit>
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

    // The queue is a PaginatedResponse (flat `{ data: [...] }`) — getDirect deserializes the whole body, same
    // as the widgets / commands lists. Pending, newest-first; one page is plenty for a review panel.
    override suspend fun queue(channelId: String): ApiResult<List<TtsQueueEntry>> =
        when (
            val page: ApiResult<PaginatedEnvelope<TtsQueueEntry>> =
                client.getDirect("api/v1/channels/$channelId/tts/queue?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun approveQueueEntry(channelId: String, entryId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/tts/queue/$entryId/approve")

    override suspend fun rejectQueueEntry(channelId: String, entryId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/tts/queue/$entryId/reject")

    // 404 = "no override, uses the channel default" — a normal answer, not an error; map it to Ok(null).
    override suspend fun userVoice(channelId: String, userId: String): ApiResult<UserTtsVoice?> =
        when (
            val result: ApiResult<UserTtsVoice> =
                client.getEnvelope("api/v1/channels/$channelId/tts/users/$userId/voice")
        ) {
            is ApiResult.Ok -> ApiResult.Ok(result.value)
            is ApiResult.Failure ->
                if (result.error.status == 404) ApiResult.Ok(null) else ApiResult.Failure(result.error)
        }

    override suspend fun setUserVoice(
        channelId: String,
        userId: String,
        voiceId: String,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/tts/users/$userId/voice",
            SetUserVoiceBody(voiceId = voiceId),
        )

    // A 404 on clear means there was nothing set — the end state (no override) is exactly what was asked, so OK.
    override suspend fun clearUserVoice(channelId: String, userId: String): ApiResult<Unit> =
        when (val result: ApiResult<Unit> = client.deleteUnit("api/v1/channels/$channelId/tts/users/$userId/voice")) {
            is ApiResult.Ok -> ApiResult.Ok(Unit)
            is ApiResult.Failure ->
                if (result.error.status == 404) ApiResult.Ok(Unit) else ApiResult.Failure(result.error)
        }
}

/** The channel's TTS configuration (backend `TtsConfigDto`). Field names mirror the DTO camelCase exactly. */
@Serializable
data class TtsConfig(
    val isEnabled: Boolean = false,
    // Dispatch plane: client_edge | byok | self_host. Display-only until the client-edge handler ships.
    val mode: String = "self_host",
    // Preferred synthesis provider: edge | azure | elevenlabs.
    val defaultProvider: String = "edge",
    val defaultVoiceId: String? = null,
    val maxCharacters: Int = 0,
    val minPermission: String = "",
    val skipBotMessages: Boolean = false,
    val readUsernames: Boolean = false,
    // Opt-OUT: masks mild swears before a message is read aloud. Default ON, so a channel with no stored value
    // (or an older backend that omits it) reads as ON.
    val profanityCensorEnabled: Boolean = true,
    // Opt-IN: hold every TTS utterance in the moderator approval queue until a mod approves it. Default OFF.
    val modApprovalRequired: Boolean = false,
    // Minimum bits attached to a message for it to be read out; null = no bits gate.
    val minBitsToTts: Int? = null,
)

// The TTS config update request (backend `UpdateTtsConfigDto`). Every field is nullable: the backend
// treats null as "leave unchanged", so a partial edit only sends the fields that moved. `explicitNulls =
// false` on the shared Json means absent fields are omitted from the wire body, not sent as JSON null.
// Field names mirror the DTO camelCase exactly (ApiClient never renames). `minPermission` must be one of
// everyone|subscribers|vip|moderators|broadcaster; `maxCharacters` is 1..500; `minBitsToTts` 0 clears the
// gate — all validated server-side.
@Serializable
data class TtsConfigUpdate(
    val isEnabled: Boolean? = null,
    val mode: String? = null,
    val defaultProvider: String? = null,
    val defaultVoiceId: String? = null,
    val maxCharacters: Int? = null,
    val minPermission: String? = null,
    val skipBotMessages: Boolean? = null,
    val readUsernames: Boolean? = null,
    val profanityCensorEnabled: Boolean? = null,
    val modApprovalRequired: Boolean? = null,
    val minBitsToTts: Int? = null,
)

/**
 * A pending TTS utterance awaiting moderator approval (backend `TtsQueueEntryDto`). The mod reviews the text
 * that WILL be spoken — [censoredText] when [wasCensored], else [originalText] — and approves or rejects it.
 * Entries auto-expire (~10 min); [expiresAt] lets the panel show the remaining window. A subset of the DTO
 * (requestedByTwitchUserId / sourceMessageId are not rendered and omitted).
 */
@Serializable
data class TtsQueueEntry(
    val id: String = "",
    val requestedByDisplayName: String = "",
    val originalText: String = "",
    val censoredText: String? = null,
    val wasCensored: Boolean = false,
    val voiceId: String = "",
    val status: String = "",
    val createdAt: String = "",
    val expiresAt: String? = null,
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

/** A viewer's per-viewer voice override (backend `UserTtsVoiceDto`): [userId] reads in [voiceId]. */
@Serializable
data class UserTtsVoice(val userId: String = "", val voiceId: String = "")

/** Request body to set a viewer's voice (backend `SetUserVoiceDto`). [voiceId] must be a synthesisable voice. */
@Serializable
data class SetUserVoiceBody(val voiceId: String)
