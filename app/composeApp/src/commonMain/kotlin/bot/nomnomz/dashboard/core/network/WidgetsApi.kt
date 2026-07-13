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

    /** Create a new widget with [name] and [type]. Returns the created row (including the assigned [WidgetSummary.id]). */
    suspend fun create(channelId: String, body: CreateWidgetBody): ApiResult<WidgetSummary>

    /** Rename a widget — sends a partial PUT with only [name]. */
    suspend fun rename(channelId: String, widgetId: String, name: String): ApiResult<Unit>

    /**
     * Save a custom widget's authored source ([code]) via a partial PUT carrying only `customCode`. An empty
     * string clears the widget's code; every other field stays untouched.
     */
    suspend fun saveCode(channelId: String, widgetId: String, code: String): ApiResult<Unit>

    /**
     * Clone a widget by creating a new one with the same [type] and "Copy of [sourceName]" as the name.
     * The backend has no clone route, so the client re-issues a POST with the derived values.
     */
    suspend fun clone(channelId: String, sourceType: String, sourceName: String): ApiResult<WidgetSummary>
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

    // POST to the list endpoint; backend returns StatusResponseDto<WidgetDetail> — postEnvelope unwraps `.data`.
    override suspend fun create(channelId: String, body: CreateWidgetBody): ApiResult<WidgetSummary> =
        client.postEnvelope("api/v1/channels/$channelId/widgets", body)

    // Partial PUT — only the name field changes; isEnabled / settings / eventSubscriptions stay as-is.
    override suspend fun rename(channelId: String, widgetId: String, name: String): ApiResult<Unit> =
        client.putUnit("api/v1/channels/$channelId/widgets/$widgetId", UpdateWidgetBody(name = name))

    // Partial PUT — only customCode changes; the backend leaves every other field of the widget as-is.
    override suspend fun saveCode(channelId: String, widgetId: String, code: String): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/widgets/$widgetId",
            UpdateWidgetBody(customCode = code),
        )

    // Clone = POST with "Copy of <sourceName>" and the same type.
    override suspend fun clone(channelId: String, sourceType: String, sourceName: String): ApiResult<WidgetSummary> =
        client.postEnvelope("api/v1/channels/$channelId/widgets", CreateWidgetBody("Copy of $sourceName", sourceType))
}

/**
 * The update-widget request body (backend `UpdateWidgetRequest`) — every field nullable so an update is a
 * partial patch. A toggle sends only [isEnabled]; a rename sends only [name]; null fields are omitted from
 * the wire body (`explicitNulls = false` on the shared Json).
 */
@Serializable
data class UpdateWidgetBody(
    val name: String? = null,
    val isEnabled: Boolean? = null,
    val customCode: String? = null,
)

/** The create-widget request body (backend `CreateWidgetRequest`). Only [name] and [type] are required. */
@Serializable
data class CreateWidgetBody(val name: String, val type: String)

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
    // The widget's authored source, for the custom-widget code editor. Null for template-driven widgets that
    // carry no hand-written code. The backend returns it on `WidgetDetail`; the editor reads and rewrites it.
    val customCode: String? = null,
)
