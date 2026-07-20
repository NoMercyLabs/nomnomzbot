// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.requirements

import java.io.File
import kotlin.test.fail
import kotlinx.serialization.json.Json
import kotlinx.serialization.json.JsonObject
import kotlinx.serialization.json.jsonObject

// Shared plumbing for the "completeness requirement" suite (the inverse of ApiContractTest).
//
// ApiContractTest guards ONE direction — a Kotlin DTO must never carry a field the backend schema lacks
// (drift). The requirement suite in this package demands the OPPOSITE, whole-surface property the project
// mandates: the dashboard must be a COMPLETE reflection of the backend API — every endpoint reachable from the
// network layer, every response field carried by a DTO, every request input carried by a body. A gap here is
// not a bug in the test; it is the backlog, surfaced as a red assertion on purpose.
//
// The single source of truth is the committed OpenAPI snapshot (server/openapi/v1.json) — the same document
// ApiContractTest reads — so these tests are self-contained, deterministic, and need no running server.
internal object BackendContract {

    /** Repo root = the nearest ancestor of the test working directory that holds the committed OpenAPI snapshot. */
    fun repoRoot(): File {
        var dir: File? = File(System.getProperty("user.dir"))
        while (dir != null) {
            if (File(dir, "server/openapi/v1.json").exists()) return dir
            dir = dir.parentFile
        }
        fail("Could not locate the repo root (server/openapi/v1.json) from ${System.getProperty("user.dir")}")
    }

    /** The parsed OpenAPI document. */
    val spec: JsonObject by lazy {
        Json.parseToJsonElement(File(repoRoot(), "server/openapi/v1.json").readText()).jsonObject
    }

    /** Every operation path the backend exposes (`/api/v{version}/...`), keyed to its path-item object. */
    fun paths(): Map<String, JsonObject> = spec["paths"]!!.jsonObject.mapValues { it.value.jsonObject }

    /** components.schemas as a { name -> schema } map. */
    fun schemas(): Map<String, JsonObject> =
        spec["components"]!!.jsonObject["schemas"]!!.jsonObject.mapValues { it.value.jsonObject }

    /**
     * The top-level property names a backend schema declares — i.e. the JSON keys that actually go on the wire
     * for that DTO. `null` when the schema name does not exist in the document (surfaced as a distinct failure
     * so a typo can never masquerade as "fully covered").
     */
    fun backendProperties(schemaName: String): Set<String>? {
        val schema = schemas()[schemaName] ?: return null
        return schema["properties"]?.jsonObject?.keys.orEmpty()
    }
}
