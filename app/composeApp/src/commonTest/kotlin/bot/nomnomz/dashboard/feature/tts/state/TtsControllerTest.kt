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

import bot.nomnomz.dashboard.core.feedback.FeedbackKind
import bot.nomnomz.dashboard.core.feedback.RecordingFeedback
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.ChannelNamePronunciation
import bot.nomnomz.dashboard.core.network.ChannelSummary
import bot.nomnomz.dashboard.core.network.ChannelsApi
import bot.nomnomz.dashboard.core.network.ModeratedChannel
import bot.nomnomz.dashboard.core.network.TtsApi
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsConfigUpdate
import bot.nomnomz.dashboard.core.network.TtsLexiconEntry
import bot.nomnomz.dashboard.core.network.TtsOverlay
import bot.nomnomz.dashboard.core.network.UpsertTtsLexiconEntryBody
import bot.nomnomz.dashboard.core.network.TtsTestRequest
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsQueueEntry
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.TtsVoicePage
import bot.nomnomz.dashboard.core.network.UserTtsVoice
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertNull
import kotlin.test.assertTrue
import kotlinx.coroutines.test.runTest

// Proves the TTS page state machine the screen renders read-only: resolve the active channel, then surface the
// real TTS configuration — or an error if either step fails. The screen is a pure projection of this, so testing
// it proves the page shows the channel's real config (no fabricated values) and degrades cleanly.
class TtsControllerTest {

    @Test
    fun load_surfaces_the_channels_tts_config_on_success() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTtsApi(
                    ApiResult.Ok(
                        TtsConfig(
                            isEnabled = true,
                            defaultVoiceId = "en-US-Brian",
                            maxCharacters = 200,
                            minPermission = "subscribers",
                            skipBotMessages = true,
                            readUsernames = false,
                        )
                    )
                ),
            )

        controller.load()

        val state: TtsState = controller.state.value
        assertTrue(state is TtsState.Ready)
        val config: TtsConfig = (state as TtsState.Ready).config
        assertEquals(true, config.isEnabled)
        assertEquals("en-US-Brian", config.defaultVoiceId)
        assertEquals(200, config.maxCharacters)
        assertEquals("subscribers", config.minPermission)
        assertEquals(true, config.skipBotMessages)
        assertEquals(false, config.readUsernames)
    }

    @Test
    fun load_surfaces_the_available_voices() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTtsApi(
                    result = ApiResult.Ok(TtsConfig(defaultVoiceId = "en-US-Brian")),
                    voicesResult =
                        ApiResult.Ok(
                            listOf(
                                TtsVoice(
                                    id = "en-US-Brian",
                                    displayName = "Brian",
                                    locale = "en-US",
                                    provider = "azure",
                                )
                            )
                        ),
                ),
            )

        controller.load()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(1, ready.voices.size)
        assertEquals("Brian", ready.voices.first().displayName)
    }

    @Test
    fun load_errors_when_no_channel_resolves() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Failure(ApiError(404, "NO_CHANNEL", "none onboarded"))),
                FakeTtsApi(ApiResult.Ok(TtsConfig())),
            )

        controller.load()

        assertTrue(controller.state.value is TtsState.Error)
    }

    @Test
    fun load_errors_when_the_config_call_fails() = runTest {
        val controller =
            TtsController(
                FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
                FakeTtsApi(ApiResult.Failure(ApiError(500, "ERR", "boom"))),
            )

        controller.load()

        assertTrue(controller.state.value is TtsState.Error)
    }

    @Test
    fun save_sends_the_edit_and_reflects_the_backend_echoed_config() = runTest {
        // The backend canonicalizes and echoes the saved config back; the controller adopts THAT, not the
        // value the user typed. Here the echo flips readUsernames on and clamps maxCharacters, proving the
        // controller surfaces the persisted server values rather than the request.
        val loaded =
            TtsConfig(
                isEnabled = false,
                defaultVoiceId = "en-US-Brian",
                maxCharacters = 200,
                minPermission = "everyone",
                skipBotMessages = false,
                readUsernames = false,
            )
        val echoed =
            TtsConfig(
                isEnabled = true,
                defaultVoiceId = "en-GB-Sonia",
                maxCharacters = 300,
                minPermission = "subscribers",
                skipBotMessages = true,
                readUsernames = true,
            )
        val ttsApi = FakeTtsApi(ApiResult.Ok(loaded), updateResult = ApiResult.Ok(echoed))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        val edited: TtsConfig = loaded.copy(isEnabled = true, minPermission = "subscribers")
        controller.save(edited)

        // The whole config is sent for the resolved channel as a TtsConfigUpdate carrying the edited values.
        assertEquals("ch1", ttsApi.lastUpdateChannelId)
        assertEquals(
            TtsConfigUpdate(
                isEnabled = true,
                mode = "self_host",
                defaultProvider = "edge",
                defaultVoiceId = "en-US-Brian",
                maxCharacters = 200,
                minPermission = "subscribers",
                skipBotMessages = false,
                readUsernames = false,
                profanityCensorEnabled = true,
                modApprovalRequired = false,
                minBitsToTts = null,
                viewerVoiceSelfServiceEnabled = true,
            ),
            ttsApi.lastUpdate,
        )

        // State now holds the backend echo, flags the save, and carries no error.
        val state: TtsState = controller.state.value
        assertTrue(state is TtsState.Ready)
        val ready: TtsState.Ready = state as TtsState.Ready
        assertEquals(echoed, ready.config)
        assertTrue(ready.justSaved)
        assertEquals(false, ready.saving)
        assertNull(ready.saveError)
    }

    @Test
    fun save_failure_surfaces_the_error_without_losing_the_loaded_config() = runTest {
        val loaded =
            TtsConfig(
                isEnabled = true,
                defaultVoiceId = "en-US-Brian",
                maxCharacters = 200,
                minPermission = "everyone",
                skipBotMessages = false,
                readUsernames = false,
            )
        val ttsApi =
            FakeTtsApi(
                ApiResult.Ok(loaded),
                updateResult = ApiResult.Failure(ApiError(500, "ERR", "save boom")),
            )
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.save(loaded.copy(maxCharacters = 50))

        // The page stays Ready on the loaded config (no data loss) but surfaces the save error, save cleared.
        val state: TtsState = controller.state.value
        assertTrue(state is TtsState.Ready)
        val ready: TtsState.Ready = state as TtsState.Ready
        assertEquals(loaded, ready.config)
        assertEquals("save boom", ready.saveError)
        assertEquals(false, ready.saving)
        assertEquals(false, ready.justSaved)
    }

    @Test
    fun load_carries_the_servers_defaults_for_the_reset_preview_and_null_when_they_fail() = runTest {
        val serverDefaults = TtsConfig(isEnabled = true, maxCharacters = 500, minPermission = "everyone")

        val withDefaults = TtsController(
            FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            FakeTtsApi(ApiResult.Ok(TtsConfig()), defaultsResult = ApiResult.Ok(serverDefaults)),
        )
        withDefaults.load()
        assertEquals(serverDefaults, (withDefaults.state.value as TtsState.Ready).resetDefaults)

        // A failed defaults call must not block the page or be replaced by a client-side guess.
        val withoutDefaults = TtsController(
            FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))),
            FakeTtsApi(
                ApiResult.Ok(TtsConfig()),
                defaultsResult = ApiResult.Failure(ApiError(500, "ERR", "boom")),
            ),
        )
        withoutDefaults.load()
        val ready: TtsState.Ready = withoutDefaults.state.value as TtsState.Ready
        assertNull(ready.resetDefaults)
    }

    @Test
    fun reset_calls_the_reset_endpoint_and_adopts_the_resulting_config() = runTest {
        val loaded = TtsConfig(isEnabled = false, maxCharacters = 120, minPermission = "moderators")
        val resulting = TtsConfig(isEnabled = true, maxCharacters = 500, minPermission = "everyone")
        val ttsApi = FakeTtsApi(ApiResult.Ok(loaded), resetResult = ApiResult.Ok(resulting))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.resetConfig()

        assertEquals(listOf("ch1"), ttsApi.resetCalls)
        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(resulting, ready.config)
        assertTrue(ready.justSaved)
        assertEquals(false, ready.saving)
        assertNull(ready.saveError)
    }

    @Test
    fun reset_failure_surfaces_the_error_and_keeps_the_loaded_config() = runTest {
        val loaded = TtsConfig(isEnabled = false, maxCharacters = 120)
        val ttsApi =
            FakeTtsApi(
                ApiResult.Ok(loaded),
                resetResult = ApiResult.Failure(ApiError(403, "FORBIDDEN", "not allowed")),
            )
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        val result: ApiResult<TtsConfig> = controller.resetConfig()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(loaded, ready.config)
        assertEquals("not allowed", (result as ApiResult.Failure).error.message)
        assertEquals(null, ready.saveError)
        assertEquals(false, ready.justSaved)
    }

    @Test
    fun look_up_viewer_with_no_override_shows_the_channel_default() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.loadUserVoice("viewer-1")

        // No override recorded → the panel resolves to "uses the channel default" (a null voice), not an error.
        val viewer: ViewerVoiceState? = (controller.state.value as? TtsState.Ready)?.viewerVoice
        assertEquals("viewer-1", viewer?.userId)
        assertNull(viewer?.currentVoiceId)
        assertNull(viewer?.error)
    }

    @Test
    fun assign_viewer_voice_persists_and_the_panel_reflects_it() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()
        controller.loadUserVoice("viewer-1")

        controller.setUserVoice("viewer-1", "en-US-Brian")

        // The assign hit the API with the viewer + voice, and the reloaded panel now shows that voice.
        assertEquals(listOf("viewer-1" to "en-US-Brian"), ttsApi.setUserVoiceCalls)
        assertEquals("en-US-Brian", (controller.state.value as? TtsState.Ready)?.viewerVoice?.currentVoiceId)
    }

    @Test
    fun clear_viewer_voice_returns_them_to_the_channel_default() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()
        controller.setUserVoice("viewer-1", "en-US-Brian")

        controller.clearUserVoice("viewer-1")

        // The clear hit the API and the reloaded panel shows no override (back to the channel default).
        assertEquals(listOf("viewer-1"), ttsApi.clearedUserVoices)
        assertNull((controller.state.value as? TtsState.Ready)?.viewerVoice?.currentVoiceId)
    }

    // ── Overlay ──────────────────────────────────────────────────────────────

    @Test
    fun load_surfaces_the_overlay_url_and_a_real_last_ran_timestamp() = runTest {
        val ttsApi =
            FakeTtsApi(
                ApiResult.Ok(TtsConfig()),
                overlayResult =
                    ApiResult.Ok(
                        TtsOverlay(
                            overlayUrl = "https://bot.example/overlays/tts_caption/abc123",
                            lastRanAt = "2026-08-29T12:00:00Z",
                        )
                    ),
            )
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)

        controller.load()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals("https://bot.example/overlays/tts_caption/abc123", ready.overlay?.overlayUrl)
        assertEquals("2026-08-29T12:00:00Z", ready.overlay?.lastRanAt)
    }

    @Test
    fun load_surfaces_a_never_ran_overlay_distinctly_from_a_real_last_run() = runTest {
        val ttsApi =
            FakeTtsApi(
                ApiResult.Ok(TtsConfig()),
                overlayResult =
                    ApiResult.Ok(TtsOverlay(overlayUrl = "https://bot.example/overlays/tts_caption/new", lastRanAt = null)),
            )
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)

        controller.load()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        // The two states are distinguishable in the loaded data itself, not merely rendered the same way:
        // a real run carries a parseable timestamp, "never ran" carries null.
        assertEquals("https://bot.example/overlays/tts_caption/new", ready.overlay?.overlayUrl)
        assertNull(ready.overlay?.lastRanAt)
    }

    @Test
    fun load_degrades_cleanly_when_the_overlay_call_fails() = runTest {
        val ttsApi =
            FakeTtsApi(ApiResult.Ok(TtsConfig()), overlayResult = ApiResult.Failure(ApiError(500, "ERR", "boom")))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)

        controller.load()

        // The rest of the config still loads — a broken overlay call never blocks the page.
        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertNull(ready.overlay)
    }

    @Test
    fun test_overlay_dispatches_through_the_api_and_shows_the_confirmation() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.testOverlay()

        assertEquals(listOf("ch1"), ttsApi.testOverlayCalls)
        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertTrue(ready.overlayTestSent)
        assertEquals(false, ready.overlayTestSending)
        assertNull(ready.overlayTestError)
    }

    @Test
    fun test_overlay_failure_surfaces_the_error_without_a_false_confirmation() = runTest {
        val ttsApi =
            FakeTtsApi(ApiResult.Ok(TtsConfig()), testOverlayResult = ApiResult.Failure(ApiError(500, "ERR", "boom")))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.testOverlay()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(false, ready.overlayTestSent)
        assertEquals(false, ready.overlayTestSending)
        assertEquals("boom", ready.overlayTestError)
    }

    // ── Live playback queue controls (S052-queue-controls) ──────────────────

    @Test
    fun skip_calls_the_skip_route_and_never_flips_the_paused_flag() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.skipPlayback()

        assertEquals(listOf("skip"), ttsApi.playbackControlCalls)
        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(false, ready.playbackPaused)
        assertEquals(false, ready.playbackControlBusy)
        assertNull(ready.playbackControlError)
    }

    @Test
    fun clear_calls_the_clear_route_and_never_flips_the_paused_flag() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.clearPlayback()

        assertEquals(listOf("clear"), ttsApi.playbackControlCalls)
        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(false, ready.playbackPaused)
    }

    @Test
    fun pause_then_resume_round_trips_the_paused_flag() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.pausePlayback()
        assertEquals(true, (controller.state.value as TtsState.Ready).playbackPaused)
        assertEquals(listOf("pause"), ttsApi.playbackControlCalls)

        controller.resumePlayback()
        assertEquals(false, (controller.state.value as TtsState.Ready).playbackPaused)
        assertEquals(listOf("pause", "resume"), ttsApi.playbackControlCalls)
    }

    @Test
    fun a_failed_playback_command_surfaces_the_error_and_leaves_paused_state_untouched() = runTest {
        val ttsApi =
            FakeTtsApi(
                ApiResult.Ok(TtsConfig()),
                playbackControlResult = ApiResult.Failure(ApiError(500, "ERR", "overlay unreachable")),
            )
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.pausePlayback()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(false, ready.playbackPaused, "a failed pause call must not claim the queue is paused")
        assertEquals(false, ready.playbackControlBusy)
        assertEquals("overlay unreachable", ready.playbackControlError)
    }

    // ── Pronunciation lexicon ────────────────────────────────────────────────

    @Test
    fun load_surfaces_the_channels_lexicon_rules() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        ttsApi.lexiconEntries.add(
            TtsLexiconEntry(id = "lex-9", phrase = "brb", replacement = "be right back", matchKind = "word")
        )
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)

        controller.load()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(listOf("brb"), ready.lexicon.map { it.phrase })
        assertEquals("be right back", ready.lexicon.single().replacement)
    }

    @Test
    fun add_lexicon_entry_persists_and_refreshes_the_list() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.addLexiconEntry("JD", "Jaydee", "word")

        // The write reached the API and the refreshed authoritative list is on the state.
        assertEquals(listOf("JD"), ttsApi.lexiconEntries.map { it.phrase })
        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(1, ready.lexicon.size)
        assertEquals("Jaydee", ready.lexicon.single().replacement)
        assertEquals("word", ready.lexicon.single().matchKind)
    }

    @Test
    fun update_lexicon_entry_rewrites_the_rule() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()
        controller.addLexiconEntry("JD", "Jaydee", "word")
        val id: String = ttsApi.lexiconEntries.single().id

        controller.updateLexiconEntry(id, "JD", "Jay Dee", "exact")

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals("Jay Dee", ready.lexicon.single().replacement)
        assertEquals("exact", ready.lexicon.single().matchKind)
    }

    @Test
    fun delete_lexicon_entry_removes_it_from_the_list() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()
        controller.addLexiconEntry("JD", "Jaydee", "word")
        val id: String = ttsApi.lexiconEntries.single().id

        controller.deleteLexiconEntry(id)

        assertTrue(ttsApi.lexiconEntries.isEmpty())
        assertTrue((controller.state.value as TtsState.Ready).lexicon.isEmpty())
    }

    @Test
    fun failed_lexicon_write_surfaces_the_error_and_keeps_the_list() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        ttsApi.lexiconEntries.add(
            TtsLexiconEntry(id = "lex-1", phrase = "brb", replacement = "be right back", matchKind = "word")
        )
        val feedback = RecordingFeedback()
        val controller =
            TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi, feedback = feedback)
        controller.load()
        ttsApi.lexiconWriteFailure = ApiError(409, "ALREADY_EXISTS", "duplicate rule")

        val result: ApiResult<*> = controller.addLexiconEntry("brb", "bathroom break", "word")

        // The failure goes back to the form dialog (inline, no toast) and the list stays untouched.
        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals(listOf("brb"), ready.lexicon.map { it.phrase })
        assertEquals("duplicate rule", (result as ApiResult.Failure).error.message)
        assertTrue(feedback.messages.isEmpty())
    }

    @Test
    fun failed_lexicon_delete_still_announces_on_the_feedback_toast() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        ttsApi.lexiconEntries.add(
            TtsLexiconEntry(id = "lex-1", phrase = "brb", replacement = "be right back", matchKind = "word")
        )
        val feedback = RecordingFeedback()
        val controller =
            TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi, feedback = feedback)
        controller.load()
        ttsApi.lexiconWriteFailure = ApiError(409, "ALREADY_EXISTS", "duplicate rule")

        controller.deleteLexiconEntry("lex-1")

        assertEquals(FeedbackKind.Error, feedback.only.kind)
        assertEquals(listOf<Any>("duplicate rule"), feedback.only.formatArgs)
    }

    @Test
    fun load_shows_the_stored_name_pronunciation_with_the_channel_name() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        ttsApi.storedNamePronunciation = "Stoney Eagle"
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)

        controller.load()

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals("Stoney_Eagle", ready.namePronunciation?.channelName)
        assertEquals("Stoney Eagle", ready.namePronunciation?.pronunciation)
    }

    @Test
    fun save_name_pronunciation_sends_it_and_the_state_holds_the_stored_value_after_reload() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.saveNamePronunciation("  Stoney Eagle  ")

        assertEquals(listOf<String?>("  Stoney Eagle  "), ttsApi.namePronunciationWrites)
        val saved: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals("Stoney Eagle", saved.namePronunciation?.pronunciation)
        assertEquals(false, saved.namePronunciationBusy)

        controller.load()
        val reloaded: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals("Stoney Eagle", reloaded.namePronunciation?.pronunciation)
    }

    @Test
    fun save_blank_name_pronunciation_clears_the_stored_value() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        ttsApi.storedNamePronunciation = "Stoney Eagle"
        val controller = TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi)
        controller.load()

        controller.saveNamePronunciation("   ")

        assertNull((controller.state.value as TtsState.Ready).namePronunciation?.pronunciation)
        assertNull(ttsApi.storedNamePronunciation)
    }

    @Test
    fun failed_name_pronunciation_save_keeps_the_old_value_and_shows_an_error() = runTest {
        val ttsApi = FakeTtsApi(ApiResult.Ok(TtsConfig()))
        ttsApi.storedNamePronunciation = "Stoney Eagle"
        val feedback = RecordingFeedback()
        val controller =
            TtsController(FakeChannelsApi(ApiResult.Ok(ChannelSummary(id = "ch1"))), ttsApi, feedback = feedback)
        controller.load()
        ttsApi.namePronunciationWriteFailure = ApiError(400, "VALIDATION_FAILED", "too long")

        controller.saveNamePronunciation("x")

        val ready: TtsState.Ready = controller.state.value as TtsState.Ready
        assertEquals("Stoney Eagle", ready.namePronunciation?.pronunciation)
        assertEquals(false, ready.namePronunciationBusy)
        assertEquals("too long", ready.namePronunciationError)
        assertEquals(emptyList(), feedback.messages)
    }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary>) : ChannelsApi {
    override suspend fun primaryChannel(): ApiResult<ChannelSummary> = result

    override suspend fun list(): ApiResult<List<ChannelSummary>> = ApiResult.Ok(emptyList())

    override suspend fun join(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun leave(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun reset(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun deleteChannel(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun channelScopes(channelId: String) = error("stub")
    override suspend fun startChannelBotConnect(channelId: String) = error("stub")
    override suspend fun channelBotStatus(channelId: String) = error("stub")
    override suspend fun disconnectChannelBot(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
    override suspend fun moderatedChannels(): ApiResult<List<ModeratedChannel>> = ApiResult.Ok(emptyList())
}

private class FakeTtsApi(
    private val result: ApiResult<TtsConfig>,
    private val updateResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig()),
    private val voicesResult: ApiResult<List<TtsVoice>> = ApiResult.Ok(emptyList()),
    private val overlayResult: ApiResult<TtsOverlay> = ApiResult.Ok(TtsOverlay()),
    private val testOverlayResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val playbackControlResult: ApiResult<Unit> = ApiResult.Ok(Unit),
    private val defaultsResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig()),
    private val resetResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig()),
) : TtsApi {
    override suspend fun overlay(channelId: String): ApiResult<TtsOverlay> = overlayResult

    override suspend fun configDefaults(channelId: String): ApiResult<TtsConfig> = defaultsResult

    // Records which channels a reset was sent for, so a test can assert the call actually went out.
    val resetCalls: MutableList<String> = mutableListOf()

    override suspend fun resetConfig(channelId: String): ApiResult<TtsConfig> {
        resetCalls.add(channelId)
        return resetResult
    }

    val testOverlayCalls: MutableList<String> = mutableListOf()

    override suspend fun testOverlay(channelId: String): ApiResult<Unit> {
        testOverlayCalls.add(channelId)
        return testOverlayResult
    }

    // Records which playback-control endpoint was hit so tests can assert the button wired to the right call.
    val playbackControlCalls: MutableList<String> = mutableListOf()

    override suspend fun skipPlayback(channelId: String): ApiResult<Unit> {
        playbackControlCalls.add("skip")
        return playbackControlResult
    }

    override suspend fun clearPlayback(channelId: String): ApiResult<Unit> {
        playbackControlCalls.add("clear")
        return playbackControlResult
    }

    override suspend fun pausePlayback(channelId: String): ApiResult<Unit> {
        playbackControlCalls.add("pause")
        return playbackControlResult
    }

    override suspend fun resumePlayback(channelId: String): ApiResult<Unit> {
        playbackControlCalls.add("resume")
        return playbackControlResult
    }

    override suspend fun myVoice(channelId: String): ApiResult<UserTtsVoice?> = ApiResult.Ok(null)

    override suspend fun setMyVoice(channelId: String, voiceId: String): ApiResult<UserTtsVoice> =
        error("stub")

    override suspend fun clearMyVoice(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    var lastUpdate: TtsConfigUpdate? = null
        private set

    var lastUpdateChannelId: String? = null
        private set

    override suspend fun config(channelId: String): ApiResult<TtsConfig> = result

    override suspend fun updateConfig(
        channelId: String,
        update: TtsConfigUpdate,
    ): ApiResult<TtsConfig> {
        lastUpdateChannelId = channelId
        lastUpdate = update
        return updateResult
    }

    override suspend fun voices(channelId: String): ApiResult<List<TtsVoice>> = voicesResult

    // Records BYOK writes so tests can assert them; each echoes an updated config with the flag toggled.
    val byokSets: MutableList<Triple<String, String, String?>> = mutableListOf()
    val byokRemovals: MutableList<String> = mutableListOf()

    override suspend fun setByokKey(
        channelId: String,
        provider: String,
        apiKey: String,
        region: String?,
    ): ApiResult<TtsConfig> {
        byokSets.add(Triple(provider, apiKey, region))
        return updateResult
    }

    override suspend fun removeByokKey(channelId: String, provider: String): ApiResult<TtsConfig> {
        byokRemovals.add(provider)
        return updateResult
    }

    override suspend fun voicesPage(
        channelId: String,
        query: String,
        locale: String,
        gender: String,
        provider: String,
        accent: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<TtsVoicePage> =
        when (val r: ApiResult<List<TtsVoice>> = voicesResult) {
            is ApiResult.Ok -> ApiResult.Ok(TtsVoicePage(data = r.value, total = r.value.size, hasMore = false))
            is ApiResult.Failure -> ApiResult.Failure(r.error)
        }

    override suspend fun testSpeak(channelId: String, request: TtsTestRequest): ApiResult<TtsTestResult> =
        ApiResult.Ok(TtsTestResult())

    override suspend fun queue(channelId: String) = ApiResult.Ok(emptyList<TtsQueueEntry>())

    override suspend fun approveQueueEntry(channelId: String, entryId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    override suspend fun rejectQueueEntry(channelId: String, entryId: String): ApiResult<Unit> =
        ApiResult.Ok(Unit)

    // Per-viewer voice override, keyed by userId. null value = "no override" (the impl maps a 404 to Ok(null)).
    val userVoices: MutableMap<String, String?> = mutableMapOf()
    val setUserVoiceCalls: MutableList<Pair<String, String>> = mutableListOf()
    val clearedUserVoices: MutableList<String> = mutableListOf()

    override suspend fun userVoice(channelId: String, userId: String): ApiResult<UserTtsVoice?> {
        val voiceId: String? = userVoices[userId]
        return ApiResult.Ok(voiceId?.let { UserTtsVoice(userId = userId, voiceId = it) })
    }

    override suspend fun setUserVoice(
        channelId: String,
        userId: String,
        voiceId: String,
    ): ApiResult<Unit> {
        setUserVoiceCalls.add(userId to voiceId)
        userVoices[userId] = voiceId
        return ApiResult.Ok(Unit)
    }

    override suspend fun clearUserVoice(channelId: String, userId: String): ApiResult<Unit> {
        clearedUserVoices.add(userId)
        userVoices[userId] = null
        return ApiResult.Ok(Unit)
    }

    // In-memory pronunciation lexicon; [lexiconWriteFailure] makes every write fail so error surfacing is testable.
    val lexiconEntries: MutableList<TtsLexiconEntry> = mutableListOf()
    var lexiconWriteFailure: ApiError? = null
    private var nextLexiconId: Int = 1

    override suspend fun lexicon(channelId: String): ApiResult<List<TtsLexiconEntry>> =
        ApiResult.Ok(lexiconEntries.sortedBy { it.phrase })

    override suspend fun createLexiconEntry(
        channelId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry> {
        lexiconWriteFailure?.let { return ApiResult.Failure(it) }
        val entry =
            TtsLexiconEntry(
                id = "lex-${nextLexiconId++}",
                phrase = body.phrase,
                replacement = body.replacement,
                matchKind = body.matchKind,
            )
        lexiconEntries.add(entry)
        return ApiResult.Ok(entry)
    }

    override suspend fun updateLexiconEntry(
        channelId: String,
        entryId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry> {
        lexiconWriteFailure?.let { return ApiResult.Failure(it) }
        val index: Int = lexiconEntries.indexOfFirst { it.id == entryId }
        if (index < 0) return ApiResult.Failure(ApiError(404, "NOT_FOUND", "no such rule"))
        val updated =
            TtsLexiconEntry(
                id = entryId,
                phrase = body.phrase,
                replacement = body.replacement,
                matchKind = body.matchKind,
            )
        lexiconEntries[index] = updated
        return ApiResult.Ok(updated)
    }

    override suspend fun deleteLexiconEntry(channelId: String, entryId: String): ApiResult<Unit> {
        lexiconWriteFailure?.let { return ApiResult.Failure(it) }
        return if (lexiconEntries.removeAll { it.id == entryId }) ApiResult.Ok(Unit)
        else ApiResult.Failure(ApiError(404, "NOT_FOUND", "no such rule"))
    }

    // In-memory channel-name pronunciation; [namePronunciationWriteFailure] makes the write fail.
    var storedNamePronunciation: String? = null
    var namePronunciationWriteFailure: ApiError? = null
    val namePronunciationWrites: MutableList<String?> = mutableListOf()

    override suspend fun channelNamePronunciation(channelId: String): ApiResult<ChannelNamePronunciation> =
        ApiResult.Ok(ChannelNamePronunciation(channelName = "Stoney_Eagle", pronunciation = storedNamePronunciation))

    override suspend fun setChannelNamePronunciation(
        channelId: String,
        pronunciation: String?,
    ): ApiResult<ChannelNamePronunciation> {
        namePronunciationWrites.add(pronunciation)
        namePronunciationWriteFailure?.let { return ApiResult.Failure(it) }
        storedNamePronunciation = pronunciation?.trim()?.takeIf { it.isNotEmpty() }
        return ApiResult.Ok(ChannelNamePronunciation(channelName = "Stoney_Eagle", pronunciation = storedNamePronunciation))
    }
}
