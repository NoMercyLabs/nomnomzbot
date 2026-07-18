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

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ExperimentalLayoutApi
import androidx.compose.foundation.layout.FlowRow
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.wrapContentWidth
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import bot.nomnomz.dashboard.core.designsystem.component.Button
import bot.nomnomz.dashboard.core.designsystem.component.RevealableSecretField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import androidx.compose.material3.Text

import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.PlayCircleGlyph
import bot.nomnomz.dashboard.core.io.playSoundPreview
import bot.nomnomz.dashboard.feature.tts.state.VoiceBrowserState
import kotlinx.coroutines.delay
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.Badge
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Spinner
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.TtsConfig
import bot.nomnomz.dashboard.core.network.TtsQueueEntry
import bot.nomnomz.dashboard.core.network.TtsTestResult
import bot.nomnomz.dashboard.core.network.TtsVoice
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecisionAtFloor
import bot.nomnomz.dashboard.feature.tts.state.TtsController
import bot.nomnomz.dashboard.feature.tts.state.TtsQueueController
import bot.nomnomz.dashboard.feature.tts.state.TtsQueueState
import bot.nomnomz.dashboard.feature.tts.state.TtsState
import bot.nomnomz.dashboard.feature.tts.state.ViewerVoiceState
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.tts_byok_description
import nomnomzbot.composeapp.generated.resources.tts_byok_key_label
import nomnomzbot.composeapp.generated.resources.tts_byok_not_stored
import nomnomzbot.composeapp.generated.resources.tts_byok_region_label
import nomnomzbot.composeapp.generated.resources.tts_byok_remove
import nomnomzbot.composeapp.generated.resources.tts_byok_save
import nomnomzbot.composeapp.generated.resources.tts_byok_stored
import nomnomzbot.composeapp.generated.resources.tts_byok_title
import nomnomzbot.composeapp.generated.resources.tts_gender_female
import nomnomzbot.composeapp.generated.resources.tts_gender_male
import nomnomzbot.composeapp.generated.resources.tts_gender_neutral
import nomnomzbot.composeapp.generated.resources.tts_label_min_bits
import nomnomzbot.composeapp.generated.resources.tts_label_viewer_self_service
import nomnomzbot.composeapp.generated.resources.tts_min_bits_hint
import nomnomzbot.composeapp.generated.resources.tts_provider_azure
import nomnomzbot.composeapp.generated.resources.tts_provider_edge
import nomnomzbot.composeapp.generated.resources.tts_provider_elevenlabs
import nomnomzbot.composeapp.generated.resources.tts_voices_current_tag
import nomnomzbot.composeapp.generated.resources.tts_voices_empty
import nomnomzbot.composeapp.generated.resources.tts_voices_filter_gender
import nomnomzbot.composeapp.generated.resources.tts_voices_filter_provider
import nomnomzbot.composeapp.generated.resources.tts_voices_loading
import nomnomzbot.composeapp.generated.resources.tts_voices_next
import nomnomzbot.composeapp.generated.resources.tts_voices_page
import nomnomzbot.composeapp.generated.resources.tts_voices_prev
import nomnomzbot.composeapp.generated.resources.tts_voices_preview_action
import nomnomzbot.composeapp.generated.resources.tts_error
import nomnomzbot.composeapp.generated.resources.tts_label_max_length
import nomnomzbot.composeapp.generated.resources.tts_label_min_permission
import nomnomzbot.composeapp.generated.resources.tts_label_filter_profanity
import nomnomzbot.composeapp.generated.resources.tts_label_mod_approval
import nomnomzbot.composeapp.generated.resources.tts_label_read_usernames
import nomnomzbot.composeapp.generated.resources.tts_label_skip_bot_messages
import nomnomzbot.composeapp.generated.resources.tts_label_voice
import nomnomzbot.composeapp.generated.resources.tts_loading
import nomnomzbot.composeapp.generated.resources.tts_max_length_invalid
import nomnomzbot.composeapp.generated.resources.tts_permission_broadcaster
import nomnomzbot.composeapp.generated.resources.tts_permission_everyone
import nomnomzbot.composeapp.generated.resources.tts_permission_moderators
import nomnomzbot.composeapp.generated.resources.tts_permission_subscribers
import nomnomzbot.composeapp.generated.resources.tts_permission_vip
import nomnomzbot.composeapp.generated.resources.tts_retry
import nomnomzbot.composeapp.generated.resources.tts_save
import nomnomzbot.composeapp.generated.resources.tts_save_error
import nomnomzbot.composeapp.generated.resources.tts_saved
import nomnomzbot.composeapp.generated.resources.tts_saving
import nomnomzbot.composeapp.generated.resources.tts_status_disabled
import nomnomzbot.composeapp.generated.resources.tts_status_enabled
import nomnomzbot.composeapp.generated.resources.tts_toggle_enabled
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_assign
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_clear
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_current
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_default
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_description
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_id_label
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_lookup
import nomnomzbot.composeapp.generated.resources.tts_viewer_voice_title
import nomnomzbot.composeapp.generated.resources.tts_voices_count
import nomnomzbot.composeapp.generated.resources.tts_voices_default
import nomnomzbot.composeapp.generated.resources.tts_voices_more
import nomnomzbot.composeapp.generated.resources.tts_voices_search
import nomnomzbot.composeapp.generated.resources.tts_voices_use
import nomnomzbot.composeapp.generated.resources.tts_voices_use_action
import nomnomzbot.composeapp.generated.resources.tts_voices_title
import nomnomzbot.composeapp.generated.resources.shell_nav_tts
import nomnomzbot.composeapp.generated.resources.tts_test_error
import nomnomzbot.composeapp.generated.resources.tts_queue_action_error
import nomnomzbot.composeapp.generated.resources.tts_queue_approve
import nomnomzbot.composeapp.generated.resources.tts_queue_censored
import nomnomzbot.composeapp.generated.resources.tts_queue_empty
import nomnomzbot.composeapp.generated.resources.tts_queue_error
import nomnomzbot.composeapp.generated.resources.tts_queue_loading
import nomnomzbot.composeapp.generated.resources.tts_queue_reject
import nomnomzbot.composeapp.generated.resources.tts_queue_title
import nomnomzbot.composeapp.generated.resources.tts_test_play
import nomnomzbot.composeapp.generated.resources.tts_test_prompt
import nomnomzbot.composeapp.generated.resources.tts_test_success
import nomnomzbot.composeapp.generated.resources.tts_test_title
import org.jetbrains.compose.resources.StringResource
import org.jetbrains.compose.resources.stringResource

// The TTS page: an editable form over the channel's text-to-speech configuration — the enabled status plus
// the editable config values, all real data from [TtsController]. The screen seeds a local form from the
// controller's loaded config; Save persists the whole config and the controller echoes the saved values
// back. It loads on first composition and offers a retry on failure.
@Composable
fun TtsScreen(
    controller: TtsController,
    queueController: TtsQueueController,
    role: ManagementRole?,
) {
    val state: TtsState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val spacing = LocalSpacing.current

    // One decision for the whole page: TTS gates every config write control at its single Editor manage floor
    // (frontend-ia.md §3). A caller below it reads the current config but every field, toggle, and Save renders
    // disabled with "Requires Editor" (§7); the backend re-checks every write regardless.
    val manage: ManageDecision = rememberManageDecision(role, ShellRoute.Tts)
    // The approval queue is a MODERATOR surface (tts:queue:review) — a lower floor than config editing. The page
    // is already visible to Moderator+ (its read floor), so this decision is Allowed for every viewer here; it
    // stays as an explicit gate so the backend's Mod re-check is mirrored in the UI.
    val queueManage: ManageDecision = rememberManageDecisionAtFloor(role, ManagementRole.Moderator)

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: TtsState = state) {
            is TtsState.Loading -> CenteredMessage(stringResource(Res.string.tts_loading))
            is TtsState.Error ->
                ErrorContent(detail = current.detail, onRetry = { scope.launch { controller.load() } })
            is TtsState.Ready ->
                ReadyContent(
                    state = current,
                    manage = manage,
                    queueController = queueController,
                    queueManage = queueManage,
                    onSave = { edited -> scope.launch { controller.save(edited) } },
                    onTestSpeak = { voiceId, text -> scope.launch { controller.testSpeak(voiceId, text) } },
                    onLookupViewerVoice = { userId -> scope.launch { controller.loadUserVoice(userId) } },
                    onAssignViewerVoice = { userId, voiceId ->
                        scope.launch { controller.setUserVoice(userId, voiceId) }
                    },
                    onClearViewerVoice = { userId -> scope.launch { controller.clearUserVoice(userId) } },
                    onSearchVoices = { q, locale, gender, provider, accent, page ->
                        scope.launch { controller.searchVoices(q, locale, gender, provider, accent, page) }
                    },
                    onSetByok = { provider, apiKey, region ->
                        scope.launch { controller.setByokKey(provider, apiKey, region) }
                    },
                    onRemoveByok = { provider -> scope.launch { controller.removeByokKey(provider) } },
                )
        }
    }
}

// The minimum-permission values the backend accepts (UpdateTtsConfigDto regex). Each pairs the wire value
// the API persists with its localized label; the form picks one, never free text, so the value is always valid.
private val PERMISSIONS: List<Pair<String, StringResource>> =
    listOf(
        "everyone" to Res.string.tts_permission_everyone,
        "subscribers" to Res.string.tts_permission_subscribers,
        "vip" to Res.string.tts_permission_vip,
        "moderators" to Res.string.tts_permission_moderators,
        "broadcaster" to Res.string.tts_permission_broadcaster,
    )

@Composable
private fun ReadyContent(
    state: TtsState.Ready,
    manage: ManageDecision,
    queueController: TtsQueueController,
    queueManage: ManageDecision,
    onSave: (TtsConfig) -> Unit,
    onTestSpeak: (voiceId: String, text: String) -> Unit,
    onLookupViewerVoice: (userId: String) -> Unit,
    onAssignViewerVoice: (userId: String, voiceId: String) -> Unit,
    onClearViewerVoice: (userId: String) -> Unit,
    onSearchVoices: (q: String, locale: String, gender: String, provider: String, accent: String, page: Int) -> Unit,
    onSetByok: (provider: String, apiKey: String, region: String?) -> Unit,
    onRemoveByok: (provider: String) -> Unit,
) {
    val spacing = LocalSpacing.current
    val loaded: TtsConfig = state.config

    // Local editable form, re-seeded whenever a new config loads (initial load or a successful save). Holding
    // it screen-side keeps the controller a thin persistence boundary; `remember(loaded)` resets every field
    // to the saved baseline so the "differs from loaded" check below is exact.
    var isEnabled: Boolean by remember(loaded) { mutableStateOf(loaded.isEnabled) }
    var defaultVoiceId: String by remember(loaded) { mutableStateOf(loaded.defaultVoiceId ?: "") }
    var maxLengthText: String by remember(loaded) { mutableStateOf(loaded.maxCharacters.toString()) }
    var minPermission: String by remember(loaded) { mutableStateOf(loaded.minPermission) }
    var skipBotMessages: Boolean by remember(loaded) { mutableStateOf(loaded.skipBotMessages) }
    var readUsernames: Boolean by remember(loaded) { mutableStateOf(loaded.readUsernames) }
    var profanityCensorEnabled: Boolean by remember(loaded) { mutableStateOf(loaded.profanityCensorEnabled) }
    var modApprovalRequired: Boolean by remember(loaded) { mutableStateOf(loaded.modApprovalRequired) }
    // Empty text = no bits gate (minBitsToTts null). A blank field is valid; a non-numeric one is not.
    var minBitsText: String by remember(loaded) { mutableStateOf(loaded.minBitsToTts?.toString() ?: "") }
    var viewerVoiceSelfService: Boolean by
        remember(loaded) { mutableStateOf(loaded.viewerVoiceSelfServiceEnabled) }

    val maxLength: Int? = maxLengthText.toIntOrNull()
    val maxLengthValid: Boolean = maxLength != null && maxLength in 1..500
    // Blank clears the gate (null); otherwise it must parse to a non-negative int.
    val minBits: Int? = minBitsText.toIntOrNull()
    val minBitsValid: Boolean = minBitsText.isBlank() || (minBits != null && minBits >= 0)

    // copy() keeps the fields this form doesn't edit (mode, defaultProvider, BYOK flags) at their loaded
    // values, so the differs-from-loaded check and the full-config save never clobber them.
    val edited: TtsConfig =
        loaded.copy(
            isEnabled = isEnabled,
            defaultVoiceId = defaultVoiceId.ifBlank { null },
            maxCharacters = maxLength ?: loaded.maxCharacters,
            minPermission = minPermission,
            skipBotMessages = skipBotMessages,
            readUsernames = readUsernames,
            profanityCensorEnabled = profanityCensorEnabled,
            modApprovalRequired = modApprovalRequired,
            minBitsToTts = if (minBitsText.isBlank()) null else (minBits ?: loaded.minBitsToTts),
            viewerVoiceSelfServiceEnabled = viewerVoiceSelfService,
        )

    // Save is offered only when the form is valid AND actually differs from the saved baseline — saving an
    // unchanged config is a no-op the user shouldn't be invited to make. A plain derivation: every input is a
    // local recomputed each recomposition (the `var`s are State-backed), so this re-evaluates as fields change.
    val canSave: Boolean = maxLengthValid && minBitsValid && edited != loaded && !state.saving

    Column(
        modifier = Modifier.fillMaxSize().verticalScroll(rememberScrollState()),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_tts))
        StatusBanner(isEnabled = isEnabled)

        EditCard(
            isEnabled = isEnabled,
            onEnabledChange = { isEnabled = it },
            defaultVoiceId = defaultVoiceId,
            onVoiceChange = { defaultVoiceId = it },
            maxLengthText = maxLengthText,
            onMaxLengthChange = { maxLengthText = it.filter { c -> c.isDigit() } },
            maxLengthValid = maxLengthValid,
            minPermission = minPermission,
            onPermissionChange = { minPermission = it },
            skipBotMessages = skipBotMessages,
            onSkipBotMessagesChange = { skipBotMessages = it },
            readUsernames = readUsernames,
            onReadUsernamesChange = { readUsernames = it },
            profanityCensorEnabled = profanityCensorEnabled,
            onProfanityCensorChange = { profanityCensorEnabled = it },
            modApprovalRequired = modApprovalRequired,
            onModApprovalChange = { modApprovalRequired = it },
            minBitsText = minBitsText,
            onMinBitsChange = { minBitsText = it.filter { c -> c.isDigit() } },
            minBitsValid = minBitsValid,
            viewerVoiceSelfService = viewerVoiceSelfService,
            onViewerVoiceSelfServiceChange = { viewerVoiceSelfService = it },
            manage = manage,
            enabled = !state.saving,
        )

        SaveBar(
            saving = state.saving,
            justSaved = state.justSaved,
            saveError = state.saveError,
            canSave = canSave,
            manage = manage,
            onSave = { onSave(edited) },
        )

        VoiceBrowser(
            browser = state.voiceBrowser,
            currentVoiceId = defaultVoiceId,
            manage = manage,
            onSearch = onSearchVoices,
            onSelect = { defaultVoiceId = it },
            onPreviewFallback = { voiceId -> onTestSpeak(voiceId, VOICE_PREVIEW_SAMPLE) },
        )

        ByokSection(
            config = loaded,
            saving = state.saving,
            manage = manage,
            onSetByok = onSetByok,
            onRemoveByok = onRemoveByok,
        )

        TestSpeakSection(
            currentVoiceId = defaultVoiceId,
            testing = state.testing,
            testResult = state.testResult,
            testError = state.testError,
            manage = manage,
            onTest = { voiceId, text -> onTestSpeak(voiceId, text) },
        )

        ViewerVoiceSection(
            voices = state.voices,
            viewerVoice = state.viewerVoice,
            manage = manage,
            onLookup = onLookupViewerVoice,
            onAssign = onAssignViewerVoice,
            onClear = onClearViewerVoice,
        )

        TtsQueueSection(controller = queueController, manage = queueManage)
    }
}

// The moderator approval queue (item 16 P.1a) — shown below the config on the same page (the page's read floor
// is Moderator, matching tts:queue:review). When "Require moderator approval" is on, each TTS utterance waits
// here until a mod approves it (played) or rejects it (discarded). It renders the text that WILL be spoken (the
// censored version when the message was masked) and who requested it; approve/reject act immediately and reload.
// Loads its own state; nothing shows a queue when approval is off (the list is simply empty).
@Composable
private fun TtsQueueSection(controller: TtsQueueController, manage: ManageDecision) {
    val state: TtsQueueState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    LaunchedEffect(Unit) { controller.load() }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.tts_queue_title),
            style = typography.lg,
            color = tokens.cardForeground,
        )
        when (val current: TtsQueueState = state) {
            is TtsQueueState.Loading ->
                Text(
                    text = stringResource(Res.string.tts_queue_loading),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            is TtsQueueState.Empty ->
                Text(
                    text = stringResource(Res.string.tts_queue_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
            is TtsQueueState.Error ->
                Text(
                    text = stringResource(Res.string.tts_queue_error, current.detail),
                    style = typography.sm,
                    color = tokens.destructive,
                )
            is TtsQueueState.Ready -> {
                current.actionError?.let { detail ->
                    ActionErrorBanner(
                        message = stringResource(Res.string.tts_queue_action_error, detail)
                    )
                }
                Card(modifier = Modifier.fillMaxWidth()) {
                    current.entries.forEachIndexed { index, entry ->
                        if (index > 0) Separator()
                        TtsQueueRow(
                            entry = entry,
                            manage = manage,
                            onApprove = { scope.launch { controller.approve(entry.id) } },
                            onReject = { scope.launch { controller.reject(entry.id) } },
                        )
                    }
                }
            }
        }
    }
}

@Composable
private fun TtsQueueRow(
    entry: TtsQueueEntry,
    manage: ManageDecision,
    onApprove: () -> Unit,
    onReject: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    // The text that WILL be spoken: the censored version when the message was masked, else the original.
    val spokenText: String =
        if (entry.wasCensored) entry.censoredText ?: entry.originalText else entry.originalText

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Row(
            modifier = Modifier.fillMaxWidth(),
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalAlignment = Alignment.CenterVertically,
        ) {
            Text(
                text = entry.requestedByDisplayName,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.weight(1f),
            )
            if (entry.wasCensored) {
                Badge(selected = false, onClick = {}) {
                    Text(stringResource(Res.string.tts_queue_censored), style = typography.xs)
                }
            }
        }
        Text(
            text = spokenText,
            style = typography.sm,
            color = tokens.mutedForeground,
            maxLines = 4,
            overflow = TextOverflow.Ellipsis,
        )
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(onClick = onApprove, enabled = enabled) {
                    Text(stringResource(Res.string.tts_queue_approve))
                }
            }
            ManageGate(decision = manage) { enabled ->
                TextButton(onClick = onReject, enabled = enabled) {
                    Text(
                        text = stringResource(Res.string.tts_queue_reject),
                        color = if (enabled) tokens.destructive else tokens.mutedForeground,
                    )
                }
            }
        }
    }
}

// The provider / gender filters offered as chips ("" = no filter). Wire values match the backend equality filters.
private val VOICE_PROVIDERS: List<Pair<String, StringResource>> =
    listOf(
        "edge" to Res.string.tts_provider_edge,
        "azure" to Res.string.tts_provider_azure,
        "elevenlabs" to Res.string.tts_provider_elevenlabs,
    )

private val VOICE_GENDERS: List<Pair<String, StringResource>> =
    listOf(
        "male" to Res.string.tts_gender_male,
        "female" to Res.string.tts_gender_female,
        "neutral" to Res.string.tts_gender_neutral,
    )

// The searchable, server-backed voice browser (item 1a): a search box + provider/gender filter chips + a paged
// result list. Each keystroke/filter change is debounced and re-queries the backend (`GET /tts/voices?q=…`), so
// the whole catalogue is reachable, not just the first page. Each row shows the voice's rich metadata, a Preview
// (plays `previewUrl` when present, else falls back to POST /tts/test), and "Use" (sets the default voice above).
@Composable
private fun VoiceBrowser(
    browser: VoiceBrowserState?,
    currentVoiceId: String,
    manage: ManageDecision,
    onSearch: (q: String, locale: String, gender: String, provider: String, accent: String, page: Int) -> Unit,
    onSelect: (String) -> Unit,
    onPreviewFallback: (voiceId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var query: String by remember { mutableStateOf("") }
    var provider: String by remember { mutableStateOf("") }
    var gender: String by remember { mutableStateOf("") }

    // Debounce query/filter changes and re-run the search from page 1 whenever they settle. The first pass (blank
    // query, no filter) loads the opening page so the browser is never empty before the operator types.
    LaunchedEffect(query, provider, gender) {
        delay(300)
        onSearch(query.trim(), "", gender, provider, "", 1)
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.tts_voices_title),
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
            )
            if (browser != null && browser.total > 0) {
                Text(
                    text = stringResource(Res.string.tts_voices_count, browser.total),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
            AppTextField(
                value = query,
                onValueChange = { query = it },
                label = stringResource(Res.string.tts_voices_search),
                modifier = Modifier.fillMaxWidth(),
            )
            VoiceFilterChips(
                label = stringResource(Res.string.tts_voices_filter_provider),
                options = VOICE_PROVIDERS,
                selected = provider,
                onSelect = { provider = if (provider == it) "" else it },
            )
            VoiceFilterChips(
                label = stringResource(Res.string.tts_voices_filter_gender),
                options = VOICE_GENDERS,
                selected = gender,
                onSelect = { gender = if (gender == it) "" else it },
            )
        }

        when {
            browser == null || (browser.loading && browser.results.isEmpty()) -> {
                Separator()
                Text(
                    text = stringResource(Res.string.tts_voices_loading),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    modifier = Modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
                )
            }
            browser.error != null -> {
                Separator()
                Text(
                    text = browser.error,
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
                )
            }
            browser.results.isEmpty() -> {
                Separator()
                Text(
                    text = stringResource(Res.string.tts_voices_empty),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    modifier = Modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
                )
            }
            else -> {
                browser.results.forEach { voice ->
                    Separator()
                    VoiceBrowserRow(
                        voice = voice,
                        isCurrent = voice.id == currentVoiceId,
                        manage = manage,
                        onUse = { onSelect(voice.id) },
                        onPreview = {
                            if (!voice.previewUrl.isNullOrBlank()) playSoundPreview(voice.previewUrl)
                            else onPreviewFallback(voice.id)
                        },
                    )
                }
                Separator()
                VoiceBrowserPager(
                    page = browser.page,
                    hasMore = browser.hasMore,
                    loading = browser.loading,
                    onPage = { onSearch(query.trim(), "", gender, provider, "", it) },
                )
            }
        }
    }
}

// A labelled row of selectable filter chips ("" clears). Reuses the Badge chip primitive, like PermissionPicker.
@Composable
private fun VoiceFilterChips(
    label: String,
    options: List<Pair<String, StringResource>>,
    selected: String,
    onSelect: (String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(text = label, style = typography.sm, color = tokens.mutedForeground)
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            for ((value: String, labelRes: StringResource) in options) {
                val chipLabel: String = stringResource(labelRes)
                Badge(
                    selected = selected == value,
                    onClick = { onSelect(value) },
                    modifier = Modifier.clearAndSetSemantics { contentDescription = chipLabel },
                ) { Text(chipLabel, maxLines = 1) }
            }
        }
    }
}

// One browser result: rich metadata (locale · provider · accent/age, styles), Preview + Use actions.
@Composable
private fun VoiceBrowserRow(
    voice: TtsVoice,
    isCurrent: Boolean,
    manage: ManageDecision,
    onUse: () -> Unit,
    onPreview: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val useLabel: String = stringResource(Res.string.tts_voices_use_action, voice.displayName)
    val previewLabel: String = stringResource(Res.string.tts_voices_preview_action, voice.displayName)
    val meta: String =
        listOfNotNull(
                voice.locale.ifBlank { null },
                voice.provider.ifBlank { null },
                voice.accent?.ifBlank { null },
                voice.age?.ifBlank { null },
            )
            .joinToString(" · ")
    val extras: String =
        (voice.styles + voice.tags).filter { it.isNotBlank() }.distinct().take(4).joinToString(", ")

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(modifier = Modifier.weight(1f), verticalArrangement = Arrangement.spacedBy(spacing.s1)) {
            Row(
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            ) {
                Text(text = voice.displayName, style = typography.sm, color = tokens.cardForeground, maxLines = 1)
                if (isCurrent) {
                    Text(
                        text = stringResource(Res.string.tts_voices_current_tag),
                        style = typography.xs,
                        color = tokens.primary,
                        maxLines = 1,
                    )
                }
            }
            if (meta.isNotBlank()) {
                Text(text = meta, style = typography.sm, color = tokens.mutedForeground, maxLines = 1)
            }
            if (extras.isNotBlank()) {
                Text(text = extras, style = typography.xs, color = tokens.mutedForeground, maxLines = 1)
            }
        }
        GlyphButton(
            imageVector = PlayCircleGlyph,
            label = previewLabel,
            onClick = onPreview,
            tint = tokens.mutedForeground,
        )
        ManageGate(decision = manage) { enabled ->
            GlyphButton(
                imageVector = CheckCircleGlyph,
                label = useLabel,
                onClick = onUse,
                enabled = enabled,
                tint = tokens.primary,
            )
        }
    }
}

// Prev / Next paging for the voice browser — Prev enabled from page 2, Next while the backend reports more.
@Composable
private fun VoiceBrowserPager(page: Int, hasMore: Boolean, loading: Boolean, onPage: (Int) -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        TextButton(onClick = { onPage(page - 1) }, enabled = page > 1 && !loading) {
            Text(
                text = stringResource(Res.string.tts_voices_prev),
                color = if (page > 1 && !loading) tokens.cardForeground else tokens.mutedForeground,
            )
        }
        Text(
            text = stringResource(Res.string.tts_voices_page, page),
            style = typography.sm,
            color = tokens.mutedForeground,
            modifier = Modifier.weight(1f),
        )
        TextButton(onClick = { onPage(page + 1) }, enabled = hasMore && !loading) {
            Text(
                text = stringResource(Res.string.tts_voices_next),
                color = if (hasMore && !loading) tokens.cardForeground else tokens.mutedForeground,
            )
        }
    }
}

// Sample text spoken when previewing a voice that ships no ready-made clip — routed through POST /tts/test.
private const val VOICE_PREVIEW_SAMPLE: String = "Hey there, this is how I sound."

// A compact local voice picker over the first-page voices — used by the viewer-voice panel to pick the voice to
// ASSIGN to a looked-up viewer (a small, closed set is enough there; the full catalogue browser is above).
@Composable
private fun VoicePicker(
    voices: List<TtsVoice>,
    currentVoiceId: String,
    manage: ManageDecision,
    onSelect: (String) -> Unit,
) {
    if (voices.isEmpty()) return

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var query: String by remember { mutableStateOf("") }
    val trimmed: String = query.trim()
    val matches: List<TtsVoice> =
        if (trimmed.isBlank()) emptyList()
        else
            voices.filter {
                it.displayName.contains(trimmed, ignoreCase = true) ||
                    it.locale.contains(trimmed, ignoreCase = true) ||
                    it.id.contains(trimmed, ignoreCase = true)
            }
    val shown: List<TtsVoice> = matches.take(8)
    val current: TtsVoice? = voices.firstOrNull { it.id == currentVoiceId }
    val selectedLabel: String? = current?.let { "${it.displayName} (${it.locale})" }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            if (selectedLabel != null) {
                Text(
                    text = stringResource(Res.string.tts_voices_default, selectedLabel),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    maxLines = 1,
                )
            }
            AppTextField(
                value = query,
                onValueChange = { query = it },
                label = stringResource(Res.string.tts_voices_search),
                modifier = Modifier.fillMaxWidth(),
            )
        }
        shown.forEach { voice ->
            Separator()
            VoiceMatchRow(
                voice = voice,
                manage = manage,
                onUse = {
                    onSelect(voice.id)
                    query = ""
                },
            )
        }
        if (matches.size > shown.size) {
            Separator()
            Text(
                text = stringResource(Res.string.tts_voices_more, matches.size),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
                modifier = Modifier.padding(horizontal = spacing.s4, vertical = spacing.s3),
            )
        }
    }
}

// The "Bring your own key" section (item 1c): per-provider write-only key entry. The key is never echoed — the
// stored state comes from the config's `has*Key` flags; a stored key shows a "key stored" state + Remove, and
// an empty box + Save otherwise. Azure additionally carries a region. Writes gate at the page's Editor floor.
@Composable
private fun ByokSection(
    config: TtsConfig,
    saving: Boolean,
    manage: ManageDecision,
    onSetByok: (provider: String, apiKey: String, region: String?) -> Unit,
    onRemoveByok: (provider: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.tts_byok_title),
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
            )
            Text(
                text = stringResource(Res.string.tts_byok_description),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
        Separator()
        ByokProviderRow(
            providerKey = "azure",
            providerLabel = stringResource(Res.string.tts_provider_azure),
            hasKey = config.hasAzureByokKey,
            region = config.azureRegion,
            showRegion = true,
            saving = saving,
            manage = manage,
            onSetByok = onSetByok,
            onRemoveByok = onRemoveByok,
        )
        Separator()
        ByokProviderRow(
            providerKey = "elevenlabs",
            providerLabel = stringResource(Res.string.tts_provider_elevenlabs),
            hasKey = config.hasElevenLabsByokKey,
            region = null,
            showRegion = false,
            saving = saving,
            manage = manage,
            onSetByok = onSetByok,
            onRemoveByok = onRemoveByok,
        )
    }
}

@Composable
private fun ByokProviderRow(
    providerKey: String,
    providerLabel: String,
    hasKey: Boolean,
    region: String?,
    showRegion: Boolean,
    saving: Boolean,
    manage: ManageDecision,
    onSetByok: (provider: String, apiKey: String, region: String?) -> Unit,
    onRemoveByok: (provider: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var apiKey: String by remember(providerKey) { mutableStateOf("") }
    var regionText: String by remember(providerKey, region) { mutableStateOf(region ?: "") }

    Column(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Text(text = providerLabel, style = typography.sm, color = tokens.cardForeground, maxLines = 1)
        Text(
            text =
                stringResource(
                    if (hasKey) Res.string.tts_byok_stored else Res.string.tts_byok_not_stored
                ),
            style = typography.xs,
            color = if (hasKey) tokens.primary else tokens.mutedForeground,
        )
        RevealableSecretField(
            value = apiKey,
            onValueChange = { apiKey = it },
            label = stringResource(Res.string.tts_byok_key_label),
            enabled = !saving,
            modifier = Modifier.fillMaxWidth(),
        )
        if (showRegion) {
            AppTextField(
                value = regionText,
                onValueChange = { regionText = it },
                label = stringResource(Res.string.tts_byok_region_label),
                modifier = Modifier.fillMaxWidth(),
            )
        }
        Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = {
                        onSetByok(providerKey, apiKey.trim(), if (showRegion) regionText.trim().ifBlank { null } else null)
                        apiKey = ""
                    },
                    enabled = enabled && apiKey.isNotBlank() && !saving,
                ) {
                    Text(stringResource(Res.string.tts_byok_save))
                }
            }
            if (hasKey) {
                ManageGate(decision = manage) { enabled ->
                    TextButton(onClick = { onRemoveByok(providerKey) }, enabled = enabled && !saving) {
                        Text(
                            text = stringResource(Res.string.tts_byok_remove),
                            color = if (enabled && !saving) tokens.destructive else tokens.mutedForeground,
                        )
                    }
                }
            }
        }
    }
}

// The per-viewer voice override (item 16): assign one viewer a specific voice so their messages always read in
// it (overriding the channel default). The operator enters the viewer's Twitch user id and looks them up; the
// panel then shows their current voice (or "channel default"), a picker to choose a synthesisable voice, Assign,
// and Clear. The reused [VoicePicker] below drives the pick. Write actions gate at the page's manage floor.
@Composable
private fun ViewerVoiceSection(
    voices: List<TtsVoice>,
    viewerVoice: ViewerVoiceState?,
    manage: ManageDecision,
    onLookup: (userId: String) -> Unit,
    onAssign: (userId: String, voiceId: String) -> Unit,
    onClear: (userId: String) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var viewerId: String by remember { mutableStateOf("") }
    // The voice picked for this viewer — re-seeded from their current override each time a fresh lookup lands, so
    // the picker starts on the voice they already have (or blank when they use the default).
    var pickedVoiceId: String by remember(viewerVoice?.userId, viewerVoice?.currentVoiceId) {
        mutableStateOf(viewerVoice?.currentVoiceId ?: "")
    }

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(
            modifier = Modifier.fillMaxWidth().padding(spacing.s4),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.tts_viewer_voice_title),
                style = typography.base,
                color = tokens.cardForeground,
                maxLines = 1,
            )
            Text(
                text = stringResource(Res.string.tts_viewer_voice_description),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
            Row(
                horizontalArrangement = Arrangement.spacedBy(spacing.s2),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                AppTextField(
                    value = viewerId,
                    onValueChange = { viewerId = it },
                    label = stringResource(Res.string.tts_viewer_voice_id_label),
                    modifier = Modifier.weight(1f),
                )
                Button(
                    onClick = { onLookup(viewerId.trim()) },
                    enabled = viewerId.isNotBlank() && viewerVoice?.busy != true,
                ) {
                    Text(stringResource(Res.string.tts_viewer_voice_lookup))
                }
            }

            viewerVoice?.let { vv ->
                vv.error?.let { detail ->
                    Text(text = detail, style = typography.sm, color = tokens.destructive)
                }
                val currentLabel: String? =
                    voices.firstOrNull { it.id == vv.currentVoiceId }?.let { "${it.displayName} (${it.locale})" }
                Text(
                    text =
                        if (vv.currentVoiceId == null) {
                            stringResource(Res.string.tts_viewer_voice_default)
                        } else {
                            stringResource(Res.string.tts_viewer_voice_current, currentLabel ?: vv.currentVoiceId)
                        },
                    style = typography.sm,
                    color = tokens.mutedForeground,
                )
                Row(horizontalArrangement = Arrangement.spacedBy(spacing.s2)) {
                    ManageGate(decision = manage) { enabled ->
                        Button(
                            onClick = { onAssign(vv.userId, pickedVoiceId) },
                            enabled = enabled && pickedVoiceId.isNotBlank() && !vv.busy,
                        ) {
                            Text(stringResource(Res.string.tts_viewer_voice_assign))
                        }
                    }
                    if (vv.currentVoiceId != null) {
                        ManageGate(decision = manage) { enabled ->
                            TextButton(onClick = { onClear(vv.userId) }, enabled = enabled && !vv.busy) {
                                Text(
                                    text = stringResource(Res.string.tts_viewer_voice_clear),
                                    color = if (enabled) tokens.destructive else tokens.mutedForeground,
                                )
                            }
                        }
                    }
                }
            }
        }
    }

    // The picker only appears once a viewer is looked up — it drives [pickedVoiceId] for the Assign above.
    if (viewerVoice != null) {
        VoicePicker(
            voices = voices,
            currentVoiceId = pickedVoiceId,
            manage = manage,
            onSelect = { pickedVoiceId = it },
        )
    }
}

// One voice match: name + locale/provider, with a "Use" action that sets it as the default voice (Editor floor).
@Composable
private fun VoiceMatchRow(voice: TtsVoice, manage: ManageDecision, onUse: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val useLabel: String = stringResource(Res.string.tts_voices_use_action, voice.displayName)
    val rowDescription: String = "${voice.displayName}, ${voice.locale}, ${voice.provider}"

    Row(
        modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s2),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = rowDescription },
            verticalArrangement = Arrangement.spacedBy(spacing.s1),
        ) {
            Text(
                text = voice.displayName,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
            )
            Text(
                text = "${voice.locale} · ${voice.provider}",
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(imageVector = CheckCircleGlyph, label = useLabel, onClick = onUse, enabled = enabled, tint = tokens.primary)
        }
    }
}

@Composable
private fun StatusBanner(isEnabled: Boolean) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val statusText: String =
        stringResource(if (isEnabled) Res.string.tts_status_enabled else Res.string.tts_status_disabled)

    Card(modifier = Modifier.fillMaxWidth()) {
        Row(
            modifier = Modifier
                .fillMaxWidth()
                .padding(horizontal = spacing.s4, vertical = spacing.s3)
                // One node for screen readers: "TTS enabled" rather than a disconnected dot + label.
                .clearAndSetSemantics { contentDescription = statusText },
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Box(
                modifier = Modifier
                    .size(spacing.s2)
                    .clip(CircleShape)
                    .background(if (isEnabled) tokens.primary else tokens.mutedForeground),
            )
            Text(text = statusText, style = typography.xl, color = tokens.cardForeground)
        }
    }
}

@Composable
private fun EditCard(
    isEnabled: Boolean,
    onEnabledChange: (Boolean) -> Unit,
    defaultVoiceId: String,
    onVoiceChange: (String) -> Unit,
    maxLengthText: String,
    onMaxLengthChange: (String) -> Unit,
    maxLengthValid: Boolean,
    minPermission: String,
    onPermissionChange: (String) -> Unit,
    skipBotMessages: Boolean,
    onSkipBotMessagesChange: (Boolean) -> Unit,
    readUsernames: Boolean,
    onReadUsernamesChange: (Boolean) -> Unit,
    profanityCensorEnabled: Boolean,
    onProfanityCensorChange: (Boolean) -> Unit,
    modApprovalRequired: Boolean,
    onModApprovalChange: (Boolean) -> Unit,
    minBitsText: String,
    onMinBitsChange: (String) -> Unit,
    minBitsValid: Boolean,
    viewerVoiceSelfService: Boolean,
    onViewerVoiceSelfServiceChange: (Boolean) -> Unit,
    manage: ManageDecision,
    enabled: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current

    Card(modifier = Modifier.fillMaxWidth()) {
        Column(modifier = Modifier.fillMaxWidth()) {
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                SwitchRow(
                    labelRes = Res.string.tts_toggle_enabled,
                    checked = isEnabled,
                    onCheckedChange = onEnabledChange,
                    manage = manage,
                    enabled = enabled,
                )
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                VoiceField(value = defaultVoiceId, onValueChange = onVoiceChange, manage = manage, enabled = enabled)
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                MaxLengthField(
                    value = maxLengthText,
                    onValueChange = onMaxLengthChange,
                    valid = maxLengthValid,
                    manage = manage,
                    enabled = enabled,
                )
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                PermissionPicker(
                    selected = minPermission,
                    onSelect = onPermissionChange,
                    manage = manage,
                    enabled = enabled,
                )
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                SwitchRow(
                    labelRes = Res.string.tts_label_skip_bot_messages,
                    checked = skipBotMessages,
                    onCheckedChange = onSkipBotMessagesChange,
                    manage = manage,
                    enabled = enabled,
                )
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                SwitchRow(
                    labelRes = Res.string.tts_label_read_usernames,
                    checked = readUsernames,
                    onCheckedChange = onReadUsernamesChange,
                    manage = manage,
                    enabled = enabled,
                )
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                SwitchRow(
                    labelRes = Res.string.tts_label_filter_profanity,
                    checked = profanityCensorEnabled,
                    onCheckedChange = onProfanityCensorChange,
                    manage = manage,
                    enabled = enabled,
                )
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                SwitchRow(
                    labelRes = Res.string.tts_label_mod_approval,
                    checked = modApprovalRequired,
                    onCheckedChange = onModApprovalChange,
                    manage = manage,
                    enabled = enabled,
                )
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                ManageGate(decision = manage) { gateEnabled ->
                    AppTextField(
                        value = minBitsText,
                        onValueChange = onMinBitsChange,
                        enabled = gateEnabled && enabled,
                        isError = !minBitsValid,
                        modifier = Modifier.fillMaxWidth(),
                        label = stringResource(Res.string.tts_label_min_bits),
                        keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
                        supportingText = stringResource(Res.string.tts_min_bits_hint),
                    )
                }
            }
            Separator()
            Column(modifier = Modifier.fillMaxWidth().padding(horizontal = spacing.s4, vertical = spacing.s3)) {
                SwitchRow(
                    labelRes = Res.string.tts_label_viewer_self_service,
                    checked = viewerVoiceSelfService,
                    onCheckedChange = onViewerVoiceSelfServiceChange,
                    manage = manage,
                    enabled = enabled,
                )
            }
        }
    }
}

@Composable
private fun SwitchRow(
    labelRes: StringResource,
    checked: Boolean,
    onCheckedChange: (Boolean) -> Unit,
    manage: ManageDecision,
    enabled: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val label: String = stringResource(labelRes)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            // One toggleable node for screen readers: the label names what the switch controls.
            .clearAndSetSemantics { contentDescription = label },
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Text(
            text = label,
            style = typography.sm,
            color = tokens.cardForeground,
            modifier = Modifier.weight(1f),
        )
        ManageGate(decision = manage) { gateEnabled ->
            Switch(checked = checked, onCheckedChange = onCheckedChange, enabled = gateEnabled && enabled)
        }
    }
}

@Composable
private fun VoiceField(
    value: String,
    onValueChange: (String) -> Unit,
    manage: ManageDecision,
    enabled: Boolean,
) {
    ManageGate(decision = manage) { gateEnabled ->
        AppTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = gateEnabled && enabled,
            modifier = Modifier.fillMaxWidth(),
            label = stringResource(Res.string.tts_label_voice),
        )
    }
}

@Composable
private fun MaxLengthField(
    value: String,
    onValueChange: (String) -> Unit,
    valid: Boolean,
    manage: ManageDecision,
    enabled: Boolean,
) {
    ManageGate(decision = manage) { gateEnabled ->
        AppTextField(
            value = value,
            onValueChange = onValueChange,
            enabled = gateEnabled && enabled,
            isError = !valid,
            modifier = Modifier.fillMaxWidth(),
            label = stringResource(Res.string.tts_label_max_length),
            keyboardOptions = KeyboardOptions(keyboardType = KeyboardType.Number),
            errorText = stringResource(Res.string.tts_max_length_invalid),
        )
    }
}

@OptIn(ExperimentalLayoutApi::class)
@Composable
private fun PermissionPicker(
    selected: String,
    onSelect: (String) -> Unit,
    manage: ManageDecision,
    enabled: Boolean,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s2)) {
        Text(
            text = stringResource(Res.string.tts_label_min_permission),
            style = typography.sm,
            color = tokens.mutedForeground,
        )
        // Five chips never fit on one row at a phone width, so they wrap rather than clip off-screen.
        FlowRow(
            horizontalArrangement = Arrangement.spacedBy(spacing.s2),
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            for ((value: String, labelRes: StringResource) in PERMISSIONS) {
                val label: String = stringResource(labelRes)
                ManageGate(decision = manage) { gateEnabled ->
                    Badge(
                        selected = selected == value,
                        onClick = { onSelect(value) },
                        enabled = gateEnabled && enabled,
                        modifier = Modifier.clearAndSetSemantics { contentDescription = label },
                    ) { Text(label, maxLines = 1) }
                }
            }
        }
    }
}

@Composable
private fun SaveBar(
    saving: Boolean,
    justSaved: Boolean,
    saveError: String?,
    canSave: Boolean,
    manage: ManageDecision,
    onSave: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Row(
        modifier = Modifier.fillMaxWidth(),
        horizontalArrangement = Arrangement.spacedBy(spacing.s4),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        // The save feedback line: an error takes priority, then the transient "Saved" confirmation.
        when {
            saveError != null ->
                Text(
                    text = stringResource(Res.string.tts_save_error, saveError),
                    style = typography.sm,
                    color = tokens.destructive,
                    modifier = Modifier.weight(1f),
                )
            justSaved ->
                Text(
                    text = stringResource(Res.string.tts_saved),
                    style = typography.sm,
                    color = tokens.mutedForeground,
                    modifier = Modifier.weight(1f),
                )
            else -> Box(modifier = Modifier.weight(1f))
        }

        if (saving) {
            val savingLabel: String = stringResource(Res.string.tts_saving)
            Spinner(
                modifier = Modifier
                    .size(spacing.s6)
                    .clearAndSetSemantics { contentDescription = savingLabel },
            )
        } else {
            ManageGate(decision = manage) { gateEnabled ->
                Button(
                    onClick = onSave,
                    enabled = gateEnabled && canSave,
                    modifier = Modifier.wrapContentWidth(),
                ) {
                    Text(stringResource(Res.string.tts_save))
                }
            }
        }
    }
}

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.tts_error, detail),
                style = typography.base,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) { Text(text = stringResource(Res.string.tts_retry)) }
        }
    }
}

// The test-speak panel: a text input + "Play" button. On success the backend returns base64 audio that the
// dashboard plays via a JS interop data URI. [testResult] carries the audio; [testError] surfaces synthesis
// failures without losing the loaded config. Nothing renders if no voice is selected.
@Composable
private fun TestSpeakSection(
    currentVoiceId: String,
    testing: Boolean,
    testResult: TtsTestResult?,
    testError: String?,
    manage: ManageDecision,
    onTest: (voiceId: String, text: String) -> Unit,
) {
    if (currentVoiceId.isBlank()) return

    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    var testText: String by remember { mutableStateOf("") }

    Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
        Text(
            text = stringResource(Res.string.tts_test_title),
            style = typography.lg,
            color = tokens.cardForeground,
            maxLines = 1,
            overflow = TextOverflow.Ellipsis,
        )

        Row(
            modifier = Modifier.fillMaxWidth(),
            verticalAlignment = Alignment.CenterVertically,
            horizontalArrangement = Arrangement.spacedBy(spacing.s3),
        ) {
            AppTextField(
                value = testText,
                onValueChange = { testText = it },
                label = stringResource(Res.string.tts_test_prompt),
                modifier = Modifier.weight(1f),
                enabled = !testing,
            )
            ManageGate(decision = manage) { enabled ->
                Button(
                    onClick = { if (testText.isNotBlank()) onTest(currentVoiceId, testText.trim()) },
                    enabled = enabled && !testing && testText.isNotBlank(),
                ) {
                    if (testing) {
                        Spinner(
                            modifier = Modifier.size(spacing.s4),
                            color = tokens.primaryForeground,
                        )
                    } else {
                        Text(text = stringResource(Res.string.tts_test_play))
                    }
                }
            }
        }

        testError?.let { error ->
            Text(
                text = stringResource(Res.string.tts_test_error, error),
                style = typography.sm,
                color = tokens.destructive,
            )
        }

        testResult?.let { result ->
            Text(
                text = stringResource(Res.string.tts_test_success, result.provider, result.durationMs),
                style = typography.sm,
                color = tokens.mutedForeground,
            )
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.base, color = tokens.mutedForeground)
    }
}
