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

// The typed alert-queue facade: the channel's ONE cross-platform alert queue (widgets-overlays.md section 1.2).
// The state holder depends on this interface and fakes it in tests without HTTP.
//
// Backend route (EventResponsesController):
//   GET /api/v1/channels/{channelId}/event-responses/alert-queue -> StatusResponseDto<AlertQueueDto>
interface AlertsApi {
    /**
     * The channel's ONE cross-platform alert queue: a follow, a sub, and a supporter tip from any provider,
     * in one ordered list, with no gallery install required. [AlertQueueDto.overlayConnected] is read live
     * from the backend's overlay presence registry, never inferred from the entries.
     */
    suspend fun queue(channelId: String): ApiResult<AlertQueueDto>
}

class RestAlertsApi(private val client: ApiClient) : AlertsApi {
    // The queue response is a `StatusResponseDto<AlertQueueDto>`, unwrapped via getEnvelope's `data: T`.
    override suspend fun queue(channelId: String): ApiResult<AlertQueueDto> =
        client.getEnvelope("api/v1/channels/$channelId/event-responses/alert-queue")
}

/**
 * The channel's alert queue (backend `AlertQueueDto`, widgets-overlays.md §1.2) — [overlayConnected] is the
 * honest, live presence check (never derived from [entries]); [entries] is the queue itself, most-recent-first.
 */
@Serializable
data class AlertQueueDto(
    val overlayConnected: Boolean = false,
    val entries: List<AlertQueueEntryDto> = emptyList(),
)

/** One queued alert (backend `AlertQueueEntryDto`) — [provider] is the cross-platform attribution. */
@Serializable
data class AlertQueueEntryDto(
    val id: String = "",
    val provider: String = "",
    val kind: String = "",
    val payloadJson: String = "",
    val status: String = "",
    val createdAt: String = "",
    val deliveredAt: String? = null,
)
