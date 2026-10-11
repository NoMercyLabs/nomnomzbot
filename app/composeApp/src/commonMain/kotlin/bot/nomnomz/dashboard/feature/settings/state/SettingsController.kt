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

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.settings_channel_action_error

// The Settings page state-holder: resolves the active channel, then runs the channel-management actions
// (join, leave, reset, delete). The stream info editor lives on the Stream page (StreamInfoController).
class SettingsController(
    private val channelsApi: ChannelsApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<SettingsState> = MutableStateFlow(SettingsState.Loading)

    /** The page render state: loading / ready / channel deleted / error. */
    val state: StateFlow<SettingsState> = _state.asStateFlow()

    /** The channel resolved by the last successful [load]; the channel actions target it without re-resolving. */
    private var channelId: String? = null

    /** Resolve the active channel. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch keeps the current content on screen.
        if (_state.value !is SettingsState.Ready) _state.value = SettingsState.Loading

        when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
            is ApiResult.Failure -> _state.value = SettingsState.Error(result.error.message)
            is ApiResult.Ok -> {
                channelId = result.value.id
                _state.value = SettingsState.Ready
            }
        }
    }

    // ── Channel management (Broadcaster floor, setup:write) ───────────────────────────────────────

    /** Make the bot (re-)join the channel's chat. Surfaces a transient action result on the current state. */
    suspend fun joinBot() {
        val target: String = channelId ?: return
        applyChannelAction(channelsApi.join(target))
    }

    /** Make the bot leave the channel's chat. The channel record stays; the bot just stops reading/writing chat. */
    suspend fun leaveBot(): ApiResult<Unit> {
        val target: String = channelId ?: return notReady()
        return channelsApi.leave(target)
    }

    /** Reset all channel configuration to factory defaults (clears every stored Configuration entry). */
    suspend fun resetConfig(): ApiResult<Unit> {
        val target: String = channelId ?: return notReady()
        return channelsApi.reset(target)
    }

    /**
     * Permanently delete the channel record and all its data (irreversible). On success the state
     * advances to [SettingsState.ChannelDeleted] — the screen must react by routing the operator
     * back to the onboarding wizard so they can start fresh. The result goes back to the confirm
     * dialog, which stays open and shows a failure inline.
     */
    suspend fun deleteChannel(): ApiResult<Unit> {
        val target: String = channelId ?: return notReady()
        if (_state.value !is SettingsState.Ready) return notReady()
        val result: ApiResult<Unit> = channelsApi.deleteChannel(target)
        if (result is ApiResult.Ok) _state.value = SettingsState.ChannelDeleted
        return result
    }

    private fun notReady(): ApiResult<Unit> =
        ApiResult.Failure(ApiError(status = 0, code = "NOT_READY", message = "The channel is not loaded yet."))

    // A join/leave/reset failure announces on the shell-level feedback toast — the page is already showing
    // content, so this is a transient outcome, not a reason to blank the page.
    private fun applyChannelAction(result: ApiResult<Unit>) {
        if (result is ApiResult.Failure && _state.value is SettingsState.Ready) {
            feedback.error(Res.string.settings_channel_action_error, result.error.message)
        }
    }
}

/** The Settings page render state. */
sealed interface SettingsState {
    data object Loading : SettingsState

    /** The active channel resolved. A channel-management failure announces on the shell-level feedback toast. */
    data object Ready : SettingsState

    /** The channel was permanently deleted. The screen must navigate the operator to onboarding. */
    data object ChannelDeleted : SettingsState

    data class Error(val detail: String) : SettingsState
}
