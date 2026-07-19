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
    override suspend fun list(channelId: String): ApiResult<List<CommandSummary>> =
        // A channel can have far more than one page of commands — walk every page so the screen shows them ALL,
        // not just the first 25/100 (PaginatedResponse is a flat `{ data, hasMore, nextPage }`).
        client.getAllPages { page -> "api/v1/channels/$channelId/commands?page=$page&pageSize=100" }

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
 * The create-command request body (backend `CreateCommandDto`). [name] plus a reaction ([templateResponse] for
 * a text command, [templateResponses] for a random-response list, or a [pipelineId] to run a visual pipeline)
 * are the essentials. [tier] names the authoring kind (template | pipeline | code). [minPermissionLevel] is the
 * unified-ladder value a chatter must clear (0 = everyone). [prefixMode] (Default | Custom | None) with an
 * optional [customPrefix], and [matchMode] (StartsWith | Exact | Contains | Regex) with an optional
 * [matchPattern] (required for Regex), govern how the trigger is recognized. [cooldownSeconds] is the global
 * spam guard; [cooldownPerUser] scopes a separate [userCooldownSeconds] per chatter. [aliases] are alternate
 * trigger names. [isEnabled] lets a command be created already-on (the default). `explicitNulls = false` on the
 * shared Json omits the null fields from the wire body.
 */
@Serializable
data class CreateCommandBody(
    val name: String,
    val tier: String = "template",
    val minPermissionLevel: Int = 0,
    val prefixMode: String = "Default",
    val customPrefix: String? = null,
    val matchMode: String = "StartsWith",
    val matchPattern: String? = null,
    val templateResponse: String? = null,
    val templateResponses: List<String>? = null,
    val pipelineId: String? = null,
    val cooldownSeconds: Int = 0,
    val userCooldownSeconds: Int = 0,
    val cooldownPerUser: Boolean = false,
    val description: String? = null,
    val aliases: List<String>? = null,
    val isEnabled: Boolean = true,
)

/**
 * The update-command request body (backend `UpdateCommandDto`) — every field nullable so an update is a partial
 * patch. A toggle sends only [isEnabled]; a full edit sends every field. All null fields stay untouched on the
 * backend. `explicitNulls = false` on the shared Json omits the null fields from the wire body. An empty-string
 * [customPrefix]/[matchPattern] clears the stored value (backend semantics); an empty [templateResponses] clears
 * the random-response list.
 */
@Serializable
data class UpdateCommandBody(
    val tier: String? = null,
    val minPermissionLevel: Int? = null,
    val prefixMode: String? = null,
    val customPrefix: String? = null,
    val matchMode: String? = null,
    val matchPattern: String? = null,
    val templateResponse: String? = null,
    val templateResponses: List<String>? = null,
    val pipelineId: String? = null,
    val cooldownSeconds: Int? = null,
    val userCooldownSeconds: Int? = null,
    val cooldownPerUser: Boolean? = null,
    val description: String? = null,
    val aliases: List<String>? = null,
    val isEnabled: Boolean? = null,
)

/**
 * A custom chat command's list-view item (backend `CommandListItem`). Guarded against the full `CommandDto`
 * schema (ApiContractTest), whose fields it is a subset of. Carries every field the edit dialog pre-fills so it
 * can open without a separate detail fetch: the trigger [name], [tier], the [minPermissionLevel] floor, the
 * [prefixMode]/[customPrefix] and [matchMode]/[matchPattern] recognition config, the [templateResponse] (single)
 * or [templateResponses] (random list), the attached [pipelineId], the [cooldownSeconds] guard plus the
 * per-user [cooldownPerUser]/[userCooldownSeconds] pair, the [description], [aliases], live flag, and usage.
 */
@Serializable
data class CommandSummary(
    val id: String = "",
    val name: String = "",
    val tier: String = "template",
    val minPermissionLevel: Int = 0,
    val isEnabled: Boolean = false,
    val prefixMode: String = "Default",
    val customPrefix: String? = null,
    val matchMode: String = "StartsWith",
    val matchPattern: String? = null,
    val cooldownSeconds: Int = 0,
    val userCooldownSeconds: Int = 0,
    val cooldownPerUser: Boolean = false,
    val description: String? = null,
    val aliases: List<String> = emptyList(),
    val useCount: Long = 0,
    val createdAt: String = "",
    val templateResponse: String? = null,
    val templateResponses: List<String>? = null,
    val pipelineId: String? = null,
)
