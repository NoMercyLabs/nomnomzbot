// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.settings.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelBotStatusDetail
import bot.nomnomz.dashboard.core.network.ChannelScope
import bot.nomnomz.dashboard.core.network.ChannelScopesResponse
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.OAuthStart
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// State-holder for the channel's white-label bot card in Settings. The channel bot is an OPTIONAL
// dedicated Twitch account whose messages appear from a channel-specific identity instead of the
// shared platform bot. Connecting it opens a Twitch OAuth flow; disconnecting revokes the stored
// token without affecting the channel record or the fallback platform bot.
class ChannelBotController(private val channelsApi: ChannelsApi) {
    private val _state: MutableStateFlow<ChannelBotState> = MutableStateFlow(ChannelBotState.Loading)

    /** The card's render state. */
    val state: StateFlow<ChannelBotState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Resolve the active channel, then load the bot status and the broadcaster-token scope list. */
    suspend fun load() {
        _state.value = ChannelBotState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ChannelBotState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        val botStatus: ChannelBotStatusDetail =
            when (val result: ApiResult<ChannelBotStatusDetail> = channelsApi.channelBotStatus(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = ChannelBotState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        val scopes: List<ChannelScope> =
            when (val result: ApiResult<ChannelScopesResponse> = channelsApi.channelScopes(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value.permissions
            }

        _state.value =
            ChannelBotState.Ready(
                connected = botStatus.connected,
                login = botStatus.displayName ?: botStatus.login,
                scopes = scopes,
            )
    }

    /**
     * Start the Twitch OAuth flow for the channel's white-label bot. Returns the authorization URL
     * the caller should open in the system browser; null when the channel is not resolved or the
     * backend call fails. The bot status refreshes automatically when Twitch redirects back.
     */
    suspend fun startConnect(): String? {
        val target: String = channelId ?: return null
        return when (val result: ApiResult<OAuthStart> = channelsApi.startChannelBotConnect(target)) {
            is ApiResult.Failure -> {
                applyActionError(result.error.message)
                null
            }
            is ApiResult.Ok -> result.value.authorizeUrl
        }
    }

    /** Revoke and remove the channel's white-label bot account, then reload the status. */
    suspend fun disconnect() {
        val target: String = channelId ?: return
        val current: ChannelBotState = _state.value
        if (current !is ChannelBotState.Ready) return
        _state.value = current.copy(busy = true, actionError = null)
        when (val result: ApiResult<Unit> = channelsApi.disconnectChannelBot(target)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> applyActionError(result.error.message)
        }
    }

    private fun applyActionError(message: String) {
        val current: ChannelBotState = _state.value
        _state.value =
            if (current is ChannelBotState.Ready) current.copy(busy = false, actionError = message)
            else current
    }
}

/** The channel white-label bot card's render state. */
sealed interface ChannelBotState {
    data object Loading : ChannelBotState

    data class Ready(
        val connected: Boolean,
        val login: String?,
        val scopes: List<ChannelScope> = emptyList(),
        val busy: Boolean = false,
        val actionError: String? = null,
    ) : ChannelBotState

    data class Error(val detail: String) : ChannelBotState
}
