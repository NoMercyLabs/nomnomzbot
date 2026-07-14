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
import kotlinx.serialization.json.JsonObject

// The typed widget-gallery facade — the PUBLIC, anonymous catalogue of verified overlay widgets a channel can
// install or clone-to-edit (widgets-overlays.md §5c). Read-only here: browse the paginated summaries, then load
// one item in full for its source/default-config preview. The install + clone-from-gallery WRITES live on
// WidgetsApi (they target a channel and are Editor-gated). The gallery itself is channel-agnostic — it lists what
// is available across the whole platform — so neither read takes a channelId. The state holder depends on this
// interface and fakes it in tests without HTTP.
//
// Backend routes (WidgetGalleryController):
//   GET /api/v1/widget-gallery                    →  PaginatedResponse<GalleryItemSummary>  (framework? + trustTier? filters)
//   GET /api/v1/widget-gallery/{galleryItemId}    →  StatusResponseDto<GalleryItemDetail>   (the full item incl. its source)
interface WidgetGalleryApi {
    /**
     * Browse the verified widget catalogue, newest page first. Optional [framework] / [trustTier] narrow the
     * list; a blank/null filter is omitted from the query so an unfiltered browse sends neither.
     */
    suspend fun listGallery(
        framework: String? = null,
        trustTier: String? = null,
        page: Int = 1,
        pageSize: Int = 50,
    ): ApiResult<List<GalleryItemSummary>>

    /** One gallery item in full — carries its [GalleryItemDetail.sourceCode] + default config for the preview. */
    suspend fun getGalleryItem(galleryItemId: String): ApiResult<GalleryItemDetail>
}

class RestWidgetGalleryApi(private val client: ApiClient) : WidgetGalleryApi {
    override suspend fun listGallery(
        framework: String?,
        trustTier: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<List<GalleryItemSummary>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), read whole-body with getDirect — same shape
        // as the widgets / commands lists. The filters append only when set so an unfiltered browse omits them.
        val query: StringBuilder = StringBuilder("api/v1/widget-gallery?page=$page&pageSize=$pageSize")
        framework?.takeIf { it.isNotBlank() }?.let { query.append("&framework=").append(it) }
        trustTier?.takeIf { it.isNotBlank() }?.let { query.append("&trustTier=").append(it) }
        return when (
            val result: ApiResult<PaginatedEnvelope<GalleryItemSummary>> = client.getDirect(query.toString())
        ) {
            is ApiResult.Failure -> ApiResult.Failure(result.error)
            is ApiResult.Ok -> ApiResult.Ok(result.value.data)
        }
    }

    override suspend fun getGalleryItem(galleryItemId: String): ApiResult<GalleryItemDetail> =
        client.getEnvelope("api/v1/widget-gallery/$galleryItemId")
}

/**
 * A row in the public widget-gallery browse list (backend `GalleryItemSummary`): its [id], display [name],
 * optional [description], the source [framework] (`vanilla | vue | react | svelte`), the [trustTier]
 * (`first_party | verified_community | unverified`), how many channels have installed it ([installCount]), and
 * whether it is offered on the hosted SaaS tier ([availableInSaaS]). The heavy fields (source, default config)
 * load only on the detail read.
 */
@Serializable
data class GalleryItemSummary(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val framework: String = "",
    val trustTier: String = "",
    val installCount: Int = 0,
    val availableInSaaS: Boolean = false,
)

/**
 * A single gallery item in full (backend `GalleryItemDetail`): the summary fields plus everything the install /
 * clone UI needs to preview and pre-configure — the item's [sourceCode] (the preview pane), its declared
 * [defaultSettings] / [defaultEventSubscriptions] (applied on install), and its [sourceKind] provenance
 * (`in_repo | github`). Unknown fields are ignored (`ignoreUnknownKeys` on the shared Json).
 */
@Serializable
data class GalleryItemDetail(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val framework: String = "",
    val trustTier: String = "",
    val installCount: Int = 0,
    val availableInSaaS: Boolean = false,
    val sourceKind: String = "",
    val defaultSettings: JsonObject? = null,
    val defaultEventSubscriptions: List<String> = emptyList(),
    val sourceCode: String? = null,
)

/**
 * The gallery browse filters the browse dialog tracks as it lists (client-side only — the gallery list is a GET
 * with query params, so this is never serialized to a wire body). [framework] / [trustTier] are null for "any".
 */
data class GalleryListRequest(
    val framework: String? = null,
    val trustTier: String? = null,
    val page: Int = 1,
    val pageSize: Int = 50,
)
