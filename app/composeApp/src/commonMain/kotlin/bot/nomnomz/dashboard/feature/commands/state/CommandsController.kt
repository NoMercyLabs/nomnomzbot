// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.commands.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BuiltinCommand
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.CommandSummary
import bot.nomnomz.dashboard.core.network.CommandsApi
import bot.nomnomz.dashboard.core.network.CreateCommandBody
import bot.nomnomz.dashboard.core.network.UpdateCommandBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_command_deleted
import nomnomzbot.composeapp.generated.resources.feedback_command_saved
import nomnomzbot.composeapp.generated.resources.feedback_command_save_failed

// The Commands page's state-holder (frontend-ia.md §3 — the Chat group). Resolves the active channel, then
// lists its real custom commands from the backend (no fabricated rows). It also drives the page's writes —
// create / edit / toggle / delete — each of which re-lists on success so the screen always reflects the
// backend's truth. The screen renders [state]; a retry / reconnect calls [load] again.
class CommandsController(
    private val channelsApi: ChannelsApi,
    private val commandsApi: CommandsApi,
    private val builtinsApi: BuiltinsApi,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<CommandsState> = MutableStateFlow(CommandsState.Loading)

    /** The page render state: loading / ready (with the commands) / empty / error. */
    val state: StateFlow<CommandsState> = _state.asStateFlow()

    // The channel the writes target — resolved by [load] and reused by every mutation so a write never has to
    // re-resolve the channel. Null until the first successful resolve.
    private var channelId: String? = null

    /** Resolve the active channel, then list its commands and built-in commands. */
    suspend fun load() {
        _state.value = CommandsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = CommandsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        val commandsResult: ApiResult<List<CommandSummary>> = commandsApi.list(channel.id)
        val builtinsResult: ApiResult<List<BuiltinCommand>> = builtinsApi.list(channel.id)

        when (commandsResult) {
            is ApiResult.Failure -> {
                _state.value = CommandsState.Error(commandsResult.error.message)
                return
            }
            is ApiResult.Ok -> Unit
        }

        val commands: List<CommandSummary> = (commandsResult as ApiResult.Ok).value
        val builtins: List<BuiltinCommand> =
            if (builtinsResult is ApiResult.Ok) builtinsResult.value else emptyList()

        _state.value =
            if (commands.isEmpty() && builtins.isEmpty()) CommandsState.Empty
            else CommandsState.Ready(commands = commands, builtins = builtins)
    }

    /** Create a command, then reload so the new row appears. Surfaces the error on failure. */
    suspend fun createCommand(name: String, response: String, isEnabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(commandsApi.create(channel, CreateCommandBody(name, response, isEnabled = isEnabled)))
    }

    /**
     * Edit a command's response (and enabled flag), addressed by its current [name]. Reloads on success.
     * Surfaces the error on failure.
     */
    suspend fun updateCommand(name: String, response: String, isEnabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(commandsApi.update(channel, name, UpdateCommandBody(response = response, isEnabled = isEnabled)))
    }

    /** Flip a command's enabled flag via the update endpoint (no dedicated toggle route). Reloads on success. */
    suspend fun toggleCommand(name: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(commandsApi.update(channel, name, UpdateCommandBody(isEnabled = enabled)))
    }

    /** Delete a command, addressed by its [name]. Reloads on success. Surfaces the error on failure. */
    suspend fun deleteCommand(name: String) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(commandsApi.delete(channel, name), success = Res.string.feedback_command_deleted)
    }

    /** Enable or disable a built-in command by its [builtinKey]. Reloads on success. */
    suspend fun toggleBuiltin(builtinKey: String, enabled: Boolean) {
        val channel: String = channelId ?: return failWrite(NoChannelError)
        afterWrite(builtinsApi.setEnabled(channel, builtinKey, enabled))
    }

    // A write either reloads the list AND announces success on the frame, or surfaces its error over the
    // current Ready list without losing it (failure) — so a failed toggle/delete leaves the page intact with
    // a visible reason AND a frame-level error message. [success] lets a delete say "Deleted" while the rest
    // default to "Saved".
    private suspend fun afterWrite(
        result: ApiResult<Unit>,
        success: org.jetbrains.compose.resources.StringResource = Res.string.feedback_command_saved,
    ) {
        when (result) {
            is ApiResult.Ok -> {
                feedback.success(success)
                load()
            }
            is ApiResult.Failure -> failWrite(result.error.message)
        }
    }

    private fun failWrite(detail: String) {
        // Announce the failure on the frame (persistent until dismissed) AND keep the in-page banner.
        feedback.error(Res.string.feedback_command_save_failed, detail)
        val current: CommandsState = _state.value
        _state.value =
            if (current is CommandsState.Ready) current.copy(actionError = detail)
            else CommandsState.Error(detail)
    }

    private companion object {
        const val NoChannelError: String = "No active channel — reconnect and try again."
    }
}

/** The Commands page render state. */
sealed interface CommandsState {
    data object Loading : CommandsState

    /**
     * The channel's commands are listed. [builtins] are the platform-defined commands (music, etc.); [commands]
     * are the user's custom commands. [actionError] is non-null only when the last create/edit/toggle/delete
     * failed — the screen surfaces it as a transient banner while keeping the list rendered.
     */
    data class Ready(
        val commands: List<CommandSummary>,
        val builtins: List<BuiltinCommand> = emptyList(),
        val actionError: String? = null,
    ) : CommandsState

    data object Empty : CommandsState

    data class Error(val detail: String) : CommandsState
}
