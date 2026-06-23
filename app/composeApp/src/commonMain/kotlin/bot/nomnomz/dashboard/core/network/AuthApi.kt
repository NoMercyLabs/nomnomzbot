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

import io.ktor.client.call.body
import io.ktor.client.request.get
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.bodyAsText
import io.ktor.http.isSuccess
import kotlinx.serialization.SerializationException

// The typed auth facade (frontend.md §3.1) — the only auth integration point for screens. This
// slice exposes streamer-session reads against the live backend; bot-account + integration auth
// add methods here in later slices on the same shape.
//
// Backend routes (read-only contract, AuthController):
//   GET /api/v1/auth/me  →  StatusResponseDto<CurrentUserDto>   (requires Authorization: Bearer)
class AuthApi(private val client: ApiClient) {

    /** The signed-in streamer for the active session, proving the captured JWT is valid. */
    suspend fun me(): ApiResult<CurrentUser> =
        client.getEnvelope("api/v1/auth/me")

    /** GETs a `StatusResponseDto<T>` endpoint, unwrapping `data` to an [ApiResult]. */
    private suspend inline fun <reified T> ApiClient.getEnvelope(path: String): ApiResult<T> {
        val base: String? = baseUrl()
        if (base == null) {
            return ApiResult.Failure(
                ApiError(status = 0, code = "NO_CONNECTION", message = "No active backend connection.")
            )
        }

        val response: HttpResponse =
            try {
                httpClient.get("$base/$path")
            } catch (cause: Throwable) {
                return ApiResult.Failure(
                    ApiError(
                        status = 0,
                        code = "NETWORK",
                        message = cause.message ?: "Network request failed.",
                    )
                )
            }

        if (!response.status.isSuccess()) {
            return ApiResult.Failure(parseError(response))
        }

        return try {
            val envelope: StatusResponse<T> = response.body()
            val value: T? = envelope.data
            if (value == null) {
                ApiResult.Failure(
                    ApiError(
                        status = response.status.value,
                        code = "EMPTY_BODY",
                        message = "Backend returned an empty payload.",
                    )
                )
            } else {
                ApiResult.Ok(value)
            }
        } catch (cause: SerializationException) {
            ApiResult.Failure(
                ApiError(
                    status = response.status.value,
                    code = "DESERIALIZATION",
                    message = cause.message ?: "Unreadable response body.",
                )
            )
        }
    }

    private suspend fun ApiClient.parseError(response: HttpResponse): ApiError {
        val text: String =
            try {
                response.bodyAsText()
            } catch (_: Throwable) {
                ""
            }
        val problem: ProblemDetails? =
            try {
                if (text.isNotBlank()) json.decodeFromString(ProblemDetails.serializer(), text) else null
            } catch (_: SerializationException) {
                null
            }
        return ApiError(
            status = response.status.value,
            code = problem?.type ?: response.status.value.toString(),
            message = problem?.detail ?: problem?.title ?: response.status.description,
            traceId = problem?.traceId,
        )
    }
}
