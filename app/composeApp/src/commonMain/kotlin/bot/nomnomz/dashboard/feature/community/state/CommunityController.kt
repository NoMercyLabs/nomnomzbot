// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.community.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Community page's state-holder (frontend-ia.md §3 — the channel's viewers). Resolves the active channel,
// then loads its real community list from the backend (Twitch API + chat history; no fabricated viewers). It
// also drives the page's per-member management — set trust level, ban, unban — each of which re-loads on
// success so the screen always reflects the backend's truth. The screen renders [state]; a pull / reconnect
// calls [load] again.
class CommunityController(
    private val channelsApi: ChannelsApi,
    private val communityApi: CommunityApi,
) {
    private val _state: MutableStateFlow<CommunityState> = MutableStateFlow(CommunityState.Loading)

    /** The page render state: loading / ready (with the members) / empty / error. */
    val state: StateFlow<CommunityState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then load its community list. */
    suspend fun load() {
        _state.value = CommunityState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = CommunityState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        when (val result: ApiResult<List<CommunityMember>> = communityApi.members(channel.id)) {
            is ApiResult.Failure -> _state.value = CommunityState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) CommunityState.Empty
                    else CommunityState.Ready(result.value)
        }
    }

    /** Set [userId]'s trust [level] (non-destructive), then reload so the row's badge reflects it. */
    suspend fun setTrust(userId: String, level: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(communityApi.setTrust(channel, userId, level))
    }

    /** Ban [userId] with [reason], then reload so the row shows as banned. The screen confirms this first. */
    suspend fun ban(userId: String, reason: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(communityApi.ban(channel, userId, reason))
    }

    /** Lift the ban on [userId], then reload so the row drops its banned badge. The screen confirms this first. */
    suspend fun unban(userId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(communityApi.unban(channel, userId))
    }

    /** Grant VIP status to [userId], then reload so the badge reflects it. The screen confirms this first. */
    suspend fun addVip(userId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(communityApi.addVip(channel, userId))
    }

    /** Revoke VIP status from [userId], then reload so the badge drops. The screen confirms this first. */
    suspend fun removeVip(userId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(communityApi.removeVip(channel, userId))
    }

    /** Send a /shoutout to [targetTwitchUserId] in the channel. No page reload needed (fire and forget). */
    suspend fun shoutout(targetTwitchUserId: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        when (val result: ApiResult<Unit> = communityApi.shoutout(channel, targetTwitchUserId)) {
            is ApiResult.Ok -> Unit
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    // A write either reloads the list (success) or surfaces its error over the current Ready list without
    // losing it (failure) — so a failed trust/ban/unban leaves the page intact with a visible reason.
    private suspend fun afterWrite(result: ApiResult<Unit>) {
        when (result) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        val current: CommunityState = _state.value
        _state.value =
            if (current is CommunityState.Ready) current.copy(actionError = detail)
            else CommunityState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Community page render state. */
sealed interface CommunityState {
    data object Loading : CommunityState

    /**
     * The channel's members are listed. [actionError] is non-null only when the last set-trust/ban/unban
     * failed — the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(val members: List<CommunityMember>, val actionError: String? = null) :
        CommunityState

    data object Empty : CommunityState

    data class Error(val detail: String) : CommunityState
}
