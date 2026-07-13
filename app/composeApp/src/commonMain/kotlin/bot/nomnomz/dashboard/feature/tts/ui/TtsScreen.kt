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
import bot.nomnomz.dashboard.core.designsystem.component.Card
import androidx.compose.material3.Text

import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.CheckCircleGlyph
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
) {
    val spacing = LocalSpacing.current
    val loaded: TtsConfig = state.config

    // Local editable form, re-seeded whenever a new config loads (initial load or a successful save). Holding
    // it screen-side keeps the controller a thin persistence boundary; `remember(loaded)` resets every field
    // to the saved baseline so the "differs from loaded" check below is exact.
    var isEnabled: Boolean by remember(loaded) { mutableStateOf(loaded.isEnabled) }
    var defaultVoiceId: String by remember(loaded) { mutableStateOf(loaded.defaultVoiceId) }
    var maxLengthText: String by remember(loaded) { mutableStateOf(loaded.maxLength.toString()) }
    var minPermission: String by remember(loaded) { mutableStateOf(loaded.minPermission) }
    var skipBotMessages: Boolean by remember(loaded) { mutableStateOf(loaded.skipBotMessages) }
    var readUsernames: Boolean by remember(loaded) { mutableStateOf(loaded.readUsernames) }
    var profanityCensorEnabled: Boolean by remember(loaded) { mutableStateOf(loaded.profanityCensorEnabled) }
    var modApprovalRequired: Boolean by remember(loaded) { mutableStateOf(loaded.modApprovalRequired) }

    val maxLength: Int? = maxLengthText.toIntOrNull()
    val maxLengthValid: Boolean = maxLength != null && maxLength in 1..500

    val edited: TtsConfig =
        TtsConfig(
            isEnabled = isEnabled,
            defaultVoiceId = defaultVoiceId,
            maxLength = maxLength ?: loaded.maxLength,
            minPermission = minPermission,
            skipBotMessages = skipBotMessages,
            readUsernames = readUsernames,
            profanityCensorEnabled = profanityCensorEnabled,
            modApprovalRequired = modApprovalRequired,
        )

    // Save is offered only when the form is valid AND actually differs from the saved baseline — saving an
    // unchanged config is a no-op the user shouldn't be invited to make. A plain derivation: every input is a
    // local recomputed each recomposition (the `var`s are State-backed), so this re-evaluates as fields change.
    val canSave: Boolean = maxLengthValid && edited != loaded && !state.saving

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

        VoicePicker(
            voices = state.voices,
            currentVoiceId = defaultVoiceId,
            manage = manage,
            onSelect = { defaultVoiceId = it },
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

// A searchable voice picker under the form: the count + current selection, then a search box. Typing filters
// the channel's voices by display name / locale / id and shows the top matches (capped, so a provider's
// hundreds never flood the page); "Use" sets the defaultVoiceId field above. Nothing renders if no voices load.
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
        if (trimmed.isBlank()) {
            emptyList()
        } else {
            voices.filter {
                it.displayName.contains(trimmed, ignoreCase = true) ||
                    it.locale.contains(trimmed, ignoreCase = true) ||
                    it.id.contains(trimmed, ignoreCase = true)
            }
        }
    val shown: List<TtsVoice> = matches.take(8)

    val current: TtsVoice? = voices.firstOrNull { it.id == currentVoiceId }
    val selectedLabel: String? = current?.let { "${it.displayName} (${it.locale})" }

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
            Text(
                text = stringResource(Res.string.tts_voices_count, voices.size),
                style = typography.sm,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
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
