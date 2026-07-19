// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.sound.state

import bot.nomnomz.dashboard.core.feedback.Feedback
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.io.AudioFile
import bot.nomnomz.dashboard.core.io.AudioFilePickerIO
import bot.nomnomz.dashboard.core.io.playSoundPreview
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.SoundApi
import bot.nomnomz.dashboard.core.network.SoundClip
import bot.nomnomz.dashboard.core.network.UpdateSoundClipBody
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_deleted
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_save_failed
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_saved
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_uploaded

// The Sound page's state-holder. Lists the channel's uploaded sound clips from the backend (real data only).
// Drives upload, update, delete, and preview; each write re-lists on success so the page stays in sync.
class SoundController(
    private val soundApi: SoundApi,
    private val audioPicker: AudioFilePickerIO,
    private val feedback: Feedback = NoOpFeedback,
) {
    private val _state: MutableStateFlow<SoundState> = MutableStateFlow(SoundState.Loading)
    val state: StateFlow<SoundState> = _state.asStateFlow()

    private val _isUploading: MutableStateFlow<Boolean> = MutableStateFlow(false)
    val isUploading: StateFlow<Boolean> = _isUploading.asStateFlow()

    suspend fun load() {
        // Only show the full-page loading state on first load; a refetch after a mutation keeps
        // the current content on screen (no flash) and swaps it when the new data arrives.
        if (_state.value !is SoundState.Ready) _state.value = SoundState.Loading
        when (val result: ApiResult<List<SoundClip>> = soundApi.list()) {
            is ApiResult.Failure -> _state.value = SoundState.Error(result.error.message)
            is ApiResult.Ok ->
                _state.value =
                    if (result.value.isEmpty()) SoundState.Empty
                    else SoundState.Ready(result.value)
        }
    }

    suspend fun uploadClip() {
        _isUploading.value = true
        try {
            val file: AudioFile = audioPicker.pick() ?: return
            // Use the filename-without-extension as both the slug name and the initial display name.
            val name: String = file.name.substringBeforeLast('.')
            when (val result: ApiResult<SoundClip> =
                soundApi.upload(name, name, 80, file)) {
                is ApiResult.Ok -> {
                    feedback.success(Res.string.feedback_sound_clip_uploaded)
                    load()
                }
                is ApiResult.Failure -> failWrite(result.error.message)
            }
        } finally {
            _isUploading.value = false
        }
    }

    suspend fun updateClip(
        id: String,
        displayName: String,
        defaultVolume: Int,
        isEnabled: Boolean,
        cooldownSeconds: Int,
        minPermissionLevel: Int,
        triggerWord: String?,
    ) {
        afterWrite(
            soundApi.update(
                id,
                UpdateSoundClipBody(
                    displayName = displayName,
                    defaultVolume = defaultVolume,
                    isEnabled = isEnabled,
                    cooldownSeconds = cooldownSeconds,
                    minPermissionLevel = minPermissionLevel,
                    // A blank trigger word means "no chat trigger" — send null so the backend clears it.
                    triggerWord = triggerWord?.trim()?.ifBlank { null },
                ),
            )
        )
    }

    suspend fun deleteClip(id: String) {
        afterWrite(soundApi.delete(id), success = Res.string.feedback_sound_clip_deleted)
    }

    // Preview plays the clip IN THE DASHBOARD so the operator hears it on click. It used to POST to the backend,
    // which only pushed the clip to the OBS overlay — silent unless OBS was open (the reported "preview doesn't
    // play"). Fire-and-forget browser playback of the clip's anonymous stream URL; no network round-trip needed.
    fun previewClip(previewUrl: String) {
        if (previewUrl.isNotBlank()) playSoundPreview(previewUrl)
    }

    private suspend fun afterWrite(
        result: ApiResult<Unit>,
        success: org.jetbrains.compose.resources.StringResource = Res.string.feedback_sound_clip_saved,
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
        feedback.error(Res.string.feedback_sound_clip_save_failed, detail)
        val current: SoundState = _state.value
        _state.value =
            if (current is SoundState.Ready) current.copy(actionError = detail)
            else SoundState.Error(detail)
    }
}

/** The Sound page render state. */
sealed interface SoundState {
    data object Loading : SoundState

    data class Ready(val clips: List<SoundClip>, val actionError: String? = null) : SoundState

    data object Empty : SoundState

    data class Error(val detail: String) : SoundState
}
