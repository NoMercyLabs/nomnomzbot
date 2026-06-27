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

import bot.nomnomz.dashboard.core.connection.SessionStore
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
     * The active channel — the one currently selected in [SessionStore.activeChannelId], or the
     * first channel in the list if no explicit selection has been made yet. Every page controller
     * calls this to get the channel id it should operate on, so a switcher selection propagates
     * automatically on the next load without each controller needing a direct SessionStore ref.
     */
    suspend fun primaryChannel(): ApiResult<ChannelSummary>

    /** All channels the signed-in user owns or moderates — used by the channel switcher. */
    suspend fun list(): ApiResult<List<ChannelSummary>>

    /** Make the bot join (or re-join) the channel's chat. */
    suspend fun join(channelId: String): ApiResult<Unit>

    /** Make the bot leave the channel's chat (channel record stays; bot just goes quiet). */
    suspend fun leave(channelId: String): ApiResult<Unit>

    /** Reset all channel configuration to defaults (clears all stored Configuration entries). */
    suspend fun reset(channelId: String): ApiResult<Unit>

    /** Permanently delete the channel record and all its data. Irreversible — requires re-onboarding. */
    suspend fun deleteChannel(channelId: String): ApiResult<Unit>
}

class RestChannelsApi(
    private val client: ApiClient,
    private val sessionStore: SessionStore,
) : ChannelsApi {

    override suspend fun primaryChannel(): ApiResult<ChannelSummary> {
        // The channel list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so
        // it is read with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap.
        return when (val page: ApiResult<PaginatedEnvelope<ChannelSummary>> = fetchPage()) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> {
                val channels: List<ChannelSummary> = page.value.data
                val activeId: String? = sessionStore.activeChannelId.value
                // Use the explicitly-selected channel; fall back to first (single-channel / first load).
                val channel: ChannelSummary? =
                    if (activeId != null) channels.firstOrNull { it.id == activeId } ?: channels.firstOrNull()
                    else channels.firstOrNull()
                if (channel == null) {
                    ApiResult.Failure(
                        ApiError(
                            status = 404,
                            code = "NO_CHANNEL",
                            message = "No channel is onboarded for this account yet.",
                        )
                    )
                } else {
                    ApiResult.Ok(channel)
                }
            }
        }
    }

    override suspend fun list(): ApiResult<List<ChannelSummary>> =
        when (val page: ApiResult<PaginatedEnvelope<ChannelSummary>> = fetchPage()) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun join(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/join")

    override suspend fun leave(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/leave")

    override suspend fun reset(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/reset")

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId")

    private suspend fun fetchPage(): ApiResult<PaginatedEnvelope<ChannelSummary>> =
        client.getDirect("api/v1/channels?page=1&pageSize=100")
}

/** The backend `PaginatedResponse<T>` shape — a flat `data` array plus paging metadata we ignore here. */
@Serializable
data class PaginatedEnvelope<T>(val data: List<T> = emptyList())
