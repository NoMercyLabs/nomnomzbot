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

// The typed event-responses (Alerts) facade — what the bot says/does when a channel event fires (a follow,
// sub, raid, cheer, …). The Alerts page renders the channel's configured responses; real data only (the
// backend lists the channel's stored event responses, no fabricated rows). The state holder depends on this
// interface and fakes it in tests without HTTP.
//
// Backend routes (EventResponsesController, keyed by event type):
//   GET    /api/v1/channels/{channelId}/event-responses              → PaginatedResponse<EventResponseListItem>
//   GET    /api/v1/channels/{channelId}/event-responses/{eventType}  → StatusResponseDto<EventResponseDto>
//   PUT    /api/v1/channels/{channelId}/event-responses/{eventType}  → upsert (create + edit + toggle)
//   DELETE /api/v1/channels/{channelId}/event-responses/{eventType}  → 204
//
// The list item is lightweight: it carries the event type, enabled flag, response type and the updated
// timestamp — NOT the message body. The message lives on the per-type detail, so the edit dialog reads it
// via [detail] before pre-filling. The PUT is an upsert keyed by event type: a brand-new event type creates
// the row; an existing one is patched. A toggle is the same upsert carrying only [UpdateAlertBody.isEnabled].
interface AlertsApi {
    /** The channel's configured event responses — the lightweight list-view items. */
    suspend fun list(channelId: String): ApiResult<List<AlertSummary>>

    /** The full configuration for one [eventType] — including the message the edit dialog pre-fills. */
    suspend fun detail(channelId: String, eventType: String): ApiResult<AlertDetail>

    /**
     * Upsert the response for [eventType] (the backend PUT route is keyed by event type): a partial patch —
     * only the non-null [body] fields are applied. A create sends the message + response type; an edit sends
     * the new message; a toggle sends only `isEnabled`. Omitted fields are left untouched by the backend.
     */
    suspend fun upsert(
        channelId: String,
        eventType: String,
        body: UpdateAlertBody,
    ): ApiResult<Unit>

    /** Remove the response for [eventType] (the backend DELETE route is keyed by event type). */
    suspend fun delete(channelId: String, eventType: String): ApiResult<Unit>
}

class RestAlertsApi(private val client: ApiClient) : AlertsApi {
    override suspend fun list(channelId: String): ApiResult<List<AlertSummary>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so it is read
        // with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap — same shape as
        // the channels and commands lists.
        return when (
            val page: ApiResult<PaginatedEnvelope<AlertSummary>> =
                client.getDirect("api/v1/channels/$channelId/event-responses?page=1&pageSize=100")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // The detail is a `StatusResponseDto<EventResponseDto>`, so it is read with getEnvelope's `data: T` unwrap.
    override suspend fun detail(channelId: String, eventType: String): ApiResult<AlertDetail> =
        client.getEnvelope("api/v1/channels/$channelId/event-responses/$eventType")

    // The upsert response is a `StatusResponseDto<EventResponseDto>`, but the controller re-lists after every
    // write, so the body is irrelevant here — any 2xx is success.
    override suspend fun upsert(
        channelId: String,
        eventType: String,
        body: UpdateAlertBody,
    ): ApiResult<Unit> = client.putUnit("api/v1/channels/$channelId/event-responses/$eventType", body)

    override suspend fun delete(channelId: String, eventType: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/event-responses/$eventType")
}

/**
 * The upsert request body (backend `UpdateEventResponseDto`) — every field nullable so a write is a partial
 * patch. A create sends [message] + [responseType] (and starts [isEnabled] = true); an edit sends [message];
 * a toggle sends only [isEnabled]; all other fields stay null and the backend leaves them untouched.
 * `explicitNulls = false` on the shared Json means null fields are omitted from the wire body.
 *
 * [responseType] is the backend's constrained set — `chat_message` (the default the dialog uses), `overlay`,
 * `pipeline`, or `none`.
 */
@Serializable
data class UpdateAlertBody(
    val isEnabled: Boolean? = null,
    val responseType: String? = null,
    val message: String? = null,
)

/**
 * An event response's list-view item (backend `EventResponseListItem`): which [eventType] it reacts to,
 * whether it is live, how the bot responds ([responseType]), and when it was last changed. The message body
 * lives on the [AlertDetail], not the list item.
 */
@Serializable
data class AlertSummary(
    val id: Int = 0,
    val eventType: String = "",
    val isEnabled: Boolean = false,
    val responseType: String = "",
    val updatedAt: String = "",
)

/**
 * An event response's full configuration (backend `EventResponseDto`): the list fields plus the [message]
 * the bot sends. The Alerts edit dialog reads this to pre-fill the message before the operator edits it.
 */
@Serializable
data class AlertDetail(
    val id: Int = 0,
    val eventType: String = "",
    val isEnabled: Boolean = false,
    val responseType: String = "",
    val message: String? = null,
    val updatedAt: String = "",
)
