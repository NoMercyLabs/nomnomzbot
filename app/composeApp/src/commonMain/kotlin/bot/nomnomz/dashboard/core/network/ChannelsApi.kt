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

    /**
     * All Twitch channels the signed-in user moderates (from Twitch API, not just onboarded ones).
     * The [ModeratedChannel.isOnboarded] flag indicates whether the channel uses this bot instance.
     * Used to show the full Twitch moderation roster in the channel switcher.
     */
    suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>>

    /** Make the bot join (or re-join) the channel's chat. */
    suspend fun join(channelId: String): ApiResult<Unit>

    /** Make the bot leave the channel's chat (channel record stays; bot just goes quiet). */
    suspend fun leave(channelId: String): ApiResult<Unit>

    /** Reset all channel configuration to defaults (clears all stored Configuration entries). */
    suspend fun reset(channelId: String): ApiResult<Unit>

    /** Permanently delete the channel record and all its data. Irreversible — requires re-onboarding. */
    suspend fun deleteChannel(channelId: String): ApiResult<Unit>

    // ── White-label channel bot (ChannelBotController) ────────────────────────
    // The channel's own dedicated bot identity (separate from the platform-shared bot).
    // Connecting a white-label bot makes bot messages appear from a channel-specific account.

    /** The granted Twitch OAuth scopes for this channel's broadcaster token. */
    suspend fun channelScopes(channelId: String): ApiResult<ChannelScopesResponse>

    /** Start Twitch OAuth for this channel's white-label bot; returns the authorize URL to open. */
    suspend fun startChannelBotConnect(channelId: String): ApiResult<OAuthStart>

    /** Whether a white-label bot account is connected to this channel. */
    suspend fun channelBotStatus(channelId: String): ApiResult<ChannelBotStatusDetail>

    /** Revoke and remove the channel's white-label bot account connection. */
    suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit>
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

    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> =
        client.getEnvelope("api/v1/channels/moderated")

    override suspend fun join(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/join")

    override suspend fun leave(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/leave")

    override suspend fun reset(channelId: String): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/reset")

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId")

    override suspend fun channelScopes(channelId: String): ApiResult<ChannelScopesResponse> =
        client.getEnvelope("api/v1/channels/$channelId/scopes")

    override suspend fun startChannelBotConnect(channelId: String): ApiResult<OAuthStart> =
        client.getEnvelope("api/v1/channels/$channelId/bot/connect")

    override suspend fun channelBotStatus(channelId: String): ApiResult<ChannelBotStatusDetail> =
        client.getEnvelope("api/v1/channels/$channelId/bot/status")

    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/bot")

    private suspend fun fetchPage(): ApiResult<PaginatedEnvelope<ChannelSummary>> =
        client.getDirect("api/v1/channels?page=1&pageSize=100")
}

/**
 * The backend `PaginatedResponse<T>` shape — a flat `data` array plus the paging signals. [hasMore] and
 * [nextPage] drive [ApiClient.getAllPages] so a caller that needs the WHOLE list (config screens) can walk
 * every page instead of silently showing only the first one.
 */
@Serializable
data class PaginatedEnvelope<T>(
    val data: List<T> = emptyList(),
    val hasMore: Boolean = false,
    val nextPage: Int? = null,
)

/** The channel's white-label bot status (`BotStatusDto`). Same shape as the platform bot status. */
typealias ChannelBotStatusDetail = BotStatus

/** One broadcaster-token scope row (`ChannelBotController.ScopeDto`). */
@Serializable
data class ChannelScope(
    val scope: String,
    val name: String = "",
    val description: String = "",
    val category: String = "",
    val granted: Boolean = false,
    val required: Boolean = false,
)

/** The full scopes status response for a channel (`ScopesResponseDto`). */
@Serializable
data class ChannelScopesResponse(
    val permissions: List<ChannelScope> = emptyList(),
    val grantedCount: Int = 0,
    val totalCount: Int = 0,
)
