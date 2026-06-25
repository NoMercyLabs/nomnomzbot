// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.moderation.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BannedUser
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModLogEntry
import bot.nomnomz.dashboard.core.network.ModerationApi
import bot.nomnomz.dashboard.core.network.ShieldStatus
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_unban_failed
import nomnomzbot.composeapp.generated.resources.feedback_unbanned

// The Moderation page's state-holder: resolve the active channel, load its real list of currently-banned
// viewers from the backend (no fabricated entries), and lift a ban on request. The screen renders [state];
// a retry calls [load] again. [unban] is the one destructive action here — the screen must confirm it first.
class ModerationController(
    private val channelsApi: ChannelsApi,
    private val moderationApi: ModerationApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<ModerationState> = MutableStateFlow(ModerationState.Loading)

    /** The page render state: loading / ready (with the bans) / empty / error. */
    val state: StateFlow<ModerationState> = _state.asStateFlow()

    // The channel the loaded bans belong to, kept so [unban] targets the same channel without re-resolving.
    private var channelId: String? = null

    /** Resolve the active channel, then load its banned-viewer list. */
    suspend fun load() {
        _state.value = ModerationState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = ModerationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        val bans: List<BannedUser> =
            when (val result: ApiResult<List<BannedUser>> = moderationApi.bans(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = ModerationState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The mod action log (recent moderator actions). A failure must NOT blank the page — the bans loaded —
        // so it degrades to an empty list rather than erroring the whole screen.
        val modLog: List<ModLogEntry> =
            when (val result: ApiResult<List<ModLogEntry>> = moderationApi.modLog(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        // Emergency Shield Mode (resilient — a failure leaves it reported off rather than blanking the page).
        val shieldEnabled: Boolean =
            when (val result: ApiResult<ShieldStatus> = moderationApi.shieldMode(channel.id)) {
                is ApiResult.Failure -> false
                is ApiResult.Ok -> result.value.enabled
            }

        // Empty only when there is genuinely nothing to show AND shield is off; if shield is on the page must
        // render so its active state (and the toggle to lift it) stays visible.
        _state.value =
            if (bans.isEmpty() && modLog.isEmpty() && !shieldEnabled) ModerationState.Empty
            else ModerationState.Ready(bans, modLog, shieldEnabled)
    }

    /**
     * Lift the ban on [userId] (a [BannedUser.id]). On success the list is reloaded so the unbanned viewer
     * drops off; on failure the current list stays put and the error surfaces on the [ModerationState.Ready]
     * state. The screen gates this behind a confirmation, so it only runs on an explicit, confirmed click.
     */
    suspend fun unban(userId: String) {
        val channel: String = channelId ?: return

        when (val result: ApiResult<Unit> = moderationApi.unban(channel, userId)) {
            is ApiResult.Ok -> {
                feedback.success(Res.string.feedback_unbanned)
                load()
            }
            is ApiResult.Failure -> {
                // Announce the failure on the frame (persistent) AND keep the in-page banner over the list.
                feedback.error(Res.string.feedback_unban_failed, result.error.message)
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }

    /**
     * Turn emergency Shield Mode on or off ([enabled]), then reload so the page reflects it. Surfaces the error
     * on the current Ready state on failure; no-ops when no channel is loaded.
     */
    suspend fun setShieldMode(enabled: Boolean) {
        val channel: String = channelId ?: return
        when (val result: ApiResult<Unit> = moderationApi.setShieldMode(channel, enabled)) {
            is ApiResult.Ok -> load()
            is ApiResult.Failure -> {
                val current: ModerationState = _state.value
                if (current is ModerationState.Ready) {
                    _state.value = current.copy(actionError = result.error.message)
                }
            }
        }
    }
}

/** The Moderation page render state. */
sealed interface ModerationState {
    data object Loading : ModerationState

    /**
     * The active bans + the recent mod action log, plus an optional message when the last unban attempt failed
     * (the lists stay intact).
     */
    data class Ready(
        val bans: List<BannedUser>,
        val modLog: List<ModLogEntry> = emptyList(),
        val shieldEnabled: Boolean = false,
        val actionError: String? = null,
    ) : ModerationState

    data object Empty : ModerationState

    data class Error(val detail: String) : ModerationState
}
