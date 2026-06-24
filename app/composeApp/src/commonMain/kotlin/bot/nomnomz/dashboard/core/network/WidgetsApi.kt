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

// The typed widgets facade — the channel's OBS browser-source overlays the Overlays page renders. Real data
// only: the backend lists the channel's stored widgets, each carrying its live browser-source URL (the value
// the operator pastes into OBS). The state holder depends on this interface and fakes it in tests without HTTP.
//
// Backend routes (WidgetsController, all under /channels/{channelId}/widgets):
//   GET    .../widgets               →  PaginatedResponse<WidgetDetail>   (the channel's overlays)
//   PUT    .../widgets/{widgetId}    →  StatusResponseDto<WidgetDetail>   (partial update — flips isEnabled)
//   DELETE .../widgets/{widgetId}    →  204 No Content                    (removes the overlay; its URL dies)
interface WidgetsApi {
    /** The channel's overlay widgets — each with its OBS browser-source URL. */
    suspend fun list(channelId: String): ApiResult<List<WidgetSummary>>

    /**
     * Flip a widget's enabled flag via the partial-update endpoint (no dedicated toggle route). Only
     * [enabled] is sent; every other field stays null and the backend leaves it untouched.
     */
    suspend fun setEnabled(channelId: String, widgetId: String, enabled: Boolean): ApiResult<Unit>

    /**
     * Delete a widget, addressed by its [widgetId]. Destructive: the overlay's browser-source URL stops
     * resolving once it is gone, so the screen confirms before calling this.
     */
    suspend fun delete(channelId: String, widgetId: String): ApiResult<Unit>
}

class RestWidgetsApi(private val client: ApiClient) : WidgetsApi {
    override suspend fun list(channelId: String): ApiResult<List<WidgetSummary>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so it is read
        // with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap — same shape as
        // the commands / channels lists.
        return when (
            val page: ApiResult<PaginatedEnvelope<WidgetSummary>> =
                client.getDirect("api/v1/channels/$channelId/widgets?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // The update response is a `StatusResponseDto<WidgetDetail>`, but the controller re-fetches the list after
    // every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun setEnabled(
        channelId: String,
        widgetId: String,
        enabled: Boolean,
    ): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/widgets/$widgetId",
            UpdateWidgetBody(isEnabled = enabled),
        )

    override suspend fun delete(channelId: String, widgetId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/widgets/$widgetId")
}

/**
 * The update-widget request body (backend `UpdateWidgetRequest`) — every field nullable so an update is a
 * partial patch. A toggle sends only [isEnabled]; all other fields stay null and the backend leaves them
 * untouched. `explicitNulls = false` on the shared Json means null fields are omitted from the wire body.
 */
@Serializable
data class UpdateWidgetBody(val isEnabled: Boolean? = null)

/**
 * An overlay widget (backend `WidgetDetail`): its [id], display [name], the widget [type], whether it is live,
 * and the [overlayUrl] — the OBS browser-source URL the operator copies into OBS. The settings, event
 * subscriptions, and timestamps the detail DTO also carries are not part of the list view and are ignored
 * (`ignoreUnknownKeys` on the shared Json).
 */
@Serializable
data class WidgetSummary(
    val id: String = "",
    val name: String = "",
    val type: String = "",
    val isEnabled: Boolean = false,
    val overlayUrl: String? = null,
)
