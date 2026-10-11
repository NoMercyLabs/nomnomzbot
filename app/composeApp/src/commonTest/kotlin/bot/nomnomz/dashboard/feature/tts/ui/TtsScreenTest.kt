// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.tts.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.SemanticsNodeInteraction
import androidx.compose.ui.test.hasAnyAncestor
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.isSelected
import androidx.compose.ui.test.isSelectable
import androidx.compose.ui.test.hasContentDescription
import androidx.compose.ui.test.assertIsSelected
import androidx.compose.ui.test.assertIsNotSelected
import androidx.compose.ui.test.assertCountEquals
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasAnyDescendant
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isToggleable
import androidx.compose.ui.test.onAllNodesWithContentDescription
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performSemanticsAction
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.component.DialogResult
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
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
import bot.nomnomz.dashboard.core.network.TtsQueueEntry
import bot.nomnomz.dashboard.core.network.TtsTestRequest
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.core.network.TtsVoicePage
import bot.nomnomz.dashboard.core.network.UpsertTtsLexiconEntryBody
import bot.nomnomz.dashboard.core.network.UserTtsVoice
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.tts.state.TtsQueueController
import bot.nomnomz.dashboard.feature.tts.state.ViewerVoiceState
import bot.nomnomz.dashboard.feature.tts.state.VoiceBrowserState
import kotlin.test.Test
import kotlin.test.assertEquals
import kotlin.test.assertTrue
import kotlinx.coroutines.runBlocking

// S-IA-MEDIA: the TTS page is four tabs, each its own address: Live, Voices, Rules and Provider (the ledger
// row `Tts`). One controller loads the channel's config for all four; every tab edits its own fields and
// ends with the same Save, which sends the whole config. These prove: each tab renders its own controls and
// none of the others' (the real text a section renders, not a "didn't crash" smoke check), that each tab's
// edit reaches the API through Save with the changed value, and that the Editor manage-floor gate on that
// Save still holds after the split.
@OptIn(ExperimentalTestApi::class)
class TtsScreenTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    // ── Each tab renders its own section ─────────────────────────────────────

    // A config inside every field's valid range, so Save is gated only by "differs from loaded".
    private val baseline: TtsConfig = TtsConfig(isEnabled = true, mode = "self_host", maxCharacters = 200, defaultVoiceId = "en-US-Brian")

    private fun ComposeUiTest.showSection(
        ttsApi: FakeTtsApi,
        section: TtsSection,
        role: ManagementRole = ManagementRole.Broadcaster,
    ) {
        val controller = TtsController(FakeChannelsApi(), ttsApi)
        val queueController = TtsQueueController(FakeChannelsApi(), FakeTtsApi())
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withLifecycle {
                EnglishContent {
                    TtsScreen(controller = controller, queueController = queueController, role = role, section = section)
                }
            }
        }
        waitForIdle()
    }

    // The switch inside a SwitchRow. The row clears its children's semantics for screen readers and names the
    // switch by its label, so the unmerged tree is searched for the toggle under that label.
    private fun ComposeUiTest.switchLabelled(label: String): SemanticsNodeInteraction =
        onNode(isToggleable() and hasAnyAncestor(hasContentDescription(label)), useUnmergedTree = true)

    @Test
    fun live_renders_the_status_the_enable_switch_the_test_tools_and_the_save_action() = runComposeUiTest {
        showSection(FakeTtsApi(configResult = ApiResult.Ok(baseline)), TtsSection.Live)

        assertTrue(rendersText("TTS enabled"), "Live must render the enabled/disabled status banner")
        assertTrue(rendersText("Enable TTS"), "Live must render the enable switch")
        assertTrue(rendersText("Test voice"), "Live must render the ad-hoc voice test")
        assertTrue(rendersText("Save"), "Live must render the Save action for the switch it edits")
        assertTrue(!rendersText("Skip bot messages"), "the rules moved to the Rules tab")
        assertTrue(!rendersText("Available voices"), "the voice browser moved to the Voices tab")
        assertTrue(!rendersText("Bring your own key"), "the provider keys moved to the Provider tab")
    }

    @Test
    fun voices_renders_the_voice_browser_and_the_voice_defaults_and_nothing_from_the_other_tabs() = runComposeUiTest {
        showSection(FakeTtsApi(configResult = ApiResult.Ok(baseline)), TtsSection.Voices)

        assertTrue(rendersText("Available voices"), "Voices must render the voice browser")
        assertTrue(
            rendersText("Let viewers pick their own voice (!voice)"),
            "Voices must render the voice defaults",
        )
        assertTrue(rendersText("Save"), "Voices must render the Save action for the defaults it edits")
        assertTrue(!rendersText("Enable TTS"), "the enable switch stayed on Live")
        assertTrue(!rendersText("Skip bot messages"), "the rules moved to the Rules tab")
        assertTrue(!rendersText("Bring your own key"), "the provider keys moved to the Provider tab")
    }

    @Test
    fun rules_renders_the_message_rules_and_nothing_from_the_other_tabs() = runComposeUiTest {
        showSection(FakeTtsApi(configResult = ApiResult.Ok(baseline)), TtsSection.Rules)

        assertTrue(rendersText("Skip bot messages"), "Rules must render the message rules")
        assertTrue(rendersText("Save"), "Rules must render the Save action for the rules it edits")
        assertTrue(!rendersText("Enable TTS"), "the enable switch stayed on Live")
        assertTrue(!rendersText("Available voices"), "the voice browser moved to the Voices tab")
        assertTrue(!rendersText("Bring your own key"), "the provider keys moved to the Provider tab")
    }

    @Test
    fun provider_renders_the_voice_source_and_the_byok_keys_and_nothing_from_the_other_tabs() = runComposeUiTest {
        showSection(FakeTtsApi(configResult = ApiResult.Ok(baseline)), TtsSection.Provider)

        assertTrue(rendersText("Where voices come from"), "Provider must render the voice source picker")
        assertTrue(rendersText("Bring your own key"), "Provider must render the BYOK key section")
        assertTrue(rendersText("Save"), "Provider must render the Save action for the source it edits")
        assertTrue(!rendersText("Enable TTS"), "the enable switch stayed on Live")
        assertTrue(!rendersText("Skip bot messages"), "the rules moved to the Rules tab")
        assertTrue(!rendersText("Available voices"), "the voice browser moved to the Voices tab")
    }

    @Test
    fun per_viewer_tab_renders_the_viewer_voice_panel() = runComposeUiTest {
        setContent {
            EnglishContent {
                PerViewerTab(
                    voices = emptyList(),
                    viewerVoice = null,
                    manage = ManageDecision.Allowed,
                    searchViewers = { emptyList() },
                    searchAssignableVoices = { emptyList() },
                    onLookup = {},
                    onAssign = { _, _ -> },
                    onClear = {},
                )
            }
        }

        assertTrue(
            onAllNodesWithText("Per-viewer voice").fetchSemanticsNodes().isNotEmpty(),
            "Per-viewer must render the viewer-voice override panel",
        )
    }

    // S-PL2 (owner: "its impossible to set a user voice in tts or search for something that does exist on
    // another page"): the per-viewer voice picker used to filter ONLY the small, unfiltered first-page cache
    // ([TtsController.load]'s `voices`, capped at 50) client-side — the live Edge/Azure/ElevenLabs catalogue
    // runs into the hundreds (see VoiceBuiltin.kt's SearchVoicesAsync note), so a voice outside that first page
    // was findable on the Voices tab's real backend search but never found here. This proves the picker now
    // hits the live search ([searchAssignableVoices], wired to the same `GET /tts/voices?q=` the Voices tab
    // uses) rather than filtering the cache — the cache passed in is deliberately empty/irrelevant, so the
    // result can ONLY have come from the live search.
    @Test
    fun per_viewer_voice_picker_search_hits_the_live_catalogue_not_the_cached_first_page() = runComposeUiTest {
        val catalogueOnlyVoice =
            TtsVoice(id = "en-GB-Zara", displayName = "Zara", name = "en-GB-Zara", locale = "en-GB", provider = "edge")
        val searchedQueries: MutableList<String> = mutableListOf()

        setContent {
            withLifecycle {
                EnglishContent {
                    PerViewerTab(
                        voices = emptyList(),
                        viewerVoice = ViewerVoiceState(userId = "viewer-1"),
                        manage = ManageDecision.Allowed,
                        searchViewers = { emptyList() },
                        searchAssignableVoices = { query ->
                            searchedQueries.add(query)
                            if (query.contains("Zara", ignoreCase = true)) listOf(catalogueOnlyVoice) else emptyList()
                        },
                        onLookup = {},
                        onAssign = { _, _ -> },
                        onClear = {},
                    )
                }
            }
        }
        waitForIdle()

        // Two search fields render: the viewer lookup (SearchPickerField) then the voice picker's own field —
        // the voice picker's is the second SetText node in the tree.
        onAllNodes(hasSetTextAction())[1].performTextInput("Zara")
        // The picker debounces via a real `delay(300)` before firing the search.
        mainClock.advanceTimeBy(500)
        waitForIdle()

        assertTrue(
            searchedQueries.any { it.contains("Zara", ignoreCase = true) },
            "typing in the picker must call the live search, not filter a static list",
        )
        onNodeWithText("Zara", substring = true).assertExists()
    }

    // Proves the Assign button, driven purely through the rendered UI (pick a catalogue-only voice via the
    // fixed search above, then click Assign), actually calls back with the real voice id — the UI wiring the
    // controller-level test (TtsControllerTest.assign_viewer_voice_persists_and_the_panel_reflects_it) doesn't
    // exercise. Together with that test, this closes the gap end to end: picker finds the voice → Assign sends
    // the right id → the controller persists it via the real API call.
    @Test
    fun per_viewer_voice_assign_button_sends_the_voice_picked_from_the_live_search() = runComposeUiTest {
        val catalogueOnlyVoice =
            TtsVoice(id = "en-GB-Zara", displayName = "Zara", name = "en-GB-Zara", locale = "en-GB", provider = "edge")
        val assignCalls: MutableList<Pair<String, String>> = mutableListOf()

        setContent {
            withLifecycle {
                EnglishContent {
                    PerViewerTab(
                        voices = emptyList(),
                        viewerVoice = ViewerVoiceState(userId = "viewer-1"),
                        manage = ManageDecision.Allowed,
                        searchViewers = { emptyList() },
                        searchAssignableVoices = { listOf(catalogueOnlyVoice) },
                        onLookup = {},
                        onAssign = { userId, voiceId -> assignCalls.add(userId to voiceId) },
                        onClear = {},
                    )
                }
            }
        }
        waitForIdle()

        onAllNodes(hasSetTextAction())[1].performTextInput("Zara")
        mainClock.advanceTimeBy(500)
        waitForIdle()

        onNode(isSelectable() and hasContentDescription("Zara", substring = true)).performClick()
        waitForIdle()
        onNodeWithText("Assign voice").performClick()
        waitForIdle()

        assertEquals(
            listOf("viewer-1" to "en-GB-Zara"),
            assignCalls,
            "clicking Assign must send the exact voice picked in the UI to the real assign call",
        )
    }

    // Owner, live 2026-10-06 ("Libby, Maisie, Ryan" all showed the same check icon; no row read as selected and
    // Assign stayed grey): a tap on one voice must mark exactly that row selected, keep the list on screen so the
    // choice is visible, and enable Assign with that voice's id.
    @Test
    fun per_viewer_voice_tap_selects_only_that_row_and_enables_assign() = runComposeUiTest {
        val libby: TtsVoice = TtsVoice(id = "en-GB-LibbyNeural", displayName = "Libby", name = "Libby", locale = "en-GB", provider = "edge")
        val maisie: TtsVoice = TtsVoice(id = "en-GB-MaisieNeural", displayName = "Maisie", name = "Maisie", locale = "en-GB", provider = "edge")
        val ryan: TtsVoice = TtsVoice(id = "en-GB-RyanNeural", displayName = "Ryan", name = "Ryan", locale = "en-GB", provider = "edge")
        val assignCalls: MutableList<Pair<String, String>> = mutableListOf()

        setContent {
            withLifecycle {
                EnglishContent {
                    PerViewerTab(
                        voices = emptyList(),
                        viewerVoice = ViewerVoiceState(userId = "viewer-1"),
                        manage = ManageDecision.Allowed,
                        searchViewers = { emptyList() },
                        searchAssignableVoices = { listOf(libby, maisie, ryan) },
                        onLookup = {},
                        onAssign = { userId, voiceId -> assignCalls.add(userId to voiceId) },
                        onClear = {},
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText("Assign voice").assertIsNotEnabled()
        onAllNodes(hasSetTextAction())[1].performTextInput("en-gb")
        mainClock.advanceTimeBy(500)
        waitForIdle()

        onAllNodes(isSelectable()).assertCountEquals(3)
        onAllNodes(isSelectable() and isSelected()).assertCountEquals(0)

        onNode(isSelectable() and hasContentDescription("Maisie", substring = true)).performClick()
        waitForIdle()

        onNode(isSelectable() and hasContentDescription("Maisie", substring = true)).assertIsSelected()
        onNode(isSelectable() and hasContentDescription("Libby", substring = true)).assertIsNotSelected()
        onNode(isSelectable() and hasContentDescription("Ryan", substring = true)).assertIsNotSelected()
        onNodeWithText("Assign voice").assertIsEnabled().performClick()
        waitForIdle()

        assertEquals(listOf("viewer-1" to "en-GB-MaisieNeural"), assignCalls)
    }

    // After the save and reload the panel must say which voice the viewer has, not the channel default.
    @Test
    fun per_viewer_voice_panel_says_this_viewer_uses_the_saved_voice() = runComposeUiTest {
        val maisie: TtsVoice = TtsVoice(id = "en-GB-MaisieNeural", displayName = "Maisie", name = "Maisie", locale = "en-GB", provider = "edge")

        setContent {
            withLifecycle {
                EnglishContent {
                    PerViewerTab(
                        voices = listOf(maisie),
                        viewerVoice = ViewerVoiceState(userId = "viewer-1", currentVoiceId = maisie.id),
                        manage = ManageDecision.Allowed,
                        searchViewers = { emptyList() },
                        searchAssignableVoices = { emptyList() },
                        onLookup = {},
                        onAssign = { _, _ -> },
                        onClear = {},
                    )
                }
            }
        }
        waitForIdle()

        onNodeWithText("This viewer uses Maisie (en-GB)").assertExists()
    }

    @Test
    fun pronunciation_tab_renders_the_lexicon_rules() = runComposeUiTest {
        setContent {
            EnglishContent {
                PronunciationTab(
                    lexicon =
                        listOf(
                            TtsLexiconEntry(id = "lex-1", phrase = "brb", replacement = "be right back", matchKind = "word")
                        ),
                    busy = false,
                    manage = ManageDecision.Allowed,
                    namePronunciation = null,
                    nameBusy = false,
                    onSaveName = {},
                    onAdd = { _, _, _ -> DialogResult.Done },
                    onUpdate = { _, _, _, _ -> DialogResult.Done },
                    onDelete = { DialogResult.Done },
                )
            }
        }

        assertTrue(
            onAllNodesWithText("brb → be right back").fetchSemanticsNodes().isNotEmpty(),
            "Pronunciation must render the channel's real rules, not a placeholder",
        )
    }

    @Test
    fun queue_and_test_tab_renders_overlay_ad_hoc_test_and_the_approval_queue_together() = runComposeUiTest {
        val ttsApi =
            FakeTtsApi(
                configResult = ApiResult.Ok(TtsConfig()),
                queueResult =
                    ApiResult.Ok(
                        listOf(TtsQueueEntry(id = "q1", requestedByDisplayName = "pixelqueen", originalText = "hello chat"))
                    ),
            )
        val queueController = TtsQueueController(FakeChannelsApi(), ttsApi)

        setContent {
            withLifecycle {
                EnglishContent {
                    QueueAndTestTab(
                        overlay = TtsOverlay(overlayUrl = "https://bot.example/overlays/tts_caption/abc123"),
                        manage = ManageDecision.Allowed,
                        sending = false,
                        sent = false,
                        overlayError = null,
                        onTestOverlay = {},
                        playbackBusy = false,
                        playbackPaused = false,
                        playbackError = null,
                        onSkip = {},
                        onClear = {},
                        onPause = {},
                        onResume = {},
                        currentVoiceId = "en-US-Brian",
                        testing = false,
                        testResult = null,
                        testError = null,
                        onTestSpeak = { _, _ -> },
                        queueController = queueController,
                        queueManage = ManageDecision.Allowed,
                    )
                }
            }
        }
        waitForIdle()

        assertTrue(
            onAllNodesWithText("OBS overlay").fetchSemanticsNodes().isNotEmpty(),
            "Queue & test must render the overlay card (URL + test dispatch)",
        )
        assertTrue(
            onAllNodesWithText("Test voice").fetchSemanticsNodes().isNotEmpty(),
            "Queue & test must render the ad-hoc voice test",
        )
        assertTrue(
            onAllNodesWithText("pixelqueen").fetchSemanticsNodes().isNotEmpty(),
            "Queue & test must render the real pending approval-queue entry",
        )
    }

    // ── Each tab saves its own fields through the one Save action ────────────

    @Test
    fun live_save_sends_the_enable_switch_and_keeps_the_other_tabs_values() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline))
        showSection(ttsApi, TtsSection.Live)
        onNodeWithText("Save").assertIsNotEnabled()

        switchLabelled("Enable TTS").performClick()
        waitForIdle()
        assertTrue(rendersText("TTS disabled"), "flipping the switch must be reflected immediately")
        onNodeWithText("Save").assertIsEnabled().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(1, ttsApi.updateCalls.size, "Save must send exactly one update")
        assertEquals(false, ttsApi.updateCalls.single().isEnabled)
        assertEquals(baseline.maxCharacters, ttsApi.updateCalls.single().maxCharacters)
        assertEquals(baseline.skipBotMessages, ttsApi.updateCalls.single().skipBotMessages)
    }

    @Test
    fun voices_save_sends_the_viewer_voice_choice() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline))
        showSection(ttsApi, TtsSection.Voices)

        switchLabelled("Let viewers pick their own voice (!voice)").performClick()
        waitForIdle()
        onNodeWithText("Save").assertIsEnabled().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(1, ttsApi.updateCalls.size, "Save must send exactly one update")
        assertEquals(!baseline.viewerVoiceSelfServiceEnabled, ttsApi.updateCalls.single().viewerVoiceSelfServiceEnabled)
        assertEquals(baseline.isEnabled, ttsApi.updateCalls.single().isEnabled)
    }

    @Test
    fun rules_save_sends_the_changed_rule() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline))
        showSection(ttsApi, TtsSection.Rules)

        switchLabelled("Skip bot messages").performClick()
        waitForIdle()
        onAllNodesWithText("Save")[0].assertIsEnabled().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(1, ttsApi.updateCalls.size, "Save must send exactly one update")
        assertEquals(!baseline.skipBotMessages, ttsApi.updateCalls.single().skipBotMessages)
        assertEquals(baseline.maxCharacters, ttsApi.updateCalls.single().maxCharacters)
    }

    @Test
    fun provider_save_sends_the_chosen_voice_source() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline))
        showSection(ttsApi, TtsSection.Provider)

        onNodeWithContentDescription("My own provider key", useUnmergedTree = true).performClick()
        waitForIdle()
        onNodeWithText("Save").assertIsEnabled().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(1, ttsApi.updateCalls.size, "Save must send exactly one update")
        assertEquals("byok", ttsApi.updateCalls.single().mode)
        assertEquals(baseline.isEnabled, ttsApi.updateCalls.single().isEnabled)
    }

    // The browser voice (`client_edge`) is gone (Stoney, 2026-10-05: "edge tts is available everywhere and must be
    // the standard. that lame outdated browser native tts goes away"). A config still carrying it renders as the
    // free voices option on the Provider tab, and a Save from any tab sends the server plane value, never
    // `client_edge`.
    @Test
    fun a_client_edge_config_renders_as_free_voices_on_the_provider_tab() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline.copy(mode = "client_edge")))
        showSection(ttsApi, TtsSection.Provider)

        // The voice-source picker offers exactly the two remaining options, with no browser-voice chip.
        onNodeWithContentDescription("Free voices (recommended)", useUnmergedTree = true).assertExists()
        onNodeWithContentDescription("My own provider key", useUnmergedTree = true).assertExists()
        assertEquals(
            0,
            onAllNodesWithContentDescription("Client (Edge)", useUnmergedTree = true).fetchSemanticsNodes().size,
            "the browser voice must no longer be offered",
        )
        // Nothing was edited yet, so the loaded client_edge config (shown as free voices) is not a pending change.
        onNodeWithText("Save").assertIsNotEnabled()
        assertEquals(emptyList(), ttsApi.updateCalls)
    }

    @Test
    fun saving_a_client_edge_config_from_live_sends_the_server_plane() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline.copy(mode = "client_edge")))
        showSection(ttsApi, TtsSection.Live)

        switchLabelled("Enable TTS").performClick()
        waitForIdle()
        onNodeWithText("Save").assertIsEnabled().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(1, ttsApi.updateCalls.size, "Save must send exactly one update")
        assertEquals("self_host", ttsApi.updateCalls.single().mode)
        assertEquals(false, ttsApi.updateCalls.single().isEnabled)
    }

    // ── Role gating survives the split ────────────────────────────────────────

    @Test
    fun a_moderator_cannot_edit_or_save_on_the_live_tab() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline))
        showSection(ttsApi, TtsSection.Live, role = ManagementRole.Moderator)

        // Never hidden — the Save button always renders; only its enabled state (and the announced reason)
        // changes with the manage floor, per the shared ManageGate convention.
        assertTrue(rendersText("Enable TTS"), "a Moderator still sees the settings")
        switchLabelled("Enable TTS").assertIsNotEnabled()
        onNodeWithText("Save").assertIsNotEnabled()
        assertEquals(emptyList(), ttsApi.updateCalls)
    }

    @Test
    fun an_editor_can_edit_and_save_on_the_live_tab() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(baseline))
        showSection(ttsApi, TtsSection.Live, role = ManagementRole.Editor)

        switchLabelled("Enable TTS").assertIsEnabled().performClick()
        waitForIdle()
        onNodeWithText("Save").assertIsEnabled().performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertEquals(false, ttsApi.updateCalls.single().isEnabled)
    }

    // ── Reset to defaults ────────────────────────────────────────────────────

    // A channel that changed several settings, and the server's defaults it would go back to.
    private val changedConfig: TtsConfig =
        TtsConfig(
            isEnabled = true,
            mode = "self_host",
            defaultProvider = "edge",
            maxCharacters = 120,
            minPermission = "moderators",
            skipBotMessages = true,
            readUsernames = true,
            profanityCensorEnabled = true,
            modApprovalRequired = true,
            minBitsToTts = 100,
            viewerVoiceSelfServiceEnabled = true,
        )
    private val serverDefaults: TtsConfig =
        changedConfig.copy(maxCharacters = 500, minPermission = "everyone", modApprovalRequired = false, minBitsToTts = null)

    @Test
    fun reset_lists_every_change_as_current_to_default_before_writing_and_confirm_writes() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(changedConfig), defaultsResult = ApiResult.Ok(serverDefaults))
        val controller = TtsController(FakeChannelsApi(), ttsApi)
        val queueController = TtsQueueController(FakeChannelsApi(), FakeTtsApi())
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withLifecycle {
                EnglishContent {
                    TtsScreen(controller = controller, queueController = queueController, role = ManagementRole.Broadcaster, section = TtsSection.Live)
                }
            }
        }
        waitForIdle()

        // The action sits below the fold of the scrolled form, so it is invoked by its semantics action rather
        // than by a pointer click at coordinates outside the test window.
        onNodeWithText("Reset to defaults").assertIsEnabled()
        onNodeWithText("Reset to defaults").performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        // Every differing setting is listed as "current → default" (the setting names also label the form
        // behind the dialog, so the value text is what proves the dialog rendered the diff).
        assertTrue(rendersText("120 → 500"))
        assertTrue(rendersText("Moderators → Everyone"))
        assertTrue(rendersText("On → Off"))
        assertTrue(rendersText("100 → None"))
        // Exactly those four differ, so exactly four rows: a setting already at its default is not listed.
        assertEquals(
            4,
            onAllNodesWithText("→", substring = true, useUnmergedTree = true).fetchSemanticsNodes().size,
        )
        // Nothing is written until the operator confirms.
        assertEquals(emptyList(), ttsApi.resetCalls)
        // It also promises what it leaves alone.
        assertTrue(onAllNodesWithText("not touched", substring = true).fetchSemanticsNodes().isNotEmpty())

        onNodeWithText("Reset settings").assertIsEnabled().performClick()
        waitForIdle()

        assertEquals(listOf("ch1"), ttsApi.resetCalls)
    }

    @Test
    fun reset_confirm_is_disabled_and_writes_nothing_when_every_setting_already_matches() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(serverDefaults), defaultsResult = ApiResult.Ok(serverDefaults))
        val controller = TtsController(FakeChannelsApi(), ttsApi)
        val queueController = TtsQueueController(FakeChannelsApi(), FakeTtsApi())
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withLifecycle {
                EnglishContent {
                    TtsScreen(controller = controller, queueController = queueController, role = ManagementRole.Broadcaster, section = TtsSection.Live)
                }
            }
        }
        waitForIdle()

        onNodeWithText("Reset to defaults").performSemanticsAction(SemanticsActions.OnClick)
        waitForIdle()

        assertTrue(onAllNodesWithText("already at its default", substring = true).fetchSemanticsNodes().isNotEmpty())
        onNodeWithText("Reset settings").assertIsNotEnabled()
        assertEquals(emptyList(), ttsApi.resetCalls)
    }

    @Test
    fun reset_action_is_disabled_below_the_editor_floor() = runComposeUiTest {
        val ttsApi = FakeTtsApi(configResult = ApiResult.Ok(changedConfig), defaultsResult = ApiResult.Ok(serverDefaults))
        val controller = TtsController(FakeChannelsApi(), ttsApi)
        val queueController = TtsQueueController(FakeChannelsApi(), FakeTtsApi())
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withLifecycle {
                EnglishContent {
                    TtsScreen(controller = controller, queueController = queueController, role = ManagementRole.Moderator, section = TtsSection.Live)
                }
            }
        }
        waitForIdle()

        onNodeWithText("Reset to defaults").assertIsNotEnabled()
    }

    @Test
    fun reset_action_is_unavailable_when_the_defaults_could_not_be_loaded() = runComposeUiTest {
        val ttsApi =
            FakeTtsApi(
                configResult = ApiResult.Ok(changedConfig),
                defaultsResult = ApiResult.Failure(ApiError(500, "ERR", "boom")),
            )
        val controller = TtsController(FakeChannelsApi(), ttsApi)
        val queueController = TtsQueueController(FakeChannelsApi(), FakeTtsApi())
        runBlocking {
            controller.load()
            queueController.load()
        }
        setContent {
            withLifecycle {
                EnglishContent {
                    TtsScreen(controller = controller, queueController = queueController, role = ManagementRole.Broadcaster, section = TtsSection.Live)
                }
            }
        }
        waitForIdle()

        onNodeWithText("Reset to defaults").assertIsNotEnabled()
    }

    // ── Test helpers ────────────────────────────────────────────────────────

    // Several labels this file asserts on (the status banner's "TTS enabled"/"TTS disabled", SwitchRow's
    // "Enable TTS") sit under a `clearAndSetSemantics` boundary in TtsScreen.kt — one node for screen readers
    // instead of a disconnected dot/switch + label — so the merged tree never surfaces their own Text node.
    // Reads the unmerged tree so every such check in this file goes through one place.
    private fun ComposeUiTest.rendersText(label: String): Boolean =
        onAllNodesWithText(label, useUnmergedTree = true).fetchSemanticsNodes().isNotEmpty()
}

@Composable
private fun withLifecycle(content: @Composable () -> Unit) {
    val owner: LifecycleOwner =
        object : LifecycleOwner {
            override val lifecycle: Lifecycle = LifecycleRegistry.createUnsafe(this)
        }
    (owner.lifecycle as LifecycleRegistry).apply {
        currentState = Lifecycle.State.CREATED
        currentState = Lifecycle.State.STARTED
        currentState = Lifecycle.State.RESUMED
    }
    CompositionLocalProvider(LocalLifecycleOwner provides owner) { content() }
}

private class FakeChannelsApi(private val result: ApiResult<ChannelSummary> = ApiResult.Ok(ChannelSummary(id = "ch1"))) :
    ChannelsApi {
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

// A trimmed-down TtsApi fake — every read the tab split needs to exercise (config/queue/overlay) is
// configurable; every write no test in this file exercises is a harmless no-op.
private class FakeTtsApi(
    private val configResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig()),
    private val queueResult: ApiResult<List<TtsQueueEntry>> = ApiResult.Ok(emptyList()),
    private val overlayResult: ApiResult<TtsOverlay> = ApiResult.Ok(TtsOverlay()),
    private val defaultsResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig()),
    private val resetResult: ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig()),
) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> = configResult

    override suspend fun configDefaults(channelId: String): ApiResult<TtsConfig> = defaultsResult

    // Which channels a reset was actually sent for — proves the confirm wired through to the API.
    val resetCalls: MutableList<String> = mutableListOf()

    override suspend fun resetConfig(channelId: String): ApiResult<TtsConfig> {
        resetCalls.add(channelId)
        return resetResult
    }

    // Every config update the screen sent — proves what a Save actually puts on the wire.
    val updateCalls: MutableList<TtsConfigUpdate> = mutableListOf()

    override suspend fun updateConfig(channelId: String, update: TtsConfigUpdate): ApiResult<TtsConfig> {
        updateCalls.add(update)
        return ApiResult.Ok(TtsConfig())
    }

    override suspend fun setByokKey(channelId: String, provider: String, apiKey: String, region: String?): ApiResult<TtsConfig> =
        ApiResult.Ok(TtsConfig())

    override suspend fun removeByokKey(channelId: String, provider: String): ApiResult<TtsConfig> = ApiResult.Ok(TtsConfig())

    override suspend fun voicesPage(
        channelId: String,
        query: String,
        locale: String,
        gender: String,
        provider: String,
        accent: String,
        page: Int,
        pageSize: Int,
    ): ApiResult<TtsVoicePage> = ApiResult.Ok(TtsVoicePage())

    override suspend fun voices(channelId: String): ApiResult<List<TtsVoice>> = ApiResult.Ok(emptyList())

    override suspend fun testSpeak(channelId: String, request: TtsTestRequest): ApiResult<TtsTestResult> =
        ApiResult.Ok(TtsTestResult())

    override suspend fun queue(channelId: String): ApiResult<List<TtsQueueEntry>> = queueResult

    override suspend fun approveQueueEntry(channelId: String, entryId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun rejectQueueEntry(channelId: String, entryId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun userVoice(channelId: String, userId: String): ApiResult<UserTtsVoice?> = ApiResult.Ok(null)

    override suspend fun setUserVoice(channelId: String, userId: String, voiceId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun clearUserVoice(channelId: String, userId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun lexicon(channelId: String): ApiResult<List<TtsLexiconEntry>> = ApiResult.Ok(emptyList())

    override suspend fun createLexiconEntry(channelId: String, body: UpsertTtsLexiconEntryBody): ApiResult<TtsLexiconEntry> =
        ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun updateLexiconEntry(
        channelId: String,
        entryId: String,
        body: UpsertTtsLexiconEntryBody,
    ): ApiResult<TtsLexiconEntry> = ApiResult.Failure(ApiError(501, "NOT_IMPLEMENTED", "unused"))

    override suspend fun deleteLexiconEntry(channelId: String, entryId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun channelNamePronunciation(channelId: String): ApiResult<ChannelNamePronunciation> =
        ApiResult.Ok(ChannelNamePronunciation())

    override suspend fun setChannelNamePronunciation(
        channelId: String,
        pronunciation: String?,
    ): ApiResult<ChannelNamePronunciation> = ApiResult.Ok(ChannelNamePronunciation(pronunciation = pronunciation))

    override suspend fun myVoice(channelId: String): ApiResult<UserTtsVoice?> = ApiResult.Ok(null)

    override suspend fun setMyVoice(channelId: String, voiceId: String): ApiResult<UserTtsVoice> = error("stub")

    override suspend fun clearMyVoice(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun overlay(channelId: String): ApiResult<TtsOverlay> = overlayResult

    override suspend fun testOverlay(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun skipPlayback(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun clearPlayback(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun pausePlayback(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)

    override suspend fun resumePlayback(channelId: String): ApiResult<Unit> = ApiResult.Ok(Unit)
}
