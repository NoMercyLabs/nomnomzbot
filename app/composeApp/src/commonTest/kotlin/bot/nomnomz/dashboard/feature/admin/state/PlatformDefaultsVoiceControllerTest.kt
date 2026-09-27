// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.admin.state

import bot.nomnomz.dashboard.core.network.TtsVoiceCandidate
import bot.nomnomz.dashboard.core.network.TtsVoiceDefault
import kotlinx.coroutines.test.runTest
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertFalse
import kotlin.test.assertNotNull
import kotlin.test.assertNull
import kotlin.test.assertTrue

/**
 * The TTS voice family of the platform-defaults tab (plan item A4): the editor opens on the current voice, a
 * different pick drops the old count, the save arms only after a preview for exactly the picked voice, it
 * echoes that count, the card and the candidates become the server read-back, and a missing default is an
 * editable empty card rather than an error.
 */
class PlatformDefaultsVoiceControllerTest {
    private val aria = TtsVoiceCandidate("en-US-AriaNeural", "Aria (US)", "en-US", "Female", isDefault = true)
    private val guy = TtsVoiceCandidate("en-US-GuyNeural", "Guy (US)", "en-US", "Male")
    private val sonia = TtsVoiceCandidate("en-GB-SoniaNeural", "Sonia (GB)", "en-GB", "Female")
    private val ariaDefault = TtsVoiceDefault(
        voiceId = "en-US-AriaNeural",
        displayName = "Aria (US)",
        locale = "en-US",
        provider = "edge",
        channelsFollowing = 3,
        channelsWithOwnVoice = 1,
    )

    private fun api(voice: TtsVoiceDefault? = ariaDefault): FakePlatformDefaultsApi =
        FakePlatformDefaultsApi(emptyList(), voice = voice, voices = listOf(sonia, aria, guy))

    private suspend fun opened(api: FakePlatformDefaultsApi): PlatformDefaultsController {
        val controller = PlatformDefaultsController(api)
        controller.loadTtsVoiceDefault()
        controller.openVoiceEdit()
        return controller
    }

    @Test
    fun the_editor_opens_on_the_current_voice_and_previews_nothing() = runTest {
        val api = api()
        val controller = opened(api)

        val edit: TtsVoiceDefaultEdit = assertNotNull(controller.state.value.voiceEdit)
        assertEquals("en-US-AriaNeural", edit.voiceId)
        assertNull(edit.preview)
        assertFalse(edit.canSave)
        assertTrue(api.voicePreviews.isEmpty(), "opening the editor previews nothing")
    }

    @Test
    fun a_different_pick_drops_the_count_and_the_save_arms_only_after_its_own_preview() = runTest {
        val api = api()
        val controller = opened(api)

        controller.pickVoice("en-GB-SoniaNeural")
        assertFalse(assertNotNull(controller.state.value.voiceEdit).canSave, "no count for this voice yet")

        controller.previewVoiceEdit()
        val edit: TtsVoiceDefaultEdit = assertNotNull(controller.state.value.voiceEdit)
        assertEquals("en-GB-SoniaNeural", api.voicePreviews.single().voiceId)
        assertEquals(3, edit.preview?.channelsAffected)
        assertEquals(1, edit.preview?.channelsKeepingOwnSetting)
        assertTrue(edit.canSave)

        controller.pickVoice("en-US-GuyNeural")
        assertNull(controller.state.value.voiceEdit?.preview, "a later pick invalidates the count")
    }

    @Test
    fun saving_echoes_the_count_and_the_card_and_candidates_become_the_read_back() = runTest {
        val api = api()
        val controller = opened(api)
        controller.pickVoice("en-GB-SoniaNeural")
        controller.previewVoiceEdit()

        controller.saveVoiceEdit()

        val body = api.voiceSaves.single()
        assertEquals(3, body.confirmedChannelsAffected)
        assertEquals("en-GB-SoniaNeural", body.voiceId)
        assertEquals("Sonia (GB)", controller.state.value.voiceDefault?.displayName)
        assertEquals(
            listOf("en-GB-SoniaNeural"),
            controller.state.value.voiceCandidates.filter { it.isDefault }.map { it.voiceId },
        )
        assertNull(controller.state.value.voiceEdit)
    }

    @Test
    fun the_current_voice_changes_nobody_and_a_stale_count_is_re_previewed() = runTest {
        val api = api()
        val controller = opened(api)

        controller.previewVoiceEdit()
        assertEquals(0, controller.state.value.voiceEdit?.preview?.channelsAffected)

        controller.pickVoice("en-GB-SoniaNeural")
        controller.previewVoiceEdit()
        api.followers = 5
        controller.saveVoiceEdit()

        assertEquals(listOf(3), api.voiceSaves.map { it.confirmedChannelsAffected })
        assertEquals("en-US-AriaNeural", controller.state.value.voiceDefault?.voiceId, "a refused save changes nothing")
        assertEquals(5, controller.state.value.voiceEdit?.preview?.channelsAffected, "re-previewed with the live count")
    }

    @Test
    fun a_missing_default_loads_the_candidates_and_the_editor_opens_on_the_first() = runTest {
        val api = api(voice = null)
        val controller = opened(api)

        assertTrue(controller.state.value.voiceLoaded)
        assertNull(controller.state.value.voiceDefault)
        assertEquals("en-GB-SoniaNeural", controller.state.value.voiceEdit?.voiceId)
    }
}
