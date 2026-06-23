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

// The typed channels facade. Onboarding needs the tenant (channel) GUID to address the per-channel
// integration routes (`/channels/{channelId}/...`), so this resolves the signed-in user's primary
// channel from the paginated channel list. The state holders depend on the interface (the existing
// "depend on interfaces" convention), so they fake the network in tests without HTTP.
//
// Backend route (ChannelsController):
//   GET /api/v1/channels  →  PaginatedResponse<ChannelSummaryDto>  (the channels the user owns/moderates)
interface ChannelsApi {
    /**
     * The signed-in user's primary channel — the first channel they own/moderate. The integrations
     * screen is single-channel for this slice; the multi-channel switcher layers on later.
     */
    suspend fun primaryChannel(): ApiResult<ChannelSummary>
}

class RestChannelsApi(private val client: ApiClient) : ChannelsApi {

    override suspend fun primaryChannel(): ApiResult<ChannelSummary> {
        // The channel list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so
        // it is read with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap.
        return when (
            val page: ApiResult<PaginatedEnvelope<ChannelSummary>> =
                client.getDirect("api/v1/channels?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> {
                val first: ChannelSummary? = page.value.data.firstOrNull()
                if (first == null) {
                    ApiResult.Failure(
                        ApiError(
                            status = 404,
                            code = "NO_CHANNEL",
                            message = "No channel is onboarded for this account yet.",
                        )
                    )
                } else {
                    ApiResult.Ok(first)
                }
            }
        }
    }
}

/** The backend `PaginatedResponse<T>` shape — a flat `data` array plus paging metadata we ignore here. */
@Serializable
data class PaginatedEnvelope<T>(val data: List<T> = emptyList())
