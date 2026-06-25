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

// The typed code-scripts facade (backend CodeScriptsController, /code-scripts). Code scripts are named,
// versioned Lua snippets attached to commands or pipeline actions. Each script has an active version
// (the published one) and an append-only version history. The editor surface lets the broadcaster:
//   • list all scripts and their current validation/runtime status;
//   • create a new script (name + description + initial source);
//   • add a new immutable version (source + publish flag to save-and-swap atomically);
//   • list prior versions of a script;
//   • publish a specific past version as current;
//   • enable or disable a script;
//   • delete a script.
//
// Backend routes (all under /api/v1/code-scripts, platform-level — NOT channel-scoped):
//   GET    /code-scripts                          →  PaginatedResponse<CodeScriptSummaryDto>
//   GET    /code-scripts/{id}                     →  StatusResponseDto<CodeScriptDetailDto>
//   POST   /code-scripts                          →  StatusResponseDto<CodeScriptSummaryDto>
//   POST   /code-scripts/{id}/versions            →  StatusResponseDto<CodeScriptVersionDto>
//   GET    /code-scripts/{id}/versions            →  PaginatedResponse<CodeScriptVersionDto>
//   POST   /code-scripts/{id}/versions/{vid}/publish →  StatusResponseDto<CodeScriptSummaryDto>
//   PUT    /code-scripts/{id}/enabled             →  StatusResponseDto<CodeScriptSummaryDto>
//   DELETE /code-scripts/{id}                     →  204 No Content
interface CodeScriptsApi {
    suspend fun list(): ApiResult<List<CodeScriptSummary>>
    suspend fun get(id: String): ApiResult<CodeScriptDetail>
    suspend fun create(body: CreateScriptBody): ApiResult<CodeScriptSummary>
    suspend fun createVersion(id: String, body: CreateVersionBody): ApiResult<CodeScriptVersion>
    suspend fun listVersions(id: String): ApiResult<List<CodeScriptVersion>>
    suspend fun publishVersion(id: String, versionId: String): ApiResult<CodeScriptSummary>
    suspend fun setEnabled(id: String, enabled: Boolean): ApiResult<CodeScriptSummary>
    suspend fun delete(id: String): ApiResult<Unit>
}

class RestCodeScriptsApi(private val client: ApiClient) : CodeScriptsApi {

    override suspend fun list(): ApiResult<List<CodeScriptSummary>> =
        when (val page: ApiResult<PaginatedEnvelope<CodeScriptSummary>> = client.getDirect("api/v1/code-scripts?page=1&pageSize=50")) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun get(id: String): ApiResult<CodeScriptDetail> =
        client.getEnvelope("api/v1/code-scripts/$id")

    override suspend fun create(body: CreateScriptBody): ApiResult<CodeScriptSummary> =
        client.postEnvelope("api/v1/code-scripts", body)

    override suspend fun createVersion(id: String, body: CreateVersionBody): ApiResult<CodeScriptVersion> =
        client.postEnvelope("api/v1/code-scripts/$id/versions", body)

    override suspend fun listVersions(id: String): ApiResult<List<CodeScriptVersion>> =
        when (val page: ApiResult<PaginatedEnvelope<CodeScriptVersion>> = client.getDirect("api/v1/code-scripts/$id/versions?page=1&pageSize=50")) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }

    override suspend fun publishVersion(id: String, versionId: String): ApiResult<CodeScriptSummary> =
        client.postEnvelope("api/v1/code-scripts/$id/versions/$versionId/publish", Unit)

    override suspend fun setEnabled(id: String, enabled: Boolean): ApiResult<CodeScriptSummary> =
        client.putEnvelope("api/v1/code-scripts/$id/enabled", SetEnabledBody(enabled))

    override suspend fun delete(id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/code-scripts/$id")
}

/** Script summary shown in the list (backend `CodeScriptSummaryDto`). */
@Serializable
data class CodeScriptSummary(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val isEnabled: Boolean = false,
    val currentVersion: Int? = null,
    val currentValidationStatus: String = "",
    val lastRuntimeError: String? = null,
    val lastRanAt: String? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/** Script detail with current version source (backend `CodeScriptDetailDto`). */
@Serializable
data class CodeScriptDetail(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val isEnabled: Boolean = false,
    val language: String = "",
    val currentVersionId: String? = null,
    val currentVersion: CodeScriptVersion? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/** One immutable version of a script (backend `CodeScriptVersionDto`). */
@Serializable
data class CodeScriptVersion(
    val id: String = "",
    val codeScriptId: String = "",
    val version: Int = 0,
    val sourceCode: String = "",
    val compiledHash: String = "",
    val validationStatus: String = "",
    val validationErrors: List<ScriptError> = emptyList(),
    val declaredCapabilities: List<String> = emptyList(),
    val publishedAt: String? = null,
    val createdAt: String = "",
)

/** A script validation error from the backend. */
@Serializable
data class ScriptError(
    val line: Int = 0,
    val column: Int = 0,
    val message: String = "",
    val severity: String = "",
)

/** Create-script body. */
@Serializable
data class CreateScriptBody(val name: String, val description: String? = null, val sourceCode: String)

/** Create-version body. */
@Serializable
data class CreateVersionBody(val sourceCode: String, val publish: Boolean = false)

@Serializable
private data class SetEnabledBody(val isEnabled: Boolean)
