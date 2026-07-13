// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.shell.state

import bot.nomnomz.dashboard.core.connection.SessionStore
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelProvisioningApi
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// Owns the channel list + the operator's active channel selection. Lives at the shell level so every
// page controller can call channelsApi.primaryChannel() (which reads the session-pinned id via
// SessionStore.activeChannelId) and get the right result after a switch.
//
// On [load] it fetches two lists:
//   1. Bot-registered channels (the user owns or mods, and has this bot installed) — fully functional.
//   2. All Twitch channels the user moderates (from the /channels/moderated endpoint) — shows the
//      full Twitch moderation roster including channels without the bot ([ModeratedChannel.isOnboarded] = false).
//
// [select] calls SessionStore.switchChannel so the change propagates globally without each controller
// needing a direct reference here.
class ChannelSwitcherController(
    private val channelsApi: ChannelsApi,
    private val provisioningApi: ChannelProvisioningApi,
    private val sessionStore: SessionStore,
) {
    private val _state: MutableStateFlow<SwitcherState> = MutableStateFlow(SwitcherState.Loading)

    val state: StateFlow<SwitcherState> = _state.asStateFlow()

    /** The currently-active channel id from the session store. */
    val activeChannelId: StateFlow<String?> = sessionStore.activeChannelId

    /** Load the full channel list and pin the first as default (idempotent if already set). */
    suspend fun load() {
        when (val result: ApiResult<List<ChannelSummary>> = channelsApi.list()) {
            is ApiResult.Failure -> _state.value = SwitcherState.Error(result.error.message)
            is ApiResult.Ok -> {
                val channels: List<ChannelSummary> = result.value
                if (channels.isNotEmpty()) {
                    // Restore the operator's last explicitly-chosen channel across a reload/relaunch. Fall back to
                    // the first (owned-first) channel when nothing is remembered OR the remembered channel is no
                    // longer in the list (access revoked / bot removed) — so a stale pin can never strand the user.
                    val remembered: String? = sessionStore.persistedActiveChannel()
                    val restore: String = channels.firstOrNull { it.id == remembered }?.id ?: channels.first().id
                    sessionStore.setDefaultChannel(restore)
                }

                // Load the full Twitch moderation roster concurrently — failures are non-fatal; an
                // empty list just means the "Moderating" section of the switcher won't show unregistered channels.
                val moderated: List<ModeratedChannel> =
                    when (val r: ApiResult<List<ModeratedChannel>> = channelsApi.moderatedChannels()) {
                        is ApiResult.Ok -> r.value
                        is ApiResult.Failure -> emptyList()
                    }

                _state.value = SwitcherState.Ready(channels = channels, moderatedChannels = moderated)
            }
        }
    }

    /** Switch the active channel — affects every page controller on its next load. */
    fun select(channelId: String) {
        sessionStore.switchChannel(channelId)
    }

    /**
     * Provision + enter a moderated channel the bot isn't installed on ("moderator mode"). On success the channel
     * becomes a real tenant: it is added to the switcher roster (and dropped from the unregistered list), and its
     * INTERNAL id is returned so the caller can [select] it. Returns null on failure (the switch is skipped).
     */
    suspend fun enterModerated(twitchBroadcasterId: String): String? =
        when (
            val result: ApiResult<ChannelSummary> =
                provisioningApi.enterModeratedChannel(twitchBroadcasterId)
        ) {
            is ApiResult.Failure -> null
            is ApiResult.Ok -> {
                val summary: ChannelSummary = result.value
                val current: SwitcherState.Ready? = _state.value as? SwitcherState.Ready
                if (current != null) {
                    val roster: List<ChannelSummary> =
                        if (current.channels.any { it.id == summary.id }) current.channels
                        else current.channels + summary
                    _state.value =
                        current.copy(
                            channels = roster,
                            moderatedChannels =
                                current.moderatedChannels.filterNot { it.id == twitchBroadcasterId },
                        )
                }
                summary.id
            }
        }
}

sealed interface SwitcherState {
    data object Loading : SwitcherState

    data class Ready(
        /** Channels where this bot is installed (user owns or mods them). */
        val channels: List<ChannelSummary>,
        /**
         * All Twitch channels the user moderates (from Twitch API). Includes entries where
         * [ModeratedChannel.isOnboarded] is false — the user mods there but hasn't installed the bot.
         */
        val moderatedChannels: List<ModeratedChannel> = emptyList(),
    ) : SwitcherState

    data class Error(val detail: String) : SwitcherState
}
