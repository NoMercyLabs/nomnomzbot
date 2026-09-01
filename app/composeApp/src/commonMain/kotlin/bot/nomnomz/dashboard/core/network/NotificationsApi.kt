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

// The dashboard's "action required" notification centre (S071a backend / S071b Home tile) — real,
// already-detected conditions needing the streamer's attention (dead integration tokens, AutoMod-held
// messages pending review), so they no longer need to be discovered by noticing something silently broke.
//
// Backend route (NotificationsController):
//   GET /api/v1/channels/{channelId}/notifications/action-required → StatusResponseDto<List<ActionRequiredItemDto>>
//        (action key `dashboard:read`) — newest first.
interface NotificationsApi {
    /** The channel's current action-required items, newest first. Never fabricated — every row traces to a
     * real, already-detected condition; an empty list means nothing needs attention right now. */
    suspend fun actionRequired(channelId: String): ApiResult<List<ActionRequiredItem>>
}

class RestNotificationsApi(private val client: ApiClient) : NotificationsApi {
    override suspend fun actionRequired(channelId: String): ApiResult<List<ActionRequiredItem>> =
        client.getEnvelope("api/v1/channels/$channelId/notifications/action-required")
}

/**
 * One action-required row (backend `ActionRequiredItemDto`). [severity] is `critical` | `warning` | `info`;
 * [kind] is a stable machine key the dashboard could group/icon by (not used for grouping yet — the tile
 * renders every item). [deepLinkRoute] is a [bot.nomnomz.dashboard.feature.shell.nav.ShellRoute] name the row
 * navigates to on click.
 */
@Serializable
data class ActionRequiredItem(
    val kind: String,
    val severity: String,
    val title: String,
    val message: String,
    val detectedAt: String = "",
    val deepLinkRoute: String,
)
