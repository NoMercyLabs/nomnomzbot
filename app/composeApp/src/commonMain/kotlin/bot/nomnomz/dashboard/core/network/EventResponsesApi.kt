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
// silent (none). Rows are a FIXED, SEEDED CATALOGUE (one per known event type, S-EVENTRESPONSE-NO-CREATE) —
// never user-created or user-deletable; the dashboard only toggles/edits a row, or resets it back to its
// seeded default in place.
//
// Backend routes (EventResponsesController):
//   GET  /api/v1/channels/{channelId}/event-responses               →  PaginatedResponse<EventResponseListItem>
//   GET  /api/v1/channels/{channelId}/event-responses/{type}        →  StatusResponseDto<EventResponseDto>
//   PUT  /api/v1/channels/{channelId}/event-responses/{type}        ←  UpdateEventResponseDto  →  StatusResponseDto<EventResponseDto>
//   POST /api/v1/channels/{channelId}/event-responses/{type}/reset  →  204 No Content (resets the row in place; never removes it)
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

    /** Reset an event response back to its seeded default (disabled, chat_message, no message/pipeline). Never removes the row. */
    suspend fun resetToDefault(channelId: String, eventType: String): ApiResult<Unit>
}

class RestEventResponsesApi(private val client: ApiClient) : EventResponsesApi {
    override suspend fun list(channelId: String): ApiResult<List<EventResponseSummary>> =
        // Walk every page so ALL event responses show — flat `{ data, hasMore, nextPage }`.
        client.getAllPages { page ->
            "api/v1/channels/$channelId/event-responses?page=$page&pageSize=100"
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

    override suspend fun resetToDefault(channelId: String, eventType: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/event-responses/$eventType/reset")
}

/**
 * One catalog preset (backend `EventResponsePresetDto`) for an [eventType]: the [defaultTemplate] the dashboard
 * pre-fills the message input with, and the exact template [variables] the trigger seeds — offered as insert chips.
 *
 * [defaultTemplate] is a backend-authored translation KEY only (S-SCHEMA-I18N-redesign) — resolve it with
 * `resolveSchemaString` (`core/i18n`) before showing it; the English/Dutch sentences live in strings.xml.
 */
@Serializable
data class EventResponsePreset(
    val eventType: String = "",
    val defaultTemplate: LocalizedTextDto = LocalizedTextDto(),
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
