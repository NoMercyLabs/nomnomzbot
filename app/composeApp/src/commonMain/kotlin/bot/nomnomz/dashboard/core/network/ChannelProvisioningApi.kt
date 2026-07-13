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

// Provisions a usable tenant for a Twitch channel the caller MODERATES but the bot is not installed on, so the
// dashboard can switch to it ("moderator mode"). Kept off ChannelsApi so the one method doesn't ripple through
// the twenty-plus test fakes that implement ChannelsApi.
//
// Backend (ChannelsController):
//   POST /api/v1/channels/moderated/{twitchBroadcasterId}/enter → StatusResponseDto<ChannelSummaryDto>
//   (verifies the caller moderates it, creates a lightweight Channel row + grants the caller Moderator, returns
//    the internal channel — its `id` is the tenant GUID to switch to, not the Twitch id.)
interface ChannelProvisioningApi {
    /** Enter (provision + join as Moderator) a moderated channel by its Twitch broadcaster id; returns the tenant. */
    suspend fun enterModeratedChannel(twitchBroadcasterId: String): ApiResult<ChannelSummary>
}

class RestChannelProvisioningApi(private val client: ApiClient) : ChannelProvisioningApi {
    override suspend fun enterModeratedChannel(twitchBroadcasterId: String): ApiResult<ChannelSummary> =
        client.postEnvelope("api/v1/channels/moderated/$twitchBroadcasterId/enter")
}
