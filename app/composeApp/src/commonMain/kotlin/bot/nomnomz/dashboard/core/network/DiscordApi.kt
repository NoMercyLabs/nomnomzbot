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

// The typed Discord facade (DiscordController, the per-channel subsystem). The Discord page reads the
// channel's linked guild(s) and MANAGES each guild's notification rules — which channel-event trigger
// (stream.online, channel.follow, …) posts to which Discord channel, with what message, enabled or not.
// All real data: the guild link is established by the OAuth bot-install in the Integrations page; this
// facade only reads the link state and the notification configs, and writes the config rules. The bot
// token never reaches the client. The state holder depends on this interface and fakes it in tests
// without HTTP (the "depend on interfaces" convention).
//
// Backend routes (DiscordController, tenant channelId in the path):
//   GET    /api/v1/channels/{channelId}/discord/connections                         → StatusResponseDto<List<DiscordGuildConnectionDto>>
//   GET    /api/v1/channels/{channelId}/discord/connections/{connectionId}/configs  → StatusResponseDto<List<DiscordNotificationConfigDto>>
//   POST   /api/v1/channels/{channelId}/discord/connections/{connectionId}/configs  → StatusResponseDto<DiscordNotificationConfigDto>  (create)
//   PUT    /api/v1/channels/{channelId}/discord/configs/{configId}                  → StatusResponseDto<DiscordNotificationConfigDto>  (update + toggle)
//   DELETE /api/v1/channels/{channelId}/discord/configs/{configId}                  → 200
//
// The config list/create/update/delete is the full management surface for "which event posts where, saying
// what, on or off". A toggle is just an update carrying the flipped [enabled] with the row's other fields
// preserved (the PUT body is whole-row, not a partial patch — see [UpdateDiscordConfigBody]).
interface DiscordApi {
    /** The channel's linked Discord guild(s). Empty when Discord has not been connected yet. */
    suspend fun connections(channelId: String): ApiResult<List<DiscordGuildConnection>>

    /** The notification rules configured for one linked guild [connectionId]. */
    suspend fun configs(channelId: String, connectionId: String): ApiResult<List<DiscordNotificationConfig>>

    /** Create a new notification rule under [connectionId]; returns the persisted row. */
    suspend fun createConfig(
        channelId: String,
        connectionId: String,
        body: CreateDiscordConfigBody,
    ): ApiResult<DiscordNotificationConfig>

    /** Update an existing rule [configId] (also the toggle path); returns the persisted row. */
    suspend fun updateConfig(
        channelId: String,
        configId: String,
        body: UpdateDiscordConfigBody,
    ): ApiResult<DiscordNotificationConfig>

    /** Remove the rule [configId]. */
    suspend fun deleteConfig(channelId: String, configId: String): ApiResult<Unit>
}

class RestDiscordApi(private val client: ApiClient) : DiscordApi {

    // connections + configs are StatusResponseDto<List<T>> envelopes (a single `data: [...]` value), so they
    // are read with getEnvelope's `data: T` unwrap (T = List<…>), same as the integrations status list — NOT
    // the PaginatedResponse getDirect path.
    override suspend fun connections(channelId: String): ApiResult<List<DiscordGuildConnection>> =
        client.getEnvelope("api/v1/channels/$channelId/discord/connections")

    override suspend fun configs(
        channelId: String,
        connectionId: String,
    ): ApiResult<List<DiscordNotificationConfig>> =
        client.getEnvelope("api/v1/channels/$channelId/discord/connections/$connectionId/configs")

    override suspend fun createConfig(
        channelId: String,
        connectionId: String,
        body: CreateDiscordConfigBody,
    ): ApiResult<DiscordNotificationConfig> =
        client.postEnvelope(
            "api/v1/channels/$channelId/discord/connections/$connectionId/configs",
            body,
        )

    override suspend fun updateConfig(
        channelId: String,
        configId: String,
        body: UpdateDiscordConfigBody,
    ): ApiResult<DiscordNotificationConfig> =
        client.putEnvelope("api/v1/channels/$channelId/discord/configs/$configId", body)

    override suspend fun deleteConfig(channelId: String, configId: String): ApiResult<Unit> =
        client.deleteUnit("api/v1/channels/$channelId/discord/configs/$configId")
}

/**
 * A linked Discord guild (backend `DiscordGuildConnectionDto`). [guildName] is null until the bot resolves it.
 * [botInstalled] + [serverConsentStatus] + [streamerEnabled] together decide [isLinkActive] (the backend's
 * computed "this link will actually dispatch" flag) — the page surfaces these so the operator sees WHY a guild
 * is or is not live. Ids are server GUIDs; [guildId] is Discord's snowflake string.
 */
@Serializable
data class DiscordGuildConnection(
    val id: String = "",
    val broadcasterId: String = "",
    val guildId: String = "",
    val guildName: String? = null,
    val botInstalled: Boolean = false,
    val serverConsentStatus: String = "",
    val approvedByDiscordUserId: String? = null,
    val approvedAt: String? = null,
    val streamerEnabled: Boolean = false,
    val isLinkActive: Boolean = false,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/**
 * One notification rule (backend `DiscordNotificationConfigDto`): when [triggerType] fires (stream.online,
 * channel.follow, …) and the rule is [enabled], the bot posts [messageTemplate] to the Discord channel
 * [targetChannelId], optionally pinging [pingRoleId]. The embed + milestone fields are richer-config the page
 * passes through untouched (it edits the trigger/channel/message/enabled core); they are preserved across an
 * update so editing the message never silently drops a configured embed.
 */
@Serializable
data class DiscordNotificationConfig(
    val id: String = "",
    val guildConnectionId: String = "",
    val triggerType: String = "",
    val enabled: Boolean = false,
    val targetChannelId: String = "",
    val pingRoleId: String? = null,
    val messageTemplate: String? = null,
    val embedConfig: DiscordEmbed? = null,
    val milestoneType: String? = null,
    val milestoneThreshold: Int? = null,
    val createdAt: String = "",
    val updatedAt: String = "",
)

/** The `[VC:JSON]` embed shape (backend `DiscordEmbedDto`) — carried through unedited by the config page. */
@Serializable
data class DiscordEmbed(
    val title: String? = null,
    val description: String? = null,
    val color: String? = null,
    val thumbnailUrl: String? = null,
    val imageUrl: String? = null,
    val footerText: String? = null,
    val fields: List<DiscordEmbedField>? = null,
)

@Serializable
data class DiscordEmbedField(
    val name: String = "",
    val value: String = "",
    val inline: Boolean = false,
)

/**
 * The create-rule body (backend `CreateDiscordNotificationConfigRequest`). The page sets the core fields —
 * [triggerType], [enabled], [targetChannelId], [messageTemplate] — and leaves the optional ping/embed/milestone
 * null (the backend stores no ping/embed). `explicitNulls = false` on the shared Json omits the nulls.
 */
@Serializable
data class CreateDiscordConfigBody(
    val triggerType: String,
    val enabled: Boolean,
    val targetChannelId: String,
    val pingRoleId: String? = null,
    val messageTemplate: String? = null,
    val embedConfig: DiscordEmbed? = null,
    val milestoneType: String? = null,
    val milestoneThreshold: Int? = null,
)

/**
 * The update-rule body (backend `UpdateDiscordNotificationConfigRequest`) — a WHOLE-ROW replace (the backend
 * PUT has no [triggerType]; the trigger is immutable once created). A toggle reuses this carrying the flipped
 * [enabled] with every other field copied from the current row, so the dispatch channel / message / embed are
 * preserved. The state holder builds it from the row being edited/toggled, never from partial input.
 */
@Serializable
data class UpdateDiscordConfigBody(
    val enabled: Boolean,
    val targetChannelId: String,
    val pingRoleId: String? = null,
    val messageTemplate: String? = null,
    val embedConfig: DiscordEmbed? = null,
    val milestoneType: String? = null,
    val milestoneThreshold: Int? = null,
)
