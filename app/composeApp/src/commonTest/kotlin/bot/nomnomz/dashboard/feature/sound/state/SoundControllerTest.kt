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
import bot.nomnomz.dashboard.core.network.ChannelAudioMix
import bot.nomnomz.dashboard.core.network.UpdateChannelAudioMixBody
import bot.nomnomz.dashboard.core.network.SoundApi
import bot.nomnomz.dashboard.core.network.SoundClip
import bot.nomnomz.dashboard.core.network.UpdateSoundClipBody
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlinx.coroutines.test.runTest
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_preview_overlay_failed
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_preview_overlay_sent
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_stop_failed
import nomnomzbot.composeapp.generated.resources.feedback_sound_clip_stop_sent
import nomnomzbot.composeapp.generated.resources.feedback_sound_mix_save_failed

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

    // S-OBS-06: the dashboard's Stop control must call the real backend endpoint (POST /sound-clips/stop),
    // which pushes StopSound(all) to the connected overlay via SignalR — proving it is wired, not a no-op.
    @Test
    fun stopAll_calls_the_real_backend_stop_endpoint() = runTest {
        val api = FakeSoundApi()
        val controller = soundController(api = api)

        controller.stopAll()

        assertEquals(1, api.stopCallCount)
    }

    @Test
    fun a_successful_stop_announces_success_on_the_frame() = runTest {
        val feedback = RecordingFeedback()
        val api = FakeSoundApi()
        val controller = soundController(api = api, feedback = feedback)

        controller.stopAll()

        assertEquals(FeedbackKind.Success, feedback.only.kind)
        assertEquals(Res.string.feedback_sound_clip_stop_sent, feedback.only.label)
    }

    @Test
    fun a_stop_with_no_overlay_attached_announces_an_error_carrying_the_backend_detail() = runTest {
        val feedback = RecordingFeedback()
        val api =
            FakeSoundApi(
                stopFailure = ApiError(409, "NOT_ATTACHED", "No overlay is connected on this channel."),
            )
        val controller = soundController(api = api, feedback = feedback)

        controller.stopAll()

        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_sound_clip_stop_failed, feedback.only.label)
        assertEquals(listOf<Any>("No overlay is connected on this channel."), feedback.only.formatArgs)
    }

    @Test
    fun load_fetches_the_mix_and_exposes_it() = runTest {
        val api = FakeSoundApi(storedMix = ChannelAudioMix(masterVolume = 60, ttsVolume = 30, ttsPlaybackVolume = 0.18))
        val controller = soundController(api = api)

        controller.load()

        assertEquals(MixState.Ready(ChannelAudioMix(60, 30, 0.18)), controller.mix.value)
    }

    @Test
    fun a_failed_mix_load_leaves_the_clips_loaded_and_the_mix_in_error() = runTest {
        val api =
            FakeSoundApi(
                clips = listOf(SoundClip(id = "c1", name = "airhorn")),
                mixLoadFailure = ApiError(500, "BOOM", "mix down"),
            )
        val controller = soundController(api = api)

        controller.load()

        assertEquals(SoundState.Ready(listOf(SoundClip(id = "c1", name = "airhorn"))), controller.state.value)
        assertEquals(MixState.Error("mix down"), controller.mix.value)
    }

    @Test
    fun updateMix_puts_exactly_the_two_values_and_exposes_the_returned_mix() = runTest {
        val api = FakeSoundApi(storedMix = ChannelAudioMix(100, 100, 0.3))
        val controller = soundController(api = api)
        controller.load()

        controller.updateMix(master = 40, tts = 70)

        assertEquals(listOf(UpdateChannelAudioMixBody(masterVolume = 40, ttsVolume = 70)), api.mixPuts)
        assertEquals(MixState.Ready(ChannelAudioMix(40, 70, 0.5)), controller.mix.value)
    }

    @Test
    fun a_failed_mix_save_restores_the_previous_mix_and_surfaces_the_error() = runTest {
        val feedback = RecordingFeedback()
        val api =
            FakeSoundApi(
                storedMix = ChannelAudioMix(80, 50, 0.15),
                mixSaveFailure = ApiError(400, "BAD", "out of range"),
            )
        val controller = soundController(api = api, feedback = feedback)
        controller.load()

        controller.updateMix(master = 10, tts = 10)

        assertEquals(MixState.Ready(ChannelAudioMix(80, 50, 0.15)), controller.mix.value)
        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(Res.string.feedback_sound_mix_save_failed, feedback.only.label)
        assertEquals(listOf<Any>("out of range"), feedback.only.formatArgs)
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
private class FakeSoundApi(
    private val previewFailure: ApiError? = null,
    private val stopFailure: ApiError? = null,
    private val clips: List<SoundClip> = emptyList(),
    private val storedMix: ChannelAudioMix = ChannelAudioMix(100, 100, 0.3),
    private val mixLoadFailure: ApiError? = null,
    private val mixSaveFailure: ApiError? = null,
) : SoundApi {
    val mixPuts: MutableList<UpdateChannelAudioMixBody> = mutableListOf()

    val previewedClipIds: MutableList<String> = mutableListOf()
    var stopCallCount: Int = 0

    override suspend fun list(): ApiResult<List<SoundClip>> = ApiResult.Ok(clips)

    override suspend fun update(id: String, body: UpdateSoundClipBody): ApiResult<Unit> = error("stub")

    override suspend fun delete(id: String): ApiResult<Unit> = error("stub")

    override suspend fun blastRadius(id: String): ApiResult<BlastRadiusSummary> = error("stub")

    override suspend fun preview(id: String): ApiResult<Unit> {
        previewedClipIds += id
        previewFailure?.let { return ApiResult.Failure(it) }
        return ApiResult.Ok(Unit)
    }

    override suspend fun stop(): ApiResult<Unit> {
        stopCallCount += 1
        stopFailure?.let { return ApiResult.Failure(it) }
        return ApiResult.Ok(Unit)
    }

    override suspend fun getMix(): ApiResult<ChannelAudioMix> {
        mixLoadFailure?.let { return ApiResult.Failure(it) }
        return ApiResult.Ok(storedMix)
    }

    override suspend fun updateMix(body: UpdateChannelAudioMixBody): ApiResult<ChannelAudioMix> {
        mixPuts += body
        mixSaveFailure?.let { return ApiResult.Failure(it) }
        return ApiResult.Ok(ChannelAudioMix(body.masterVolume, body.ttsVolume, 0.5))
    }

    override suspend fun upload(
        name: String,
        displayName: String,
        defaultVolume: Int,
        file: AudioFile,
    ): ApiResult<SoundClip> = error("stub")
}
