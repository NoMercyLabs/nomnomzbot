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
// Backend routes (NotificationsController):
//   GET  /api/v1/channels/{channelId}/notifications/action-required → StatusResponseDto<List<ActionRequiredItemDto>>
//        (action key `dashboard:read`) — newest first.
//   POST /api/v1/channels/{channelId}/notifications/action-required/dismiss  body `{ "ids": [...] }` —
//        persists a per-item dismissal so the listed [ActionRequiredItem.id]s stop coming back.
interface NotificationsApi {
    /** The channel's current action-required items, newest first. Never fabricated — every row traces to a
     * real, already-detected condition; an empty list means nothing needs attention right now. */
    suspend fun actionRequired(channelId: String): ApiResult<List<ActionRequiredItem>>

    /** Dismiss the action-required items with the given [ActionRequiredItem.id]s — persisted server-side, so
     * a dismissed item stays gone across reloads (a NEW condition mints a new id and reappears). */
    suspend fun dismissActionRequired(channelId: String, ids: List<String>): ApiResult<Unit>
}

class RestNotificationsApi(private val client: ApiClient) : NotificationsApi {
    override suspend fun actionRequired(channelId: String): ApiResult<List<ActionRequiredItem>> =
        client.getEnvelope("api/v1/channels/$channelId/notifications/action-required")

    override suspend fun dismissActionRequired(channelId: String, ids: List<String>): ApiResult<Unit> =
        client.postUnit(
            "api/v1/channels/$channelId/notifications/action-required/dismiss",
            DismissActionRequiredBody(ids = ids),
        )
}

/**
 * One action-required row (backend `ActionRequiredItemDto`). [severity] is `critical` | `warning` | `info`;
 * [kind] is a stable machine key (`held_chat_message` | `integration_token_dead`) the dashboard maps to a
 * [bot.nomnomz.dashboard.feature.shell.nav.ShellRoute] name itself. [id] is the stable dismissal key
 * (`held:{guid}` | `held-user:{userId}` | `token:{connectionId}:{ticks}`). Held messages are grouped per
 * user: [count] > 1 means [queueItemIds] carries every held message's queue-item guid for the group, and
 * [sourceUserId]/[sourceUserName] name the chatter. [deepLinkRoute] stays on the wire but is no longer
 * consumed — navigation derives from [kind].
 */
@Serializable
data class ActionRequiredItem(
    val kind: String,
    val severity: String,
    val title: String,
    val message: String,
    val detectedAt: String = "",
    val deepLinkRoute: String,
    val id: String = "",
    val sourceUserId: String? = null,
    val sourceUserName: String? = null,
    val count: Int = 1,
    val queueItemIds: List<String> = emptyList(),
)

/** Request body for the action-required dismiss endpoint — the [ActionRequiredItem.id]s to dismiss. */
@Serializable
data class DismissActionRequiredBody(val ids: List<String>)
