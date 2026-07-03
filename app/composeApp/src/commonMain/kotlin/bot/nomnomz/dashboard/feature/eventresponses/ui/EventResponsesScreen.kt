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
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.Switch
import androidx.compose.material3.SwitchDefaults
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
import bot.nomnomz.dashboard.core.designsystem.component.AppTextField
import bot.nomnomz.dashboard.core.designsystem.component.Card
import bot.nomnomz.dashboard.core.designsystem.component.GlyphButton
import bot.nomnomz.dashboard.core.designsystem.component.ManageDecision
import bot.nomnomz.dashboard.core.designsystem.component.ManageGate
import bot.nomnomz.dashboard.core.designsystem.component.PageHeader
import bot.nomnomz.dashboard.core.designsystem.component.TextButton
import bot.nomnomz.dashboard.core.designsystem.icon.ChevronDownGlyph
import bot.nomnomz.dashboard.core.designsystem.icon.EditGlyph
import bot.nomnomz.dashboard.core.designsystem.theme.LocalSpacing
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTokens
import bot.nomnomz.dashboard.core.designsystem.theme.LocalTypography
import bot.nomnomz.dashboard.core.network.EventResponseSummary
import bot.nomnomz.dashboard.feature.eventresponses.state.EventResponsesController
import bot.nomnomz.dashboard.feature.eventresponses.state.EventResponsesState
import bot.nomnomz.dashboard.feature.shell.nav.ManagementRole
import bot.nomnomz.dashboard.feature.shell.nav.ShellRoute
import bot.nomnomz.dashboard.feature.shell.nav.rememberManageDecision
import kotlinx.coroutines.launch
import nomnomzbot.composeapp.generated.resources.Res
import nomnomzbot.composeapp.generated.resources.event_responses_action_error
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_cancel
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_delete
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_message_label
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_pipeline_label
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_response_type_label
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_save
import nomnomzbot.composeapp.generated.resources.event_responses_dialog_title
import nomnomzbot.composeapp.generated.resources.event_responses_edit_action
import nomnomzbot.composeapp.generated.resources.event_responses_empty
import nomnomzbot.composeapp.generated.resources.event_responses_error
import nomnomzbot.composeapp.generated.resources.event_responses_loading
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
        EditDialog(
            response = response,
            onDismiss = { editing = null },
            onSave = { responseType, message, pipelineId ->
                editing = null
                scope.launch { controller.save(response.eventType, responseType, message, pipelineId) }
            },
            onDelete = {
                editing = null
                scope.launch { controller.delete(response.eventType) }
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
                            HorizontalDivider(color = tokens.border.copy(alpha = 0.5f))
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
            Switch(
                checked = response.isEnabled,
                onCheckedChange = onToggle,
                enabled = enabled,
                colors = SwitchDefaults.colors(
                    checkedThumbColor = tokens.primaryForeground,
                    checkedTrackColor = tokens.primary,
                    uncheckedThumbColor = tokens.mutedForeground,
                    uncheckedTrackColor = tokens.muted,
                    uncheckedBorderColor = tokens.border,
                ),
                modifier = Modifier.clearAndSetSemantics { contentDescription = toggleSemantics },
            )
        }
        ManageGate(decision = manage) { enabled ->
            GlyphButton(imageVector = EditGlyph, label = editSemantics, onClick = onEdit, enabled = enabled)
        }
    }
}

private val ResponseTypes: List<String> = listOf("none", "chat_message", "overlay", "pipeline")

// Edit dialog — response type picker, optional message template, optional pipeline ID.
// Uses AppTextField throughout for design-system consistency.
@Composable
private fun EditDialog(
    response: EventResponseSummary,
    onDismiss: () -> Unit,
    onSave: (responseType: String, message: String?, pipelineId: String?) -> Unit,
    onDelete: () -> Unit,
    manage: ManageDecision,
) {
    val tokens = LocalTokens.current
    val typography = LocalTypography.current
    val spacing = LocalSpacing.current

    var selectedType: String by remember { mutableStateOf(response.responseType) }
    var message: String by remember { mutableStateOf("") }
    var pipelineId: String by remember { mutableStateOf("") }
    var typeMenuOpen: Boolean by remember { mutableStateOf(false) }

    AlertDialog(
        onDismissRequest = onDismiss,
        containerColor = tokens.card,
        titleContentColor = tokens.cardForeground,
        textContentColor = tokens.mutedForeground,
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
                            IconButton(onClick = { typeMenuOpen = true }) {
                                Icon(
                                    imageVector = ChevronDownGlyph,
                                    contentDescription = null,
                                    tint = tokens.mutedForeground,
                                    modifier = Modifier.size(spacing.s4),
                                )
                            }
                        },
                    )
                    DropdownMenu(
                        expanded = typeMenuOpen,
                        onDismissRequest = { typeMenuOpen = false },
                        containerColor = tokens.popover,
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

                // Message template — chat_message and overlay responses need a body.
                if (selectedType == "chat_message" || selectedType == "overlay") {
                    AppTextField(
                        value = message,
                        onValueChange = { message = it },
                        label = stringResource(Res.string.event_responses_dialog_message_label),
                        modifier = Modifier.fillMaxWidth(),
                    )
                }

                // Pipeline ID — pipeline responses point to a pipeline by ID.
                if (selectedType == "pipeline") {
                    AppTextField(
                        value = pipelineId,
                        onValueChange = { pipelineId = it },
                        label = stringResource(Res.string.event_responses_dialog_pipeline_label),
                        modifier = Modifier.fillMaxWidth(),
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
                        pipelineId.takeIf { it.isNotBlank() },
                    )
                },
                enabled = manage.isAllowed,
            ) {
                Text(
                    text = stringResource(Res.string.event_responses_dialog_save),
                    color = if (manage.isAllowed) tokens.primary else tokens.mutedForeground,
                )
            }
        },
        dismissButton = {
            Row(horizontalArrangement = Arrangement.spacedBy(spacing.s1)) {
                TextButton(onClick = onDelete, enabled = manage.isAllowed) {
                    Text(
                        text = stringResource(Res.string.event_responses_dialog_delete),
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
