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
import kotlinx.coroutines.CancellationException
import kotlinx.serialization.json.Json

// The one shared HttpClient (frontend-structure.md F7 — exactly one client). It is base-URL- and
// token-agnostic at construction: the active backend URL and the bearer token are read on EVERY
// request from the injected lambdas, so a connection switch / sign-in re-targets the live client
// without rebuilding it (frontend.md §3.1/§6).
//
// This is also the single home for the request/response/envelope plumbing every typed facade reuses:
// the StatusResponseDto<T> unwrap, the problem-details error mapping, the network-failure guard, and the
// 401→refresh-once interceptor. Facades (AuthApi, BotAuthApi, ChannelsApi, IntegrationsApi) call
// [getEnvelope] / [postEnvelope] / [postUnit] / [deleteUnit] and never touch the raw client.
class ApiClient(
    private val baseUrlProvider: () -> String?,
    private val tokenProvider: () -> String?,
    // The operator's active managed channel id (SessionStore.activeChannelId), sent as X-Channel-Id so the
    // backend TenantResolutionMiddleware resolves EVERY request against the switched channel — not just the
    // endpoints that thread {channelId} through their route. Null (the default) targets the caller's own
    // channel, so login/restore before a channel is selected is unaffected.
    private val channelProvider: () -> String? = { null },
) {
    /**
     * Set by [AppGraph] after construction to break the circular dependency (AuthApi → ApiClient → refresher).
     * Called once on 401: should POST /api/v1/auth/refresh, store the new access token, and return true.
     * Returning false or throwing leaves the 401 error to propagate normally. Never invoked for the
     * refresh endpoint itself (prevents infinite loops).
     */
    @PublishedApi
    internal var tokenRefresher: (suspend () -> Boolean)? = null
    internal val json: Json = Json {
        ignoreUnknownKeys = true
        isLenient = true
        explicitNulls = false
        encodeDefaults = true
    }

    @PublishedApi
    internal val httpClient: HttpClient = buildHttpClient {
        install(ContentNegotiation) { json(this@ApiClient.json) }
        defaultRequest {
            // Per-request bearer; the base URL is applied per-call by the facade (the active
            // profile can change between requests, so it is not pinned in defaultRequest).
            tokenProvider()?.let { header(HttpHeaders.Authorization, "Bearer $it") }
            // Per-request tenant target: the operator's active channel. The backend honours X-Channel-Id for
            // tenant resolution, so a channel switch retargets every request — mod tools included — without each
            // facade threading the id through its route. A route {channelId} still wins server-side.
            channelProvider()?.let { header("X-Channel-Id", it) }
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
    @PublishedApi
    internal suspend inline fun <reified T> getDirect(path: String): ApiResult<T> {
        val base: String = baseUrl() ?: return noConnection()
        var response: HttpResponse =
            try {
                httpClient.get("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }
        if (response.status.value == 401 && !path.startsWith("api/v1/auth/refresh")) {
            val refreshed: Boolean = try { tokenRefresher?.invoke() ?: false } catch (_: Exception) { false }
            if (refreshed) {
                response = try { httpClient.get("$base/$path") } catch (cause: Throwable) { return networkFailure(cause) }
            }
        }
        if (!response.status.isSuccess()) return ApiResult.Failure(parseError(response))
        return try {
            ApiResult.Ok(response.body<T>())
        } catch (cause: Exception) {
            if (cause is CancellationException) throw cause
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

    /** PATCHes an optional JSON [body] to a `StatusResponseDto<T>` endpoint, unwrapping `data`. */
    internal suspend inline fun <reified T> patchEnvelope(path: String, body: Any? = null): ApiResult<T> =
        envelope(path) { url ->
            httpClient.patch(url) {
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

    /** DELETEs a path against a `StatusResponseDto<T>` endpoint (a delete that echoes the new state), unwrapping `data`. */
    internal suspend inline fun <reified T> deleteEnvelope(path: String): ApiResult<T> =
        envelope(path) { url -> httpClient.delete(url) }

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
     * GETs a `text/plain` endpoint and returns the whole raw body as a string (no envelope unwrap) — for the
     * generated SDK type declarations (`/sdk/types.d.ts`), which stream a `.d.ts` file rather than a
     * `StatusResponseDto<T>`. Honours the same 401 → refresh-once retry as the JSON helpers.
     */
    internal suspend fun getText(path: String): ApiResult<String> {
        val base: String = baseUrl() ?: return noConnection()
        var response: HttpResponse =
            try {
                httpClient.get("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }
        if (response.status.value == 401 && !path.startsWith("api/v1/auth/refresh")) {
            val refreshed: Boolean = try { tokenRefresher?.invoke() ?: false } catch (_: Exception) { false }
            if (refreshed) {
                response = try { httpClient.get("$base/$path") } catch (cause: Throwable) { return networkFailure(cause) }
            }
        }
        if (!response.status.isSuccess()) return ApiResult.Failure(parseError(response))
        return try {
            ApiResult.Ok(response.bodyAsText())
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
     * POSTs a JSON [body] to a file-download endpoint and returns the whole raw response body as bytes — for the
     * bundle export, which takes a JSON pick-list and streams back a ZIP rather than a `StatusResponseDto<T>`. On a
     * non-2xx the error body is problem-details JSON, mapped through [parseError] like every other helper.
     */
    internal suspend fun postBytesWithBody(path: String, body: Any): ApiResult<ByteArray> {
        val base: String = baseUrl() ?: return noConnection()
        val response: HttpResponse =
            try {
                httpClient.post("$base/$path") {
                    contentType(ContentType.Application.Json)
                    setBody(body)
                }
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
     * POSTs multipart/form-data with one file part ([fileFieldName]) plus additional string [fields] to a
     * `StatusResponseDto<T>` endpoint — for sound-clip uploads where the server also expects text fields
     * (name, displayName, defaultVolume) alongside the audio bytes.
     */
    internal suspend inline fun <reified T> postMultipartWithFields(
        path: String,
        fileFieldName: String,
        fileName: String,
        fileBytes: ByteArray,
        fileContentType: ContentType,
        fields: Map<String, String>,
    ): ApiResult<T> =
        envelope(path) { url ->
            httpClient.post(url) {
                setBody(
                    MultiPartFormDataContent(
                        formData {
                            fields.forEach { (key, value) -> append(key, value) }
                            append(
                                fileFieldName,
                                fileBytes,
                                Headers.build {
                                    append(HttpHeaders.ContentType, fileContentType.toString())
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
    // (body, content-type) stay with the verb helper above. On 401, one silent token refresh + retry is
    // attempted before surfacing the error (prevents stale-JWT failures after the 60-min expiry).
    @PublishedApi
    internal suspend inline fun <reified T> envelope(
        path: String,
        send: (url: String) -> HttpResponse,
    ): ApiResult<T> {
        // `send` is invoked inline within this suspend body, so the suspend `httpClient.*` calls in the
        // callers' lambdas run in the right context without the param itself being marked suspend.
        val base: String = baseUrl() ?: return noConnection()
        var response: HttpResponse =
            try {
                send("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }

        // 401 → one silent refresh + retry. Guard on the refresh path itself to prevent loops.
        if (response.status.value == 401 && !path.startsWith("api/v1/auth/refresh")) {
            val refreshed: Boolean = try { tokenRefresher?.invoke() ?: false } catch (_: Exception) { false }
            if (refreshed) {
                response = try { send("$base/$path") } catch (cause: Throwable) { return networkFailure(cause) }
            }
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
        } catch (cause: Exception) {
            if (cause is CancellationException) throw cause
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
        var response: HttpResponse =
            try {
                send("$base/$path")
            } catch (cause: Throwable) {
                return networkFailure(cause)
            }
        if (response.status.value == 401 && !path.startsWith("api/v1/auth/refresh")) {
            val refreshed: Boolean = try { tokenRefresher?.invoke() ?: false } catch (_: Exception) { false }
            if (refreshed) {
                response = try { send("$base/$path") } catch (cause: Throwable) { return networkFailure(cause) }
            }
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
            } catch (_: Exception) {
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
