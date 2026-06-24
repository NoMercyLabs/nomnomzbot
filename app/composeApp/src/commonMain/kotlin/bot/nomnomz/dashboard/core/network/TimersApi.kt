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

// The typed timers facade — the channel's scheduled chat timers, the real rows the backend persists (no
// fabricated timers). The list is a `PaginatedResponse<TimerListItem>` (a flat `{ data: [...] }`), read with
// getDirect like the channel/community lists; the writes are the single-value `StatusResponseDto<TimerDto>`
// endpoints, read for their 2xx only (the page reloads the list afterwards). State holders depend on this
// interface and fake it in tests without HTTP.
//
// Backend routes (TimersController):
//   GET    /api/v1/channels/{channelId}/timers            →  PaginatedResponse<TimerListItem>
//   POST   /api/v1/channels/{channelId}/timers            →  StatusResponseDto<TimerDto>   (CreateTimerDto)
//   PUT    /api/v1/channels/{channelId}/timers/{id}       →  StatusResponseDto<TimerDto>   (UpdateTimerDto)
//   DELETE /api/v1/channels/{channelId}/timers/{id}       →  204 No Content
//   POST   /api/v1/channels/{channelId}/timers/{id}/toggle→  StatusResponseDto<TimerDto>   (flips IsEnabled)
interface TimersApi {
    /** The channel's scheduled timers — the first page the backend returns. */
    suspend fun list(channelId: String): ApiResult<List<TimerSummary>>

    /** Create a new scheduled timer; succeeds on the backend's 201. */
    suspend fun create(channelId: String, request: CreateTimerRequest): ApiResult<Unit>

    /** Update an existing timer's name / message / interval / enabled state. */
    suspend fun update(channelId: String, id: Int, request: UpdateTimerRequest): ApiResult<Unit>

    /** Delete a timer (soft-delete on the backend). */
    suspend fun delete(channelId: String, id: Int): ApiResult<Unit>

    /** Flip a timer's enabled state server-side (the dedicated toggle endpoint, no body). */
    suspend fun toggle(channelId: String, id: Int): ApiResult<Unit>
}

class RestTimersApi(private val client: ApiClient) : TimersApi {

    override suspend fun list(channelId: String): ApiResult<List<TimerSummary>> {
        // PaginatedResponse is a flat `{ data: [...] }` (not the single-value StatusResponseDto envelope), so it
        // is read with getDirect (whole-body deserialize) exactly like the channel/community lists. First page
        // only — the page reloads it after every write.
        return when (
            val page: ApiResult<PaginatedEnvelope<TimerSummary>> =
                client.getDirect("api/v1/channels/$channelId/timers?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // The writes return `StatusResponseDto<TimerDto>`, but the page reloads the list on success rather than
    // splicing the returned row, so only the 2xx matters here — postUnit / putUnit / deleteUnit ignore the body.
    override suspend fun create(channelId: String, request: CreateTimerRequest): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/timers", request)

    override suspend fun update(channelId: String, id: Int, request: UpdateTimerRequest): ApiResult<Unit> =
        client.putUnit("api/v1/channels/$channelId/timers/$id", request)

    override suspend fun delete(channelId: String, id: Int): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/timers/$id")

    override suspend fun toggle(channelId: String, id: Int): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/timers/$id/toggle")
}

/**
 * A scheduled timer (backend `TimerListItem`) — the lightweight list projection the rows render: the timer's
 * name, how often it fires, how many rotating messages it carries, and whether it is on. The field names are
 * the serialized (camelCase) names of `TimerListItem`; the client reads a subset (ApiClient's Json ignores
 * unknown keys), so the timestamp fields are omitted here.
 */
@Serializable
data class TimerSummary(
    val id: Int,
    val name: String = "",
    val intervalMinutes: Int = 0,
    val isEnabled: Boolean = false,
    val messageCount: Int = 0,
)

/**
 * Create-timer request (backend `CreateTimerDto`): a [name], one or more rotating [messages], how often it
 * fires ([intervalMinutes]), the chat-activity floor before it may fire ([minChatActivity]), and whether it
 * starts on ([isEnabled]). The dialog carries a single message; it is sent as a one-element list to match the
 * backend's `List<string> Messages`.
 */
@Serializable
data class CreateTimerRequest(
    val name: String,
    val messages: List<String>,
    val intervalMinutes: Int,
    val minChatActivity: Int = 0,
    val isEnabled: Boolean = true,
)

/**
 * Update-timer request (backend `UpdateTimerDto`): every field is optional — only the supplied ones change.
 * The edit dialog re-sends the full set it owns (name, message, interval, enabled); `minChatActivity` is left
 * null so the stored value is preserved (the dialog does not expose it).
 */
@Serializable
data class UpdateTimerRequest(
    val name: String? = null,
    val messages: List<String>? = null,
    val intervalMinutes: Int? = null,
    val isEnabled: Boolean? = null,
)
