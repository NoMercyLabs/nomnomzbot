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

import bot.nomnomz.dashboard.core.network.AnalyticsApi
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ChatActivityEntry
import bot.nomnomz.dashboard.core.network.CommunityApi
import bot.nomnomz.dashboard.core.network.CommunityMember
import bot.nomnomz.dashboard.core.network.UserStats
import bot.nomnomz.dashboard.core.network.UsersApi
import bot.nomnomz.dashboard.core.network.ViewerAnalyticsProfile
import bot.nomnomz.dashboard.core.network.ViewerDataApi
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
    private val usersApi: UsersApi,
    private val viewerDataApi: ViewerDataApi,
    // Optional: the channel-scoped analytics facade the per-viewer detail panel reads a FOREIGN viewer's stats
    // through (`analytics/viewers/{internalUserId}`), which a moderator may call for anyone — unlike the self-only
    // usersApi.stats. Nullable so existing state-holder tests construct the controller without it.
    private val analyticsApi: AnalyticsApi? = null,
) {
    private val _state: MutableStateFlow<CommunityState> = MutableStateFlow(CommunityState.Loading)

    /** The page render state: loading / ready (with the members) / empty / error. */
    val state: StateFlow<CommunityState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then load its community list. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is CommunityState.Ready) _state.value = CommunityState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = CommunityState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val members: List<CommunityMember> =
            when (val result: ApiResult<List<CommunityMember>> = communityApi.members(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = CommunityState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // Top chatters is supplementary — a failure just surfaces an empty leaderboard, not a page error.
        val topChatters: List<ChatActivityEntry> =
            when (val result: ApiResult<List<ChatActivityEntry>> = communityApi.topChatters(channel.id)) {
                is ApiResult.Ok -> result.value
                is ApiResult.Failure -> emptyList()
            }

        _state.value =
            if (members.isEmpty() && topChatters.isEmpty()) CommunityState.Empty
            else CommunityState.Ready(members, topChatters)
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

    /**
     * Load engagement stats for a specific viewer. Returns the stats on success or null on failure. Callers
     * drive their own loading/error state for the per-user detail panel.
     */
    suspend fun getUserStats(userId: String): UserStats? =
        when (val result: ApiResult<UserStats> = usersApi.stats(userId)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> null
        }

    /**
     * Load [member]'s channel-scoped analytics profile via their [CommunityMember.internalUserId] — the moderator-
     * accessible read that works for ANY viewer (unlike self-only [getUserStats]). Returns null when the member
     * has no resolved internal id, the analytics facade is absent, the channel hasn't resolved, or the call fails.
     */
    suspend fun getViewerAnalytics(member: CommunityMember): ViewerAnalyticsProfile? {
        val internalId: String = member.internalUserId ?: return null
        val api: AnalyticsApi = analyticsApi ?: return null
        val channel: String = channelId ?: return null
        return when (val result: ApiResult<ViewerAnalyticsProfile> = api.viewerProfile(channel, internalId)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> null
        }
    }

    /**
     * Load a viewer's custom key/value data (the per-viewer store pipelines write). Returns the map on success
     * (empty when the viewer has none) or null on failure. [userId] is the community member's Twitch id — the
     * backend resolves it to the viewer. The caller drives its own loading/error state for the detail panel.
     */
    suspend fun getViewerData(userId: String): Map<String, String>? =
        when (val result: ApiResult<Map<String, String>> = viewerDataApi.getData(userId)) {
            is ApiResult.Ok -> result.value
            is ApiResult.Failure -> null
        }

    /**
     * Upsert one custom-data [key]=[value] for [userId]. Returns null on success, or the backend's error message
     * on failure (e.g. an over-cap value, which the backend rejects rather than truncates — surface it verbatim).
     */
    suspend fun setViewerDatum(userId: String, key: String, value: String): String? =
        when (val result: ApiResult<Unit> = viewerDataApi.setDatum(userId, key, value)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> result.error.message
        }

    /** Delete one custom-data [key] for [userId]. Returns null on success, or the error message on failure. */
    suspend fun deleteViewerDatum(userId: String, key: String): String? =
        when (val result: ApiResult<Unit> = viewerDataApi.deleteDatum(userId, key)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> result.error.message
        }

    /**
     * Request a GDPR data export for [userId]. The backend emails the export to the user. Broadcaster-only.
     * Returns `null` on success (nothing to show other than a confirmation), or an error string on failure.
     */
    suspend fun exportUserData(userId: String): String? =
        when (val result: ApiResult<Unit> = usersApi.export(userId)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> result.error.message
        }

    /**
     * Permanently erase all data for [userId] (GDPR erasure). Broadcaster-only. Irreversible — the screen
     * must confirm before calling this. Returns `null` on success, error string on failure.
     */
    suspend fun eraseUserData(userId: String): String? =
        when (val result: ApiResult<Unit> = usersApi.erase(userId)) {
            is ApiResult.Ok -> null
            is ApiResult.Failure -> result.error.message
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
     * The channel's members are listed. [topChatters] are the top 50 by message count (empty when the
     * leaderboard call fails). [actionError] is non-null only when the last set-trust/ban/unban/vip/shoutout
     * failed — the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(
        val members: List<CommunityMember>,
        val topChatters: List<ChatActivityEntry> = emptyList(),
        val actionError: String? = null,
    ) : CommunityState

    data object Empty : CommunityState

    data class Error(val detail: String) : CommunityState
}
