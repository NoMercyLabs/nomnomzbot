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

    /**
     * Store a bring-your-own-key provider credential ([provider] = `azure` | `elevenlabs`). The key is
     * vault-encrypted server-side and never echoed back; [region] is Azure-only. The backend returns the
     * refreshed config (with the provider's `has*Key` flag now true).
     */
    suspend fun setByokKey(
        channelId: String,
        provider: String,
        apiKey: String,
        region: String?,
    ): ApiResult<TtsConfig>

    /** Remove the stored BYOK key for [provider]; the backend returns the refreshed config (flag cleared). */
    suspend fun removeByokKey(channelId: String, provider: String): ApiResult<TtsConfig>

    /**
     * One page of the channel's TTS voice catalogue, filtered/searched server-side. Backed by the paginated
     * `GET /tts/voices` endpoint: free-text [query] matches name/display-name/description/tags; [locale] /
     * [gender] / [provider] / [accent] are equality filters (blank = no filter). Paging is 1-based.
     */
    suspend fun voicesPage(
        channelId: String,
        query: String = "",
        locale: String = "",
        gender: String = "",
        provider: String = "",
        accent: String = "",
        page: Int = 1,
        pageSize: Int = 50,
    ): ApiResult<TtsVoicePage>

    /** The first page of the channel's voices (unfiltered) — a convenience for seeding the default-voice label. */
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

    /** All pronunciation rules for the channel (phrase → what TTS speaks instead), ordered by phrase. */
    suspend fun lexicon(channelId: String): ApiResult<List<TtsLexiconEntry>>

    /** Add a pronunciation rule; a duplicate (phrase, match kind) is refused by the backend. */
    suspend fun createLexiconEntry(
        channelId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry>

    /** Rewrite the rule [entryId] (phrase, replacement, and match kind). */
    suspend fun updateLexiconEntry(
        channelId: String,
        entryId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry>

    /** Remove the rule [entryId]. */
    suspend fun deleteLexiconEntry(channelId: String, entryId: String): ApiResult<Unit>

    /** The signed-in viewer's OWN voice for this channel, or null when they use the channel default (404→null). */
    suspend fun myVoice(channelId: String): ApiResult<UserTtsVoice?>

    /** Pick the signed-in viewer's OWN voice (must be one the channel can synthesise). */
    suspend fun setMyVoice(channelId: String, voiceId: String): ApiResult<UserTtsVoice>

    /** Reset the signed-in viewer's OWN voice back to the channel default (404 = nothing set = success). */
    suspend fun clearMyVoice(channelId: String): ApiResult<Unit>
}

class RestTtsApi(private val client: ApiClient) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> =
        client.getEnvelope("api/v1/channels/$channelId/tts/config")

    override suspend fun updateConfig(
        channelId: String,
        update: TtsConfigUpdate,
    ): ApiResult<TtsConfig> =
        client.putEnvelope("api/v1/channels/$channelId/tts/config", update)

    override suspend fun setByokKey(
        channelId: String,
        provider: String,
        apiKey: String,
        region: String?,
    ): ApiResult<TtsConfig> =
        client.putEnvelope(
            "api/v1/channels/$channelId/tts/config/byok/$provider",
            SetTtsByokKeyBody(apiKey = apiKey, region = region),
        )

    override suspend fun removeByokKey(channelId: String, provider: String): ApiResult<TtsConfig> =
        client.deleteEnvelope("api/v1/channels/$channelId/tts/config/byok/$provider")

    // The voices endpoint is a PaginatedResponse (flat `{ data, total, hasMore, ... }`) — getDirect reads the
    // whole body. Only non-blank filters are appended so the query stays clean; values are percent-encoded.
    override suspend fun voicesPage(
        channelId: String,
        query: String,
        locale: String,
        gender: String,
        provider: String,
        accent: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<TtsVoicePage> {
        val params: MutableList<String> = mutableListOf("page=$page", "pageSize=$pageSize")
        if (query.isNotBlank()) params.add("q=${query.encodeQuery()}")
        if (locale.isNotBlank()) params.add("locale=${locale.encodeQuery()}")
        if (gender.isNotBlank()) params.add("gender=${gender.encodeQuery()}")
        if (provider.isNotBlank()) params.add("provider=${provider.encodeQuery()}")
        if (accent.isNotBlank()) params.add("accent=${accent.encodeQuery()}")
        return client.getDirect("api/v1/channels/$channelId/tts/voices?${params.joinToString("&")}")
    }

    // Convenience: read the first (default-size) page and surface just its list for label resolution.
    override suspend fun voices(channelId: String): ApiResult<List<TtsVoice>> =
        when (val page: ApiResult<TtsVoicePage> = voicesPage(channelId)) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

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

    // The lexicon list is a StatusResponseDto<List<TtsLexiconEntryDto>> envelope — getEnvelope unwraps `data`.
    override suspend fun lexicon(channelId: String): ApiResult<List<TtsLexiconEntry>> =
        client.getEnvelope("api/v1/channels/$channelId/tts/lexicon")

    override suspend fun createLexiconEntry(
        channelId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry> =
        client.postEnvelope("api/v1/channels/$channelId/tts/lexicon", body)

    override suspend fun updateLexiconEntry(
        channelId: String,
        entryId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry> =
        client.putEnvelope("api/v1/channels/$channelId/tts/lexicon/$entryId", body)

    override suspend fun deleteLexiconEntry(channelId: String, entryId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/tts/lexicon/$entryId")

    override suspend fun myVoice(channelId: String): ApiResult<UserTtsVoice?> =
        when (
            val result: ApiResult<UserTtsVoice> =
                client.getEnvelope("api/v1/channels/$channelId/tts/me/voice")
        ) {
            is ApiResult.Ok -> ApiResult.Ok(result.value)
            is ApiResult.Failure ->
                if (result.error.status == 404) ApiResult.Ok(null) else ApiResult.Failure(result.error)
        }

    override suspend fun setMyVoice(channelId: String, voiceId: String): ApiResult<UserTtsVoice> =
        client.putEnvelope(
            "api/v1/channels/$channelId/tts/me/voice",
            SetUserVoiceBody(voiceId = voiceId),
        )

    // A 404 on clear means nothing was set — the asked-for end state (channel default) already holds, so OK.
    override suspend fun clearMyVoice(channelId: String): ApiResult<Unit> =
        when (val result: ApiResult<Unit> = client.deleteUnit("api/v1/channels/$channelId/tts/me/voice")) {
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
    // Opt-IN: viewers pick their own voice in chat with `!voice <search>`. Default ON server-side.
    val viewerVoiceSelfServiceEnabled: Boolean = true,
    // BYOK status flags — the key itself is never echoed; these say only whether one is stored.
    val hasAzureByokKey: Boolean = false,
    val hasElevenLabsByokKey: Boolean = false,
    // The Azure region stored alongside the Azure BYOK key (null when no Azure key is set).
    val azureRegion: String? = null,
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
    val viewerVoiceSelfServiceEnabled: Boolean? = null,
)

/** Request body for storing a BYOK provider key (backend `SetTtsByokKeyDto`). [region] is Azure-only. */
@Serializable
data class SetTtsByokKeyBody(val apiKey: String, val region: String? = null)

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
    // Rich catalogue metadata (backend added for the searchable browser). All nullable/empty-tolerant.
    val accent: String? = null,
    val age: String? = null,
    val styles: List<String> = emptyList(),
    val tags: List<String> = emptyList(),
    val description: String? = null,
    // A ready-made preview clip URL when the provider ships one; else the page falls back to POST /tts/test.
    val previewUrl: String? = null,
)

/**
 * One page of the voice catalogue (backend `PaginatedResponse<TtsVoiceDto>`): the [data] rows plus the paging
 * signals the browser needs — [total] result count and [hasMore] (another page exists after this one).
 */
@Serializable
data class TtsVoicePage(
    val data: List<TtsVoice> = emptyList(),
    val total: Int = 0,
    val hasMore: Boolean = false,
    val nextPage: Int? = null,
)

/** A viewer's per-viewer voice override (backend `UserTtsVoiceDto`): [userId] reads in [voiceId]. */
@Serializable
data class UserTtsVoice(val userId: String = "", val voiceId: String = "")

/** Request body to set a viewer's voice (backend `SetUserVoiceDto`). [voiceId] must be a synthesisable voice. */
@Serializable
data class SetUserVoiceBody(val voiceId: String)

/**
 * One pronunciation-lexicon rule (backend `TtsLexiconEntryDto`): whenever [phrase] appears in an utterance,
 * TTS speaks [replacement] instead. [matchKind] is `word` (whole-word, case-insensitive) or `exact`
 * (case-sensitive literal).
 */
@Serializable
data class TtsLexiconEntry(
    val id: String = "",
    val phrase: String = "",
    val replacement: String = "",
    val matchKind: String = "word",
)

/** Request body to create/update a pronunciation rule (backend `UpsertTtsLexiconEntryDto`). */
@Serializable
data class UpsertTtsLexiconEntryBody(
    val phrase: String,
    val replacement: String,
    val matchKind: String = "word",
)
