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

// Plane-C platform content authoring + propagation (platform-admin.md §2-§5,
// PlatformContentController — 9 routes). Draft/publish versioned platform content (this slice: `command`
// kind only, server-side) and fan it out to installed tenant rows under one of the three publish modes
// (§2.1). `saas`-only platform-employee surface (§0 marker) — the whole controller is gated behind Plane-C
// `content:*` action keys and never reachable on a self-host profile.

import kotlinx.serialization.Serializable

// ─── DTOs (mirror server/openapi/v1.json — PlatformContentDefinitionDto/…) ────────────────────

/** One platform content definition row — the definitions list (PlatformContentDefinitionDto). */
@Serializable
data class PlatformContentDefinition(
    val id: String,
    val kind: String,
    val key: String,
    val displayName: String,
    val description: String? = null,
    val currentVersionId: String? = null,
    val currentVersion: Int? = null,
    val latestDraftVersionId: String? = null,
    val createdAt: String,
    val retiredAt: String? = null,
)

/** A definition plus its full version history (PlatformContentDefinitionDetailDto). */
@Serializable
data class PlatformContentDefinitionDetail(
    val definition: PlatformContentDefinition,
    val versions: List<PlatformContentVersion> = emptyList(),
)

/** One immutable content version — a draft until [publishedAt] is set (PlatformContentVersionDto). */
@Serializable
data class PlatformContentVersion(
    val id: String,
    val definitionId: String,
    val version: Int,
    val contentHash: String,
    val payloadJson: String,
    val renderGalleryRefs: List<String> = emptyList(),
    val publishNote: String? = null,
    val draftedAt: String,
    val draftedByPrincipalId: String,
    val publishedAt: String? = null,
    val publishedByPrincipalId: String? = null,
)

/** The blast-radius preview (§2.1) — rendered before a publish's confirm control enables
 * (PublishPreviewDto). */
@Serializable
data class PublishPreview(
    val affectedCount: Int,
    val skippedCount: Int,
    val sampleTenantNames: List<String> = emptyList(),
)

/** One publish attempt (PlatformContentPublishJobDto). */
@Serializable
data class PlatformContentPublishJob(
    val id: String,
    val definitionId: String,
    val fromVersion: Int? = null,
    val toVersion: Int,
    val mode: String,
    val requestedByPrincipalId: String,
    val requestedAt: String,
    val previewAffectedCount: Int,
    val previewSkippedCount: Int,
    val confirmedAffectedCount: Int? = null,
    val status: String,
    val completedAt: String? = null,
    val failureReason: String? = null,
)

/** The three publish modes §2.1 defines — matches the backend's `PlatformContentPublishModes` verbatim. */
object PlatformContentPublishModes {
    const val PublishAsNew: String = "publish_as_new"
    const val UpdateInPlaceWhereUntouched: String = "update_in_place_where_untouched"
    const val Force: String = "force"
}

// ─── Request bodies ────────────────────────────────────────────────────────────────────────────

/** Creates a new definition with its first draft version (CreateContentDefinitionRequest). */
@Serializable
data class CreateContentDefinitionBody(
    val kind: String,
    val key: String,
    val displayName: String,
    val description: String? = null,
    val payloadJson: String,
)

/** Drafts a new version on an existing definition (DraftContentVersionRequest). */
@Serializable
data class DraftContentVersionBody(
    val payloadJson: String,
    val renderGalleryRefs: List<String>? = null,
)

/** Requests the counted blast radius for one publish mode, ahead of committing it
 * (PublishPreviewRequest). */
@Serializable
data class PublishPreviewBody(val mode: String)

/** Commits a publish. [confirmedPreviewAffectedCount] must byte-match the immediately-prior preview's
 * [PublishPreview.affectedCount] or the server fails closed with `PREVIEW_STALE` (PublishContentRequest). */
@Serializable
data class PublishContentBody(
    val mode: String,
    val publishNote: String? = null,
    val confirmedPreviewAffectedCount: Int,
)

// ─── API interface + implementation ───────────────────────────────────────────────────────────

interface PlatformContentApi {
    suspend fun listDefinitions(
        kind: String? = null,
        page: Int = 1,
        pageSize: Int = 25,
    ): ApiResult<PaginatedEnvelope<PlatformContentDefinition>>

    suspend fun getDefinition(definitionId: String): ApiResult<PlatformContentDefinitionDetail>

    suspend fun createDefinition(body: CreateContentDefinitionBody): ApiResult<PlatformContentDefinition>

    suspend fun draftVersion(definitionId: String, body: DraftContentVersionBody): ApiResult<PlatformContentVersion>

    suspend fun getVersion(definitionId: String, versionId: String): ApiResult<PlatformContentVersion>

    suspend fun previewPublish(
        definitionId: String,
        versionId: String,
        body: PublishPreviewBody,
    ): ApiResult<PublishPreview>

    suspend fun publish(
        definitionId: String,
        versionId: String,
        body: PublishContentBody,
    ): ApiResult<PlatformContentPublishJob>

    suspend fun getPublishJob(publishJobId: String): ApiResult<PlatformContentPublishJob>

    suspend fun retireDefinition(definitionId: String): ApiResult<Unit>
}

class PlatformContentApiImpl(private val client: ApiClient) : PlatformContentApi {
    override suspend fun listDefinitions(
        kind: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<PaginatedEnvelope<PlatformContentDefinition>> {
        val query: String = buildQuery(
            "page" to page.toString(),
            "take" to pageSize.toString(),
            "kind" to kind?.takeIf { it.isNotBlank() },
        )
        return client.getDirect("api/v1/platform/content/definitions$query")
    }

    override suspend fun getDefinition(definitionId: String): ApiResult<PlatformContentDefinitionDetail> =
        client.getEnvelope("api/v1/platform/content/definitions/$definitionId")

    override suspend fun createDefinition(body: CreateContentDefinitionBody): ApiResult<PlatformContentDefinition> =
        client.postEnvelope("api/v1/platform/content/definitions", body)

    override suspend fun draftVersion(
        definitionId: String,
        body: DraftContentVersionBody,
    ): ApiResult<PlatformContentVersion> =
        client.postEnvelope("api/v1/platform/content/definitions/$definitionId/versions", body)

    override suspend fun getVersion(definitionId: String, versionId: String): ApiResult<PlatformContentVersion> =
        client.getEnvelope("api/v1/platform/content/definitions/$definitionId/versions/$versionId")

    override suspend fun previewPublish(
        definitionId: String,
        versionId: String,
        body: PublishPreviewBody,
    ): ApiResult<PublishPreview> =
        client.postEnvelope(
            "api/v1/platform/content/definitions/$definitionId/versions/$versionId/publish-preview",
            body,
        )

    override suspend fun publish(
        definitionId: String,
        versionId: String,
        body: PublishContentBody,
    ): ApiResult<PlatformContentPublishJob> =
        client.postEnvelope(
            "api/v1/platform/content/definitions/$definitionId/versions/$versionId/publish",
            body,
        )

    override suspend fun getPublishJob(publishJobId: String): ApiResult<PlatformContentPublishJob> =
        client.getEnvelope("api/v1/platform/content/publish-jobs/$publishJobId")

    override suspend fun retireDefinition(definitionId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/platform/content/definitions/$definitionId")

    /** Builds a `?a=1&b=2` query string from non-null pairs, percent-encoding each value. */
    private fun buildQuery(vararg params: Pair<String, String?>): String {
        val parts: List<String> = params.mapNotNull { (key, value) ->
            value?.let { "$key=${it.encodeQuery()}" }
        }
        return if (parts.isEmpty()) "" else "?" + parts.joinToString("&")
    }
}
