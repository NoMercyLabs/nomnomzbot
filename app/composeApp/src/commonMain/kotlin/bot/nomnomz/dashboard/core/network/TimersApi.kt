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
// fabricated timers). The list is a `PaginatedResponse<TimerListItem>` (a flat `{ data: [...] }`), so it is
// read with getDirect like the channel/community lists. State holders depend on this interface and fake it in
// tests without HTTP.
//
// Backend route (TimersController):
//   GET /api/v1/channels/{channelId}/timers  →  PaginatedResponse<TimerListItem>
interface TimersApi {
    /** The channel's scheduled timers — the first page the backend returns. */
    suspend fun list(channelId: String): ApiResult<List<TimerSummary>>
}

class RestTimersApi(private val client: ApiClient) : TimersApi {

    override suspend fun list(channelId: String): ApiResult<List<TimerSummary>> {
        // PaginatedResponse is a flat `{ data: [...] }` (not the single-value StatusResponseDto envelope), so it
        // is read with getDirect (whole-body deserialize) exactly like the channel/community lists. First page
        // only for this read-only slice.
        return when (
            val page: ApiResult<PaginatedEnvelope<TimerSummary>> =
                client.getDirect("api/v1/channels/$channelId/timers?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }
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
