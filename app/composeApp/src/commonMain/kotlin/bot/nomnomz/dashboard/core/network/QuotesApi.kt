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

// The typed quotes facade — the channel's numbered quote library the Quotes page renders. Real data only:
// the backend lists the channel's stored quotes (no fabricated rows). The state holder depends on this
// interface and fakes it in tests without HTTP.
//
// Unlike the commands routes, the quotes controller resolves the tenant (channel) from the JWT, so the
// routes carry no `{channelId}` — every call is "my own channel". A quote is addressed by its per-channel
// `number` (immutable; never reused after deletion).
//
// Backend routes (QuotesController):
//   GET    /api/v1/quotes          →  PaginatedResponse<QuoteDto>      (newest first)
//   POST   /api/v1/quotes          →  StatusResponseDto<QuoteDto> (201)
//   PUT    /api/v1/quotes/{number} →  StatusResponseDto<QuoteDto>
//   DELETE /api/v1/quotes/{number} →  StatusResponseDto<QuoteDto>
interface QuotesApi {
    /** The channel's quotes, newest first. */
    suspend fun list(): ApiResult<List<Quote>>

    /**
     * One page of the channel's quotes (`GET /quotes?search=&page=&take=`), optionally filtered by the [search]
     * text fragment (backend `QuoteSearch`, matched against text/attribution). Newest first; the envelope carries
     * the page continuation so the library is navigable beyond the first page.
     */
    suspend fun page(search: String?, page: Int, pageSize: Int): ApiResult<QuotePage>

    /** Create a new quote on the channel (backend POST). */
    suspend fun create(body: AddQuoteBody): ApiResult<Unit>

    /** Edit an existing quote, addressed by its immutable [number] (the backend PUT route is keyed by number). */
    suspend fun update(number: Int, body: EditQuoteBody): ApiResult<Unit>

    /** Delete a quote, addressed by its [number] (the backend DELETE route is keyed by number). */
    suspend fun delete(number: Int): ApiResult<Unit>
}

class RestQuotesApi(private val client: ApiClient) : QuotesApi {
    override suspend fun list(): ApiResult<List<Quote>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so it is read
        // with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap — same shape as
        // the channels and commands lists.
        return when (
            val page: ApiResult<PaginatedEnvelope<Quote>> =
                client.getDirect("api/v1/quotes?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    override suspend fun page(
        search: String?,
        page: Int,
        pageSize: Int,
    ): ApiResult<QuotePage> {
        val searchParam: String = if (search.isNullOrBlank()) "" else "&search=${search.encodeQuery()}"
        return client.getDirect("api/v1/quotes?page=$page&take=$pageSize$searchParam")
    }

    // The create response is a `StatusResponseDto<QuoteDto>` (201), but the controller re-fetches the list
    // after every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun create(body: AddQuoteBody): ApiResult<Unit> =
        client.postUnit("api/v1/quotes", body)

    override suspend fun update(number: Int, body: EditQuoteBody): ApiResult<Unit> =
        client.putUnit("api/v1/quotes/$number", body)

    override suspend fun delete(number: Int): ApiResult<Unit> =
        client.deleteUnit("api/v1/quotes/$number")
}

/**
 * The create-quote request body (backend `AddQuoteRequest`). camelCase JSON. [text] is the only required
 * field; [quotedDisplayName] (who said it) and [contextGame] (what was being played) are optional attribution.
 * The backend's `quotedAt` defaults to creation time and `createdByUserId` is server-resolved, so neither is
 * collected here — `explicitNulls = false` on the shared Json omits the unset fields from the wire body.
 */
@Serializable
data class AddQuoteBody(
    val text: String,
    val quotedDisplayName: String? = null,
    val contextGame: String? = null,
)

/**
 * The edit-quote request body (backend `EditQuoteRequest`). The per-channel [number] is immutable and not part
 * of the payload (it addresses the route). [text] is required; the attribution fields are optional.
 */
@Serializable
data class EditQuoteBody(
    val text: String,
    val quotedDisplayName: String? = null,
    val contextGame: String? = null,
)

/**
 * One page of the quote library (backend `PaginatedResponse<QuoteDto>`). Flat `data` plus the page continuation;
 * the screen pages through this with next/prev.
 */
@Serializable
data class QuotePage(
    val data: List<Quote> = emptyList(),
    val nextPage: Int? = null,
    val hasMore: Boolean = false,
    val total: Int? = null,
)

/**
 * A quote (backend `QuoteDto`): the stable [id], the per-channel [number] used to address it, the [text], the
 * optional attribution ([quotedDisplayName] / [contextGame]), when it was [quotedAt] (nullable), and when the
 * row was [createdAt]. Dates are the backend's ISO-8601 strings, left as text (the page shows them verbatim).
 */
@Serializable
data class Quote(
    val id: String = "",
    val number: Int = 0,
    val text: String = "",
    val quotedDisplayName: String? = null,
    val contextGame: String? = null,
    val quotedAt: String? = null,
    val createdAt: String = "",
)
