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
)
