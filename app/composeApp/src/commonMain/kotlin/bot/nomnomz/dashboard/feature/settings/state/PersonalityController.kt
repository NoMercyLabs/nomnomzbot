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
import bot.nomnomz.dashboard.core.network.ChannelPersonality
import bot.nomnomz.dashboard.core.network.ChannelSettingsApi
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.EventResponseSummary
import bot.nomnomz.dashboard.core.network.EventResponsesApi
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow

// The Settings "Bot personality" card's state-holder: resolves the active channel, loads its current tone plus
// the selectable set (both real, from the backend — never fabricated), and persists a new selection back,
// adopting the server's echoed (normalized) tone rather than the requested string. Mirrors [BillingController]'s
// (channelsApi + featureApi) shape; the resolved channel id is cached from [load] so [select] reuses it.
class PersonalityController(
    private val channelsApi: ChannelsApi,
    private val settingsApi: ChannelSettingsApi,
    private val eventResponsesApi: EventResponsesApi,
) {
    private val _state: MutableStateFlow<PersonalityState> = MutableStateFlow(PersonalityState.Loading)

    /** The card render state: loading / ready (current tone + selectable set) / error. */
    val state: StateFlow<PersonalityState> = _state.asStateFlow()

    /** The channel resolved by the last successful [load]; [select] targets it without re-resolving. */
    private var channelId: String? = null

    /** Resolve the active channel, then load its personality tone and the selectable set. */
    suspend fun load() {
        // Only show the full loading state on first load; a refetch after a change keeps the current content.
        if (_state.value !is PersonalityState.Ready) _state.value = PersonalityState.Loading

        val channel: ChannelSummary =
            when (val result: ApiResult<ChannelSummary> = channelsApi.primaryChannel()) {
                is ApiResult.Failure -> {
                    _state.value = PersonalityState.Error(result.error.message)
                    return
                }
                is ApiResult.Ok -> result.value
            }
        channelId = channel.id

        _state.value =
            when (val result: ApiResult<ChannelPersonality> = settingsApi.getPersonality(channel.id)) {
                is ApiResult.Failure -> PersonalityState.Error(result.error.message)
                is ApiResult.Ok ->
                    PersonalityState.Ready(
                        current = result.value.personality,
                        available = result.value.available,
                    )
            }
    }

    /**
     * The operator picked [tone]: nothing is saved yet. Counts the channel's active event responses — those that
     * follow the platform default (they change voice) and those with their own text (they do not) — and holds the
     * change as [PersonalityState.Ready.pending] until [confirm] or [cancel]. When the counts cannot be read the
     * change is still offered, with the counts unknown, so a failing list never blocks the tone.
     */
    suspend fun choose(tone: String) {
        val target: String = channelId ?: return
        val current: PersonalityState = _state.value
        if (current !is PersonalityState.Ready || current.saving || tone == current.current) return

        val summaries: List<EventResponseSummary>? =
            when (val result: ApiResult<List<EventResponseSummary>> = eventResponsesApi.list(target)) {
                is ApiResult.Ok -> result.value.filter { it.isEnabled }
                is ApiResult.Failure -> null
            }
        _state.value =
            current.copy(
                pending =
                    ToneChange(
                        tone = tone,
                        following = summaries?.count { it.speaksInTone },
                        own = summaries?.count { !it.speaksInTone },
                    ),
                justSaved = false,
                saveError = null,
            )
    }

    /** Drop the pending tone change without saving anything. */
    fun cancel() {
        val current: PersonalityState = _state.value
        if (current is PersonalityState.Ready) _state.value = current.copy(pending = null)
    }

    /**
     * Save the pending tone change (see [choose]) and hand the server's answer back to the confirm dialog. The
     * change stays pending until the server accepts it, so a rejected write keeps the dialog open to show the
     * reason inline (nothing is set on the card). A no-op success when nothing is pending.
     */
    suspend fun confirm(): ApiResult<Unit> {
        val target: String = channelId ?: return ApiResult.Ok(Unit)
        val current: PersonalityState = _state.value
        if (current !is PersonalityState.Ready) return ApiResult.Ok(Unit)
        val pending: ToneChange = current.pending ?: return ApiResult.Ok(Unit)

        return when (val result: ApiResult<ChannelPersonality> = settingsApi.setPersonality(target, pending.tone)) {
            is ApiResult.Failure -> ApiResult.Failure(result.error)
            is ApiResult.Ok -> {
                _state.value =
                    current.copy(
                        current = result.value.personality,
                        available = result.value.available,
                        saving = false,
                        justSaved = true,
                        saveError = null,
                        pending = null,
                    )
                ApiResult.Ok(Unit)
            }
        }
    }

    /**
     * Persist [tone] as the channel's personality. Nothing on screen changes until the backend echoes the saved
     * tone (adopted verbatim — the server normalizes case and is the source of truth), so a rejected write never
     * leaves a wrong tone selected. A no-op when the tone is already current or no channel is loaded yet.
     */
    suspend fun select(tone: String) {
        val target: String = channelId ?: return
        val current: PersonalityState = _state.value
        if (current !is PersonalityState.Ready) return
        if (tone == current.current && !current.saving) return

        _state.value = current.copy(saving = true, justSaved = false, saveError = null)

        _state.value =
            when (val result: ApiResult<ChannelPersonality> = settingsApi.setPersonality(target, tone)) {
                is ApiResult.Failure ->
                    current.copy(saving = false, justSaved = false, saveError = result.error.message)
                is ApiResult.Ok ->
                    current.copy(
                        current = result.value.personality,
                        available = result.value.available,
                        saving = false,
                        justSaved = true,
                        saveError = null,
                    )
            }
    }
}

/** The "Bot personality" card render state. */
sealed interface PersonalityState {
    data object Loading : PersonalityState

    /**
     * The loaded tone ([current]) and the selectable set ([available]), plus the in-flight select signals:
     * [saving] while a write is pending, [justSaved] right after a successful change (the confirmation), and
     * [saveError] when the last change failed. The screen renders the picker from [available], marking [current].
     */
    data class Ready(
        val current: String,
        val available: List<String>,
        val saving: Boolean = false,
        val justSaved: Boolean = false,
        val saveError: String? = null,
        val pending: ToneChange? = null,
    ) : PersonalityState

    data class Error(val detail: String) : PersonalityState
}

/**
 * A tone the operator picked but has not saved. [following] counts the active event responses that follow the
 * platform default (they change voice); [own] counts those with their own text (unchanged). Both are null when the
 * event responses could not be listed.
 */
data class ToneChange(val tone: String, val following: Int?, val own: Int?)
