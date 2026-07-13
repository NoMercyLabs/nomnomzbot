// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.discord.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CreateDiscordConfigBody
import bot.nomnomz.dashboard.core.network.CreateDiscordRoleBody
import bot.nomnomz.dashboard.core.network.DiscordApi
import bot.nomnomz.dashboard.core.network.DiscordConfigPreview
import bot.nomnomz.dashboard.core.network.DiscordDispatchLogEntry
import bot.nomnomz.dashboard.core.network.DiscordGuildChannel
import bot.nomnomz.dashboard.core.network.DiscordGuildConnection
import bot.nomnomz.dashboard.core.network.DiscordGuildRole
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.DiscordNotificationConfig
import bot.nomnomz.dashboard.core.network.DiscordNotificationRole
import bot.nomnomz.dashboard.core.network.UpdateDiscordConfigBody
import bot.nomnomz.dashboard.core.network.UpdateDiscordRoleBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Discord page's state-holder (frontend-ia.md — the Stream group): the channel's linked Discord guild(s)
// and, per guild, the notification rules — which channel-event trigger posts to which Discord channel, with
// what message, on or off. Resolves the active channel, then reads the real guild link(s) and each guild's
// configs from the backend (no fabricated rows). It drives the full management surface — create / edit /
// toggle / delete a rule — each re-listing on success so the screen always reflects the backend's truth.
//
// When no guild is linked yet, it lands on [DiscordState.Empty] (the screen points the operator at the
// Integrations page to connect Discord). The screen renders [state]; a retry / reconnect calls [load] again.
class DiscordController(
    private val channelsApi: ChannelsApi,
    private val discordApi: DiscordApi,
) {
    private val _state: MutableStateFlow<DiscordState> = MutableStateFlow(DiscordState.Loading)

    /** The page render state: loading / ready (guilds + each guild's configs) / empty (not connected) / error. */
    val state: StateFlow<DiscordState> = _state.asStateFlow()

    // The channel the reads/writes target — resolved by [load] and reused by every mutation so a write never
    // re-resolves the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then read its linked guild(s) and each guild's notification rules. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is DiscordState.Ready) _state.value = DiscordState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = DiscordState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val connections: List<DiscordGuildConnection> =
            when (val result: ApiResult<List<DiscordGuildConnection>> = discordApi.connections(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = DiscordState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        if (connections.isEmpty()) {
            // No guild linked — Discord isn't connected for this channel. The screen routes to Integrations.
            _state.value = DiscordState.Empty
            return
        }

        // For each linked guild, read its notification rules. A per-guild read failure surfaces as that guild's
        // [GuildNotifications.loadError] rather than blowing away the whole page — the other guilds still render.
        val guilds: List<GuildNotifications> = connections.map { connection -> loadGuild(channel.id, connection) }
        _state.value = DiscordState.Ready(guilds = guilds)
    }

    /**
     * Create a notification rule under [connectionId]: when [triggerType] fires, post [messageTemplate] to the
     * Discord channel [targetChannelId], starting [enabled]. Reloads on success; surfaces the error on failure.
     */
    suspend fun createConfig(
        connectionId: String,
        triggerType: String,
        targetChannelId: String,
        messageTemplate: String,
        enabled: Boolean,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            discordApi.createConfig(
                channel,
                connectionId,
                CreateDiscordConfigBody(
                    triggerType = triggerType,
                    enabled = enabled,
                    targetChannelId = targetChannelId,
                    messageTemplate = messageTemplate,
                ),
            )
        )
    }

    /**
     * Edit an existing rule [configId]'s dispatch channel + message + enabled flag, preserving the row's
     * trigger (immutable) and any configured ping/embed/milestone. Reloads on success; surfaces the error.
     */
    suspend fun updateConfig(
        configId: String,
        targetChannelId: String,
        messageTemplate: String,
        enabled: Boolean,
    ) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val current: DiscordNotificationConfig =
            findConfig(configId) ?: return failWrite(NoConfigError)
        afterWrite(
            discordApi.updateConfig(
                channel,
                configId,
                current.toUpdateBody(
                    enabled = enabled,
                    targetChannelId = targetChannelId,
                    messageTemplate = messageTemplate,
                ),
            )
        )
    }

    /**
     * Flip a rule [configId]'s enabled flag. The PUT is a whole-row replace, so the flip copies every other
     * field from the current row (channel / message / embed preserved). Reloads on success; surfaces the error.
     */
    suspend fun toggleConfig(configId: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        val current: DiscordNotificationConfig =
            findConfig(configId) ?: return failWrite(NoConfigError)
        afterWrite(discordApi.updateConfig(channel, configId, current.toUpdateBody(enabled = enabled)))
    }

    /** Delete the rule [configId]. Reloads on success; surfaces the error on failure. */
    suspend fun deleteConfig(configId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(discordApi.deleteConfig(channel, configId))
    }

    /**
     * Fetch the rendered preview for a config — what the next dispatch will look like. Stores it on the Ready
     * state; the screen shows it in a sheet/panel. Returns the preview so the caller can show it immediately, or
     * surfaces the error on the Ready state on failure.
     */
    suspend fun previewConfig(configId: String): DiscordConfigPreview? {
        val channel: String = channelId ?: return null
        return when (val result: ApiResult<DiscordConfigPreview> = discordApi.previewConfig(channel, configId)) {
            is ApiResult.Failure -> {
                failWrite(result.error.message)
                null
            }
            is ApiResult.Ok -> result.value
        }
    }

    /** Load the notification roles for [connectionId]. Returns them directly (caller stores in UI state). */
    suspend fun roles(connectionId: String): ApiResult<List<DiscordNotificationRole>> {
        val channel: String = channelId ?: return ApiResult.Failure(ApiError(0, null, NoChannelError))
        return discordApi.roles(channel, connectionId)
    }

    /** Create a new notification role for [connectionId]. Reloads on success; surfaces the error on failure. */
    suspend fun createRole(connectionId: String, discordRoleId: String, roleName: String?, selfAssign: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(
            discordApi.createRole(
                channel,
                connectionId,
                CreateDiscordRoleBody(discordRoleId = discordRoleId, roleName = roleName?.ifBlank { null }, selfAssignEnabled = selfAssign),
            )
        )
    }

    /**
     * The guild's assignable roles for [connectionId] — populates the role picker so the operator picks a role
     * instead of pasting a snowflake. Returned directly (the dialog stores them in UI state, like [roles]).
     */
    suspend fun guildRoles(connectionId: String): ApiResult<List<DiscordGuildRole>> {
        val channel: String = channelId ?: return ApiResult.Failure(ApiError(0, null, NoChannelError))
        return discordApi.guildRoles(channel, connectionId)
    }

    /**
     * The guild's channels for [connectionId] — populates the channel picker for posting the opt-in button.
     * Returned directly (the dialog stores them in UI state, like [roles]).
     */
    suspend fun guildChannels(connectionId: String): ApiResult<List<DiscordGuildChannel>> {
        val channel: String = channelId ?: return ApiResult.Failure(ApiError(0, null, NoChannelError))
        return discordApi.guildChannels(channel, connectionId)
    }

    /** Delete the notification role [roleId]. Reloads on success; surfaces the error on failure. */
    suspend fun deleteRole(roleId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(discordApi.deleteRole(channel, roleId))
    }

    /** Post the opt-in button for [roleId] to [buttonChannelId]. Reloads on success; surfaces the error. */
    suspend fun postRoleButton(roleId: String, buttonChannelId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(discordApi.postRoleButton(channel, roleId, buttonChannelId))
    }

    /** Approve server consent for [connectionId]. Reloads on success; surfaces the error on failure. */
    suspend fun approveServerConsent(connectionId: String, approvedByDiscordUserId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(discordApi.approveServerConsent(channel, connectionId, approvedByDiscordUserId))
    }

    /** Revoke server consent for [connectionId]. Reloads on success; surfaces the error on failure. */
    suspend fun revokeServerConsent(connectionId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(discordApi.revokeServerConsent(channel, connectionId))
    }

    /** Fetch the dispatch log for [connectionId]. Returns it directly (caller stores in UI state). */
    suspend fun dispatchLog(connectionId: String): ApiResult<List<DiscordDispatchLogEntry>> {
        val channel: String = channelId ?: return ApiResult.Failure(ApiError(0, null, NoChannelError))
        return discordApi.dispatchLog(channel, connectionId)
    }

    // ── internals ────────────────────────────────────────────────────────────

    private suspend fun loadGuild(
        channel: String,
        connection: DiscordGuildConnection,
    ): GuildNotifications =
        when (val result: ApiResult<List<DiscordNotificationConfig>> = discordApi.configs(channel, connection.id)) {
            is ApiResult.Ok -> GuildNotifications(connection = connection, configs = result.value)
            is ApiResult.Failure ->
                GuildNotifications(connection = connection, configs = emptyList(), loadError = result.error.message)
        }

    // A write either reloads (success) or surfaces its error over the current Ready state without losing it
    // (failure) — so a failed toggle/delete leaves the page intact with a visible reason.
    private suspend fun afterWrite(result: ApiResult<*>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: DiscordState = _state.value
        _state.value =
            if (current is DiscordState.Ready) current.copy(actionError = detail)
            else DiscordState.Error(detail)
    }

    // The current row for [configId], scanned across every loaded guild — the source of the preserved fields a
    // whole-row PUT needs. Null when the page isn't Ready or the row vanished (a concurrent delete).
    private fun findConfig(configId: String): DiscordNotificationConfig? =
        (_state.value as? DiscordState.Ready)
            ?.guilds
            ?.firstNotNullOfOrNull { guild -> guild.configs.firstOrNull { it.id == configId } }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
        const val NoConfigError: String = "That notification rule is no longer available — reload the page."
    }
}

/**
 * Build the whole-row update body from this row, overriding only the supplied fields. A toggle passes just
 * [enabled]; an edit passes [enabled] + [targetChannelId] + [messageTemplate]. Every other field (ping / embed
 * / milestone) is carried unchanged, so a partial edit never silently drops configured extras.
 */
private fun DiscordNotificationConfig.toUpdateBody(
    enabled: Boolean = this.enabled,
    targetChannelId: String = this.targetChannelId,
    messageTemplate: String? = this.messageTemplate,
): UpdateDiscordConfigBody =
    UpdateDiscordConfigBody(
        enabled = enabled,
        targetChannelId = targetChannelId,
        pingRoleId = pingRoleId,
        messageTemplate = messageTemplate,
        embedConfig = embedConfig,
        milestoneType = milestoneType,
        milestoneThreshold = milestoneThreshold,
    )

/** The Discord page render state. */
sealed interface DiscordState {
    data object Loading : DiscordState

    /**
     * The channel's linked guild(s), each with its notification rules. [actionError] is non-null only when the
     * last create/edit/toggle/delete failed — the screen surfaces it as a transient banner while keeping the
     * guilds rendered.
     */
    data class Ready(val guilds: List<GuildNotifications>, val actionError: String? = null) : DiscordState

    /** Discord is not connected for this channel (no guild link) — the screen points to Integrations. */
    data object Empty : DiscordState

    data class Error(val detail: String) : DiscordState
}

/**
 * One linked guild and its notification rules. [loadError] is non-null when this specific guild's rules could
 * not be read (the link is fine, the config fetch failed) — surfaced inline so the rest of the page still works.
 */
data class GuildNotifications(
    val connection: DiscordGuildConnection,
    val configs: List<DiscordNotificationConfig>,
    val loadError: String? = null,
)
