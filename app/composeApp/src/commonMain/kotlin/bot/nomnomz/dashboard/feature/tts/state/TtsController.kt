// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.state

import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsConfigUpdate
import bot.nomnomz.dashboard.core.network.TtsTestRequest
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsVoice
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The TTS page's state-holder: resolves the active channel, loads its real TTS configuration, and
// persists edits back (no fabricated values). The screen renders [state]; it edits a local form seeded
// from the loaded config and calls [save] to write the whole config through. A retry / reconnect calls
// [load] again. The resolved channel id is cached from [load] so [save] reuses it without re-resolving.
class TtsController(
    private val channelsApi: ChannelsApi,
    private val ttsApi: TtsApi,
) {
    private val _state: MutableStateFlow<TtsState> = MutableStateFlow(TtsState.Loading)

    /** The page render state: loading / ready (with the config) / error. */
    val state: StateFlow<TtsState> = _state.asStateFlow()

    /** The channel resolved by the last successful [load]; [save] targets it without re-resolving. */
    private var channelId: String? = null

    /** Resolve the active channel, then load its TTS configuration. */
    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is TtsState.Ready) _state.value = TtsState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = TtsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        channelId = channel.id

        val config: TtsConfig =
            when (val result: ApiResult<TtsConfig> = ttsApi.config(channel.id)) {
                is ApiResult.Failure -> {
                    _state.value = TtsState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }

        // The available voices (resilient — a failure degrades to an empty list; the config still shows).
        val voices: List<TtsVoice> =
            when (val result: ApiResult<List<TtsVoice>> = ttsApi.voices(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        _state.value = TtsState.Ready(config = config, voices = voices)
    }

    /**
     * Synthesise a test utterance with [voiceId] (the voice selected in the picker) and [text]. Stores the result
     * on the Ready state so the screen can play the audio back inline via a data URI. Clears the previous test
     * result before starting so the UI doesn't flash stale audio. Surfaces the error on failure.
     */
    suspend fun testSpeak(voiceId: String, text: String) {
        val channel: String = channelId ?: return
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return
        _state.value = current.copy(testResult = null, testError = null, testing = true)
        _state.value =
            when (val result: ApiResult<TtsTestResult> = ttsApi.testSpeak(channel, TtsTestRequest(text, voiceId))) {
                is ApiResult.Failure ->
                    (_state.value as? TtsState.Ready)?.copy(testing = false, testError = result.error.message)
                        ?: current.copy(testing = false, testError = result.error.message)
                is ApiResult.Ok ->
                    (_state.value as? TtsState.Ready)?.copy(testing = false, testResult = result.value)
                        ?: current.copy(testing = false, testResult = result.value)
            }
    }

    /**
     * Persist [config] for the loaded channel. Sends the whole configuration as the update; the backend
     * echoes the saved values, which become the new loaded baseline ([TtsState.Ready.justSaved] flags the
     * confirmation). A failure surfaces on the current Ready state without discarding the in-progress edit.
     * No-ops when no channel is loaded yet (the form is only shown once Ready).
     */
    suspend fun save(config: TtsConfig) {
        val target: String = channelId ?: return
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return

        _state.value = current.copy(saving = true, justSaved = false, saveError = null)

        val update: TtsConfigUpdate =
            TtsConfigUpdate(
                isEnabled = config.isEnabled,
                defaultVoiceId = config.defaultVoiceId,
                maxLength = config.maxLength,
                minPermission = config.minPermission,
                skipBotMessages = config.skipBotMessages,
                readUsernames = config.readUsernames,
                profanityCensorEnabled = config.profanityCensorEnabled,
                modApprovalRequired = config.modApprovalRequired,
            )

        _state.value =
            when (val result: ApiResult<TtsConfig> = ttsApi.updateConfig(target, update)) {
                is ApiResult.Failure ->
                    current.copy(saving = false, justSaved = false, saveError = result.error.message)
                is ApiResult.Ok ->
                    current.copy(
                        config = result.value,
                        saving = false,
                        justSaved = true,
                        saveError = null,
                    )
            }
    }
}

/** The TTS page render state. */
sealed interface TtsState {
    data object Loading : TtsState

    /**
     * The loaded configuration plus the in-flight save signals: [saving] while a write is pending,
     * [justSaved] right after a successful save (the "Saved" confirmation), and [saveError] when the
     * last save failed. The screen seeds its editable form from [config].
     */
    data class Ready(
        val config: TtsConfig,
        val voices: List<TtsVoice> = emptyList(),
        val saving: Boolean = false,
        val justSaved: Boolean = false,
        val saveError: String? = null,
        val testing: Boolean = false,
        val testResult: TtsTestResult? = null,
        val testError: String? = null,
    ) : TtsState

    data class Error(val detail: String) : TtsState
}
