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

// The typed commands facade — the channel's custom chat commands the Commands page renders. Real data only:
// the backend lists the channel's stored commands (no fabricated rows). The state holder depends on this
// interface and fakes it in tests without HTTP.
//
// Backend route (CommandsController):
//   GET /api/v1/channels/{channelId}/commands  →  PaginatedResponse<CommandListItem>
// The list serializes `CommandListItem` (the controller's `PaginatedResponse<CommandDto>` annotation is the
// loose detail type; the service returns `PagedList<CommandListItem>`, whose `Items` are written verbatim).
interface CommandsApi {
    /** The channel's custom chat commands — the lightweight list-view items. */
    suspend fun list(channelId: String): ApiResult<List<CommandSummary>>

    /** Create a new custom command on the channel (backend POST). */
    suspend fun create(channelId: String, body: CreateCommandBody): ApiResult<Unit>

    /**
     * Update an existing command, addressed by its current [commandName] (the backend PUT route is keyed by
     * name, not id). A partial update: only the non-null [body] fields are applied — this is how a toggle is
     * expressed (flip `isEnabled`, leave the rest null).
     */
    suspend fun update(
        channelId: String,
        commandName: String,
        body: UpdateCommandBody,
    ): ApiResult<Unit>

    /** Delete a command, addressed by its [commandName] (the backend DELETE route is keyed by name). */
    suspend fun delete(channelId: String, commandName: String): ApiResult<Unit>
}

class RestCommandsApi(private val client: ApiClient) : CommandsApi {
    override suspend fun list(channelId: String): ApiResult<List<CommandSummary>> {
        // The list is a PaginatedResponse (a flat `{ data: [...] }`), not a StatusResponseDto, so it is read
        // with getDirect (whole-body deserialize) rather than getEnvelope's `data: T` unwrap — same shape as
        // the channels list.
        return when (
            val page: ApiResult<PaginatedEnvelope<CommandSummary>> =
                client.getDirect("api/v1/channels/$channelId/commands?page=1&pageSize=25")
        ) {
            is ApiResult.Failure -> ApiResult.Failure(page.error)
            is ApiResult.Ok -> ApiResult.Ok(page.value.data)
        }
    }

    // The create response is a `StatusResponseDto<CommandDto>` (201), but the controller re-fetches the list
    // after every write, so the body is irrelevant here — any 2xx is success.
    override suspend fun create(channelId: String, body: CreateCommandBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/commands", body)

    override suspend fun update(
        channelId: String,
        commandName: String,
        body: UpdateCommandBody,
    ): ApiResult<Unit> = client.putUnit("api/v1/channels/$channelId/commands/$commandName", body)

    override suspend fun delete(channelId: String, commandName: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/commands/$commandName")
}

/**
 * The create-command request body (backend `CreateCommandDto`). camelCase JSON. [name] and [response] are
 * the essentials the create dialog collects; [type] defaults to a plain text command and [permission] to
 * everyone, matching the backend defaults. [isEnabled] lets a command be created already-on (the default).
 */
@Serializable
data class CreateCommandBody(
    val name: String,
    val response: String,
    val type: String = "text",
    val permission: String = "everyone",
    val isEnabled: Boolean = true,
)

/**
 * The update-command request body (backend `UpdateCommandDto`) — every field nullable so an update is a
 * partial patch. A toggle sends only [isEnabled]; an edit sends [response] (and may flip [isEnabled]); all
 * other fields stay null and the backend leaves them untouched. `explicitNulls = false` on the shared Json
 * means null fields are omitted from the wire body.
 */
@Serializable
data class UpdateCommandBody(
    val response: String? = null,
    val isEnabled: Boolean? = null,
)

/**
 * A custom chat command's list-view item (backend `CommandListItem`): the trigger [name], who may run it,
 * whether it is live, its cooldown, an optional [description], aliases, and the lifetime usage count. The
 * full response text / pipeline lives on the detail DTO, not the list item.
 */
@Serializable
data class CommandSummary(
    val id: Int = 0,
    val name: String = "",
    val type: String = "",
    val permission: String = "",
    val isEnabled: Boolean = false,
    val cooldownSeconds: Int = 0,
    val description: String? = null,
    val aliases: List<String> = emptyList(),
    val usageCount: Int = 0,
    val createdAt: String = "",
)
