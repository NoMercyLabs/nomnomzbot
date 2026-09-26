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

// The dashboard's "action required" notification centre (S071a backend, plan item A0) — real, already-detected
// conditions needing the streamer's attention, from every backend producer (dead integrations, held messages,
// refused Twitch permissions, failed widget builds, failing webhooks, lost song requests, ...). The backend
// pushes `ConfigChanged("notifications")` over the dashboard hub whenever the list may have changed.
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
 * [kind] is a stable machine key. The row carries no prose: [titleKey] and [messageKey] name dashboard string
 * resources and [parameters] are the named values they format in, so the text renders in the viewer's language
 * (see `feature/attention/ui/AttentionText.kt`). [deepLinkRoute] is the lower-cased shell route of the page where
 * the condition is fixed. [id] is the stable dismissal key. Held messages are grouped per user: [count] > 1 means
 * [queueItemIds] carries every held message's queue-item guid for the group, and [sourceUserId]/[sourceUserName]
 * name the chatter.
 */
@Serializable
data class ActionRequiredItem(
    val kind: String,
    val severity: String,
    val titleKey: String,
    val messageKey: String,
    val parameters: Map<String, String> = emptyMap(),
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
