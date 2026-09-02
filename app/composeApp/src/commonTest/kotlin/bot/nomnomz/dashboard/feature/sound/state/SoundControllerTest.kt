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
import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.NoOpFeedback
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.io.AudioFile
import bot.nomnomz.dashboard.core.io.AudioFilePickerIO
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.BlastRadiusSummary
import bot.nomnomz.dashboard.core.network.SoundApi
import bot.nomnomz.dashboard.core.network.SoundClip
import bot.nomnomz.dashboard.core.network.UpdateSoundClipBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_preview_overlay_failed
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_preview_overlay_sent

// S104-PREVIEW-ON-OVERLAY: proves the sound library's "Preview on overlay" action actually calls the real
// backend endpoint (POST /sound-clips/{id}/preview, which pushes a PlaySound event to the connected OBS
// overlay via SignalR) — not the unrelated, purely-local `previewClip` in-browser playback. Before this
// slice, SoundApi.preview(id) had a real backend behind it but zero callers anywhere in the dashboard.
class SoundControllerTest {

    @Test
    fun previewOnOverlay_calls_the_real_backend_preview_endpoint_for_that_clip() = runTest {
        val api = FakeSoundApi()
        val controller = soundController(api = api)

        controller.previewOnOverlay(id = "clip-1")

        assertEquals(listOf("clip-1"), api.previewedClipIds)
    }

    @Test
    fun a_successful_overlay_preview_announces_success_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val api = FakeSoundApi()
        val controller = soundController(api = api, feedback = feedback)

        controller.previewOnOverlay(id = "clip-1")

        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_sound_clip_preview_overlay_sent, feedback.only.label)
    }

    @Test
    fun a_failed_overlay_preview_announces_an_error_carrying_the_backend_detail() = runTest {
        val feedback = RecordingFeedback()
        val api =
            FakeSoundApi(previewFailure = ApiError(400, "NOT_FOUND", "Sound clip not found or disabled."))
        val controller = soundController(api = api, feedback = feedback)

        controller.previewOnOverlay(id = "clip-1")

        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_sound_clip_preview_overlay_failed, feedback.only.label)
        assertEquals(listOf<Any>("Sound clip not found or disabled."), feedback.only.formatArgs)
    }
}

private fun soundController(
    api: SoundApi,
    feedback: Feedback = NoOpFeedback,
): SoundController =
    SoundController(
        soundApi = api,
        audioPicker = StubAudioFilePicker,
        feedback = feedback,
    )

private object StubAudioFilePicker : AudioFilePickerIO {
    override suspend fun pick(): AudioFile? = null
}

/** A fake [SoundApi] that records every clip id sent to the real overlay-preview endpoint. */
private class FakeSoundApi(private val previewFailure: ApiError? = null) : SoundApi {
    val previewedClipIds: MutableList<String> = mutableListOf()

    override suspend fun list(): ApiResult<List<SoundClip>> = ApiResult.Ok(emptyList())

    override suspend fun update(id: String, body: UpdateSoundClipBody): ApiResult<Unit> = error("stub")

    override suspend fun delete(id: String): ApiResult<Unit> = error("stub")

    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> = error("stub")

    override suspend fun preview(id: String): ApiResult<Unit> {
        previewedClipIds += id
        previewFailure?.let { return ApiResult.Failure(it) }
        return ApiResult.Ok(Unit)
    }

    override suspend fun upload(
        name: String,
        displayName: String,
        defaultVolume: Int,
        file: AudioFile,
    ): ApiResult<SoundClip> = error("stub")
}
