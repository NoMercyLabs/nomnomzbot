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
import kotlinx.serialization.json.JsonArray
import kotlinx.serialization.json.JsonElement
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.JsonPrimitive
import kotlinx.serialization.json.booleanOrNull
import kotlinx.serialization.json.contentOrNull

// The typed pipelines facade — a channel's visual automation pipelines (the action-chain engine the
// Pipelines page edits). Real data only: the backend lists/stores the channel's pipelines; the editor's
// action chain round-trips through the pipeline's `graph` JSON exactly as the PipelineEngine reads it.
// State holders depend on this interface and fake it in tests without HTTP.
//
// Backend routes (PipelinesController, base `/api/v1/channels/{channelId}/pipelines`):
//   GET    .                →  PaginatedResponse<PipelineListItemDto>   (the list view)
//   GET    ./{id}           →  StatusResponseDto<PipelineDto>           (full detail incl. the graph)
//   POST   .                →  StatusResponseDto<PipelineDto>  (201)    (CreatePipelineDto)
//   PUT    ./{id}           →  StatusResponseDto<PipelineDto>           (UpdatePipelineDto)
//   DELETE ./{id}           →  204 No Content
//
// The `graph` is the backend's free-form pipeline definition (PipelineDefinition): an object with a `steps`
// array, each step `{ condition?: {type, ...params}, action: {type, ...params}, stop_on_match: bool }`. The
// PipelineEngine reads `action.type` / `condition.type` plus flat extension-data params, so the editor builds
// exactly that flat shape (the `type` field sits alongside its params, not nested) — see [PipelineGraph].
interface PipelinesApi {
    /** The channel's pipelines — the lightweight list-view items (first page). */
    suspend fun list(channelId: String): ApiResult<List<PipelineSummary>>

    /**
     * The action + condition palette the builder renders from — every registered `ICommandAction` (grouped by
     * category) and `ICommandCondition`, sourced from the backend registry (`GET pipelines/actions`) so the
     * builder can never drift out of sync with the blocks the engine actually runs.
     */
    suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote>

    /** A single pipeline's full detail, including the decoded action-chain [PipelineGraph]. */
    suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail>

    /** Create a new pipeline (name + optional description + the initial graph). Succeeds on the backend's 201. */
    suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit>

    /**
     * Create a pipeline and return the created [PipelineDetail] (with its server-assigned id) — used by the
     * event-responses "create-and-bind" flow, which needs the new id to bind immediately.
     */
    suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail>

    /** Update a pipeline — any null field is left untouched (partial patch). Used for rename, toggle, and graph edits. */
    suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit>

    /** Delete a pipeline. */
    suspend fun delete(channelId: String, id: String): ApiResult<Unit>
}

class RestPipelinesApi(private val client: ApiClient) : PipelinesApi {

    override suspend fun list(channelId: String): ApiResult<List<PipelineSummary>> {
        // PaginatedResponse is a flat `{ data: [...] }` (not the single-value StatusResponseDto envelope), read
        // with getDirect like the command/timer lists. First page only — the page reloads after every write.
        return when (
            val page: ApiResult<PaginatedEnvelope<PipelineSummary>> =
                client.getDirect("api/v1/channels/$channelId/pipelines?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    override suspend fun catalogue(channelId: String): ApiResult<PipelineCatalogueRemote> =
        client.getEnvelope("api/v1/channels/$channelId/pipelines/actions")

    override suspend fun get(channelId: String, id: String): ApiResult<PipelineDetail> =
        client.getEnvelope("api/v1/channels/$channelId/pipelines/$id")

    // The writes return `StatusResponseDto<PipelineDto>`, but the page reloads the list/detail on success rather
    // than splicing the returned row, so only the 2xx matters — postUnit / putUnit / deleteUnit ignore the body.
    override suspend fun create(channelId: String, body: CreatePipelineBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/pipelines", body)

    override suspend fun createReturning(channelId: String, body: CreatePipelineBody): ApiResult<PipelineDetail> =
        client.postEnvelope("api/v1/channels/$channelId/pipelines", body)

    override suspend fun update(channelId: String, id: String, body: UpdatePipelineBody): ApiResult<Unit> =
        client.putUnit("api/v1/channels/$channelId/pipelines/$id", body)

    override suspend fun delete(channelId: String, id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/pipelines/$id")
}

// ── Action-catalogue DTOs (the backend-sourced palette — mirror the backend) ──

/**
 * One available pipeline action block (backend `PipelineActionDescriptorDto`): its snake_case [type]
 * discriminator, the [category] it groups under in the palette, and a human [description]. The backend
 * discovers these from the registered `ICommandAction` implementations, so the palette can never drift.
 */
@Serializable
data class PipelineActionDescriptor(
    val type: String = "",
    val category: String = "general",
    val description: String = "",
)

/** One available pipeline condition type (backend `PipelineConditionDescriptorDto`). */
@Serializable
data class PipelineConditionDescriptor(
    val type: String = "",
)

/**
 * The full builder palette (backend `PipelineCatalogueDto`): every registered [actions] block (grouped by
 * category by the client) and every available [conditions] gate. The client merges these backend-sourced
 * identities with its local field HINTS (parameter shapes the backend contract does not carry) — see
 * [PipelineCatalogue.buildPalette].
 */
@Serializable
data class PipelineCatalogueRemote(
    val actions: List<PipelineActionDescriptor> = emptyList(),
    val conditions: List<PipelineConditionDescriptor> = emptyList(),
)

// ── List + detail DTOs (mirror the backend) ──────────────────────────────────

/**
 * A pipeline's list-view item (backend `PipelineListItemDto`): the [name], an optional [description], whether
 * it is on, the lifetime [triggerCount], and when it last ran. The action chain (the graph) lives on the
 * detail DTO, not the list item. camelCase JSON; the timestamps the backend also carries are ignored here.
 */
@Serializable
data class PipelineSummary(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val isEnabled: Boolean = false,
    val triggerCount: Int = 0,
)

/**
 * A pipeline's full detail (backend `PipelineDto`). The backend's `graph` is free-form JSON, so it deserializes
 * to a [JsonElement] here and is decoded into the editable [PipelineGraph] by [graph]. The editor reads/writes
 * the decoded chain; on save it re-encodes to the same flat shape the PipelineEngine consumes.
 */
@Serializable
data class PipelineDetail(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val isEnabled: Boolean = false,
    val triggerCount: Int = 0,
    val graph: JsonElement? = null,
) {
    /** The decoded action chain — an empty chain when the stored graph is null/blank/`{}`. */
    val chain: PipelineGraph
        get() = PipelineGraph.fromJson(graph)
}

// ── Request bodies (mirror the backend) ──────────────────────────────────────

/**
 * Create-pipeline request (backend `CreatePipelineDto`): a [name], optional [description], the starting
 * [isEnabled] flag, and the initial [graph] (the encoded action chain). A pipeline can be created already-on
 * with a starter chain, matching the backend defaults.
 */
@Serializable
data class CreatePipelineBody(
    val name: String,
    val description: String? = null,
    val isEnabled: Boolean = true,
    val graph: JsonObject,
)

/**
 * Update-pipeline request (backend `UpdatePipelineDto`) — every field nullable so an update is a partial patch.
 * A rename sends only [name]; a toggle sends only [isEnabled]; a chain edit sends [graph]. `explicitNulls =
 * false` on the shared Json omits the null fields from the wire body, so each write touches only what it sets.
 */
@Serializable
data class UpdatePipelineBody(
    val name: String? = null,
    val description: String? = null,
    val isEnabled: Boolean? = null,
    val graph: JsonObject? = null,
)

// ── The editable action chain (the editor's working model) ────────────────────

/**
 * The decoded pipeline graph the editor works on: an ordered list of [steps]. This is the client-side mirror
 * of the backend's `PipelineDefinition` (`{ "steps": [...] }`). [toJson] re-encodes it to exactly the wire
 * shape the PipelineEngine reads, so what the editor saves is what the engine runs.
 */
data class PipelineGraph(val steps: List<PipelineStep> = emptyList()) {

    /** Encode to the backend `{ "steps": [ ... ] }` JSON the PipelineEngine deserializes. */
    fun toJson(): JsonObject =
        JsonObject(mapOf("steps" to JsonArray(steps.map { it.toJson() })))

    companion object {
        /**
         * Decode a stored graph. A null / non-object / `steps`-less graph (a brand-new pipeline serializes
         * `{}`) yields an empty chain — the editor opens on a blank, addable chain rather than failing.
         */
        fun fromJson(graph: JsonElement?): PipelineGraph {
            val obj: JsonObject = (graph as? JsonObject) ?: return PipelineGraph()
            val rawSteps: List<JsonElement> = (obj["steps"] as? JsonArray) ?: return PipelineGraph()
            return PipelineGraph(rawSteps.mapNotNull { PipelineStep.fromJson(it) })
        }
    }
}

/**
 * One step in the chain: a required [action] (what to do), an optional [condition] (the gate that must pass),
 * and [stopOnMatch] — when the condition matched and the action ran, stop the rest of the chain. Mirrors the
 * backend `PipelineStepDefinition` (`{ condition?, action, stop_on_match }`).
 */
data class PipelineStep(
    val action: PipelineNode,
    val condition: PipelineNode? = null,
    val stopOnMatch: Boolean = false,
) {
    fun toJson(): JsonObject {
        val map: MutableMap<String, JsonElement> = mutableMapOf("action" to action.toJson())
        condition?.let { map["condition"] = it.toJson() }
        if (stopOnMatch) map["stop_on_match"] = JsonPrimitive(true)
        return JsonObject(map)
    }

    companion object {
        fun fromJson(element: JsonElement): PipelineStep? {
            val obj: JsonObject = element as? JsonObject ?: return null
            val action: PipelineNode = PipelineNode.fromJson(obj["action"]) ?: return null
            val condition: PipelineNode? = PipelineNode.fromJson(obj["condition"])
            val stop: Boolean = (obj["stop_on_match"] as? JsonPrimitive)?.booleanOrNull ?: false
            return PipelineStep(action = action, condition = condition, stopOnMatch = stop)
        }
    }
}

/**
 * An action or condition node: its [type] (the backend's snake_case discriminator, e.g. `send_message`,
 * `user_role`) plus the flat [params] map keyed by the backend's parameter names. The backend stores `type`
 * inline with the params (extension data), so [toJson] writes `{ "type": <type>, <param>: <value>, ... }` and
 * [fromJson] reads it back. Params are kept as strings here (the editor edits text/number fields as text);
 * numeric params are written as JSON numbers so the engine's `GetInt` reads them.
 */
data class PipelineNode(val type: String, val params: Map<String, String> = emptyMap()) {

    fun toJson(): JsonObject {
        val map: MutableMap<String, JsonElement> = mutableMapOf("type" to JsonPrimitive(type))
        for ((key, value) in params) {
            map[key] = encodeParam(type, key, value)
        }
        return JsonObject(map)
    }

    // A param the editor declares as numeric is written as a JSON number so the engine's GetInt(...) reads it;
    // everything else stays a JSON string (the engine's GetString tolerates both). Booleans (stop flags) are
    // modelled on the step, not here, so only number/string params occur on a node.
    private fun encodeParam(nodeType: String, key: String, value: String): JsonElement {
        val isNumeric: Boolean =
            PipelineCatalogue.fieldFor(nodeType, key)?.kind == FieldKind.Number
        if (isNumeric) {
            val number: Int? = value.trim().toIntOrNull()
            if (number != null) return JsonPrimitive(number)
        }
        return JsonPrimitive(value)
    }

    companion object {
        /** Read a node back from `{ "type": <type>, <param>: <value>, ... }`; null when absent or typeless. */
        fun fromJson(element: JsonElement?): PipelineNode? {
            val obj: JsonObject = element as? JsonObject ?: return null
            val type: String =
                (obj["type"] as? JsonPrimitive)?.contentOrNull ?: return null
            val params: Map<String, String> =
                obj
                    .filterKeys { it != "type" }
                    .mapValues { (_, value) -> primitiveText(value) }
            return PipelineNode(type = type, params = params)
        }

        // Read any flat param value as the editor's display text: a JSON string keeps its content, a number/
        // bool render as their literal text. Nested objects/arrays are not produced by the editor and read
        // back as their raw JSON (harmless — the editor only surfaces catalogued scalar fields).
        private fun primitiveText(value: JsonElement): String =
            when (value) {
                is JsonPrimitive -> value.contentOrNull ?: value.toString()
                else -> value.toString()
            }
    }
}
