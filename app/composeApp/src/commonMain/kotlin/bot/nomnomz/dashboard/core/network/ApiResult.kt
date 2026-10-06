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

// The single result type every facade returns (frontend.md §3.1) — mirrors the backend
// StatusResponseDto<T> / problem-details envelopes. Operations never throw across the
// facade boundary or return null; they return Ok or Failure.
sealed interface ApiResult<out T> {
    data class Ok<T>(val value: T) : ApiResult<T>

    data class Failure(val error: ApiError) : ApiResult<Nothing>
}

/** A normalized backend failure (frontend.md §3.1): the HTTP status plus a human message. */
data class ApiError(
    val status: Int,
    val code: String?,
    val message: String,
    val traceId: String? = null,
    /** Every build problem a rejected project save listed, each with its file, line and column when known. */
    val errors: List<BuildError> = emptyList(),
    /** A forbidden action: the action key, the role it needs and the role the caller holds (role names). */
    val action: String? = null,
    val requiredRole: String? = null,
    val heldRole: String? = null,
)

/** One build or runtime problem the backend located in a project file (the `data.errors` items of a failed save). */
@Serializable
data class BuildError(
    val code: String? = null,
    val message: String = "",
    val file: String? = null,
    val line: Int? = null,
    val column: Int? = null,
)
