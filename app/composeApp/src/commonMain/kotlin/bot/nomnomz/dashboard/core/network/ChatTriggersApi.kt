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

// The typed chat-triggers facade — the channel's keyword auto-replies ("someone says X → the bot reacts"). A
// chat trigger matches an incoming chat message (by pattern + match type) and either posts a template response
// or runs a pipeline. Real data only: the backend lists the channel's stored triggers (no fabricated rows). The
// state holder depends on this interface and fakes it in tests without HTTP.
//
// The list comes back as a `StatusResponseDto<IReadOnlyList<ChatTriggerDto>>` — the single-value `{ data: [...] }`
// envelope where the payload is the list — so it is read with getEnvelope (like the games list), NOT the flat
// `PaginatedResponse` shape. Writes echo the saved trigger, but the page re-lists after every write, so create /
// update are treated as Unit results; the backend re-checks `chattriggers:write` regardless.
//
// Backend routes (ChatTriggersController):
//   GET    /api/v1/channels/{channelId}/chat-triggers               →  StatusResponseDto<IReadOnlyList<ChatTriggerDto>>
//   POST   /api/v1/channels/{channelId}/chat-triggers               →  StatusResponseDto<ChatTriggerDto>
//   PATCH  /api/v1/channels/{channelId}/chat-triggers/{triggerId}   →  StatusResponseDto<ChatTriggerDto>
//   DELETE /api/v1/channels/{channelId}/chat-triggers/{triggerId}   →  204 No Content
interface ChatTriggersApi {
    /** The channel's chat triggers. */
    suspend fun list(channelId: String): ApiResult<List<ChatTrigger>>

    /** Create a chat trigger on the channel (backend POST). */
    suspend fun create(channelId: String, body: CreateChatTriggerBody): ApiResult<Unit>

    /**
     * Edit an existing trigger, addressed by its [triggerId] (the backend PATCH route is keyed by id). A partial
     * update — only the non-null [body] fields are applied — so a toggle sends only [UpdateChatTriggerBody.isEnabled].
     */
    suspend fun update(channelId: String, triggerId: String, body: UpdateChatTriggerBody): ApiResult<Unit>

    /** Delete a trigger, addressed by its [triggerId] (the backend DELETE route is keyed by id). */
    suspend fun delete(channelId: String, triggerId: String): ApiResult<Unit>
}

class RestChatTriggersApi(private val client: ApiClient) : ChatTriggersApi {
    override suspend fun list(channelId: String): ApiResult<List<ChatTrigger>> =
        client.getEnvelope("api/v1/channels/$channelId/chat-triggers")

    // The create/update responses are `StatusResponseDto<ChatTriggerDto>`, but the page re-lists after every write,
    // so the body is irrelevant here — any 2xx is success.
    override suspend fun create(channelId: String, body: CreateChatTriggerBody): ApiResult<Unit> =
        client.postUnit("api/v1/channels/$channelId/chat-triggers", body)

    override suspend fun update(
        channelId: String,
        triggerId: String,
        body: UpdateChatTriggerBody,
    ): ApiResult<Unit> = client.patchUnit("api/v1/channels/$channelId/chat-triggers/$triggerId", body)

    override suspend fun delete(channelId: String, triggerId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/chat-triggers/$triggerId")
}

/**
 * A chat trigger (backend `ChatTriggerDto`): the [pattern] to look for in chat, the [matchType]
 * (`contains` | `exact` | `starts_with` | `regex`), whether the match is [caseSensitive], the [isEnabled] flag,
 * and the reaction — a template [response] (`{user}`/`{args}` variables, like commands) OR a [pipelineId] (a
 * chained reaction). Exactly one of the two is set. [cooldownSeconds] is the per-trigger spam guard;
 * [minPermissionLevel] is the unified-ladder value a chatter must clear to fire it (0 = everyone).
 */
@Serializable
data class ChatTrigger(
    val id: String = "",
    val pattern: String = "",
    val matchType: String = "contains",
    val caseSensitive: Boolean = false,
    val isEnabled: Boolean = true,
    val response: String? = null,
    val pipelineId: String? = null,
    val cooldownSeconds: Int = 30,
    val minPermissionLevel: Int = 0,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/**
 * The create-trigger request body (backend `CreateChatTriggerRequest`). camelCase JSON. [pattern] is required;
 * the rest carry the backend defaults when omitted. A trigger needs a [response] OR a [pipelineId] — the backend
 * 400s otherwise (surfaced inline); a `regex` [matchType] must compile (also a 400). `explicitNulls = false` on
 * the shared Json omits the null reaction field from the wire body.
 */
@Serializable
data class CreateChatTriggerBody(
    val pattern: String,
    val matchType: String,
    val caseSensitive: Boolean,
    val isEnabled: Boolean,
    val response: String? = null,
    val pipelineId: String? = null,
    val cooldownSeconds: Int,
    val minPermissionLevel: Int,
)

/**
 * The edit-trigger request body (backend `UpdateChatTriggerRequest`) — every field nullable so an update is a
 * partial patch. A toggle sends only [isEnabled]; a full edit sends every field. `explicitNulls = false` omits
 * the null fields from the wire body, so a patch touches only what it sets. NOTE: because null means "leave
 * unchanged", the response↔pipeline SWITCH is expressed by the edit dialog always sending BOTH — the chosen one
 * with a value and the other as an empty string (the backend clears it) — so a trigger can flip its reaction kind.
 */
@Serializable
data class UpdateChatTriggerBody(
    val pattern: String? = null,
    val matchType: String? = null,
    val caseSensitive: Boolean? = null,
    val isEnabled: Boolean? = null,
    val response: String? = null,
    val pipelineId: String? = null,
    val cooldownSeconds: Int? = null,
    val minPermissionLevel: Int? = null,
)
