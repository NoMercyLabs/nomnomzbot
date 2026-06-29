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

// The typed custom-events facade (custom-events.md §5).  Each [CustomDataSource] lets the bot ingest an
// external data feed — heart-rate monitors, sensor boards, companion apps — via push, poll, or socket ingress.
// Every ingest fires a `custom.<name>` pipeline trigger and exposes `{{custom.<name>.<field>}}` template vars.
//
// Backend routes (CustomDataSourcesController, all under /api/v1/custom-data-sources):
//   GET  /                   →  PaginatedResponse<CustomDataSourceDto>
//   POST /                   →  StatusResponseDto<CustomDataSourceDto>
//   GET  /presets            →  StatusResponseDto<List<CustomDataSourcePresetDto>>
//   GET  /{id}               →  StatusResponseDto<CustomDataSourceDto>
//   PUT  /{id}               →  StatusResponseDto<CustomDataSourceDto>
//   DELETE /{id}             →  StatusResponseDto<bool>
//   POST /{id}/test          →  StatusResponseDto<bool>
interface CustomEventsApi {
    /** The channel's configured data sources, ordered by display name. */
    suspend fun list(): ApiResult<List<CustomDataSource>>

    /** Available quick-start presets (Pulsoid, HypeRate, …). */
    suspend fun listPresets(): ApiResult<List<CustomDataSourcePreset>>

    /** Create a new custom data source for the authenticated channel. */
    suspend fun create(body: UpsertCustomDataSourceBody): ApiResult<CustomDataSource>

    /** Update an existing data source by [id]. */
    suspend fun update(id: String, body: UpsertCustomDataSourceBody): ApiResult<CustomDataSource>

    /** Soft-delete a data source. */
    suspend fun delete(id: String): ApiResult<Unit>

    /**
     * Fire [samplePayload] through the ingest pipeline — useful to preview the field extraction and pipeline
     * trigger before deploying the real feed.
     */
    suspend fun test(id: String, samplePayload: String): ApiResult<Unit>
}

class RestCustomEventsApi(private val client: ApiClient) : CustomEventsApi {

    override suspend fun list(): ApiResult<List<CustomDataSource>> =
        when (val page: ApiResult<PaginatedEnvelope<CustomDataSource>> =
            client.getDirect("api/v1/custom-data-sources?page=1&take=50")) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun listPresets(): ApiResult<List<CustomDataSourcePreset>> =
        client.getEnvelope("api/v1/custom-data-sources/presets")

    override suspend fun create(body: UpsertCustomDataSourceBody): ApiResult<CustomDataSource> =
        client.postEnvelope("api/v1/custom-data-sources", body)

    override suspend fun update(id: String, body: UpsertCustomDataSourceBody): ApiResult<CustomDataSource> =
        client.putEnvelope("api/v1/custom-data-sources/$id", body)

    override suspend fun delete(id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/custom-data-sources/$id")

    override suspend fun test(id: String, samplePayload: String): ApiResult<Unit> =
        client.postUnit("api/v1/custom-data-sources/$id/test", TestCustomDataSourceBody(samplePayload))
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

/**
 * A configured custom data source (mirrors `CustomDataSourceDto`). [hasAuthSecret] is true when an auth token
 * is stored; the token itself is never echoed by the backend — send a new value in [UpsertCustomDataSourceBody]
 * to rotate it.
 */
@Serializable
data class CustomDataSource(
    val id: String = "",
    val name: String = "",
    val displayName: String = "",
    /** One of `push`, `poll`, or `socket`. */
    val sourceKind: String = "",
    val presetKey: String? = null,
    val endpointUrl: String? = null,
    val hasAuthSecret: Boolean = false,
    val fieldMap: Map<String, String> = emptyMap(),
    val pollIntervalSeconds: Int? = null,
    val isEnabled: Boolean = false,
    val lastReceivedAt: String? = null,
)

/** A quick-start preset descriptor (e.g. Pulsoid, HypeRate). */
@Serializable
data class CustomDataSourcePreset(
    val key: String = "",
    val displayName: String = "",
    val sourceKind: String = "",
)

/** Create / update request body (mirrors `UpsertCustomDataSourceRequest`). */
@Serializable
data class UpsertCustomDataSourceBody(
    val name: String,
    val displayName: String,
    val sourceKind: String,
    val presetKey: String? = null,
    val endpointUrl: String? = null,
    /** Plaintext auth token. Omit (null) to keep the existing secret; send empty string to clear. */
    val authSecret: String? = null,
    val fieldMap: Map<String, String> = emptyMap(),
    val pollIntervalSeconds: Int? = null,
    val isEnabled: Boolean = false,
)

/** Body for the test endpoint. */
@Serializable
data class TestCustomDataSourceBody(val samplePayload: String)
