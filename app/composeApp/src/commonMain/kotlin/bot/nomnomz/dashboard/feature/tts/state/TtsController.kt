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
import bot.nomnomz.dashboard.core.network.TtsLexiconEntry
import bot.nomnomz.dashboard.core.network.TtsTestRequest
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.TtsVoicePage
import bot.nomnomz.dashboard.core.network.UpsertTtsLexiconEntryBody
import bot.nomnomz.dashboard.core.network.UserTtsVoice
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

        // The pronunciation lexicon (same resilience — the config page never blocks on it).
        val lexicon: List<TtsLexiconEntry> =
            when (val result: ApiResult<List<TtsLexiconEntry>> = ttsApi.lexicon(channel.id)) {
                is ApiResult.Failure -> emptyList()
                is ApiResult.Ok -> result.value
            }

        _state.value = TtsState.Ready(config = config, voices = voices, lexicon = lexicon)
    }

    /**
     * Add a pronunciation rule ([phrase] spoken as [replacement], matched per [matchKind]) and refresh the
     * rule list on success. A failure (e.g. a duplicate phrase) surfaces on the Ready state's lexicon panel
     * without dropping the loaded config.
     */
    suspend fun addLexiconEntry(phrase: String, replacement: String, matchKind: String) {
        mutateLexicon { channel ->
            ttsApi.createLexiconEntry(channel, UpsertTtsLexiconEntryBody(phrase, replacement, matchKind))
        }
    }

    /** Rewrite the rule [entryId], then refresh the list. Failures surface on the lexicon panel. */
    suspend fun updateLexiconEntry(entryId: String, phrase: String, replacement: String, matchKind: String) {
        mutateLexicon { channel ->
            ttsApi.updateLexiconEntry(channel, entryId, UpsertTtsLexiconEntryBody(phrase, replacement, matchKind))
        }
    }

    /** Delete the rule [entryId], then refresh the list. Failures surface on the lexicon panel. */
    suspend fun deleteLexiconEntry(entryId: String) {
        mutateLexicon { channel -> ttsApi.deleteLexiconEntry(channel, entryId) }
    }

    // Run one lexicon write, then re-fetch the authoritative list on success (the backend orders and
    // de-duplicates — the UI never guesses). On failure the error lands on the panel, the list stays put.
    private suspend fun mutateLexicon(write: suspend (channelId: String) -> ApiResult<*>) {
        val channel: String = channelId ?: return
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return

        _state.value = current.copy(lexiconBusy = true, lexiconError = null)
        when (val result: ApiResult<*> = write(channel)) {
            is ApiResult.Failure ->
                (_state.value as? TtsState.Ready)?.let {
                    _state.value = it.copy(lexiconBusy = false, lexiconError = result.error.message)
                }
            is ApiResult.Ok -> {
                val refreshed: List<TtsLexiconEntry> =
                    when (val list: ApiResult<List<TtsLexiconEntry>> = ttsApi.lexicon(channel)) {
                        is ApiResult.Failure -> (_state.value as? TtsState.Ready)?.lexicon ?: emptyList()
                        is ApiResult.Ok -> list.value
                    }
                (_state.value as? TtsState.Ready)?.let {
                    _state.value = it.copy(lexicon = refreshed, lexiconBusy = false, lexiconError = null)
                }
            }
        }
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
     * Look up the per-viewer voice override for [userId] and open the viewer-voice panel on it. A 404 (no
     * override) is not an error — it resolves to "uses the channel default" (a null voice). Surfaces a real
     * error on the panel. No-ops when no channel is loaded.
     */
    suspend fun loadUserVoice(userId: String) {
        val channel: String = channelId ?: return
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return

        _state.value = current.copy(viewerVoice = ViewerVoiceState(userId = userId, busy = true))
        val next: ViewerVoiceState =
            when (val result: ApiResult<UserTtsVoice?> = ttsApi.userVoice(channel, userId)) {
                is ApiResult.Ok -> ViewerVoiceState(userId = userId, currentVoiceId = result.value?.voiceId)
                is ApiResult.Failure -> ViewerVoiceState(userId = userId, error = result.error.message)
            }
        (_state.value as? TtsState.Ready)?.let { _state.value = it.copy(viewerVoice = next) }
    }

    /**
     * Assign [voiceId] to [userId], then reload the viewer-voice panel so it reflects the new override. A failure
     * (e.g. a voice the channel can't synthesise) surfaces on the panel. No-ops when no channel is loaded.
     */
    suspend fun setUserVoice(userId: String, voiceId: String) {
        val channel: String = channelId ?: return
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return

        _state.value =
            current.copy(viewerVoice = (current.viewerVoice ?: ViewerVoiceState(userId)).copy(busy = true, error = null))
        when (val result: ApiResult<Unit> = ttsApi.setUserVoice(channel, userId, voiceId)) {
            is ApiResult.Ok -> loadUserVoice(userId)
            is ApiResult.Failure -> surfaceViewerVoiceError(userId, result.error.message)
        }
    }

    /** Clear [userId]'s voice override, then reload the panel so it shows "uses the channel default". */
    suspend fun clearUserVoice(userId: String) {
        val channel: String = channelId ?: return
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return

        _state.value =
            current.copy(viewerVoice = (current.viewerVoice ?: ViewerVoiceState(userId)).copy(busy = true, error = null))
        when (val result: ApiResult<Unit> = ttsApi.clearUserVoice(channel, userId)) {
            is ApiResult.Ok -> loadUserVoice(userId)
            is ApiResult.Failure -> surfaceViewerVoiceError(userId, result.error.message)
        }
    }

    /**
     * Run a voice-catalogue search with the given [query] and equality filters against the paginated backend
     * endpoint, replacing the browser results on the Ready state. [page] is 1-based; the screen calls this on a
     * query/filter change (page 1) and on paging. A failure surfaces on the browser without dropping the config.
     */
    suspend fun searchVoices(
        query: String = "",
        locale: String = "",
        gender: String = "",
        provider: String = "",
        accent: String = "",
        page: Int = 1,
    ) {
        val channel: String = channelId ?: return
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return

        val querying: VoiceBrowserState =
            VoiceBrowserState(
                query = query,
                locale = locale,
                gender = gender,
                provider = provider,
                accent = accent,
                page = page,
                loading = true,
            )
        _state.value = current.copy(voiceBrowser = querying)

        val next: VoiceBrowserState =
            when (
                val result: ApiResult<TtsVoicePage> =
                    ttsApi.voicesPage(channel, query, locale, gender, provider, accent, page)
            ) {
                is ApiResult.Ok ->
                    querying.copy(
                        loading = false,
                        results = result.value.data,
                        total = result.value.total,
                        hasMore = result.value.hasMore,
                        error = null,
                    )
                is ApiResult.Failure -> querying.copy(loading = false, error = result.error.message)
            }
        (_state.value as? TtsState.Ready)?.let { _state.value = it.copy(voiceBrowser = next) }
    }

    /**
     * Store a bring-your-own-key credential for [provider] (`azure` | `elevenlabs`); [region] is Azure-only.
     * The backend echoes the refreshed config (the provider's stored-flag now true), which replaces the loaded
     * baseline. A failure surfaces on the Ready state without discarding the form.
     */
    suspend fun setByokKey(provider: String, apiKey: String, region: String?) {
        val target: String = channelId ?: return
        applyConfigResult { ttsApi.setByokKey(target, provider, apiKey, region) }
    }

    /** Remove the stored BYOK key for [provider]; the backend echoes the refreshed config (flag cleared). */
    suspend fun removeByokKey(provider: String) {
        val target: String = channelId ?: return
        applyConfigResult { ttsApi.removeByokKey(target, provider) }
    }

    // Run a config-returning write; on success replace the loaded config, on failure surface the error.
    private suspend fun applyConfigResult(write: suspend () -> ApiResult<TtsConfig>) {
        val current: TtsState = _state.value
        if (current !is TtsState.Ready) return
        _state.value = current.copy(saving = true, justSaved = false, saveError = null)
        _state.value =
            when (val result: ApiResult<TtsConfig> = write()) {
                is ApiResult.Failure -> current.copy(saving = false, saveError = result.error.message)
                is ApiResult.Ok ->
                    current.copy(config = result.value, saving = false, justSaved = true, saveError = null)
            }
    }

    // Put a viewer-voice write error back on the panel without losing the queried viewer.
    private fun surfaceViewerVoiceError(userId: String, message: String) {
        val current: TtsState = _state.value
        if (current is TtsState.Ready) {
            _state.value =
                current.copy(
                    viewerVoice = (current.viewerVoice ?: ViewerVoiceState(userId)).copy(busy = false, error = message)
                )
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
                mode = config.mode,
                defaultProvider = config.defaultProvider,
                defaultVoiceId = config.defaultVoiceId,
                maxCharacters = config.maxCharacters,
                minPermission = config.minPermission,
                skipBotMessages = config.skipBotMessages,
                readUsernames = config.readUsernames,
                profanityCensorEnabled = config.profanityCensorEnabled,
                modApprovalRequired = config.modApprovalRequired,
                minBitsToTts = config.minBitsToTts,
                viewerVoiceSelfServiceEnabled = config.viewerVoiceSelfServiceEnabled,
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
        // The per-viewer voice panel's state, or null until the operator looks up a viewer.
        val viewerVoice: ViewerVoiceState? = null,
        // The searchable voice-browser state, or null until the operator opens/searches it.
        val voiceBrowser: VoiceBrowserState? = null,
        // The pronunciation lexicon: the channel's rules plus the panel's write-in-flight / error signals.
        val lexicon: List<TtsLexiconEntry> = emptyList(),
        val lexiconBusy: Boolean = false,
        val lexiconError: String? = null,
    ) : TtsState

    data class Error(val detail: String) : TtsState
}

/**
 * The searchable voice-browser panel's state: the current [query] + equality filters, the 1-based [page], the
 * fetched [results] page with its [total] count and whether [hasMore] pages follow, plus [loading] / [error].
 */
data class VoiceBrowserState(
    val query: String = "",
    val locale: String = "",
    val gender: String = "",
    val provider: String = "",
    val accent: String = "",
    val page: Int = 1,
    val results: List<TtsVoice> = emptyList(),
    val total: Int = 0,
    val hasMore: Boolean = false,
    val loading: Boolean = false,
    val error: String? = null,
)

/**
 * The per-viewer voice panel's state: the looked-up [userId], their [currentVoiceId] (null = uses the channel
 * default), whether a read/write is in flight ([busy]), and the last [error]. Loaded on demand from a viewer id.
 */
data class ViewerVoiceState(
    val userId: String,
    val currentVoiceId: String? = null,
    val busy: Boolean = false,
    val error: String? = null,
)
