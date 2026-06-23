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

import kotlinx.serialization.SerialName
import kotlinx.serialization.Serializable

// Hand-authored mirrors of the backend auth contract for this slice. These move into the
// committed OpenAPI-generated layer (core/network/generated, frontend-structure.md §5) when
// the generator task lands; the AuthApi facade keeps the same surface, so callers don't change.

/**
 * The backend's uniform envelope: `StatusResponseDto<T>` — `{ "data": <T>, ... }`. We read only
 * `data`; the success/message fields the backend also carries are not needed by the client.
 */
@Serializable
data class StatusResponse<T>(
    val data: T? = null,
    val message: String? = null,
)

/**
 * The auth payload the callback / refresh endpoints return inside `data`
 * (AuthController: `{ accessToken, refreshToken, expiresIn, user }`).
 */
@Serializable
data class AuthPayload(
    val accessToken: String,
    val refreshToken: String? = null,
    val expiresIn: Long? = null,
    val user: AuthUser? = null,
)

/** The `user` block on the auth payload — the backend `UserDto`. */
@Serializable
data class AuthUser(
    val id: String,
    val username: String,
    val displayName: String,
    val profileImageUrl: String? = null,
)

/** The `/api/v1/auth/me` payload — the backend `CurrentUserDto`. */
@Serializable
data class CurrentUser(
    val id: String,
    val username: String,
    val displayName: String,
    val profileImageUrl: String? = null,
    val color: String? = null,
    val broadcasterType: String = "",
    val isAdmin: Boolean = false,
)

/** RFC-7807 problem details the backend returns for 4xx/5xx. */
@Serializable
data class ProblemDetails(
    val type: String? = null,
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
    @SerialName("traceId") val traceId: String? = null,
)
