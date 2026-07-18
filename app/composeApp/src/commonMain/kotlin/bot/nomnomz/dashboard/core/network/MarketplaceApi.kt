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

import io.ktor.http.ContentType
import kotlinx.serialization.Serializable

// The typed hosted-marketplace facade (bundles.md §6) — the OPTIONAL hosted catalogue of shareable bundles: browse
// published items, read one, install it into the channel (reusing the same [InstalledBundle] row as a local
// import), and — for publishers — submit a bundle for review with a publisher token. The whole marketplace is a
// hosted add-on that a self-host deployment may not run: EVERY call can fail with `503`/`MARKETPLACE_UNAVAILABLE`,
// which the controller renders as an honest "not available yet" state, NOT an error. The state holder depends on
// this interface and fakes it in tests without HTTP.
//
// Backend routes (MarketplaceController), all channel-scoped:
//   GET    /api/v1/channels/{channelId}/marketplace/items                 →  PaginatedResponse<MarketplaceItem>
//   GET    /api/v1/channels/{channelId}/marketplace/items/{itemId}        →  StatusResponseDto<MarketplaceItem>
//   POST   /api/v1/channels/{channelId}/marketplace/items/{itemId}/install→  StatusResponseDto<InstalledBundle>
//   POST   /api/v1/channels/{channelId}/marketplace/publish               →  StatusResponseDto<PublishSubmission>
//   GET    /api/v1/channels/{channelId}/marketplace/submissions/{id}      →  StatusResponseDto<PublishSubmission>
//   GET    /api/v1/channels/{channelId}/marketplace/publisher-token       →  StatusResponseDto<PublisherTokenStatus>
//   PUT    /api/v1/channels/{channelId}/marketplace/publisher-token       →  204 No Content
//   DELETE /api/v1/channels/{channelId}/marketplace/publisher-token       →  204 No Content
interface MarketplaceApi {
    /**
     * Browse the catalogue. [q] / [type] / [tags] filter when set; [page] / [pageSize] always page. Can fail with
     * `503` / `MARKETPLACE_UNAVAILABLE` when the hosted marketplace is not running — the caller distinguishes that
     * from a real error and shows the unavailable state.
     */
    suspend fun items(
        channelId: String,
        q: String?,
        type: String?,
        tags: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<List<MarketplaceItem>>

    /** Read one catalogue item by [itemId]. */
    suspend fun item(channelId: String, itemId: String): ApiResult<MarketplaceItem>

    /** Install a catalogue item into the channel under a conflict [policy]. Returns the resulting installed row. */
    suspend fun install(channelId: String, itemId: String, policy: String): ApiResult<InstalledBundle>

    /**
     * Submit a bundle ZIP for review (multipart: the file plus the `Name` / `Version` / `Summary` / `Tags` fields).
     * Returns the created [PublishSubmission] so the publisher can track its review [PublishSubmission.status].
     */
    suspend fun publish(
        channelId: String,
        fileName: String,
        bytes: ByteArray,
        name: String,
        version: String,
        summary: String,
        tagsCsv: String,
    ): ApiResult<PublishSubmission>

    /** Read a publish submission's review status by its [id]. */
    suspend fun submission(channelId: String, id: String): ApiResult<PublishSubmission>

    /** Whether the channel has a publisher token stored (never echoes the token itself). */
    suspend fun publisherToken(channelId: String): ApiResult<PublisherTokenStatus>

    /** Store (or replace) the channel's publisher token — write-only; the value is never read back. */
    suspend fun setPublisherToken(channelId: String, token: String): ApiResult<Unit>

    /** Remove the channel's stored publisher token. */
    suspend fun clearPublisherToken(channelId: String): ApiResult<Unit>
}

class RestMarketplaceApi(private val client: ApiClient) : MarketplaceApi {

    override suspend fun items(
        channelId: String,
        q: String?,
        type: String?,
        tags: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<List<MarketplaceItem>> {
        // page/pageSize always present; the optional filters append only when set. Flat PaginatedResponse, read
        // with getDirect and unwrapped to its data array (like the reward/command lists).
        val query: StringBuilder = StringBuilder("?page=$page&pageSize=$pageSize")
        if (!q.isNullOrBlank()) query.append("&q=$q")
        if (!type.isNullOrBlank()) query.append("&type=$type")
        if (!tags.isNullOrBlank()) query.append("&tags=$tags")
        return when (
            val paged: ApiResult<PaginatedEnvelope<MarketplaceItem>> =
                client.getDirect("api/v1/channels/$channelId/marketplace/items$query")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(paged.error)
            is ApiResult.Ok -> ApiResult.Ok(paged.value.data)
        }
    }

    override suspend fun item(channelId: String, itemId: String): ApiResult<MarketplaceItem> =
        client.getEnvelope("api/v1/channels/$channelId/marketplace/items/$itemId")

    override suspend fun install(
        channelId: String,
        itemId: String,
        policy: String,
    ): ApiResult<InstalledBundle> =
        client.postEnvelope(
            "api/v1/channels/$channelId/marketplace/items/$itemId/install",
            MarketplaceInstallBody(policy = policy),
        )

    override suspend fun publish(
        channelId: String,
        fileName: String,
        bytes: ByteArray,
        name: String,
        version: String,
        summary: String,
        tagsCsv: String,
    ): ApiResult<PublishSubmission> =
        client.postMultipartWithFields(
            "api/v1/channels/$channelId/marketplace/publish",
            fileFieldName = "file",
            fileName = fileName,
            fileBytes = bytes,
            fileContentType = ContentType.parse("application/zip"),
            fields =
                mapOf(
                    "Name" to name,
                    "Version" to version,
                    "Summary" to summary,
                    "Tags" to tagsCsv,
                ),
        )

    override suspend fun submission(channelId: String, id: String): ApiResult<PublishSubmission> =
        client.getEnvelope("api/v1/channels/$channelId/marketplace/submissions/$id")

    override suspend fun publisherToken(channelId: String): ApiResult<PublisherTokenStatus> =
        client.getEnvelope("api/v1/channels/$channelId/marketplace/publisher-token")

    override suspend fun setPublisherToken(channelId: String, token: String): ApiResult<Unit> =
        client.putUnit(
            "api/v1/channels/$channelId/marketplace/publisher-token",
            PublisherTokenBody(token = token),
        )

    override suspend fun clearPublisherToken(channelId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/marketplace/publisher-token")
}

/**
 * One catalogue item (backend `MarketplaceItemDto`): its [itemId], display [name], [author], [version], a short
 * [summary], the content [type], searchable [tags], the [capabilities] it declares, and its social proof
 * ([rating] 0–5, lifetime [installs]). Defaults everywhere so a lenient/partial row still renders.
 */
@Serializable
data class MarketplaceItem(
    val itemId: String = "",
    val name: String = "",
    val author: String = "",
    val version: String = "",
    val summary: String = "",
    val type: String = "",
    val tags: List<String> = emptyList(),
    val capabilities: List<String> = emptyList(),
    val rating: Double = 0.0,
    val installs: Long = 0,
)

/** The install request body (backend `MarketplaceInstallRequest`): the conflict [policy] (see [ImportPolicy]). */
@Serializable
data class MarketplaceInstallBody(val policy: String)

/**
 * A publish submission (backend `PublishSubmission`): its [submissionId], review [status] (e.g. pending /
 * approved / rejected), and an optional reviewer [reviewNote]. Defaults so a partial response still deserializes.
 */
@Serializable
data class PublishSubmission(
    val submissionId: String = "",
    val status: String = "",
    val reviewNote: String? = null,
)

/** Whether the channel has a publisher token stored (backend `PublisherTokenStatus`) — never the token itself. */
@Serializable
data class PublisherTokenStatus(val hasToken: Boolean = false)

/** The set-publisher-token body (backend `MarketplacePublisherTokenRequest`): the write-only [token]. */
@Serializable
data class PublisherTokenBody(val token: String)
