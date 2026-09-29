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
 * (AuthController: `{ accessToken, refreshToken, expiresIn, user, impersonation }`).
 *
 * [impersonation] is set only by `/auth/refresh` while an act-as session is open: [accessToken] and [user] are
 * then the impersonated user's and no refresh token comes back. It is the ONLY signal a reload uses to decide
 * it boots as someone else — the dashboard keeps no act-as marker of its own.
 */
@Serializable
data class AuthPayload(
    val accessToken: String,
    val refreshToken: String? = null,
    val expiresIn: Long? = null,
    val user: AuthUser? = null,
    val impersonation: ActAsSessionInfo? = null,
)

/** The support session an act-as refresh runs under: its id and when it ends (ISO-8601). */
@Serializable
data class ActAsSessionInfo(val sessionId: String, val expiresAt: String)

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

/**
 * The `StatusResponseDto` error envelope — the OTHER shape a failing endpoint answers with. Controllers that
 * map a `Result` failure (every `BadRequestResponse`/`ConflictResponse`/… helper in `BaseController`) return
 * `{"status":"error","message":"...","code":"..."}` rather than problem details, so the reason — and the
 * machine-readable `code` (the failing `Result.ErrorCode`, e.g. `PROVIDER_NOT_CONFIGURED`) — has to be read
 * from here or it is lost and the caller sees only a bare status code.
 */
@Serializable
data class ErrorEnvelope(val message: String? = null, val code: String? = null)

/** RFC-7807 problem details the backend returns for 4xx/5xx. */
@Serializable
data class ProblemDetails(
    val type: String? = null,
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
    @SerialName("traceId") val traceId: String? = null,
)
