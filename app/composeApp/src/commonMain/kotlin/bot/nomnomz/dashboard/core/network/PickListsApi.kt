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

// The typed pick-lists facade — the channel's named lists the Pick Lists page renders. A pick-list is the
// generic primitive behind the `{list.pick.<name>}` template variable: it holds a bag of entries and a command
// picks one at random. Real data only: the backend lists the channel's stored lists (no fabricated rows). The
// state holder depends on this interface and fakes it in tests without HTTP.
//
// Like the quotes routes, the pick-lists controller resolves the tenant (channel) from the request, so the
// routes carry no `{channelId}` — every call is "my own channel". A list is addressed by its opaque [id] (a
// 26-character ULID, also accepted as a raw GUID string) which is treated as a string end-to-end, never parsed.
//
// Backend routes (PickListsController):
//   GET    /api/v1/picklists          →  PaginatedResponse<PickListDto>       (by name)
//   GET    /api/v1/picklists/{id}     →  StatusResponseDto<PickListDto>
//   POST   /api/v1/picklists          →  StatusResponseDto<PickListDto> (201)
//   PUT    /api/v1/picklists/{id}     →  StatusResponseDto<PickListDto>
//   DELETE /api/v1/picklists/{id}     →  StatusResponseDto<PickListDto>
interface PickListsApi {
    /** The channel's pick-lists, by name. */
    suspend fun list(): ApiResult<List<PickList>>

    /** A single pick-list addressed by its opaque [id] (the backend GET route is keyed by id). */
    suspend fun get(id: String): ApiResult<PickList>

    /** Create a new pick-list on the channel (backend POST). */
    suspend fun create(body: CreatePickListBody): ApiResult<Unit>

    /** Edit an existing pick-list, addressed by its [id] (the backend PUT route is keyed by id). */
    suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit>

    /** Delete a pick-list, addressed by its [id] (the backend DELETE route is keyed by id). */
    suspend fun delete(id: String): ApiResult<Unit>
}

class RestPickListsApi(private val client: ApiClient) : PickListsApi {
    override suspend fun list(): ApiResult<List<PickList>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so it is read
        // with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap — same shape as
        // the channels and quotes lists.
        return when (
            val page: ApiResult<PaginatedEnvelope<PickList>> =
                client.getDirect("api/v1/picklists?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // A single list comes back as a `StatusResponseDto<PickListDto>` (a `data: T` envelope), so it is read with
    // getEnvelope, which unwraps the payload.
    override suspend fun get(id: String): ApiResult<PickList> =
        client.getEnvelope("api/v1/picklists/$id")

    // The create response is a `StatusResponseDto<PickListDto>` (201), but the controller re-fetches the list
    // after every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun create(body: CreatePickListBody): ApiResult<Unit> =
        client.postUnit("api/v1/picklists", body)

    override suspend fun update(id: String, body: UpdatePickListBody): ApiResult<Unit> =
        client.putUnit("api/v1/picklists/$id", body)

    override suspend fun delete(id: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/picklists/$id")
}

/**
 * The create-pick-list request body (backend `CreatePickListRequest`). camelCase JSON. [name] is the list's
 * unique key on the channel; [description] is optional free text; [items] is the (possibly empty) bag of entries
 * to pick from. `explicitNulls = false` on the shared Json omits a null [description] from the wire body.
 */
@Serializable
data class CreatePickListBody(
    val name: String,
    val description: String? = null,
    val items: List<String> = emptyList(),
)

/**
 * The edit-pick-list request body (backend `UpdatePickListRequest`) — the desired FULL state of the list. The
 * [id] is not part of the payload (it addresses the route); [name] may be changed (renamed), and [items] fully
 * replaces the stored entries.
 */
@Serializable
data class UpdatePickListBody(
    val name: String,
    val description: String? = null,
    val items: List<String> = emptyList(),
)

/**
 * A pick-list (backend `PickListDto`): the opaque [id] used to address it, its channel-unique [name], the
 * optional [description], its [items] (the entries `{list.pick.<name>}` draws from), and the [createdAt] /
 * [updatedAt] timestamps. Dates are the backend's ISO-8601 strings, left as text (the page shows them verbatim).
 */
@Serializable
data class PickList(
    val id: String = "",
    val name: String = "",
    val description: String? = null,
    val items: List<String> = emptyList(),
    val createdAt: String = "",
    val updatedAt: String = "",
)
