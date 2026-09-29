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

import bot.nomnomz.dashboard.core.designsystem.PermissionRungs
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BuiltinCommand
import bot.nomnomz.dashboard.core.network.BuiltinsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

/** The longest cooldown the backend accepts for a built-in (one hour). */
const val MaxBuiltinCooldownSeconds: Int = 3600

// The built-in detail editor's state-holder (commands-pipelines.md §4.5): one built-in as the channel sees it,
// and every setting the channel can change on it — on/off, read out with TTS, cooldown, who can use it — plus a
// full reset. Every write replaces the built-in with the backend's answer, so the editor shows the backend's
// truth, and [onChanged] lets the Commands list refetch so its row matches.
class BuiltinDetailController(
    private val channelsApi: ChannelsApi,
    private val builtinsApi: BuiltinsApi,
    private val onChanged: suspend () -> Unit = {},
) {
    private val _state: MutableStateFlow<BuiltinDetailState> = MutableStateFlow(BuiltinDetailState())

    /** The editor render state. */
    val state: StateFlow<BuiltinDetailState> = _state.asStateFlow()

    private var channelId: String? = null

    /** Opens the editor on [builtinKey] and loads it. */
    suspend fun open(builtinKey: String) {
        _state.value = BuiltinDetailState(openKey = builtinKey, loading = true)
        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = _state.value.copy(loading = false, error = result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id
        when (val result: ApiResult<BuiltinCommand> = builtinsApi.get(channel.id, builtinKey)) {
            is ApiResult.Ok -> show(result.value)
            is ApiResult.Failure -> _state.value = _state.value.copy(loading = false, error = result.error.message)
        }
    }

    /** Closes the editor and drops any unsaved change. */
    fun close() {
        _state.value = BuiltinDetailState()
    }

    /** Edits the cooldown field; blank means "use the default". Only digits are kept. */
    fun editCooldown(text: String) {
        _state.value = _state.value.copy(cooldownText = text.filter(Char::isDigit), error = null)
    }

    /** Picks who can use the command; null means "use the default". */
    fun editPermission(rung: String?) {
        _state.value = _state.value.copy(permission = rung, error = null)
    }

    /** Saves the cooldown and permission. An out-of-range cooldown never reaches the backend. */
    suspend fun saveSettings() {
        val current: BuiltinDetailState = _state.value
        val channel: String = channelId ?: return
        val builtin: BuiltinCommand = current.builtin ?: return
        if (!current.cooldownValid) return
        _state.value = current.copy(saving = true, error = null)
        write(
            builtinsApi.updateSettings(
                channel,
                builtin.builtinKey,
                current.cooldownText.toIntOrNull(),
                current.permission,
            ),
        )
    }

    /** Turns the command on or off in chat. */
    suspend fun setEnabled(enabled: Boolean) = writeThenReload { channel, key ->
        builtinsApi.setEnabled(channel, key, enabled)
    }

    /** Turns "also read it out with TTS" on or off. */
    suspend fun setSpeakWithTts(enabled: Boolean) = writeThenReload { channel, key ->
        builtinsApi.setSpeakWithTts(channel, key, enabled)
    }

    /** Puts the command back on every default: on, no TTS, default cooldown and permission, default replies. */
    suspend fun reset() {
        val channel: String = channelId ?: return
        val builtin: BuiltinCommand = _state.value.builtin ?: return
        _state.value = _state.value.copy(saving = true, error = null)
        write(builtinsApi.reset(channel, builtin.builtinKey))
    }

    private suspend fun writeThenReload(call: suspend (channel: String, key: String) -> ApiResult<Unit>) {
        val channel: String = channelId ?: return
        val builtin: BuiltinCommand = _state.value.builtin ?: return
        when (val result: ApiResult<Unit> = call(channel, builtin.builtinKey)) {
            is ApiResult.Failure -> _state.value = _state.value.copy(error = result.error.message)
            is ApiResult.Ok -> write(builtinsApi.get(channel, builtin.builtinKey))
        }
    }

    private suspend fun write(result: ApiResult<BuiltinCommand>) {
        when (result) {
            is ApiResult.Ok -> {
                show(result.value)
                onChanged()
            }
            is ApiResult.Failure -> _state.value = _state.value.copy(saving = false, error = result.error.message)
        }
    }

    // The form starts from what the backend stores: blank / null where the default applies.
    private fun show(builtin: BuiltinCommand) {
        _state.value =
            _state.value.copy(
                loading = false,
                saving = false,
                error = null,
                builtin = builtin,
                cooldownText = builtin.cooldownSecondsOverride?.toString().orEmpty(),
                permission = builtin.minPermissionLevelOverride,
            )
    }
}

/**
 * The built-in detail editor's state. [openKey] is the built-in shown (null = closed). [cooldownText] and
 * [permission] are the form: blank / null mean "use the default".
 */
data class BuiltinDetailState(
    val openKey: String? = null,
    val loading: Boolean = false,
    val saving: Boolean = false,
    val error: String? = null,
    val builtin: BuiltinCommand? = null,
    val cooldownText: String = "",
    val permission: String? = null,
) {
    /** False when the typed cooldown is outside 0–3600 seconds. */
    val cooldownValid: Boolean
        get() = cooldownText.isEmpty() || (cooldownText.toIntOrNull() ?: -1) in 0..MaxBuiltinCooldownSeconds

    /** True when the form differs from what is saved. */
    val settingsDirty: Boolean
        get() {
            val saved: BuiltinCommand = builtin ?: return false
            return cooldownText != saved.cooldownSecondsOverride?.toString().orEmpty() ||
                permission != saved.minPermissionLevelOverride
        }

    /**
     * The rungs the picker offers: the built-in's own floor and up. A built-in's floor is a safety gate (e.g.
     * !whisper stays moderator-only), so it can be raised but never lowered — the backend refuses it too.
     */
    val permissionChoices: List<String>
        get() {
            val floor: String = builtin?.defaultMinPermissionLevel ?: PermissionRungs.Everyone
            val start: Int = PermissionRungs.Ordered.indexOfFirst { it.first.equals(floor, ignoreCase = true) }
            return PermissionRungs.Ordered.drop(maxOf(start, 0)).map { it.first }
        }
}
