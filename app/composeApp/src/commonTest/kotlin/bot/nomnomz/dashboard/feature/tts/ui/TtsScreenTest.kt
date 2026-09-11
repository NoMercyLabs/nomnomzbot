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
import androidx.compose.ui.test.ComposeUiTest
import androidx.compose.ui.test.ExperimentalTestApi
import androidx.compose.ui.test.assertIsEnabled
import androidx.compose.ui.test.assertIsNotEnabled
import androidx.compose.ui.test.hasAnyDescendant
import androidx.compose.ui.test.hasClickAction
import androidx.compose.ui.test.hasSetTextAction
import androidx.compose.ui.test.hasText
import androidx.compose.ui.test.isToggleable
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithContentDescription
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.performClick
import androidx.compose.ui.test.performTextInput
import androidx.compose.ui.test.runComposeUiTest
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.LifecycleOwner
import androidx.lifecycle.LifecycleRegistry
import androidx.lifecycle.compose.LocalLifecycleOwner
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.theme.NomNomzTheme
import bot.nomnomz.dashboard.core.i18n.AppEnvironment
import bot.nomnomz.dashboard.core.network.ApiError
import bot.nomnomz.dashboard.core.network.ApiResult
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

// S-TTS-TABS (owner-punch-list-2026-09-08.md §2): the TTS page used to stack eight unrelated sections
// (overlay test, global settings, voice browser, BYOK keys, ad-hoc test, per-viewer voice, pronunciation
// dictionary, live queue) in one long scroll. It is now split into five tabs (General / Voices /
// Per-viewer / Pronunciation / Queue & test). These prove: each tab renders its own real section content
// (not a "didn't crash" smoke check — the actual text a section renders), that the tab strip genuinely
// swaps content rather than accumulating it, that an in-progress edit survives switching away and back
// (the hoisted state, not the tab body, is what's real), and that the Editor manage-floor gate on the
// page's one write action (Save) still holds after the split.
@OptIn(ExperimentalTestApi::class)
class TtsScreenTest {

    @Composable
    private fun EnglishContent(content: @Composable () -> Unit) {
        AppEnvironment(tag = "en") {
            NomNomzTheme { content() }
        }
    }

    // ── Each tab renders its own section ─────────────────────────────────────

    @Test
    fun general_tab_renders_the_status_banner_settings_form_and_save_action() = runComposeUiTest {
        setContent {
            EnglishContent {
                GeneralTab(
                    isEnabled = true,
                    onEnabledChange = {},
                    mode = "self_host",
                    onModeChange = {},
                    defaultProvider = "edge",
                    onProviderChange = {},
                    defaultVoiceId = "",
                    onVoiceChange = {},
                    maxLengthText = "200",
                    onMaxLengthChange = {},
                    maxLengthValid = true,
                    minPermission = "everyone",
                    onPermissionChange = {},
                    skipBotMessages = false,
                    onSkipBotMessagesChange = {},
                    readUsernames = false,
                    onReadUsernamesChange = {},
                    profanityCensorEnabled = true,
                    onProfanityCensorChange = {},
                    modApprovalRequired = false,
                    onModApprovalChange = {},
                    minBitsText = "",
                    onMinBitsChange = {},
                    minBitsValid = true,
                    viewerVoiceSelfService = true,
                    onViewerVoiceSelfServiceChange = {},
                    manage = ManageDecision.Allowed,
                    formEnabled = true,
                    saving = false,
                    justSaved = false,
                    saveError = null,
                    canSave = true,
                    onSave = {},
                )
            }
        }

        assertTrue(
            rendersText("TTS enabled"),
            "General must render the enabled/disabled status banner",
        )
        assertTrue(
            rendersText("Enable TTS"),
            "General must render the settings form (the enable toggle's own label)",
        )
        assertTrue(
            onAllNodesWithText("Save").fetchSemanticsNodes().isNotEmpty(),
            "General must render the Save action for the settings it edits",
        )
        // Sections that moved to other tabs must NOT bleed into General.
        assertTrue(
            onAllNodesWithText("Bring your own key").fetchSemanticsNodes().isEmpty(),
            "BYOK moved to the Voices tab — it must not render on General",
        )
    }

    @Test
    fun voices_tab_renders_the_voice_browser_results_and_the_byok_keys() = runComposeUiTest {
        setContent {
            EnglishContent {
                VoicesTab(
                    browser =
                        VoiceBrowserState(
                            results =
                                listOf(
                                    TtsVoice(
                                        id = "en-US-Brian",
                                        displayName = "Brian",
                                        name = "en-US-Brian",
                                        locale = "en-US",
                                        provider = "azure",
                                    )
                                ),
                            total = 1,
                        ),
                    currentVoiceId = "",
                    manage = ManageDecision.Allowed,
                    onSearch = { _, _, _, _, _, _ -> },
                    onSelect = {},
                    onPreviewFallback = {},
                    byokConfig = TtsConfig(),
                    saving = false,
                    onSetByok = { _, _, _ -> },
                    onRemoveByok = {},
                )
            }
        }

        assertTrue(
            onAllNodesWithText("Available voices").fetchSemanticsNodes().isNotEmpty(),
            "Voices must render the voice browser",
        )
        assertTrue(
            onAllNodesWithText("Brian", substring = true).fetchSemanticsNodes().isNotEmpty(),
            "the searched voice must actually render on the Voices tab",
        )
        assertTrue(
            onAllNodesWithText("Bring your own key").fetchSemanticsNodes().isNotEmpty(),
            "Voices must render the BYOK key section",
        )
        // The plain settings form must NOT bleed into Voices.
        assertTrue(
            onAllNodesWithText("Enable TTS").fetchSemanticsNodes().isEmpty(),
            "the settings form stayed on General — it must not render on Voices",
        )
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

        onNodeWithContentDescription("Use Zara").performClick()
        waitForIdle()
        onNodeWithText("Assign voice").performClick()
        waitForIdle()

        assertEquals(
            listOf("viewer-1" to "en-GB-Zara"),
            assignCalls,
            "clicking Assign must send the exact voice picked in the UI to the real assign call",
        )
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
                    onAdd = { _, _, _ -> },
                    onUpdate = { _, _, _, _ -> },
                    onDelete = {},
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

    // ── Switching tabs swaps content and preserves in-progress edits ─────────

    @Test
    fun switching_to_voices_hides_general_and_switching_back_shows_general_again() = runComposeUiTest {
        val controller = TtsController(FakeChannelsApi(), FakeTtsApi(configResult = ApiResult.Ok(TtsConfig(isEnabled = true))))
        val queueController = TtsQueueController(FakeChannelsApi(), FakeTtsApi(configResult = ApiResult.Ok(TtsConfig())))
        runBlocking {
            controller.load()
            queueController.load()
        }

        setContent {
            withLifecycle {
                EnglishContent {
                    TtsScreen(controller = controller, queueController = queueController, role = ManagementRole.Broadcaster)
                }
            }
        }
        waitForIdle()

        // Starts on General.
        assertTrue(rendersText("Enable TTS"), "the page must default to General")

        clickTab("Voices")
        waitForIdle()
        assertTrue(
            !rendersText("Enable TTS"),
            "General's content must be gone once Voices is selected — tabs swap, not accumulate",
        )
        assertTrue(onAllNodesWithText("Bring your own key").fetchSemanticsNodes().isNotEmpty())

        clickTab("General")
        waitForIdle()
        assertTrue(rendersText("Enable TTS"), "General's content must come back when reselected")
        assertTrue(
            onAllNodesWithText("Bring your own key").fetchSemanticsNodes().isEmpty(),
            "Voices' content must be gone once General is reselected",
        )
    }

    @Test
    fun toggling_enable_tts_survives_switching_away_to_voices_and_back() = runComposeUiTest {
        val controller = TtsController(FakeChannelsApi(), FakeTtsApi(configResult = ApiResult.Ok(TtsConfig(isEnabled = true))))
        val queueController = TtsQueueController(FakeChannelsApi(), FakeTtsApi(configResult = ApiResult.Ok(TtsConfig())))
        runBlocking {
            controller.load()
            queueController.load()
        }

        setContent {
            withLifecycle {
                EnglishContent {
                    TtsScreen(controller = controller, queueController = queueController, role = ManagementRole.Broadcaster)
                }
            }
        }
        waitForIdle()

        // The loaded config starts enabled.
        assertTrue(rendersText("TTS enabled"))

        // Flip the Enable-TTS switch off — an in-progress, unsaved edit (Save is not clicked). Unmerged: the
        // switch sits under SwitchRow's `clearAndSetSemantics` boundary (see TtsScreen.kt), so the merged
        // tree would not surface it directly.
        onAllNodes(isToggleable(), useUnmergedTree = true)[0].performClick()
        waitForIdle()
        assertTrue(rendersText("TTS disabled"), "flipping the switch must be reflected immediately")

        // Leave General for another tab and come back — the unsaved edit must not have been reset back
        // to the loaded baseline, because the field's State lives above the tab dispatch, not inside it.
        clickTab("Voices")
        waitForIdle()
        clickTab("General")
        waitForIdle()

        assertTrue(rendersText("TTS disabled"), "the unsaved toggle must survive switching tabs away and back")
        assertTrue(
            !rendersText("TTS enabled"),
            "the page must not have silently reverted to the loaded baseline",
        )
    }

    // ── Role gating survives the split ────────────────────────────────────────

    @Test
    fun the_save_action_is_disabled_below_the_editor_floor_and_enabled_at_it() = runComposeUiTest {
        setContent {
            EnglishContent {
                GeneralTab(
                    isEnabled = true,
                    onEnabledChange = {},
                    mode = "self_host",
                    onModeChange = {},
                    defaultProvider = "edge",
                    onProviderChange = {},
                    defaultVoiceId = "",
                    onVoiceChange = {},
                    maxLengthText = "200",
                    onMaxLengthChange = {},
                    maxLengthValid = true,
                    minPermission = "everyone",
                    onPermissionChange = {},
                    skipBotMessages = false,
                    onSkipBotMessagesChange = {},
                    readUsernames = false,
                    onReadUsernamesChange = {},
                    profanityCensorEnabled = true,
                    onProfanityCensorChange = {},
                    modApprovalRequired = false,
                    onModApprovalChange = {},
                    minBitsText = "",
                    onMinBitsChange = {},
                    minBitsValid = true,
                    viewerVoiceSelfService = true,
                    onViewerVoiceSelfServiceChange = {},
                    manage = ManageDecision.Denied("Requires Editor"),
                    formEnabled = true,
                    saving = false,
                    justSaved = false,
                    saveError = null,
                    canSave = true,
                    onSave = {},
                )
            }
        }

        // Never hidden — the Save button always renders; only its enabled state (and the announced reason)
        // changes with the manage floor, per the shared ManageGate convention.
        onNodeWithText("Save").assertIsNotEnabled()
    }

    @Test
    fun the_save_action_is_enabled_at_the_editor_floor() = runComposeUiTest {
        setContent {
            EnglishContent {
                GeneralTab(
                    isEnabled = true,
                    onEnabledChange = {},
                    mode = "self_host",
                    onModeChange = {},
                    defaultProvider = "edge",
                    onProviderChange = {},
                    defaultVoiceId = "",
                    onVoiceChange = {},
                    maxLengthText = "200",
                    onMaxLengthChange = {},
                    maxLengthValid = true,
                    minPermission = "everyone",
                    onPermissionChange = {},
                    skipBotMessages = false,
                    onSkipBotMessagesChange = {},
                    readUsernames = false,
                    onReadUsernamesChange = {},
                    profanityCensorEnabled = true,
                    onProfanityCensorChange = {},
                    modApprovalRequired = false,
                    onModApprovalChange = {},
                    minBitsText = "",
                    onMinBitsChange = {},
                    minBitsValid = true,
                    viewerVoiceSelfService = true,
                    onViewerVoiceSelfServiceChange = {},
                    manage = ManageDecision.Allowed,
                    formEnabled = true,
                    saving = false,
                    justSaved = false,
                    saveError = null,
                    canSave = true,
                    onSave = {},
                )
            }
        }

        onNodeWithText("Save").assertIsEnabled()
    }

    // ── Test helpers ────────────────────────────────────────────────────────

    // Clicks the tab strip's [label] trigger. Each TabsTrigger renders its label text TWICE (an invisible
    // width-sizing copy plus the visible one — see Tabs.kt's SegmentLabel), so a plain onNodeWithText would
    // be ambiguous; this instead finds the single clickable ancestor that has the label among its
    // descendants, the same disambiguation shape used elsewhere in this codebase for gated controls.
    private fun ComposeUiTest.clickTab(label: String) {
        onNode(hasClickAction() and hasAnyDescendant(hasText(label)), useUnmergedTree = true).performClick()
    }

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
) : TtsApi {
    override suspend fun config(channelId: String): ApiResult<TtsConfig> = configResult

    override suspend fun updateConfig(channelId: String, update: TtsConfigUpdate): ApiResult<TtsConfig> =
        ApiResult.Ok(TtsConfig())

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
