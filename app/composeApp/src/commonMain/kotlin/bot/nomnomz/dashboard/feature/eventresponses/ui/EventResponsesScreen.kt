// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

package bot.nomnomz.dashboard.feature.eventresponses.ui

import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.semantics.clearAndSetSemantics
import androidx.compose.ui.semantics.contentDescription
import androidx.compose.ui.text.style.TextAlign
import androidx.compose.ui.text.style.TextOverflow
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import bot.nomnomz.dashboard.core.designsystem.component.ActionErrorBanner
import bot.nomnomz.dashboard.core.designsystem.component.AlertDialog
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.ConfirmDialog
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenu
import bot.nomnomz.dashboard.core.designsystem.component.DropdownMenuItem
import bot.nomnomz.dashboard.core.designsystem.component.EntityPickerField
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.PipelineBindPicker
import bot.nomnomz.dashboard.core.designsystem.component.Separator
import bot.nomnomz.dashboard.core.designsystem.component.Switch
import bot.nomnomz.dashboard.core.designsystem.component.TemplateHelpersLink
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.ChevronDownGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.ApiResult
import bot.nomnomz.dashboard.core.network.EventResponse
import bot.nomnomz.dashboard.core.i18n.resolveSchemaString
import bot.nomnomz.dashboard.core.network.EventResponsePreset
import bot.nomnomz.dashboard.core.network.EventResponseSummary
import bot.nomnomz.dashboard.core.network.PipelineSummary
import bot.nomnomz.dashboard.core.network.TemplateHelperContext
import bot.nomnomz.dashboard.core.network.TemplateHelpersApi
import bot.nomnomz.dashboard.core.network.TestRunResult
import bot.nomnomz.dashboard.core.network.WidgetSummary
import bot.nomnomz.dashboard.feature.eventresponses.state.EventResponsesController
import bot.nomnomz.dashboard.feature.eventresponses.state.EventResponsesState
import bot.nomnomz.dashboard.feature.picklists.ui.PickListInsertMenu
import bot.nomnomz.dashboard.feature.pipelines.state.PipelineTestRunController
import bot.nomnomz.dashboard.feature.pipelines.state.PipelineTestRunUiState
import bot.nomnomz.dashboard.feature.pipelines.ui.PipelineTestAction
import bot.nomnomz.dashboard.feature.pipelines.ui.PipelineTestRunDialog
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.event_responses_action_error
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_cancel
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_message_label
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_pipeline_choose
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_pipeline_create_confirm
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_pipeline_create_new
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_pipeline_help
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_pipeline_new_name
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_pipeline_pick
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_response_type_label
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_save
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_title
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_widget_choose
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_widget_empty
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_widget_pick
import nomnomzbot.composeapp.generated.resources.event_responses_edit_action
import nomnomzbot.composeapp.generated.resources.event_responses_empty
import nomnomzbot.composeapp.generated.resources.event_responses_error
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_reset
import nomnomzbot.composeapp.generated.resources.event_responses_loading
import nomnomzbot.composeapp.generated.resources.event_responses_reset_confirm_message
import nomnomzbot.composeapp.generated.resources.event_responses_reset_confirm_title
import nomnomzbot.composeapp.generated.resources.event_responses_retry
import nomnomzbot.composeapp.generated.resources.event_responses_toggle_action
import nomnomzbot.composeapp.generated.resources.event_responses_type_chat_message
import nomnomzbot.composeapp.generated.resources.event_responses_type_none
import nomnomzbot.composeapp.generated.resources.event_responses_type_overlay
import nomnomzbot.composeapp.generated.resources.event_responses_type_pipeline
import nomnomzbot.composeapp.generated.resources.event_type_channel_cheer
import nomnomzbot.composeapp.generated.resources.event_type_channel_follow
import nomnomzbot.composeapp.generated.resources.event_type_channel_points_redemption
import nomnomzbot.composeapp.generated.resources.event_type_channel_poll_begin
import nomnomzbot.composeapp.generated.resources.event_type_channel_prediction_begin
import nomnomzbot.composeapp.generated.resources.event_type_channel_raid
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscribe
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_gift
import nomnomzbot.composeapp.generated.resources.event_type_channel_subscription_message
import nomnomzbot.composeapp.generated.resources.event_type_engagement_first_time_chatter
import nomnomzbot.composeapp.generated.resources.event_type_engagement_session_first_message
import nomnomzbot.composeapp.generated.resources.event_type_stream_offline
import nomnomzbot.composeapp.generated.resources.event_type_stream_online
import nomnomzbot.composeapp.generated.resources.event_type_unknown
import nomnomzbot.composeapp.generated.resources.shell_nav_event_responses
import org.jetbrains.compose.resources.stringResource

// The Event Responses page: maps Twitch channel events to a configured bot reaction
// (chat message, overlay, pipeline, or none). Moderator+ can view/toggle; Editor+ can edit.
@Composable
fun EventResponsesScreen(
    controller: EventResponsesController,
    role: ManagementRole?,
    templateHelpersApi: TemplateHelpersApi,
) {
    val state: EventResponsesState by controller.state.collectAsStateWithLifecycle()
    val scope = rememberCoroutineScope()
    val manage: ManageDecision = rememberManageDecision(role = role, route = ShellRoute.EventResponses)
    val spacing = LocalSpacing.current

    var editing: EventResponseSummary? by remember { mutableStateOf(null) }

    LaunchedEffect(Unit) { controller.load() }

    Box(modifier = Modifier.fillMaxSize().padding(spacing.s6)) {
        when (val current: EventResponsesState = state) {
            is EventResponsesState.Loading -> CenteredMessage(stringResource(Res.string.event_responses_loading))
            is EventResponsesState.Empty -> CenteredMessage(stringResource(Res.string.event_responses_empty))
            is EventResponsesState.Error ->
                ErrorContent(
                    detail = current.detail,
                    onRetry = { scope.launch { controller.load() } },
                )
            is EventResponsesState.Ready ->
                ReadyContent(
                    responses = current.responses,
                    actionError = current.actionError,
                    manage = manage,
                    onToggle = { response, enabled ->
                        scope.launch { controller.toggle(response.eventType, enabled) }
                    },
                    onEdit = { response -> editing = response },
                )
        }
    }

    editing?.let { response ->
        val ready: EventResponsesState.Ready? = state as? EventResponsesState.Ready
        EditDialog(
            response = response,
            preset = ready?.presets?.get(response.eventType),
            pipelines = ready?.pipelines ?: emptyList(),
            pickListNames = ready?.pickListNames ?: emptyList(),
            widgets = ready?.widgets ?: emptyList(),
            templateHelpersApi = templateHelpersApi,
            loadDetail = { controller.detail(response.eventType) },
            onDismiss = { editing = null },
            onSave = { responseType, message, pipelineId, widgetId ->
                editing = null
                scope.launch {
                    controller.save(response.eventType, responseType, message, pipelineId, widgetId)
                }
            },
            onCreatePipeline = { name -> controller.createPipelineReturning(name) },
            onTestRunPipeline = { pipelineId, variables -> controller.testRunPipeline(pipelineId, variables) },
            onResetToDefault = {
                editing = null
                scope.launch { controller.resetToDefault(response.eventType) }
            },
            manage = manage,
        )
    }
}

// Ready state: PageHeader + optional error banner + single-card table of event responses.
@Composable
private fun ReadyContent(
    responses: List<EventResponseSummary>,
    actionError: String?,
    manage: ManageDecision,
    onToggle: (EventResponseSummary, Boolean) -> Unit,
    onEdit: (EventResponseSummary) -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    Column(
        modifier = Modifier.fillMaxSize(),
        verticalArrangement = Arrangement.spacedBy(spacing.s4),
    ) {
        PageHeader(title = stringResource(Res.string.shell_nav_event_responses))

        actionError?.let { detail ->
            ActionErrorBanner(message = stringResource(Res.string.event_responses_action_error, detail))
        }

        // Single card table — all events in one container, rows separated by hairlines.
        Card(modifier = Modifier.fillMaxWidth().weight(1f)) {
            if (responses.isEmpty()) {
                Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(
                        text = stringResource(Res.string.event_responses_empty),
                        style = typography.base,
                        color = tokens.mutedForeground,
                    )
                }
            } else {
                LazyColumn(modifier = Modifier.fillMaxSize()) {
                    itemsIndexed(items = responses, key = { _, r -> r.id }) { index, response ->
                        EventResponseRow(
                            response = response,
                            manage = manage,
                            onToggle = { enabled -> onToggle(response, enabled) },
                            onEdit = { onEdit(response) },
                        )
                        if (index < responses.lastIndex) {
                            Separator()
                        }
                    }
                }
            }
        }
    }
}

// Single event row inside the shared card — no per-row background; dividers separate entries.
@Composable
private fun EventResponseRow(
    response: EventResponseSummary,
    manage: ManageDecision,
    onToggle: (Boolean) -> Unit,
    onEdit: () -> Unit,
) {
    val tokens = LocalTokens.current
    val spacing = LocalSpacing.current
    val typography = LocalTypography.current

    val eventLabel: String = response.eventType.toEventLabel()
    val typeLabel: String = response.responseType.toResponseTypeLabel()
    val toggleSemantics: String = stringResource(Res.string.event_responses_toggle_action, eventLabel)
    val editSemantics: String = stringResource(Res.string.event_responses_edit_action, eventLabel)

    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(horizontal = spacing.s4, vertical = spacing.s3),
        verticalAlignment = Alignment.CenterVertically,
        horizontalArrangement = Arrangement.spacedBy(spacing.s3),
    ) {
        Column(
            modifier = Modifier
                .weight(1f)
                .clearAndSetSemantics { contentDescription = "$eventLabel, $typeLabel" },
            verticalArrangement = Arrangement.spacedBy(spacing.s0_5),
        ) {
            Text(
                text = eventLabel,
                style = typography.sm,
                color = tokens.cardForeground,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
            )
            Text(
                text = typeLabel,
                style = typography.xs,
                color = tokens.mutedForeground,
                maxLines = 1,
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(icon = EditGlyph, label = editSemantics, onClick = onEdit, enabled = enabled)
        }
        ManageGate(decision = manage) { enabled ->
            Switch(
                checked = response.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                modifier = Modifier.clearAndSetSemantics { contentDescription = toggleSemantics },
            )
        }
    }
}

private val ResponseTypes: List<String> = listOf("none", "chat_message", "overlay", "pipeline")

// Edit dialog — response type picker, a pre-filled message template with insert chips (chat/overlay), and a
// first-class pipeline BINDING for pipeline responses: pick an existing pipeline OR create-and-bind a new one
// (no pasting ids), via the shared [PipelineBindPicker] (S046). The stored config + preset catalog are loaded so
// the fields open pre-filled, never blank.
@Composable
private fun EditDialog(
    response: EventResponseSummary,
    preset: EventResponsePreset?,
    pipelines: List<PipelineSummary>,
    pickListNames: List<String>,
    widgets: List<WidgetSummary>,
    templateHelpersApi: TemplateHelpersApi,
    loadDetail: suspend () -> EventResponse?,
    onDismiss: () -> Unit,
    onSave: (responseType: String, message: String?, pipelineId: String?, widgetId: String?) -> Unit,
    onCreatePipeline: suspend (name: String) -> PipelineSummary?,
    onTestRunPipeline: suspend (pipelineId: String, variables: Map<String, String>) -> ApiResult<TestRunResult>,
    onResetToDefault: () -> Unit,
    manage: ManageDecision,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current
    val testRunController: PipelineTestRunController = remember { PipelineTestRunController(onTestRunPipeline) }
    val testRunState: PipelineTestRunUiState by testRunController.state.collectAsStateWithLifecycle()
    var testRunDialogOpen: Boolean by remember { mutableStateOf(false) }
    val testRunScope: kotlinx.coroutines.CoroutineScope = rememberCoroutineScope()

    var selectedType: String by remember { mutableStateOf(response.responseType) }
    var message: String by remember { mutableStateOf("") }
    var pipelineChoice: String? by remember { mutableStateOf(null) }
    var widgetChoice: String by remember { mutableStateOf("") }
    var typeMenuOpen: Boolean by remember { mutableStateOf(false) }
    // The reset is destructive (it discards the current config), so it confirms first and names exactly what
    // happens — the row goes back to its disabled, no-message default, it is NOT a permanent removal (the
    // backend's list read re-seeds the catalog default the moment the row is gone).
    var confirmingReset: Boolean by remember { mutableStateOf(false) }

    // The catalog serves a translation KEY for the default template; resolve it in the viewer's locale here,
    // where a Composable context exists, so the LaunchedEffect below pre-fills real text (never a raw key).
    val presetTemplate: String = resolveSchemaString(preset?.defaultTemplate)

    // Load the stored config (so the fields open pre-filled) and fall back to the preset's default template when
    // there is no stored message yet — the "pre-filled templates in every input" the owner asked for.
    LaunchedEffect(response.eventType) {
        val detail: EventResponse? = loadDetail()
        selectedType = detail?.responseType?.takeIf { it.isNotBlank() } ?: response.responseType
        val storedMessage: String = detail?.message.orEmpty()
        message = storedMessage.ifBlank { presetTemplate }
        pipelineChoice = detail?.pipelineId?.ifBlank { null }
        widgetChoice = detail?.metadata?.get(EventResponsesController.WidgetIdMetadataKey).orEmpty()
    }

    val canSubmit: Boolean =
        manage.isAllowed &&
            when (selectedType) {
                "pipeline" -> pipelineChoice != null
                // An overlay response must target a widget — but only gate on it when the channel actually has
                // widgets to pick (an empty channel can still save the type and add a widget later).
                "overlay" -> widgets.isEmpty() || widgetChoice.isNotBlank()
                else -> true
            }

    AlertDialog(
        onDismissRequest = onDismiss,
        title = {
            Text(
                text = stringResource(Res.string.event_responses_dialog_title, response.eventType.toEventLabel()),
                style = typography.base,
                color = tokens.cardForeground,
            )
        },
        text = {
            Column(verticalArrangement = Arrangement.spacedBy(spacing.s3)) {
                // Response-type dropdown — AppTextField as read-only trigger + chevron icon.
                Box {
                    AppTextField(
                        value = selectedType.toResponseTypeLabel(),
                        onValueChange = {},
                        label = stringResource(Res.string.event_responses_dialog_response_type_label),
                        modifier = Modifier.fillMaxWidth().clickable { typeMenuOpen = true },
                        trailingIcon = {
                            GlyphButton(
                                icon = ChevronDownGlyph,
                                label = stringResource(Res.string.event_responses_dialog_response_type_label),
                                onClick = { typeMenuOpen = true },
                                tint = tokens.mutedForeground,
                            )
                        },
                    )
                    DropdownMenu(
                        expanded = typeMenuOpen,
                        onDismissRequest = { typeMenuOpen = false },
                    ) {
                        ResponseTypes.forEach { type ->
                            DropdownMenuItem(
                                text = {
                                    Text(
                                        text = type.toResponseTypeLabel(),
                                        style = typography.sm,
                                        color = tokens.popoverForeground,
                                    )
                                },
                                onClick = {
                                    selectedType = type
                                    typeMenuOpen = false
                                },
                            )
                        }
                    }
                }

                // Message template — chat_message and overlay responses need a body; pre-filled from the preset,
                // with the event's seeded variables offered as insert chips.
                if (selectedType == "chat_message" || selectedType == "overlay") {
                    AppTextField(
                        value = message,
                        onValueChange = { message = it },
                        label = stringResource(Res.string.event_responses_dialog_message_label),
                        modifier = Modifier.fillMaxWidth(),
                    )
                    TemplateHelpersLink(
                        context = TemplateHelperContext.EventResponse,
                        api = templateHelpersApi,
                        onInsert = { token -> message = appendToken(message, token) },
                    )
                    // Insert a random-response token (`{list.pick.<name>}`) — renders only when lists exist.
                    PickListInsertMenu(
                        names = pickListNames,
                        onInsert = { token -> message = appendToken(message, token) },
                    )
                }

                // Overlay target — which widget this event fires. Persisted in the response's MetadataJson so the
                // overlay dispatch can render the chosen widget.
                if (selectedType == "overlay") {
                    // The overlay target is a reference to another table (the channel's widgets) → the shared
                    // search dropdown; clearing it selects no widget.
                    EntityPickerField(
                        items = widgets,
                        selectedId = widgetChoice.ifBlank { null },
                        onSelect = { widgetChoice = it ?: "" },
                        idOf = { it.id },
                        labelOf = { it.name },
                        label = stringResource(Res.string.event_responses_dialog_widget_pick),
                        placeholder = stringResource(Res.string.event_responses_dialog_widget_choose),
                        emptyText = stringResource(Res.string.event_responses_dialog_widget_empty),
                    )
                }

                // Pipeline binding — pick an existing pipeline OR create-and-bind a new one (no pasted ids), via
                // the shared bind picker (S046): its own name field resolves to a real id before this dialog is
                // ever submitted, so Save always sends a genuine pipeline id.
                if (selectedType == "pipeline") {
                    PipelineBindPicker(
                        pipelines = pipelines,
                        selectedId = pipelineChoice,
                        onSelect = { pipelineChoice = it },
                        onCreate = { name -> onCreatePipeline(name) },
                        pickLabel = stringResource(Res.string.event_responses_dialog_pipeline_pick),
                        choosePlaceholder = stringResource(Res.string.event_responses_dialog_pipeline_choose),
                        createNewLabel = stringResource(Res.string.event_responses_dialog_pipeline_create_new),
                        newNameLabel = stringResource(Res.string.event_responses_dialog_pipeline_new_name),
                        createLabel = stringResource(Res.string.event_responses_dialog_pipeline_create_confirm),
                        cancelLabel = stringResource(Res.string.event_responses_dialog_cancel),
                    )
                    Text(
                        text = stringResource(Res.string.event_responses_dialog_pipeline_help),
                        style = typography.xs,
                        color = tokens.mutedForeground,
                    )
                    // S047-remaining: dry-run the bound pipeline from right here.
                    PipelineTestAction(
                        pipelineId = pipelineChoice,
                        onClick = { testRunController.reset(); testRunDialogOpen = true },
                    )
                }
            }
        },
        confirmButton = {
            TextButton(
                onClick = {
                    onSave(
                        selectedType,
                        message.takeIf { it.isNotBlank() },
                        pipelineChoice,
                        widgetChoice.takeIf { selectedType == "overlay" && it.isNotBlank() },
                    )
                },
                enabled = canSubmit,
            ) {
                Text(
                    text = stringResource(Res.string.event_responses_dialog_save),
                    color = if (canSubmit) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
                TextButton(onClick = { confirmingReset = true }, enabled = manage.isAllowed) {
                    Text(
                        text = stringResource(Res.string.event_responses_dialog_reset),
                        color = if (manage.isAllowed) tokens.destructive else tokens.mutedForeground,
                    )
                }
                TextButton(onClick = onDismiss) {
                    Text(
                        text = stringResource(Res.string.event_responses_dialog_cancel),
                        color = tokens.mutedForeground,
                    )
                }
            }
        },
    )

    // Names exactly what will change BEFORE the operator commits: this event's response reverts to disabled +
    // no message — never phrased as a permanent delete, because the backend's list read re-seeds it right back.
    if (confirmingReset) {
        ConfirmDialog(
            title = stringResource(Res.string.event_responses_reset_confirm_title),
            message = stringResource(
                Res.string.event_responses_reset_confirm_message,
                response.eventType.toEventLabel(),
            ),
            confirmLabel = stringResource(Res.string.event_responses_dialog_reset),
            dismissLabel = stringResource(Res.string.event_responses_dialog_cancel),
            onConfirm = {
                confirmingReset = false
                onResetToDefault()
            },
            onDismiss = { confirmingReset = false },
            destructive = true,
        )
    }

    if (testRunDialogOpen) {
        val boundPipelineId: String? = pipelineChoice
        PipelineTestRunDialog(
            running = testRunState.running,
            result = testRunState.result,
            error = testRunState.error,
            onRun = { variables ->
                boundPipelineId?.let { id -> testRunScope.launch { testRunController.run(id, variables) } }
            },
            onDismiss = { testRunDialogOpen = false },
        )
    }
}

// Append a template token to the message, inserting a separating space only when needed.
private fun appendToken(current: String, token: String): String =
    when {
        current.isEmpty() -> token
        current.endsWith(" ") -> current + token
        else -> "$current $token"
    }

@Composable
private fun ErrorContent(detail: String, onRetry: () -> Unit) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(
            horizontalAlignment = Alignment.CenterHorizontally,
            verticalArrangement = Arrangement.spacedBy(spacing.s2),
        ) {
            Text(
                text = stringResource(Res.string.event_responses_error, detail),
                style = typography.sm,
                color = tokens.mutedForeground,
                textAlign = TextAlign.Center,
            )
            TextButton(onClick = onRetry) {
                Text(
                    text = stringResource(Res.string.event_responses_retry),
                    color = tokens.primary,
                )
            }
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text = text, style = typography.sm, color = tokens.mutedForeground)
    }
}

@Composable
private fun String.toEventLabel(): String =
    when (this) {
        "channel.follow" -> stringResource(Res.string.event_type_channel_follow)
        "channel.subscribe" -> stringResource(Res.string.event_type_channel_subscribe)
        "channel.subscription.gift" -> stringResource(Res.string.event_type_channel_subscription_gift)
        "channel.subscription.message" -> stringResource(Res.string.event_type_channel_subscription_message)
        "channel.cheer" -> stringResource(Res.string.event_type_channel_cheer)
        "channel.raid" -> stringResource(Res.string.event_type_channel_raid)
        "stream.online" -> stringResource(Res.string.event_type_stream_online)
        "stream.offline" -> stringResource(Res.string.event_type_stream_offline)
        "channel.poll.begin" -> stringResource(Res.string.event_type_channel_poll_begin)
        "channel.prediction.begin" -> stringResource(Res.string.event_type_channel_prediction_begin)
        "channel.channel_points_custom_reward_redemption.add" ->
            stringResource(Res.string.event_type_channel_points_redemption)
        "engagement.session_first_message" ->
            stringResource(Res.string.event_type_engagement_session_first_message)
        "engagement.first_time_chatter" ->
            stringResource(Res.string.event_type_engagement_first_time_chatter)
        else -> stringResource(Res.string.event_type_unknown, this)
    }

@Composable
private fun String.toResponseTypeLabel(): String =
    when (this) {
        "chat_message" -> stringResource(Res.string.event_responses_type_chat_message)
        "overlay" -> stringResource(Res.string.event_responses_type_overlay)
        "pipeline" -> stringResource(Res.string.event_responses_type_pipeline)
        else -> stringResource(Res.string.event_responses_type_none)
    }
