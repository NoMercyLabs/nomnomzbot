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

import io.ktor.client.HttpClient
import io.ktor.client.call.body
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.defaultRequest
import io.ktor.client.request.delete
import io.ktor.client.request.patch
import io.ktor.client.request.forms.MultiPartFormDataContent
import io.ktor.client.request.forms.formData
import io.ktor.client.request.get
import io.ktor.client.request.header
import io.ktor.client.request.post
import io.ktor.client.request.put
import io.ktor.client.request.setBody
import io.ktor.client.statement.HttpResponse
import io.ktor.client.statement.bodyAsText
import io.ktor.http.ContentType
import io.ktor.http.Headers
import io.ktor.http.HttpHeaders
import io.ktor.http.contentType
import io.ktor.http.isSuccess
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.SerializationException
import kotlinx.serialization.json.Json

// The one shared HttpClient (frontend-structure.md F7 — exactly one client). It is base-URL- and
// token-agnostic at construction: the active backend URL and the bearer token are read on EVERY
// request from the injected lambdas, so a connection switch / sign-in re-targets the live client
// without rebuilding it (frontend.md §3.1/§6).
//
// This is also the single home for the request/response/envelope plumbing every typed facade reuses:
// the StatusResponseDto<T> unwrap, the problem-details error mapping, and the network-failure guard
// (the 401→refresh-once interceptor lands with the QueryClient slice). Facades (AuthApi, BotAuthApi,
// ChannelsApi, IntegrationsApi) call [getEnvelope] / [postEnvelope] / [postUnit] / [deleteUnit] and
// never touch the raw client.
class ApiClient(
    private val baseUrlProvider: () -> String?,
    private val tokenProvider: () -> String?,
) {
    internal val json: Json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        explicitNulls = false
    }

    internal val httpClient: HttpClient = buildHttpClient {
        install(ContentNegotiation) { json(this@ApiClient.json) }
        defaultRequest {
            // Per-request bearer; the base URL is applied per-call by the facade (the active
            // profile can change between requests, so it is not pinned in defaultRequest).
            tokenProvider()?.let { header(HttpHeaders.Authorization, "Bearer $it") }
        }
    }

    /** The active backend base URL with any trailing slash trimmed; null when no profile is active. */
    @PublishedApi
    internal fun baseUrl(): String? = baseUrlProvider()?.trimEnd('/')

    fun close() = httpClient.close()

    // ── Shared request/response plumbing ─────────────────────────────────────────

    /** GETs a `StatusResponseDto<T>` endpoint, unwrapping `data` to an [ApiResult]. */
    internal suspend inline fun <reified T> getEnvelope(path: String): ApiResult<T> =
        envelope(path) { url -> httpClient.get(url) }

    /**
     * GETs an endpoint whose whole body deserializes to [T] (no `StatusResponseDto<T>` unwrap) — the
     * `PaginatedResponse<T>` lists, which are a flat `{ data: [...] }` object rather than the
     * single-value `{ data: <T> }` envelope.
     */
    internal suspend inline fun <reified T> getDirect(path: String): ApiResult<T> {
        val base: String = baseUrl() ?: return noConnection()
        val response: HttpResponse =
            try {
                httpClient.get("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }
        if (!response.status.isSuccess()) return ApiResult.Failure(parseError(response))
        return try {
            ApiResult.Ok(response.body<T>())
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

    /** POSTs an optional JSON [body] to a `StatusResponseDto<T>` endpoint, unwrapping `data`. */
    internal suspend inline fun <reified T> postEnvelope(path: String, body: Any? = null): ApiResult<T> =
        envelope(path) { url ->
            httpClient.post(url) {
                if (body != null) {
                    contentType(ContentType.Application.Json)
                    setBody(body)
                }
            }
        }

    /** PUTs an optional JSON [body] to a `StatusResponseDto<T>` endpoint, unwrapping `data`. */
    internal suspend inline fun <reified T> putEnvelope(path: String, body: Any? = null): ApiResult<T> =
        envelope(path) { url ->
            httpClient.put(url) {
                if (body != null) {
                    contentType(ContentType.Application.Json)
                    setBody(body)
                }
            }
        }

    /** POSTs an optional JSON [body] and treats any 2xx as success, ignoring the response body. */
    internal suspend fun postUnit(path: String, body: Any? = null): ApiResult<Unit> =
        unit(path) { url ->
            httpClient.post(url) {
                if (body != null) {
                    contentType(ContentType.Application.Json)
                    setBody(body)
                }
            }
        }

    /** PUTs an optional JSON [body] and treats any 2xx as success, ignoring the response body. */
    internal suspend fun putUnit(path: String, body: Any? = null): ApiResult<Unit> =
        unit(path) { url ->
            httpClient.put(url) {
                if (body != null) {
                    contentType(ContentType.Application.Json)
                    setBody(body)
                }
            }
        }

    /** PATCHes an optional JSON [body] and treats any 2xx as success, ignoring the response body. */
    internal suspend fun patchUnit(path: String, body: Any? = null): ApiResult<Unit> =
        unit(path) { url ->
            httpClient.patch(url) {
                if (body != null) {
                    contentType(ContentType.Application.Json)
                    setBody(body)
                }
            }
        }

    /** DELETEs a path and treats any 2xx (including 204 No Content) as success. */
    internal suspend fun deleteUnit(path: String): ApiResult<Unit> =
        unit(path) { url -> httpClient.delete(url) }

    /**
     * POSTs to a file-download endpoint and returns the whole raw response body as bytes (no envelope unwrap) —
     * for the event-journal export, which streams a JSONL file rather than a `StatusResponseDto<T>`.
     */
    internal suspend fun postBytes(path: String): ApiResult<ByteArray> {
        val base: String = baseUrl() ?: return noConnection()
        val response: HttpResponse =
            try {
                httpClient.post("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }
        if (!response.status.isSuccess()) return ApiResult.Failure(parseError(response))
        return try {
            ApiResult.Ok(response.body<ByteArray>())
        } catch (cause: Throwable) {
            ApiResult.Failure(
                ApiError(
                    status = response.status.value,
                    code = "READ_BODY",
                    message = cause.message ?: "Could not read the response body.",
                )
            )
        }
    }

    /**
     * POSTs a single file part as `multipart/form-data` (field name [fieldName]) to a `StatusResponseDto<T>`
     * endpoint, unwrapping `data` — for the event-journal import upload.
     */
    internal suspend inline fun <reified T> postMultipartFile(
        path: String,
        fieldName: String,
        fileName: String,
        bytes: ByteArray,
        contentType: ContentType,
    ): ApiResult<T> =
        envelope(path) { url ->
            httpClient.post(url) {
                setBody(
                    MultiPartFormDataContent(
                        formData {
                            append(
                                fieldName,
                                bytes,
                                Headers.build {
                                    append(HttpHeaders.ContentType, contentType.toString())
                                    append(
                                        HttpHeaders.ContentDisposition,
                                        "filename=\"$fileName\"",
                                    )
                                },
                            )
                        }
                    )
                )
            }
        }

    // The shared core: resolve the base URL, run the request guarded against transport failures, and map
    // a non-2xx to a problem-details [ApiError]. `send` builds the URL itself so verb-specific options
    // (body, content-type) stay with the verb helper above.
    @PublishedApi
    internal suspend inline fun <reified T> envelope(
        path: String,
        send: (url: String) -> HttpResponse,
    ): ApiResult<T> {
        // `send` is invoked inline within this suspend body, so the suspend `httpClient.*` calls in the
        // callers' lambdas run in the right context without the param itself being marked suspend.
        val base: String = baseUrl() ?: return noConnection()
        val response: HttpResponse =
            try {
                send("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }

        if (!response.status.isSuccess()) return ApiResult.Failure(parseError(response))

        return try {
            val value: T? = response.body<StatusResponse<T>>().data
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

    private suspend fun unit(path: String, send: suspend (url: String) -> HttpResponse): ApiResult<Unit> {
        val base: String = baseUrl() ?: return noConnection()
        val response: HttpResponse =
            try {
                send("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }
        return if (response.status.isSuccess()) ApiResult.Ok(Unit)
        else ApiResult.Failure(parseError(response))
    }

    @PublishedApi
    internal fun <T> noConnection(): ApiResult<T> =
        ApiResult.Failure(
            ApiError(status = 0, code = "NO_CONNECTION", message = "No active backend connection.")
        )

    @PublishedApi
    internal fun <T> networkFailure(cause: Throwable): ApiResult<T> =
        ApiResult.Failure(
            ApiError(status = 0, code = "NETWORK", message = cause.message ?: "Network request failed.")
        )

    @PublishedApi
    internal suspend fun parseError(response: HttpResponse): ApiError {
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
