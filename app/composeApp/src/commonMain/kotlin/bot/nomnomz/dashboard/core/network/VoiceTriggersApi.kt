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

// The typed voice-triggers facade — spoken-word counters + overlay stickers. A streamer's voice-listener page
// (a real Chrome tab running webkitSpeechRecognition — OBS's browser source cannot run speech recognition at
// all) reports a heard word to the backend, which increments the count and fires a sticker on the overlay.
// [listenerLink] is the ready-to-open URL for that page — the dashboard never builds it itself, since the
// origin-resolution (self-host LAN vs tunnel vs SaaS) is a backend concern (ResolvePublicOrigin).
//
// Backend routes (VoiceTriggersController):
//   GET    /api/v1/channels/{channelId}/voice-triggers                 →  StatusResponseDto<IReadOnlyList<VoiceTriggerDto>>
//   POST   /api/v1/channels/{channelId}/voice-triggers                 →  StatusResponseDto<VoiceTriggerDto>
//   PATCH  /api/v1/channels/{channelId}/voice-triggers/{triggerId}     →  StatusResponseDto<VoiceTriggerDto>
//   DELETE /api/v1/channels/{channelId}/voice-triggers/{triggerId}     →  204 No Content
//   GET    /api/v1/channels/{channelId}/voice-triggers/listener-link   →  StatusResponseDto<string>
interface VoiceTriggersApi {
    /** The channel's voice triggers, with their live counts. */
    suspend fun list(channelId: String): ApiResult<List<VoiceTrigger>>

    /** Create a voice trigger on the channel. */
    suspend fun create(channelId: String, body: CreateVoiceTriggerBody): ApiResult<Unit>

    /** Edit an existing trigger, addressed by its [triggerId]. A partial update — only the sent fields change. */
    suspend fun update(channelId: String, triggerId: String, body: UpdateVoiceTriggerBody): ApiResult<Unit>

    /** Delete a trigger, addressed by its [triggerId]. */
    suspend fun delete(channelId: String, triggerId: String): ApiResult<Unit>

    /** The streamer's ready-to-open voice-listener page link (a real Chrome tab, never an OBS source). */
    suspend fun listenerLink(channelId: String): ApiResult<String>
}

class RestVoiceTriggersApi(private val client: ApiClient) : VoiceTriggersApi {
    override suspend fun list(channelId: String): ApiResult<List<VoiceTrigger>> =
        client.getEnvelope("api/v1/channels/$channelId/voice-triggers")

    override suspend fun create(channelId: String, body: CreateVoiceTriggerBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/voice-triggers", body)

    override suspend fun update(
        channelId: String,
        triggerId: String,
        body: UpdateVoiceTriggerBody,
    ): ApiResult<Unit> = client.patchUnit("api/v1/channels/$channelId/voice-triggers/$triggerId", body)

    override suspend fun delete(channelId: String, triggerId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/voice-triggers/$triggerId")

    override suspend fun listenerLink(channelId: String): ApiResult<String> =
        client.getEnvelope("api/v1/channels/$channelId/voice-triggers/listener-link")
}

/**
 * A voice trigger (backend `VoiceTriggerDto`): the [word] to listen for, [startingCount] (the seed value set
 * once at creation — e.g. to preserve a number already tracked by another bot) and the live [currentCount],
 * the per-fire [cooldownSeconds] spam/stutter guard, and the bound [stickerAssetId] (+ its resolved
 * [stickerImageUrl], null when no sticker is set).
 */
@Serializable
data class VoiceTrigger(
    val id: String = "",
    val word: String = "",
    val isEnabled: Boolean = true,
    val startingCount: Int = 0,
    val currentCount: Int = 0,
    val cooldownSeconds: Int = 5,
    val stickerAssetId: String? = null,
    val stickerImageUrl: String? = null,
    val lastFiredAt: String? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
)

@Serializable
data class CreateVoiceTriggerBody(
    val word: String,
    val isEnabled: Boolean,
    val startingCount: Int,
    val cooldownSeconds: Int,
    val stickerAssetId: String? = null,
)

/** Partial update (backend `UpdateVoiceTriggerRequest`) — every field nullable; a toggle sends only [isEnabled]. */
@Serializable
data class UpdateVoiceTriggerBody(
    val word: String? = null,
    val isEnabled: Boolean? = null,
    val cooldownSeconds: Int? = null,
    val stickerAssetId: String? = null,
)

/** The client-side sentinel that clears a bound sticker on update (mirrors [EMPTY_PIPELINE_ID]). */
const val EMPTY_STICKER_ASSET_ID: String = "00000000-0000-0000-0000-000000000000"
