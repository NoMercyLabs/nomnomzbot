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
import io.ktor.client.plugins.contentnegotiation.ContentNegotiation
import io.ktor.client.plugins.defaultRequest
import io.ktor.client.request.header
import io.ktor.http.HttpHeaders
import io.ktor.serialization.kotlinx.json.json
import kotlinx.serialization.json.Json

// The one shared HttpClient (frontend-structure.md F7 — exactly one client). It is base-URL- and
// token-agnostic at construction: the active backend URL and the bearer token are read on EVERY
// request from the injected lambdas, so a connection switch / sign-in re-targets the live client
// without rebuilding it (frontend.md §3.1/§6).
//
// This slice wires REST request/response + auth header; the 401→refresh-once interceptor (§3.1)
// lands with the QueryClient slice that owns the refresh policy.
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
    internal fun baseUrl(): String? = baseUrlProvider()?.trimEnd('/')

    fun close() = httpClient.close()
}
