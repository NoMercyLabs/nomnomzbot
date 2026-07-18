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

// The typed event-responses facade — the channel's configured reactions to Twitch channel events (follow, sub,
// cheer, raid, etc.). Each event type can be bound to a chat message, an overlay feed, a pipeline, or left
// silent (none). The list is seeded by the backend on channel join with sensible defaults; the dashboard edits
// them (enable/disable + response body) and may delete one to restore the default on next seed.
//
// Backend routes (EventResponsesController):
//   GET    /api/v1/channels/{channelId}/event-responses            →  PaginatedResponse<EventResponseListItem>
//   GET    /api/v1/channels/{channelId}/event-responses/{type}     →  StatusResponseDto<EventResponseDto>
//   PUT    /api/v1/channels/{channelId}/event-responses/{type}     ←  UpdateEventResponseDto  →  StatusResponseDto<EventResponseDto>
//   DELETE /api/v1/channels/{channelId}/event-responses/{type}     →  204 No Content
//
// Floors: read = eventresponses:read (Moderator+), write = eventresponses:write (Editor+).
interface EventResponsesApi {
    /** All configured event responses for the channel, first page. */
    suspend fun list(channelId: String): ApiResult<List<EventResponseSummary>>

    /**
     * The preset catalog — one entry per configurable event type with a ready-to-use [EventResponsePreset.defaultTemplate]
     * (the dashboard pre-fills the message input with it when empty) and the exact template [EventResponsePreset.variables]
     * that event seeds (offered as insert chips).
     */
    suspend fun catalog(channelId: String): ApiResult<List<EventResponsePreset>>

    /** The full event response config for a single event type. */
    suspend fun get(channelId: String, eventType: String): ApiResult<EventResponse>

    /** Upsert (PUT) an event response — [eventType] is the URL address key. */
    suspend fun upsert(
        channelId: String,
        eventType: String,
        body: UpdateEventResponseBody,
    ): ApiResult<EventResponse>

    /** Delete an event response (restores it to seed state on next channel join). */
    suspend fun delete(channelId: String, eventType: String): ApiResult<Unit>
}

class RestEventResponsesApi(private val client: ApiClient) : EventResponsesApi {
    override suspend fun list(channelId: String): ApiResult<List<EventResponseSummary>> =
        when (
            val page: ApiResult<PaginatedEnvelope<EventResponseSummary>> =
                client.getDirect(
                    "api/v1/channels/$channelId/event-responses?page=1&pageSize=50"
                )
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun catalog(channelId: String): ApiResult<List<EventResponsePreset>> =
        client.getEnvelope("api/v1/channels/$channelId/event-responses/catalog")

    override suspend fun get(channelId: String, eventType: String): ApiResult<EventResponse> =
        client.getEnvelope("api/v1/channels/$channelId/event-responses/$eventType")

    override suspend fun upsert(
        channelId: String,
        eventType: String,
        body: UpdateEventResponseBody,
    ): ApiResult<EventResponse> =
        client.putEnvelope("api/v1/channels/$channelId/event-responses/$eventType", body)

    override suspend fun delete(channelId: String, eventType: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/event-responses/$eventType")
}

/**
 * One catalog preset (backend `EventResponsePresetDto`) for an [eventType]: the [defaultTemplate] the dashboard
 * pre-fills the message input with, and the exact template [variables] the trigger seeds — offered as insert chips.
 */
@Serializable
data class EventResponsePreset(
    val eventType: String = "",
    val defaultTemplate: String = "",
    val variables: List<String> = emptyList(),
)

/** Lightweight event-response summary from the list endpoint (backend `EventResponseListItem`). */
@Serializable
data class EventResponseSummary(
    val id: String = "",
    val eventType: String = "",
    val isEnabled: Boolean = false,
    val responseType: String = "none",
    val updatedAt: String = "",
)

/** Full event-response config (backend `EventResponseDto`). */
@Serializable
data class EventResponse(
    val id: String = "",
    val eventType: String = "",
    val isEnabled: Boolean = false,
    val responseType: String = "none",
    val message: String? = null,
    val pipelineId: String? = null,
    val metadata: Map<String, String> = emptyMap(),
    val createdAt: String = "",
    val updatedAt: String = "",
)

/**
 * The upsert request body (backend `UpdateEventResponseDto`). All fields nullable — only the supplied ones
 * apply. The backend applies a partial-style merge for every non-null field.
 */
@Serializable
data class UpdateEventResponseBody(
    val isEnabled: Boolean? = null,
    val responseType: String? = null,
    val message: String? = null,
    val pipelineId: String? = null,
    val metadata: Map<String, String>? = null,
)
